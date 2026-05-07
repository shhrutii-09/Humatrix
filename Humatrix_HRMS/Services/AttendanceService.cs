using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class AttendanceService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;
        private readonly UserManager<ApplicationUser> _userManager;

        // ─── SYSTEM CONSTANTS ────────────────────────────────────────────────────
        private const int OVERTIME_GRACE_MINUTES = 15;   // ignore OT < 15 min after shift end
        private const double MIN_OVERTIME_HOURS = 0.5;  // must work ≥ 30 min past shift end
        private const double MAX_OVERTIME_HOURS_DAY = 4.0;  // cap claimable OT per day
        private const int GEO_BUFFER_METERS = 50;   // extra geo tolerance on checkout
        // ─────────────────────────────────────────────────────────────────────────

        public AttendanceService(
            ApplicationDbContext context,
            CurrentUserService currentUser,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _currentUser = currentUser;
            _userManager = userManager;
        }

        // =========================================================================
        // CHECK IN
        // =========================================================================
        public async Task CheckInAsync(double latitude, double longitude)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            if (latitude == 0 && longitude == 0)
                throw new Exception("Invalid location");

            var orgId = user.OrganizationId ?? Guid.Empty;
            if (orgId == Guid.Empty)
                throw new Exception("Organization not found");

            var employee = await _context.Employees
                .Include(e => e.Shift)
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee profile not found");

            if (employee.Status != "Active")
                throw new Exception("Inactive employee cannot check in");

            var org = await _context.Organizations
                .FirstOrDefaultAsync(o => o.OrganizationId == orgId)
                ?? throw new Exception("Organization not found");

            var tz = GetOrgTimezone(org.TimeZoneId);
            var today = TimeHelper.GetOrgDate(org.TimeZoneId);

            // ── Blocking conditions ──────────────────────────────────────────────
            var isHoliday = await _context.Holidays.AnyAsync(h =>
                h.OrganizationId == orgId &&
                h.Date.Date == today &&
                !h.IsOptional);
            if (isHoliday)
                throw new Exception("Today is a holiday — check-in blocked");

            var isOnLeave = await _context.LeaveRequests.AnyAsync(l =>
                l.EmployeeId == employee.EmployeeId &&
                l.Status == "Approved" &&
                l.FromDate.Date <= today &&
                l.ToDate.Date >= today);
            if (isOnLeave)
                throw new Exception("You are on approved leave");

            var isWFH = await _context.WorkFromHomeRequests.AnyAsync(w =>
                w.EmployeeId == employee.EmployeeId &&
                w.Status == "Approved" &&
                w.Date.Date == today);
            if (isWFH)
                throw new Exception("You are on Work From Home today");

            // ── Geo check ────────────────────────────────────────────────────────
            var office = await _context.OfficeLocations
                .FirstOrDefaultAsync(x => x.OrganizationId == orgId)
                ?? throw new Exception("Office location not configured");

            double distance = GetDistance(latitude, longitude, office.Latitude, office.Longitude);
            if (distance > office.RadiusInMeters)
                throw new Exception($"You are not within office premises ({(int)distance}m away, limit {office.RadiusInMeters}m)");

            // ── Duplicate check ──────────────────────────────────────────────────
            var attendance = await _context.Attendances
                .FirstOrDefaultAsync(a => a.UserId == user.Id && a.WorkDate == today);

            if (attendance?.CheckIn != null)
                throw new Exception("Already checked in today");

            // ── Create or reuse record ───────────────────────────────────────────
            if (attendance == null)
            {
                attendance = new Attendance
                {
                    AttendanceId = Guid.NewGuid(),
                    UserId = user.Id,
                    EmployeeId = employee.EmployeeId,
                    WorkDate = today,
                    OrganizationId = orgId,
                    IsManual = false
                };
                _context.Attendances.Add(attendance);
            }

            attendance.CheckIn = DateTime.UtcNow;
            attendance.IsPresent = true;
            attendance.Latitude = latitude;
            attendance.Longitude = longitude;

            // ── Late detection ───────────────────────────────────────────────────
            var shift = employee.Shift;
            if (shift != null)
            {
                var checkInLocal = TimeZoneInfo.ConvertTimeFromUtc(attendance.CheckIn.Value, tz);
                var checkInTime = checkInLocal.TimeOfDay;

                // Overnight shift: if employee checks in after midnight (before shift end), adjust
                if (shift.EndTime < shift.StartTime && checkInTime < shift.EndTime)
                    checkInTime = checkInTime.Add(TimeSpan.FromHours(24));

                var lateThreshold = shift.StartTime.Add(TimeSpan.FromMinutes(shift.LateAllowanceMinutes));
                attendance.Status = checkInTime > lateThreshold
                    ? AttendanceStatuses.Late
                    : AttendanceStatuses.Present;
            }
            else
            {
                attendance.Status = AttendanceStatuses.Present;
            }

            await _context.SaveChangesAsync();
        }

        // =========================================================================
        // CHECK OUT
        // =========================================================================
        public async Task CheckOutAsync(double latitude, double longitude)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            if (latitude == 0 && longitude == 0)
                throw new Exception("Invalid location");

            var orgId = user.OrganizationId ?? Guid.Empty;
            if (orgId == Guid.Empty)
                throw new Exception("Organization not found");

            var org = await _context.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrganizationId == orgId)
                ?? throw new Exception("Organization not found");

            var tz = GetOrgTimezone(org.TimeZoneId);
            var today = TimeHelper.GetOrgDate(org.TimeZoneId);

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // ── Load attendance ──────────────────────────────────────────────
                var attendance = await _context.Attendances
                    .Include(a => a.Employee)
                        .ThenInclude(e => e.Shift)
                    .FirstOrDefaultAsync(a => a.UserId == user.Id && a.WorkDate == today);

                if (attendance == null || attendance.CheckIn == null)
                    throw new Exception("You must check in first");

                if (attendance.CheckOut != null)
                    throw new Exception("Already checked out today");

                if (attendance.Employee?.Status != "Active")
                    throw new Exception("Inactive employee cannot check out");

                // ── Geo check ────────────────────────────────────────────────────
                var office = await _context.OfficeLocations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.OrganizationId == orgId)
                    ?? throw new Exception("Office location not configured");

                double distance = GetDistance(latitude, longitude, office.Latitude, office.Longitude);
                if (distance > office.RadiusInMeters + GEO_BUFFER_METERS)
                    throw new Exception($"You are not within office premises ({(int)distance}m away)");

                // ── Record checkout ──────────────────────────────────────────────
                var utcNow = DateTime.UtcNow;

                attendance.CheckOut = utcNow;
                attendance.ActualCheckOut = utcNow;
                attendance.IsAutoCheckedOut = false;
                attendance.Latitude = latitude;
                attendance.Longitude = longitude;
                attendance.IsManual = false;

                var shift = attendance.Employee?.Shift;

                // ── Total hours ──────────────────────────────────────────────────
                var totalHours = (utcNow - attendance.CheckIn.Value).TotalHours;
                attendance.TotalHours = Math.Round(totalHours, 2);

                if (shift != null)
                {
                    var checkInLocal = TimeZoneInfo.ConvertTimeFromUtc(attendance.CheckIn.Value, tz);
                    var checkOutLocal = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
                    var shiftDate = checkInLocal.Date;

                    var shiftStart = shiftDate.Add(shift.StartTime);
                    var shiftEnd = shiftDate.Add(shift.EndTime);

                    // Handle overnight shift
                    if (shift.EndTime < shift.StartTime)
                        shiftEnd = shiftEnd.AddDays(1);

                    // ─── SystemCheckOut = expected shift end (fixed reference) ───
                    // Always record what the SCHEDULED end was, regardless of when
                    // the employee actually left.  OvertimeService uses this as the
                    // baseline so it never changes after checkout.
                    var shiftEndUtc = TimeZoneInfo.ConvertTimeToUtc(shiftEnd, tz);
                    attendance.SystemCheckOut = shiftEndUtc;

                    // ── Overtime detection ───────────────────────────────────────
                    // Only flag if employee stayed > (shift end + grace period)
                    // and worked at least MIN_OVERTIME_HOURS beyond shift end.
                    var overtimeThreshold = shiftEnd.AddMinutes(OVERTIME_GRACE_MINUTES);

                    if (checkOutLocal > overtimeThreshold)
                    {
                        var overtimeHours = (checkOutLocal - shiftEnd).TotalHours;
                        // Cap the pre-calculated value; actual approved hours set later
                        overtimeHours = Math.Min(overtimeHours, MAX_OVERTIME_HOURS_DAY);

                        if (overtimeHours >= MIN_OVERTIME_HOURS)
                        {
                            attendance.NeedsOvertimeApproval = true;
                            attendance.OvertimeHours = Math.Round(overtimeHours, 2);
                        }
                        else
                        {
                            attendance.NeedsOvertimeApproval = false;
                            attendance.OvertimeHours = 0;
                        }
                    }
                    else
                    {
                        attendance.NeedsOvertimeApproval = false;
                        attendance.OvertimeHours = 0;
                    }

                    // ── Attendance status ────────────────────────────────────────
                    bool leftEarly = checkOutLocal < shiftEnd;

                    if (totalHours < shift.MinimumHoursForHalfDay)
                        attendance.Status = AttendanceStatuses.ShortHours;
                    else if (totalHours < shift.MinimumHoursForFullDay)
                        attendance.Status = AttendanceStatuses.HalfDay;
                    else if (leftEarly)
                        attendance.Status = AttendanceStatuses.EarlyExit;
                    else
                        // Preserve Late flag set at check-in
                        attendance.Status = attendance.Status == AttendanceStatuses.Late
                            ? AttendanceStatuses.Late
                            : AttendanceStatuses.Present;
                }
                else
                {
                    // No shift configured — treat as basic present, no overtime
                    attendance.Status = AttendanceStatuses.Present;
                    attendance.NeedsOvertimeApproval = false;
                    attendance.OvertimeHours = 0;
                    attendance.SystemCheckOut = utcNow; // best guess
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // =========================================================================
        // GET TODAY STATUS
        // =========================================================================
        public async Task<Attendance?> GetTodayStatusAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var today = TimeHelper.GetOrgDate(
                (await _context.Organizations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.OrganizationId == user.OrganizationId))?.TimeZoneId);

            return await _context.Attendances
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.UserId == user.Id && a.WorkDate == today);
        }

        // =========================================================================
        // MY ATTENDANCE (last 30 days)
        // =========================================================================
        public async Task<List<AttendanceListDto>> GetMyAttendanceAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var org = await _context.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrganizationId == user.OrganizationId);

            var today = TimeHelper.GetOrgDate(org?.TimeZoneId);

            var employee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            var records = await _context.Attendances
                .AsNoTracking()
                .Where(a => a.UserId == user.Id)
                .OrderByDescending(a => a.WorkDate)
                .Take(30)
                .ToListAsync();

            // ── Overtime status map ──────────────────────────────────────────────
            var attendanceIds = records
                .Select(r => r.AttendanceId)
                .Where(id => id != Guid.Empty)
                .ToList();

            var overtimeMap = await _context.OvertimeRequests
                .Where(o => attendanceIds.Contains(o.AttendanceId))
                .GroupBy(o => o.AttendanceId)
                .Select(g => new
                {
                    AttendanceId = g.Key,
                    Status = g.OrderByDescending(x => x.AppliedAt)
                               .Select(x => x.Status)
                               .FirstOrDefault()
                })
                .ToDictionaryAsync(x => x.AttendanceId, x => x.Status);

            var list = new List<AttendanceListDto>();

            // Show "Not Started" if no record yet for today
            if (!records.Any(r => r.WorkDate == today))
            {
                list.Add(new AttendanceListDto
                {
                    AttendanceId = Guid.Empty,
                    EmployeeName = $"{employee?.FirstName} {employee?.LastName}",
                    Date = today,
                    Status = "Not Started"
                });
            }

            foreach (var r in records)
            {
                overtimeMap.TryGetValue(r.AttendanceId, out var otStatus);

                list.Add(new AttendanceListDto
                {
                    AttendanceId = r.AttendanceId,
                    EmployeeName = $"{employee?.FirstName} {employee?.LastName}",
                    Date = r.WorkDate,
                    CheckIn = r.CheckIn,
                    CheckOut = r.CheckOut,
                    Status = r.Status,
                    TotalHours = r.TotalHours,
                    IsManual = r.IsManual,
                    NeedsOvertimeApproval = r.NeedsOvertimeApproval,
                    OvertimeStatus = otStatus
                });
            }

            return list;
        }

        // =========================================================================
        // ALL ATTENDANCE (HR / Admin view)
        // =========================================================================
        public async Task<List<AttendanceListDto>> GetAllAttendanceAsync(
            DateTime? date = null,
            Guid? departmentId = null,
            string? role = null)
        {
            var currentUser = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = currentUser.OrganizationId ?? Guid.Empty;

            var currentEmployee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == currentUser.Id);

            List<string>? filteredUserIds = null;
            if (!string.IsNullOrEmpty(role))
            {
                var usersInRole = await _userManager.GetUsersInRoleAsync(role);
                filteredUserIds = usersInRole
                    .Where(u => u.OrganizationId == orgId)
                    .Select(u => u.Id)
                    .ToList();

                if (!filteredUserIds.Any())
                    return new List<AttendanceListDto>();
            }

            var query = _context.Attendances
                .AsNoTracking()
                .Include(a => a.User)
                .Include(a => a.Employee)
                .Where(a => a.OrganizationId == orgId);

            if (date.HasValue)
            {
                var targetDate = date.Value.Date;
                query = query.Where(a => a.WorkDate >= targetDate && a.WorkDate < targetDate.AddDays(1));
            }

            bool isHR = await _userManager.IsInRoleAsync(currentUser, "HR");
            bool isOrgAdmin = await _userManager.IsInRoleAsync(currentUser, "OrgAdmin");

            if (isHR && !isOrgAdmin)
            {
                if (currentEmployee?.DepartmentId != null)
                    query = query.Where(a => a.Employee != null && a.Employee.DepartmentId == currentEmployee.DepartmentId);
                else
                    return new List<AttendanceListDto>();
            }

            if (departmentId.HasValue)
                query = query.Where(a => a.Employee != null && a.Employee.DepartmentId == departmentId);

            if (filteredUserIds != null)
                query = query.Where(a => filteredUserIds.Contains(a.UserId));

            var results = await query.ToListAsync();

            var departments = await _context.Departments
                .AsNoTracking()
                .ToDictionaryAsync(d => d.DepartmentId, d => d.Name);

            var userIds = results.Select(a => a.UserId).Distinct().ToList();
            var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToListAsync();

            var userRoleMap = new Dictionary<string, string>();
            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                userRoleMap[u.Id] = roles.FirstOrDefault() ?? "Employee";
            }

            var list = new List<AttendanceListDto>();
            foreach (var a in results)
            {
                var deptId = a.Employee?.DepartmentId;
                list.Add(new AttendanceListDto
                {
                    EmployeeName = a.Employee != null
                        ? $"{a.Employee.FirstName} {a.Employee.LastName}"
                        : $"{a.User?.FirstName} {a.User?.LastName}",
                    Email = a.User?.Email,
                    Department = deptId != null && departments.ContainsKey(deptId.Value)
                        ? departments[deptId.Value]
                        : "N/A",
                    Role = userRoleMap.GetValueOrDefault(a.UserId, "Employee"),
                    Date = a.WorkDate,
                    CheckIn = a.CheckIn,
                    CheckOut = a.CheckOut,
                    Status = a.Status,
                    IsManual = a.IsManual
                });
            }

            return list;
        }

        // =========================================================================
        // ATTENDANCE FOR SPECIFIC DATE  (HR / Admin calendar view)
        // =========================================================================
        public async Task<List<AttendanceListDto>> GetAttendanceForDateAsync(
            DateTime date,
            Guid? departmentId = null,
            string? role = null)
        {
            var currentUser = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = currentUser.OrganizationId ?? Guid.Empty;

            var org = await _context.Organizations
                .FirstOrDefaultAsync(o => o.OrganizationId == orgId)
                ?? throw new Exception("Organization not found");

            var targetDate = date.Date;

            bool isHR = await _userManager.IsInRoleAsync(currentUser, "HR");
            bool isOrgAdmin = await _userManager.IsInRoleAsync(currentUser, "OrgAdmin");

            var currentEmployee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == currentUser.Id);

            var workWeek = await _context.WorkWeeks
                .FirstOrDefaultAsync(w => w.OrganizationId == orgId);

            bool isWeekend = workWeek != null
                ? !DateHelper.IsWorkingDay(targetDate, workWeek)
                : (targetDate.DayOfWeek == DayOfWeek.Saturday || targetDate.DayOfWeek == DayOfWeek.Sunday);

            var holiday = await _context.Holidays
                .FirstOrDefaultAsync(h =>
                    h.OrganizationId == orgId &&
                    h.Date.Date == targetDate &&
                    !h.IsOptional);

            var empQuery = _context.Employees
                .AsNoTracking()
                .Where(e => e.OrganizationId == orgId && e.Status == "Active");

            if (isHR && !isOrgAdmin)
            {
                if (currentEmployee?.DepartmentId != null)
                    empQuery = empQuery.Where(e => e.DepartmentId == currentEmployee.DepartmentId);
                else
                    return new List<AttendanceListDto>();
            }

            if (departmentId.HasValue)
                empQuery = empQuery.Where(e => e.DepartmentId == departmentId);

            var employees = await empQuery.Include(e => e.Department).ToListAsync();

            if (!string.IsNullOrEmpty(role))
            {
                var usersInRole = await _userManager.GetUsersInRoleAsync(role);
                var userIdsInRole = usersInRole
                    .Where(u => u.OrganizationId == orgId)
                    .Select(u => u.Id)
                    .ToHashSet();
                employees = employees.Where(e => e.UserId != null && userIdsInRole.Contains(e.UserId)).ToList();
            }

            var employeeIds = employees.Select(e => e.EmployeeId).ToList();
            var userIds = employees.Where(e => e.UserId != null).Select(e => e.UserId!).ToList();

            var attendances = await _context.Attendances
                .AsNoTracking()
                .Where(a => a.OrganizationId == orgId && a.WorkDate == targetDate && employeeIds.Contains(a.EmployeeId))
                .ToDictionaryAsync(a => a.EmployeeId);

            var onLeave = await _context.LeaveRequests
                .AsNoTracking()
                .Include(l => l.LeaveType)
                .Where(l =>
                    l.Status == "Approved" &&
                    l.FromDate.Date <= targetDate &&
                    l.ToDate.Date >= targetDate &&
                    employeeIds.Contains(l.EmployeeId))
                .ToDictionaryAsync(l => l.EmployeeId);

            var wfhSet = (await _context.WorkFromHomeRequests
                .AsNoTracking()
                .Where(w =>
                    w.Status == "Approved" &&
                    w.Date.Date == targetDate &&
                    employeeIds.Contains(w.EmployeeId))
                .Select(w => w.EmployeeId)
                .ToListAsync())
                .ToHashSet();

            var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToListAsync();
            var userRoleMap = new Dictionary<string, string>();
            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                userRoleMap[u.Id] = roles.FirstOrDefault() ?? "Employee";
            }
            var userMap = users.ToDictionary(u => u.Id);
            var today = TimeHelper.GetOrgDate(org.TimeZoneId);
            var list = new List<AttendanceListDto>();

            foreach (var emp in employees)
            {
                var deptName = emp.Department?.Name ?? "N/A";
                var empRole = emp.UserId != null ? userRoleMap.GetValueOrDefault(emp.UserId, "Employee") : "Employee";
                var email = emp.UserId != null && userMap.ContainsKey(emp.UserId) ? userMap[emp.UserId].Email : null;
                var empName = $"{emp.FirstName} {emp.LastName}";

                if (holiday != null)
                {
                    list.Add(new AttendanceListDto { EmployeeName = empName, Email = email, Department = deptName, Role = empRole, Date = targetDate, Status = $"Holiday: {holiday.Name}", IsManual = false });
                    continue;
                }
                if (isWeekend)
                {
                    list.Add(new AttendanceListDto { EmployeeName = empName, Email = email, Department = deptName, Role = empRole, Date = targetDate, Status = $"Weekend ({targetDate.DayOfWeek})", IsManual = false });
                    continue;
                }
                if (onLeave.TryGetValue(emp.EmployeeId, out var leaveReq))
                {
                    list.Add(new AttendanceListDto { EmployeeName = empName, Email = email, Department = deptName, Role = empRole, Date = targetDate, Status = $"On Leave ({leaveReq.LeaveType?.Name ?? "Leave"})", IsManual = false });
                    continue;
                }
                if (wfhSet.Contains(emp.EmployeeId))
                {
                    list.Add(new AttendanceListDto { EmployeeName = empName, Email = email, Department = deptName, Role = empRole, Date = targetDate, Status = AttendanceStatuses.WorkFromHome, IsManual = false });
                    continue;
                }
                if (attendances.TryGetValue(emp.EmployeeId, out var att))
                {
                    list.Add(new AttendanceListDto { EmployeeName = empName, Email = email, Department = deptName, Role = empRole, Date = targetDate, CheckIn = att.CheckIn, CheckOut = att.CheckOut, Status = att.Status, IsManual = att.IsManual });
                    continue;
                }

                list.Add(new AttendanceListDto { EmployeeName = empName, Email = email, Department = deptName, Role = empRole, Date = targetDate, Status = targetDate == today ? "Not Started" : AttendanceStatuses.Absent, IsManual = false });
            }

            return list.OrderBy(x => x.EmployeeName).ToList();
        }

        // =========================================================================
        // RESOLVE DAILY STATUS  (used by calendar / summary widgets)
        // =========================================================================
        public async Task<string> ResolveDailyStatus(Guid employeeId, DateTime date)
        {
            var user = await _currentUser.GetUserAsync() ?? throw new Exception("Unauthorized");
            var orgId = user.OrganizationId ?? Guid.Empty;

            if (await _context.Holidays.AnyAsync(h => h.OrganizationId == orgId && h.Date.Date == date.Date && !h.IsOptional))
                return "Holiday";

            var leave = await _context.LeaveRequests
                .Include(l => l.LeaveType)
                .FirstOrDefaultAsync(l => l.EmployeeId == employeeId && l.Status == "Approved" && l.FromDate.Date <= date && l.ToDate.Date >= date);
            if (leave != null)
                return $"Leave ({leave.LeaveType.Name})";

            var wfh = await _context.WorkFromHomeRequests
                .FirstOrDefaultAsync(w => w.EmployeeId == employeeId && w.Status == "Approved" && w.Date.Date == date);
            if (wfh != null)
                return AttendanceStatuses.WorkFromHome;

            var att = await _context.Attendances.FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.WorkDate == date);
            if (att != null) return att.Status;

            var today = TimeHelper.GetOrgDate(
                (await _context.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.OrganizationId == orgId))?.TimeZoneId);

            return date.Date == today ? "Not Started" : AttendanceStatuses.Absent;
        }

        // =========================================================================
        // HELPERS
        // =========================================================================
        private TimeZoneInfo GetOrgTimezone(string? timeZoneId)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId ?? "UTC"); }
            catch { return TimeZoneInfo.Utc; }
        }

        private double GetDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000;
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private double ToRadians(double angle) => angle * Math.PI / 180;
    }
}