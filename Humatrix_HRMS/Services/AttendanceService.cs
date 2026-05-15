using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    /// <summary>
    /// Handles employee check-in / check-out, today-status queries, and
    /// attendance list views for both employees and HR/Admin.
    ///
    /// Timezone contract:
    ///   • All DateTime values in the database are UTC (or Unspecified-from-EF).
    ///   • WorkDate is the org-local calendar date (DATE only — no time component).
    ///   • Display conversions happen only in Razor via TimeHelper.
    ///   • DateTime.Now and .ToLocalTime() are NEVER used here.
    /// </summary>
    public class AttendanceService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AttendanceCalculationService _calc;
        private readonly HRPolicyValidationService _policy;

        private const int GEO_BUFFER_CHECKOUT = AttendanceConstants.GeoBufferMetersOnCheckout;

        public AttendanceService(
            ApplicationDbContext context,
            CurrentUserService currentUser,
            UserManager<ApplicationUser> userManager,
            AttendanceCalculationService calc,
            HRPolicyValidationService policy)
        {
            _context = context;
            _currentUser = currentUser;
            _userManager = userManager;
            _calc = calc;
            _policy = policy;
        }

        // =========================================================================
        // CHECK IN
        // =========================================================================
        public async Task CheckInAsync(double latitude, double longitude)
        {
            if (latitude == 0 && longitude == 0)
                throw new Exception("Invalid location: coordinates cannot both be zero.");

            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = user.OrganizationId ?? Guid.Empty;
            if (orgId == Guid.Empty)
                throw new Exception("No organisation linked to your account.");

            var employee = await _context.Employees
                .Include(e => e.Shift)
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee profile not found. Contact HR.");

            if (employee.Status != "Active")
                throw new Exception("Inactive employees cannot check in.");

            var org = await _context.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrganizationId == orgId)
                ?? throw new Exception("Organisation not found.");

            var tz = TimeHelper.GetOrgTimeZone(org.TimeZoneId);
            var utcNow = DateTime.UtcNow;
            var orgNow = TimeHelper.ToOrgLocal(utcNow, tz);
            var today = orgNow.Date; // WorkDate for this check-in

            // ── Policy checks (holiday, workweek, leave, WFH) ────────────────────
            await _policy.AssertEmployeeCanActAsync(orgId, employee.EmployeeId, today, "CheckIn");

            // ── Geo validation ────────────────────────────────────────────────────
            var office = await _context.OfficeLocations
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.OrganizationId == orgId)
                ?? throw new Exception("Office location not configured. Contact HR.");

            double distance = GeoDistance(latitude, longitude, office.Latitude, office.Longitude);
            if (distance > office.RadiusInMeters)
                throw new Exception(
                    $"You are {(int)distance}m from the office. Check-in requires being within {office.RadiusInMeters}m.");

            // ── Orphaned open-session guard ───────────────────────────────────────
            // An open session can exist when an employee's browser was open across
            // midnight on a non-overnight shift and auto-checkout hasn't run yet.
            var openSession = await _context.Attendances
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employee.EmployeeId &&
                    a.CheckIn != null &&
                    a.CheckOut == null);

            if (openSession != null)
            {
                // Provide a clear message — do NOT silently close the session,
                // as this could corrupt hours for the employee.
                throw new Exception(
                    "You have an open session from a previous shift. " +
                    "Please check out first or contact HR if you are unable to.");
            }

            // ── Duplicate check-in for today's WorkDate ───────────────────────────
            var existing = await _context.Attendances
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employee.EmployeeId &&
                    a.WorkDate == today);

            if (existing?.CheckIn != null)
                throw new Exception("You have already checked in today.");

            // ── Late detection ────────────────────────────────────────────────────
            var shift = employee.Shift;
            string initialStatus;

            if (shift != null)
            {
                var checkInLocalTime = orgNow.TimeOfDay;

                // Overnight shift edge-case: if check-in time is after midnight but
                // before shift end, it is actually a cross-day check-in — this is
                // handled by the open-session guard above. Here we are creating a new
                // record so checkInLocalTime always anchors to today's shift start.
                var lateThreshold = shift.StartTime.Add(TimeSpan.FromMinutes(shift.LateAllowanceMinutes));
                initialStatus = checkInLocalTime > lateThreshold
                    ? AttendanceStatuses.Late
                    : AttendanceStatuses.Present;
            }
            else
            {
                initialStatus = AttendanceStatuses.Present;
            }

            // ── Create or update record ───────────────────────────────────────────
            if (existing == null)
            {
                existing = new Attendance
                {
                    AttendanceId = Guid.NewGuid(),
                    UserId = user.Id,
                    EmployeeId = employee.EmployeeId,
                    WorkDate = today,
                    OrganizationId = orgId,
                    IsManual = false,
                    CreatedAt = utcNow
                };
                _context.Attendances.Add(existing);
            }

            existing.CheckIn = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
            existing.IsPresent = true;
            existing.Status = initialStatus;
            existing.Latitude = latitude;
            existing.Longitude = longitude;

            // Reset computed fields — will be set at checkout
            existing.CheckOut = null;
            existing.ActualCheckOut = null;
            existing.SystemCheckOut = null;
            existing.TotalHours = null;
            existing.OvertimeHours = 0;
            existing.ApprovedOvertimeHours = 0;
            existing.NeedsOvertimeApproval = false;
            existing.IsAutoCheckedOut = false;

            await _context.SaveChangesAsync();
        }

        // =========================================================================
        // CHECK OUT
        // Night-shift support: employee who checked in on WorkDate=yesterday can
        // check out even though org-local date is today. We first look for a record
        // with today's WorkDate; if none, we fall back to any open session.
        // =========================================================================
        public async Task CheckOutAsync(double latitude, double longitude)
        {
            if (latitude == 0 && longitude == 0)
                throw new Exception("Invalid location: coordinates cannot both be zero.");

            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = user.OrganizationId ?? Guid.Empty;
            if (orgId == Guid.Empty)
                throw new Exception("Organisation not found.");

            var org = await _context.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrganizationId == orgId)
                ?? throw new Exception("Organisation not found.");

            var tz = TimeHelper.GetOrgTimeZone(org.TimeZoneId);
            var utcNow = DateTime.UtcNow;
            var today = TimeHelper.GetOrgDate(tz);

            // Use a transaction to prevent race conditions with concurrent checkout attempts
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // ── Load attendance — prefer today's WorkDate ─────────────────────
                var attendance = await _context.Attendances
                    .Include(a => a.Employee)
                        .ThenInclude(e => e!.Shift)
                    .FirstOrDefaultAsync(a =>
                        a.EmployeeId == _context.Employees
                            .Where(e => e.UserId == user.Id)
                            .Select(e => e.EmployeeId)
                            .FirstOrDefault() &&
                        a.WorkDate == today);

                // ── Night-shift fallback: any open session ────────────────────────
                if (attendance == null || attendance.CheckIn == null)
                {
                    // Re-query using EmployeeId directly to avoid subquery issues
                    var empId = await _context.Employees
                        .Where(e => e.UserId == user.Id)
                        .Select(e => e.EmployeeId)
                        .FirstOrDefaultAsync();

                    //attendance = await _context.Attendances
                    //    .Include(a => a.Employee)
                    //        .ThenInclude(e => e!.Shift)
                    //    .FirstOrDefaultAsync(a =>
                    //        a.EmployeeId == empId &&
                    //        a.CheckIn != null &&
                    //        a.CheckOut == null);

                    attendance = await _context.Attendances
    .Include(a => a.Employee)
        .ThenInclude(e => e!.Shift)
    .Where(a =>
        a.EmployeeId == empId &&
        a.CheckIn != null &&
        a.CheckOut == null)
    .OrderByDescending(a => a.CheckIn)
    .FirstOrDefaultAsync();
                }

                if (attendance == null || attendance.CheckIn == null)
                    throw new Exception("You have not checked in yet.");

                if (attendance.CheckOut != null)
                    throw new Exception("You have already checked out for this session.");

                if (attendance.Employee?.Status != "Active")
                    throw new Exception("Inactive employees cannot check out.");

                if (attendance.CheckIn > utcNow)
                {
                    throw new Exception("Invalid attendance session detected.");
                }

                // ── Geo validation (wider radius buffer for checkout) ──────────────
                var office = await _context.OfficeLocations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.OrganizationId == orgId)
                    ?? throw new Exception("Office location not configured.");

                double distance = GeoDistance(latitude, longitude, office.Latitude, office.Longitude);
                if (distance > office.RadiusInMeters + GEO_BUFFER_CHECKOUT)
                    throw new Exception(
                        $"You are {(int)distance}m from the office. Check-out requires being within {office.RadiusInMeters + GEO_BUFFER_CHECKOUT}m.");

                // ── Record checkout ───────────────────────────────────────────────
                attendance.CheckOut = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
                attendance.ActualCheckOut = attendance.CheckOut;
                attendance.IsAutoCheckedOut = false;
                attendance.Latitude = latitude;
                attendance.Longitude = longitude;
                attendance.IsManual = false;

                // ── Recalculate everything via the canonical service ───────────────
                _calc.Recalculate(attendance, attendance.Employee?.Shift, tz);

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
        // TODAY STATUS
        // Returns the current employee's attendance record for today.
        // Also surfaces open night-shift sessions (WorkDate=yesterday) so the
        // UI correctly shows "Checked In" during a cross-midnight shift.
        // =========================================================================
        //public async Task<Attendance?> GetTodayStatusAsync()
        public async Task<AttendanceListDto?> GetTodayStatusAsync()
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

            if (employee == null)
                return null;

            // Primary: today's WorkDate record
            var todayRecord = await _context.Attendances
                .AsNoTracking()
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employee.EmployeeId &&
                    a.WorkDate == today);

            // Night-shift fallback: open session from previous WorkDate
            var openSession = await _context.Attendances
                .AsNoTracking()
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employee.EmployeeId &&
                    a.CheckIn != null &&
                    a.CheckOut == null);

            //var record = openSession ?? todayRecord;
            var record = todayRecord ?? openSession;

            if (record == null)
                return null;

            return new AttendanceListDto
            {
                AttendanceId = record.AttendanceId,
                EmployeeName = $"{employee.FirstName} {employee.LastName}",
                Date = record.WorkDate,
                CheckIn = record.CheckIn,
                CheckOut = record.CheckOut,
                SystemCheckOut = record.SystemCheckOut,
                OvertimeHours = record.OvertimeHours ?? 0,
                ApprovedOvertimeHours = record.ApprovedOvertimeHours,
                Status = record.Status,
                TotalHours = record.TotalHours,
                IsManual = record.IsManual,
                NeedsOvertimeApproval = record.NeedsOvertimeApproval
            };
        }


        // =========================================================================
        // MY ATTENDANCE  (employee view — last 30 days)
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

            if (employee == null) return new List<AttendanceListDto>();

            var records = await _context.Attendances
                .AsNoTracking()
                .Where(a => a.EmployeeId == employee.EmployeeId)
                .OrderByDescending(a => a.WorkDate)
                .Take(30)
                .ToListAsync();

            // Batch-load overtime status for all attendance records
            var attendanceIds = records
                .Where(r => r.AttendanceId != Guid.Empty)
                .Select(r => r.AttendanceId)
                .ToList();

            var overtimeStatusMap = await _context.OvertimeRequests
                .AsNoTracking()
                .Where(o => attendanceIds.Contains(o.AttendanceId))
                .GroupBy(o => o.AttendanceId)
                .Select(g => new
                {
                    AttendanceId = g.Key,
                    // Most recent status wins
                    Status = g.OrderByDescending(x => x.AppliedAt)
                               .Select(x => x.Status)
                               .FirstOrDefault()
                })
                .ToDictionaryAsync(x => x.AttendanceId, x => x.Status);

            var list = new List<AttendanceListDto>();

            // Placeholder for today when no record exists yet
            if (records.All(r => r.WorkDate != today))
            {
                list.Add(new AttendanceListDto
                {
                    AttendanceId = Guid.Empty,
                    EmployeeName = $"{employee.FirstName} {employee.LastName}",
                    Date = today,
                    Status = "Not Started"
                });
            }

            foreach (var r in records)
            {
                overtimeStatusMap.TryGetValue(r.AttendanceId, out var otStatus);

                list.Add(new AttendanceListDto
                {
                    AttendanceId = r.AttendanceId,
                    EmployeeName = $"{employee.FirstName} {employee.LastName}",
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
        // ATTENDANCE FOR A SPECIFIC DATE  (HR / Admin)
        //
        // HR rule: HR sees only their own department's employees. They do NOT see
        // their own row (their record is visible to OrgAdmin). OrgAdmin sees all.
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
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrganizationId == orgId)
                ?? throw new Exception("Organisation not found.");

            var tz = TimeHelper.GetOrgTimeZone(org.TimeZoneId);
            var targetDate = date.Date;
            var orgToday = TimeHelper.GetOrgDate(tz);

            bool isHR = await _userManager.IsInRoleAsync(currentUser, "HR");
            bool isOrgAdmin = await _userManager.IsInRoleAsync(currentUser, "OrgAdmin");

            var currentEmployee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == currentUser.Id);

            // ── Work week & holiday status for this date ──────────────────────────
            var workWeek = await _context.WorkWeeks
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.OrganizationId == orgId);

            bool isWeekend = workWeek != null
                ? !DateHelper.IsWorkingDay(targetDate, workWeek)
                : (targetDate.DayOfWeek == DayOfWeek.Saturday || targetDate.DayOfWeek == DayOfWeek.Sunday);

            var holiday = await _context.Holidays
                .AsNoTracking()
                .FirstOrDefaultAsync(h =>
                    h.OrganizationId == orgId &&
                    h.Date.Date == targetDate &&
                    !h.IsOptional);

            // ── Build employee query ──────────────────────────────────────────────
            var empQuery = _context.Employees
                .AsNoTracking()
                .Where(e => e.OrganizationId == orgId && e.Status == "Active");

            if (isHR && !isOrgAdmin)
            {
                if (currentEmployee?.DepartmentId == null)
                    return new List<AttendanceListDto>();

                // HR sees only their department, EXCLUDING their own row.
                empQuery = empQuery
                    .Where(e =>
                        e.DepartmentId == currentEmployee.DepartmentId &&
                        e.UserId != currentUser.Id);
            }

            if (isOrgAdmin && departmentId.HasValue && departmentId != Guid.Empty)
                empQuery = empQuery.Where(e => e.DepartmentId == departmentId);

            if (isHR && !isOrgAdmin && departmentId.HasValue && departmentId != Guid.Empty)
                empQuery = empQuery.Where(e => e.DepartmentId == departmentId);

            var employees = await empQuery
                .Include(e => e.Department)
                .ToListAsync();

            // ── Optional role filter ──────────────────────────────────────────────
            if (!string.IsNullOrEmpty(role))
            {
                var usersInRole = await _userManager.GetUsersInRoleAsync(role);
                var idsInRole = usersInRole
                    .Where(u => u.OrganizationId == orgId)
                    .Select(u => u.Id)
                    .ToHashSet();

                employees = employees
                    .Where(e => e.UserId != null && idsInRole.Contains(e.UserId))
                    .ToList();
            }

            if (!employees.Any()) return new List<AttendanceListDto>();

            var employeeIds = employees.Select(e => e.EmployeeId).ToList();
            var userIds = employees.Where(e => e.UserId != null).Select(e => e.UserId!).ToList();

            // ── Batch-load related data ───────────────────────────────────────────
            var attendances = await _context.Attendances
                .AsNoTracking()
                .Where(a =>
                    a.OrganizationId == orgId &&
                    a.WorkDate == targetDate &&
                    employeeIds.Contains(a.EmployeeId))
                .ToDictionaryAsync(a => a.EmployeeId);

            var onLeaveMap = await _context.LeaveRequests
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

            // Role-display map
            var appUsers = await _userManager.Users
                .Where(u => userIds.Contains(u.Id))
                .ToListAsync();

            var userRoleMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var u in appUsers)
            {
                var roles = await _userManager.GetRolesAsync(u);
                userRoleMap[u.Id] = roles.FirstOrDefault() ?? "Employee";
            }

            var userEmailMap = appUsers.ToDictionary(u => u.Id, u => u.Email);

            // ── Build result ──────────────────────────────────────────────────────
            var list = new List<AttendanceListDto>();

            foreach (var emp in employees)
            {
                var empName = $"{emp.FirstName} {emp.LastName}";
                var deptName = emp.Department?.Name ?? "N/A";
                var empRole = emp.UserId != null
                    ? userRoleMap.GetValueOrDefault(emp.UserId, "Employee")
                    : "Employee";
                var email = emp.UserId != null
                    ? userEmailMap.GetValueOrDefault(emp.UserId)
                    : null;

                // Priority: Holiday → Weekend → Leave → WFH → Attendance → Absent
                if (holiday != null)
                {
                    list.Add(MakeDto(empName, email, deptName, empRole, targetDate,
                        $"{AttendanceStatuses.Holiday}: {holiday.Name}", false));
                    continue;
                }

                if (isWeekend)
                {
                    list.Add(MakeDto(empName, email, deptName, empRole, targetDate,
                        $"{AttendanceStatuses.Weekend} ({targetDate.DayOfWeek})", false));
                    continue;
                }

                if (onLeaveMap.TryGetValue(emp.EmployeeId, out var leave))
                {
                    list.Add(MakeDto(empName, email, deptName, empRole, targetDate,
                        $"{AttendanceStatuses.OnLeave} ({leave.LeaveType?.Name ?? "Leave"})", false));
                    continue;
                }

                if (wfhSet.Contains(emp.EmployeeId))
                {
                    list.Add(MakeDto(empName, email, deptName, empRole, targetDate,
                        AttendanceStatuses.WorkFromHome, false));
                    continue;
                }

                if (attendances.TryGetValue(emp.EmployeeId, out var att))
                {
                    list.Add(new AttendanceListDto
                    {
                        AttendanceId = att.AttendanceId,
                        EmployeeName = empName,
                        Email = email,
                        Department = deptName,
                        Role = empRole,
                        Date = targetDate,
                        CheckIn = att.CheckIn,
                        CheckOut = att.CheckOut,
                        Status = att.Status,
                        TotalHours = att.TotalHours,
                        IsManual = att.IsManual,
                        NeedsOvertimeApproval = att.NeedsOvertimeApproval
                    });
                    continue;
                }

                // No record
                list.Add(MakeDto(empName, email, deptName, empRole, targetDate,
                    targetDate == orgToday ? "Not Started" : AttendanceStatuses.Absent,
                    false));
            }

            return list.OrderBy(x => x.EmployeeName).ToList();
        }

        // =========================================================================
        // RESOLVE DAILY STATUS  (calendar / summary widgets)
        // =========================================================================
        public async Task<string> ResolveDailyStatusAsync(Guid employeeId, DateTime date)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = user.OrganizationId ?? Guid.Empty;

            if (await _context.Holidays.AnyAsync(h =>
                    h.OrganizationId == orgId &&
                    h.Date.Date == date.Date &&
                    !h.IsOptional))
                return AttendanceStatuses.Holiday;

            var leave = await _context.LeaveRequests
                .Include(l => l.LeaveType)
                .FirstOrDefaultAsync(l =>
                    l.EmployeeId == employeeId &&
                    l.Status == "Approved" &&
                    l.FromDate.Date <= date &&
                    l.ToDate.Date >= date);

            if (leave != null)
                return $"{AttendanceStatuses.OnLeave} ({leave.LeaveType?.Name ?? "Leave"})";

            var wfh = await _context.WorkFromHomeRequests
                .FirstOrDefaultAsync(w =>
                    w.EmployeeId == employeeId &&
                    w.Status == "Approved" &&
                    w.Date.Date == date);

            if (wfh != null) return AttendanceStatuses.WorkFromHome;

            var att = await _context.Attendances
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.WorkDate == date);

            if (att != null) return att.Status ?? AttendanceStatuses.Present;

            var org = await _context.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrganizationId == orgId);

            var orgToday = TimeHelper.GetOrgDate(org?.TimeZoneId);
            return date.Date == orgToday ? "Not Started" : AttendanceStatuses.Absent;
        }

        // =========================================================================
        // HELPERS
        // =========================================================================

        private static AttendanceListDto MakeDto(
            string empName,
            string? email,
            string dept,
            string role,
            DateTime date,
            string status,
            bool isManual)
        {
            return new AttendanceListDto
            {
                EmployeeName = empName,
                Email = email,
                Department = dept,
                Role = role,
                Date = date,
                Status = status,
                IsManual = isManual
            };
        }

        private static double GeoDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6_371_000; // Earth radius in metres
            var dLat = ToRad(lat2 - lat1);
            var dLon = ToRad(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        public async Task<TimeZoneInfo> GetOrgTimeZoneAsync()
        {
            var user = await _currentUser.GetUserAsync();

            var tzId = await _context.Organizations
                .AsNoTracking()
                .Where(o => o.OrganizationId == user.OrganizationId)
                .Select(o => o.TimeZoneId)
                .FirstOrDefaultAsync();

            return TimeHelper.GetOrgTimeZone(tzId);
        }


        public async Task<List<AttendanceListDto>> GetMyAttendanceByMonthAsync(int year, int month)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            if (employee == null)
                return new List<AttendanceListDto>();

            var records = await _context.Attendances
                .AsNoTracking()
                .Where(a =>
                    a.EmployeeId == employee.EmployeeId &&
                    a.WorkDate.Year == year &&
                    a.WorkDate.Month == month)
                .OrderByDescending(a => a.WorkDate)
                .ToListAsync();

            return records.Select(r => new AttendanceListDto
            {
                AttendanceId = r.AttendanceId,
                EmployeeName = $"{employee.FirstName} {employee.LastName}",
                Date = r.WorkDate,
                CheckIn = r.CheckIn,
                CheckOut = r.CheckOut,
                Status = r.Status,
                TotalHours = r.TotalHours,
                IsManual = r.IsManual,
                NeedsOvertimeApproval = r.NeedsOvertimeApproval,
                ApprovedOvertimeHours = r.ApprovedOvertimeHours
            }).ToList();
        }

        private static double ToRad(double angle) => angle * Math.PI / 180;
    }
}