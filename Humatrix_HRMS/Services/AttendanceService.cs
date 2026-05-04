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

        public AttendanceService(
            ApplicationDbContext context,
            CurrentUserService currentUser,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _currentUser = currentUser;
            _userManager = userManager;
        }

        // =========================
        // CHECK IN
        // =========================
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
                ?? throw new Exception("Profile not found.");

            if (employee.Status != "Active")
                throw new Exception("Inactive employee cannot check in");

            var org = await _context.Organizations
                .FirstOrDefaultAsync(o => o.OrganizationId == orgId)
                ?? throw new Exception("Organization not found");

            var tz = GetOrgTimezone(org.TimeZoneId);
            var today = TimeHelper.GetOrgDate(org.TimeZoneId);

            // 🚫 BLOCK invalid cases
            var isHoliday = await _context.Holidays.AnyAsync(h =>
                h.OrganizationId == orgId &&
                h.Date.Date == today &&
                !h.IsOptional);

            if (isHoliday)
                throw new Exception("Holiday - cannot check in");

            var isOnLeave = await _context.LeaveRequests.AnyAsync(l =>
                l.EmployeeId == employee.EmployeeId &&
                l.Status == "Approved" &&
                l.FromDate.Date <= today &&
                l.ToDate.Date >= today);

            if (isOnLeave)
                throw new Exception("You are on leave");

            var isWFH = await _context.WorkFromHomeRequests.AnyAsync(w =>
                w.EmployeeId == employee.EmployeeId &&
                w.Status == "Approved" &&
                w.Date.Date == today);

            if (isWFH)
                throw new Exception("You are on Work From Home");

            var office = await _context.OfficeLocations
                .FirstOrDefaultAsync(x => x.OrganizationId == orgId)
                ?? throw new Exception("Office location not set");

            double distance = GetDistance(latitude, longitude, office.Latitude, office.Longitude);
            if (distance > office.RadiusInMeters)
                throw new Exception("You are not within office location");

            var attendance = await _context.Attendances
                .FirstOrDefaultAsync(a => a.UserId == user.Id && a.WorkDate == today);

            if (attendance?.CheckIn != null)
                throw new Exception("Already checked in.");

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

            var shift = employee.Shift;

            if (shift != null)
            {
                var checkInLocal = TimeZoneInfo.ConvertTimeFromUtc(attendance.CheckIn.Value, tz);
                var checkInTime = checkInLocal.TimeOfDay;

                bool isNextDayShift = shift.EndTime < shift.StartTime;
                if (isNextDayShift && checkInTime < shift.EndTime)
                    checkInTime = checkInTime.Add(TimeSpan.FromHours(24));

                var lateTime = shift.StartTime.Add(TimeSpan.FromMinutes(shift.LateAllowanceMinutes));
                bool isLate = checkInTime > lateTime;

                // ✅ NO "CheckedIn"
                attendance.Status = isLate
                    ? AttendanceStatuses.Late
                    : AttendanceStatuses.Present;
            }
            else
            {
                attendance.Status = AttendanceStatuses.Present;
            }

            attendance.Latitude = latitude;
            attendance.Longitude = longitude;

            await _context.SaveChangesAsync();
        }


        // =========================
        // CHECK OUT
        // =========================
        public async Task CheckOutAsync(double latitude, double longitude)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = user.OrganizationId ?? Guid.Empty;

            var org = await _context.Organizations
                .FirstOrDefaultAsync(o => o.OrganizationId == orgId)
                ?? throw new Exception("Organization not found");

            var today = TimeHelper.GetOrgDate(org.TimeZoneId);

            var attendance = await _context.Attendances
                .Include(a => a.Employee)
                    .ThenInclude(e => e.Shift)
                .FirstOrDefaultAsync(a => a.UserId == user.Id && a.WorkDate == today);

            if (attendance == null || attendance.CheckIn == null)
                throw new Exception("You must check in first.");

            if (attendance.CheckOut != null)
                throw new Exception("Already checked out.");

            var office = await _context.OfficeLocations
                .FirstOrDefaultAsync(x => x.OrganizationId == orgId)
                ?? throw new Exception("Office location not set");

            double distance = GetDistance(latitude, longitude, office.Latitude, office.Longitude);
            if (distance > office.RadiusInMeters)
                throw new Exception("You are not within office location");

            attendance.CheckOut = DateTime.UtcNow;
            attendance.IsPresent = true;

            var totalHours = (attendance.CheckOut.Value - attendance.CheckIn.Value).TotalHours;
            attendance.TotalHours = totalHours;

            var shift = attendance.Employee?.Shift;

            if (shift != null)
            {
                // ✅ Overtime
                if (totalHours > shift.MinimumHoursForFullDay)
                {
                    attendance.OvertimeHours = totalHours - shift.MinimumHoursForFullDay;
                    attendance.NeedsOvertimeApproval = true;
                }
                else
                {
                    attendance.OvertimeHours = 0;
                    attendance.NeedsOvertimeApproval = false;
                }

                // ✅ Final status
                if (totalHours < shift.MinimumHoursForHalfDay)
                    attendance.Status = AttendanceStatuses.ShortHours;
                else if (totalHours < shift.MinimumHoursForFullDay)
                    attendance.Status = AttendanceStatuses.HalfDay;
                else
                    attendance.Status = attendance.Status == AttendanceStatuses.Late
                        ? AttendanceStatuses.Late
                        : AttendanceStatuses.Present;
            }
            else
            {
                attendance.Status = AttendanceStatuses.Present;
                attendance.OvertimeHours = 0;
                attendance.NeedsOvertimeApproval = false;
            }

            attendance.IsManual = false;
            attendance.Latitude = latitude;
            attendance.Longitude = longitude;

            await _context.SaveChangesAsync();
        }


        // =========================
        // GET TODAY STATUS
        // =========================
        public async Task<Attendance?> GetTodayStatusAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var org = await _context.Organizations
                .FirstOrDefaultAsync(o => o.OrganizationId == user.OrganizationId);

            //var today = org != null
            //    ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetOrgTimezone(org.TimeZoneId)).Date
            //    : DateTime.UtcNow.Date;
            var today = TimeHelper.GetOrgDate(org?.TimeZoneId);

            return await _context.Attendances
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.UserId == user.Id && a.WorkDate == today);
        }

        // =========================
        // MY ATTENDANCE
        // =========================
        public async Task<List<AttendanceListDto>> GetMyAttendanceAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var org = await _context.Organizations
                .FirstOrDefaultAsync(o => o.OrganizationId == user.OrganizationId);

            //var today = org != null
            //    ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetOrgTimezone(org.TimeZoneId)).Date
            //    : DateTime.UtcNow.Date;
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

            var list = new List<AttendanceListDto>();

            if (!records.Any(r => r.WorkDate == today))
            {
                list.Add(new AttendanceListDto
                {
                    EmployeeName = $"{employee?.FirstName} {employee?.LastName}",
                    Date = today.Date,
                    Status = "Not Started"
                });
            }

            foreach (var r in records)
            {
                list.Add(new AttendanceListDto
                {
                    Date = r.WorkDate,
                    CheckIn = r.CheckIn,
                    CheckOut = r.CheckOut,
                    Status = r.Status
                });
            }

            return list;
        }

        public async Task<List<AttendanceListDto>> GetAllAttendanceAsync(
       DateTime? date = null,
       Guid? departmentId = null,
       string? role = null)
        {
            var currentUser = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = currentUser.OrganizationId ?? Guid.Empty;

            // ✅ Get employee of current user (for HR filtering)
            var currentEmployee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == currentUser.Id);

            // =========================
            // ROLE FILTER
            // =========================
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

            // =========================
            // BASE QUERY
            // =========================
            var query = _context.Attendances
                .AsNoTracking()
                .Include(a => a.User)
                .Include(a => a.Employee)
                .Where(a => a.OrganizationId == orgId);

            // =========================
            // DATE FILTER
            // =========================
            if (date.HasValue)
            {
                var targetDate = date.Value.Date;

                query = query.Where(a =>
                    a.WorkDate >= targetDate &&
                    a.WorkDate < targetDate.AddDays(1));
            }

            // =========================
            // HR RESTRICTION
            // =========================
            bool isHR = await _userManager.IsInRoleAsync(currentUser, "HR");
            bool isOrgAdmin = await _userManager.IsInRoleAsync(currentUser, "OrgAdmin");

            if (isHR && !isOrgAdmin)
            {
                if (currentEmployee?.DepartmentId != null)
                {
                    query = query.Where(a =>
                        a.Employee != null &&
                        a.Employee.DepartmentId == currentEmployee.DepartmentId);
                }
                else
                {
                    return new List<AttendanceListDto>();
                }
            }

            // =========================
            // DEPARTMENT FILTER
            // =========================
            if (departmentId.HasValue)
            {
                query = query.Where(a =>
                    a.Employee != null &&
                    a.Employee.DepartmentId == departmentId);
            }

            // =========================
            // ROLE FILTER APPLY
            // =========================
            if (filteredUserIds != null)
            {
                query = query.Where(a => filteredUserIds.Contains(a.UserId));
            }

            var results = await query.ToListAsync();

            // =========================
            // DEPARTMENTS MAP
            // =========================
            var departments = await _context.Departments
                .AsNoTracking()
                .ToDictionaryAsync(d => d.DepartmentId, d => d.Name);

            // =========================
            // USER ROLE MAP
            // =========================
            var userIds = results.Select(a => a.UserId).Distinct().ToList();

            var users = await _userManager.Users
                .Where(u => userIds.Contains(u.Id))
                .ToListAsync();

            var userRoleMap = new Dictionary<string, string>();

            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                userRoleMap[u.Id] = roles.FirstOrDefault() ?? "Employee";
            }

            // =========================
            // FINAL MAPPING
            // =========================
            var list = new List<AttendanceListDto>();

            foreach (var a in results)
            {
                var deptId = a.Employee?.DepartmentId;

                list.Add(new AttendanceListDto
                {
                    EmployeeName = a.Employee != null
                        ? $"{a.Employee.FirstName} {a.Employee.LastName}"
                        : $"{a.User.FirstName} {a.User.LastName}",

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

        // =========================
        // HELPERS
        // =========================
        private TimeZoneInfo GetOrgTimezone(string timeZoneId)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
            catch { return TimeZoneInfo.Utc; }
        }

        private double GetDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000;

            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);

            var a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private double ToRadians(double angle) => angle * Math.PI / 180;


        public async Task<string> ResolveDailyStatus(Guid employeeId, DateTime date)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = user.OrganizationId ?? Guid.Empty;

            // 1. Holiday
            var isHoliday = await _context.Holidays.AnyAsync(h =>
                h.OrganizationId == orgId &&
                h.Date.Date == date.Date &&
                !h.IsOptional);

            if (isHoliday)
                return "Holiday";

            // 2. Leave
            var leave = await _context.LeaveRequests
                .Include(l => l.LeaveType)
                .FirstOrDefaultAsync(l =>
                    l.EmployeeId == employeeId &&
                    l.Status == "Approved" &&
                    l.FromDate.Date <= date &&
                    l.ToDate.Date >= date);

            if (leave != null)
                return $"Leave ({leave.LeaveType.Name})";

            // 3. Work From Home
            var wfh = await _context.WorkFromHomeRequests
                .FirstOrDefaultAsync(w =>
                    w.EmployeeId == employeeId &&
                    w.Status == "Approved" &&
                    w.Date.Date == date);

            if (wfh != null)
                return AttendanceStatuses.WorkFromHome;

            // 4. Attendance
            var att = await _context.Attendances
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employeeId &&
                    a.WorkDate == date);

            if (att != null)
                return att.Status;

            // 5. Not Started / Absent
            var org = await _context.Organizations
                .FirstOrDefaultAsync(o => o.OrganizationId == orgId);

            var today = TimeHelper.GetOrgDate(org?.TimeZoneId);

            if (date.Date == today)
                return "Not Started";

            return AttendanceStatuses.Absent;
        }
    }
}