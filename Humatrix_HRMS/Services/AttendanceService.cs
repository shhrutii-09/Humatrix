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
            var today = orgNow.Date;

            await _policy.AssertEmployeeCanActAsync(orgId, employee.EmployeeId, today, "CheckIn");

            var office = await _context.OfficeLocations
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.OrganizationId == orgId)
                ?? throw new Exception("Office location not configured. Contact HR.");

            double distance = GeoDistance(latitude, longitude, office.Latitude, office.Longitude);
            if (distance > office.RadiusInMeters)
                throw new Exception($"You are {(int)distance}m from the office. Check-in requires being within {office.RadiusInMeters}m.");

            var openSession = await _context.Attendances
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employee.EmployeeId &&
                    a.CheckIn != null &&
                    a.CheckOut == null);

            if (openSession != null)
            {
                throw new Exception(
                    "You have an open session from a previous shift. " +
                    "Please check out first or contact HR if you are unable to.");
            }

            var existing = await _context.Attendances
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employee.EmployeeId &&
                    a.WorkDate == today);

            if (existing?.CheckIn != null)
                throw new Exception("You have already checked in today.");

            var shift = employee.Shift;
            string initialStatus;

            if (shift != null)
            {
                var checkInLocalTime = orgNow.TimeOfDay;
                var lateThreshold = shift.StartTime.Add(TimeSpan.FromMinutes(shift.LateAllowanceMinutes));
                initialStatus = checkInLocalTime > lateThreshold ? AttendanceStatuses.Late : AttendanceStatuses.Present;
            }
            else
            {
                initialStatus = AttendanceStatuses.Present;
            }

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

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var attendance = await _context.Attendances
                    .Include(a => a.Employee)
                        .ThenInclude(e => e!.Shift)
                    .FirstOrDefaultAsync(a =>
                        a.EmployeeId == _context.Employees
                            .Where(e => e.UserId == user.Id)
                            .Select(e => e.EmployeeId)
                            .FirstOrDefault() &&
                        a.WorkDate == today);

                if (attendance == null || attendance.CheckIn == null)
                {
                    var empId = await _context.Employees
                        .Where(e => e.UserId == user.Id)
                        .Select(e => e.EmployeeId)
                        .FirstOrDefaultAsync();

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

                var office = await _context.OfficeLocations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.OrganizationId == orgId)
                    ?? throw new Exception("Office location not configured.");

                double distance = GeoDistance(latitude, longitude, office.Latitude, office.Longitude);
                if (distance > office.RadiusInMeters + GEO_BUFFER_CHECKOUT)
                    throw new Exception($"You are {(int)distance}m from the office. Check-out requires being within {office.RadiusInMeters + GEO_BUFFER_CHECKOUT}m.");

                // FIX: Provide local time to the calculation engine so durations match correctly
                var orgLocalNow = TimeHelper.ToOrgLocal(utcNow, tz);
                attendance.CheckOut = orgLocalNow;
                attendance.ActualCheckOut = orgLocalNow;
                attendance.IsAutoCheckedOut = false;
                attendance.Latitude = latitude;
                attendance.Longitude = longitude;
                attendance.IsManual = false;

                _calc.Recalculate(attendance, attendance.Employee?.Shift, tz);

                // FIX: Ensure it is saved back to the database strictly as a standardized UTC timestamp
                attendance.CheckOut = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
                attendance.ActualCheckOut = attendance.CheckOut;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<AttendanceListDto?> GetTodayStatusAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var org = await _context.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrganizationId == user.OrganizationId);

            if (org == null) return null;

            var tz = TimeHelper.GetOrgTimeZone(org.TimeZoneId);
            var today = TimeHelper.GetOrgDate(org.TimeZoneId);

            var employee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            if (employee == null) return null;

            var todayRecord = await _context.Attendances
                .AsNoTracking()
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employee.EmployeeId &&
                    a.WorkDate == today);

            var openSession = await _context.Attendances
                .AsNoTracking()
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employee.EmployeeId &&
                    a.CheckIn != null &&
                    a.CheckOut == null);

            var record = todayRecord ?? openSession;

            if (record == null) return null;

            return new AttendanceListDto
            {
                AttendanceId = record.AttendanceId,
                EmployeeName = $"{employee.FirstName} {employee.LastName}",
                Date = record.WorkDate,
                // ✅ RETURN UTC - let UI handle conversion
                CheckIn = record.CheckIn,  // Keep as UTC
                CheckOut = record.CheckOut,  // Keep as UTC
                SystemCheckOut = record.SystemCheckOut,
                OvertimeHours = record.OvertimeHours ?? 0,
                ApprovedOvertimeHours = record.ApprovedOvertimeHours,
                Status = record.Status,
                TotalHours = record.TotalHours,
                IsManual = record.IsManual,
                NeedsOvertimeApproval = record.NeedsOvertimeApproval
            };
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

            var org = await _context.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrganizationId == employee.OrganizationId);

            var tz = TimeHelper.GetOrgTimeZone(org?.TimeZoneId);

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
                // KEEP AS UTC - let UI handle conversion
                CheckIn = r.CheckIn,
                CheckOut = r.CheckOut,
                Status = r.Status,
                TotalHours = r.TotalHours,
                IsManual = r.IsManual,
                NeedsOvertimeApproval = r.NeedsOvertimeApproval,
                ApprovedOvertimeHours = r.ApprovedOvertimeHours
            }).ToList();
        }
        public async Task<List<AttendanceListDto>> GetMyAttendanceAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var org = await _context.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrganizationId == user.OrganizationId);

            if (org == null) return new List<AttendanceListDto>();

            var tz = TimeHelper.GetOrgTimeZone(org.TimeZoneId);
            var today = TimeHelper.GetOrgDate(org.TimeZoneId);

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

            var list = new List<AttendanceListDto>();

            foreach (var r in records)
            {
                list.Add(new AttendanceListDto
                {
                    AttendanceId = r.AttendanceId,
                    EmployeeName = $"{employee.FirstName} {employee.LastName}",
                    Date = r.WorkDate,
                    // FIX: Use organization timezone
                    CheckIn = r.CheckIn.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(r.CheckIn.Value, tz) : null,
                    CheckOut = r.CheckOut.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(r.CheckOut.Value, tz) : null,
                    Status = r.Status,
                    TotalHours = r.TotalHours,
                    IsManual = r.IsManual,
                    NeedsOvertimeApproval = r.NeedsOvertimeApproval,
                    ApprovedOvertimeHours = r.ApprovedOvertimeHours
                });
            }

            return list;
        }

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

            // Get current user's roles
            var userRoles = await _userManager.GetRolesAsync(currentUser);
            bool isOrgAdmin = userRoles.Contains("OrgAdmin");
            bool isHR = userRoles.Contains("HR");

            // Start with base query - include Department
            var employeesQuery = _context.Employees
                .Include(e => e.Department)  // IMPORTANT: Include Department
                .AsNoTracking()
                .Where(e => e.OrganizationId == orgId && e.Status == "Active");

            // If HR (not OrgAdmin), filter by their own department
            if (isHR && !isOrgAdmin)
            {
                var hrEmployee = await _context.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.UserId == currentUser.Id);

                if (hrEmployee != null && hrEmployee.DepartmentId != Guid.Empty)
                {
                    employeesQuery = employeesQuery.Where(e => e.DepartmentId == hrEmployee.DepartmentId);
                }
                else
                {
                    return new List<AttendanceListDto>();
                }
            }

            var employees = await employeesQuery.ToListAsync();

            var attendances = await _context.Attendances
                .AsNoTracking()
                .Where(a => a.OrganizationId == orgId && a.WorkDate == targetDate)
                .ToDictionaryAsync(a => a.EmployeeId);

            var list = new List<AttendanceListDto>();

            foreach (var emp in employees)
            {
                // Skip if HR is trying to see themselves
                if (isHR && !isOrgAdmin && emp.UserId == currentUser.Id)
                    continue;

                // Get user and roles for this employee
                var empUser = await _userManager.FindByIdAsync(emp.UserId);
                var empRoles = empUser != null ? await _userManager.GetRolesAsync(empUser) : new List<string>();
                var primaryRole = empRoles.FirstOrDefault(r => r == "Employee" || r == "HR") ?? "Employee";

                // Skip other HR users if current user is HR (non-OrgAdmin)
                if (isHR && !isOrgAdmin && primaryRole == "HR")
                    continue;

                if (attendances.TryGetValue(emp.EmployeeId, out var att))
                {
                    list.Add(new AttendanceListDto
                    {
                        AttendanceId = att.AttendanceId,
                        EmployeeName = $"{emp.FirstName} {emp.LastName}",
                        Email = empUser?.Email ?? "",
                        Role = primaryRole,
                        Department = emp.Department?.Name ?? "N/A",
                        DepartmentId = emp.DepartmentId,
                        Date = targetDate,
                        CheckIn = att.CheckIn,
                        CheckOut = att.CheckOut,
                        Status = att.Status,
                        TotalHours = att.TotalHours,
                        IsManual = att.IsManual,
                        NeedsOvertimeApproval = att.NeedsOvertimeApproval
                    });
                }
                else
                {
                    list.Add(new AttendanceListDto
                    {
                        EmployeeName = $"{emp.FirstName} {emp.LastName}",
                        Email = empUser?.Email ?? "",
                        Role = primaryRole,
                        Department = emp.Department?.Name ?? "N/A",
                        DepartmentId = emp.DepartmentId,
                        Date = targetDate,
                        Status = AttendanceStatuses.Absent
                    });
                }
            }

            // Apply additional filters
            if (departmentId.HasValue && departmentId != Guid.Empty)
            {
                list = list.Where(x => x.DepartmentId == departmentId).ToList();
            }

            if (!string.IsNullOrEmpty(role))
            {
                list = list.Where(x => x.Role == role).ToList();
            }

            return list.OrderBy(x => x.EmployeeName).ToList();
        }
        public async Task<string> ResolveDailyStatusAsync(Guid employeeId, DateTime date)
        {
            var att = await _context.Attendances
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.WorkDate == date);

            if (att != null) return att.Status ?? AttendanceStatuses.Present;

            return AttendanceStatuses.Absent;
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

        private static double GeoDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6_371_000;
            var dLat = ToRad(lat2 - lat1);
            var dLon = ToRad(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private static double ToRad(double angle) => angle * Math.PI / 180;
    }
}