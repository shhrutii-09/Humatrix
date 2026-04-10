using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
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

            var userId = user.Id;
            var orgId = user.OrganizationId ?? Guid.Empty;

            if (orgId == Guid.Empty)
                throw new Exception("Organization not found");

            //var employee = await _context.Employees
            //    .FirstOrDefaultAsync(e => e.UserId == userId);

            var employee = await _context.Employees
    .Include(e => e.Shift)
    .FirstOrDefaultAsync(e => e.UserId == userId);

            if (employee == null)
                throw new Exception("Profile not found.");

            if (employee.Status != "Active")
                throw new Exception("Inactive employee cannot check in");

            // 🔥 GET OFFICE LOCATION
            var office = await _context.OfficeLocations
                .FirstOrDefaultAsync(x => x.OrganizationId == orgId);

            if (office == null)
                throw new Exception("Office location not set");

            // 🔥 CALCULATE DISTANCE
            double distance = GetDistance(
                latitude, longitude,
                office.Latitude, office.Longitude
            );

            // ❌ BLOCK IF OUTSIDE
            if (distance > office.RadiusInMeters)
                throw new Exception("You are not within office location");

            var today = DateTime.UtcNow.Date;

            var attendance = await _context.Attendances
                .FirstOrDefaultAsync(a => a.UserId == userId && a.Date == today);

            if (attendance?.CheckIn != null)
                throw new Exception("Already checked in.");

            if (attendance == null)
            {
                attendance = new Attendance
                {
                    AttendanceId = Guid.NewGuid(),
                    UserId = userId,
                    EmployeeId = employee.EmployeeId,
                    Date = today,
                    OrganizationId = orgId
                };

                _context.Attendances.Add(attendance);
            }

            // ✅ SAVE DATA
            attendance.CheckIn = DateTime.UtcNow;
            attendance.IsPresent = true;

            // 🔥 STATUS LOGIC ON CHECK-IN
            //var shift = employee.Shift;

            //if (shift != null)
            //{
            //    var checkInTime = attendance.CheckIn.Value.ToLocalTime().TimeOfDay;
            //    var lateTime = shift.StartTime.Add(TimeSpan.FromMinutes(shift.LateAllowanceMinutes));

            //    attendance.Status = checkInTime > lateTime ? "Late (Active)" : "Checked In";
            //}
            //else
            //{
            //    attendance.Status = "Checked In";
            //}

            var shift = employee.Shift;

            if (shift != null)
            {
                var shiftStart = shift.StartTime;
                var checkInTime = attendance.CheckIn.Value.ToLocalTime().TimeOfDay;

                // 🔥 handle overnight shift
                bool isNextDayShift = shift.EndTime < shift.StartTime;

                if (isNextDayShift)
                {
                    if (checkInTime < shift.EndTime)
                        checkInTime = checkInTime.Add(TimeSpan.FromHours(24));
                }

                var lateTime = shiftStart.Add(TimeSpan.FromMinutes(shift.LateAllowanceMinutes));

                bool isLate = checkInTime > lateTime;

                attendance.Status = isLate ? "Late (Active)" : "Checked In";
            }
            else
            {
                attendance.Status = "Checked In";
            }

            // 🔥 SAVE LOCATION
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

            var today = DateTime.UtcNow.Date;

            var attendance = await _context.Attendances
                .Include(a => a.Employee)
                .ThenInclude(e => e.Shift)
                .FirstOrDefaultAsync(a => a.UserId == user.Id && a.Date == today);

            if (attendance == null || attendance.CheckIn == null)
                throw new Exception("You must check in first.");

            if (attendance.CheckOut != null)
                throw new Exception("Already checked out.");

            // 🔥 GET OFFICE LOCATION
            var office = await _context.OfficeLocations
                .FirstOrDefaultAsync(x => x.OrganizationId == orgId);

            if (office == null)
                throw new Exception("Office location not set");

            // 🔥 VALIDATE LOCATION
            double distance = GetDistance(
                latitude, longitude,
                office.Latitude, office.Longitude
            );

            if (distance > office.RadiusInMeters)
                throw new Exception("You are not within office location");

            // ✅ SET CHECKOUT
            attendance.CheckOut = DateTime.UtcNow;

            // 🔥 CALCULATE HOURS
            var totalHours = (attendance.CheckOut.Value - attendance.CheckIn.Value).TotalHours;
            attendance.TotalHours = totalHours;

            var shift = attendance.Employee?.Shift;
            if (shift != null && totalHours > shift.MinimumHoursForFullDay)
            {
                attendance.OvertimeHours = totalHours - shift.MinimumHoursForFullDay;
            }
            else
            {
                attendance.OvertimeHours = 0;
            }

            // 🔥 FINAL STATUS LOGIC AFTER CHECKOUT
            if (shift != null)
            {
                if (totalHours < shift.MinimumHoursForHalfDay)
                    attendance.Status = "Short Hours";
                else if (totalHours < shift.MinimumHoursForFullDay)
                    attendance.Status = "Half Day";
                else
                    attendance.Status = attendance.Status.Contains("Late") ? "Late" : "Present";
            }
            else
            {
                attendance.Status = "Present";
            }

            // 🔥 UPDATE LOCATION AGAIN (optional but good)
            attendance.Latitude = latitude;
            attendance.Longitude = longitude;

            await _context.SaveChangesAsync();
        }

        private double GetDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371000; // meters
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);

            var a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c;
        }

        private double ToRadians(double angle)
        {
            return angle * Math.PI / 180;
        }

        // =========================
        // GET ALL ATTENDANCE
        // =========================
        public async Task<List<AttendanceListDto>> GetAllAttendanceAsync(
            DateTime? date = null,
            Guid? departmentId = null,
            string? role = null)
        {
            var currentUser = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = currentUser.OrganizationId;

            var query = _context.Attendances
                .AsNoTracking()
                .Include(a => a.User)
                .Include(a => a.Employee)
                .ThenInclude(e => e.Shift)
                .Where(a => a.OrganizationId == orgId);

            if (date.HasValue)
            {
                var d = date.Value.Date;
                query = query.Where(a => a.Date == d);
            }

            if (departmentId.HasValue)
            {
                query = query.Where(a => a.Employee.DepartmentId == departmentId);
            }

            var results = await query.ToListAsync();

            // ✅ Load departments once
            var departments = await _context.Departments
                .AsNoTracking()
                .ToDictionaryAsync(d => d.DepartmentId, d => d.Name);

            var list = new List<AttendanceListDto>();

            foreach (var a in results)
            {
                var roles = await _userManager.GetRolesAsync(a.User);
                var displayRole = roles.FirstOrDefault() ?? "Employee";

                if (!string.IsNullOrEmpty(role) && displayRole != role)
                    continue;

                var deptId = a.Employee?.DepartmentId;

                list.Add(new AttendanceListDto
                {
                    EmployeeName = a.Employee != null
                        ? $"{a.Employee.FirstName} {a.Employee.LastName}"
                        : $"{a.User.FirstName} {a.User.LastName}",

                    Email = a.User.Email,
                    Department = deptId != null && departments.ContainsKey(deptId.Value)
                        ? departments[deptId.Value]
                        : "N/A",

                    Role = displayRole,
                    Date = a.Date,
                    CheckIn = a.CheckIn,
                    CheckOut = a.CheckOut,
                    Status = a.Status
                });
            }

            return list;
        }

        // =========================
        // STATUS LOGIC
        // =========================
        //private string GetStatus(Attendance a)
        //{
        //    if (a.Date.Date == DateTime.Today && a.CheckIn == null)
        //        return "Not Started";

        //    if (a.CheckIn == null)
        //        return "Absent";

        //    var shift = a.Employee?.Shift;
        //    if (shift == null)
        //        return "Present";

        //    var checkInTime = a.CheckIn.Value.ToLocalTime().TimeOfDay;
        //    var lateTime = shift.StartTime.Add(TimeSpan.FromMinutes(shift.LateAllowanceMinutes));

        //    bool isLate = checkInTime > lateTime;

        //    if (a.CheckOut != null)
        //    {
        //        var hours = (a.CheckOut.Value - a.CheckIn.Value).TotalHours;

        //        if (hours < shift.MinimumHoursForHalfDay) return "Short Hours";
        //        if (hours < shift.MinimumHoursForFullDay) return "Half Day";

        //        return isLate ? "Late" : "Present";
        //    }

        //    return isLate ? "Late (Active)" : "Checked In";
        //}

        // =========================
        // TODAY STATUS
        // =========================
        public async Task<Attendance?> GetTodayStatusAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var today = DateTime.UtcNow.Date;

            return await _context.Attendances
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.UserId == user.Id && a.Date == today);
        }

        // =========================
        // MY ATTENDANCE
        // =========================
        public async Task<List<AttendanceListDto>> GetMyAttendanceAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .AsNoTracking()
                .Include(e => e.Shift)
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            var records = await _context.Attendances
                .AsNoTracking()
                .Include(a => a.Employee)
                .ThenInclude(e => e.Shift)
                .Where(a => a.UserId == user.Id)
                .OrderByDescending(a => a.Date)
                .Take(30)
                .ToListAsync();

            var list = new List<AttendanceListDto>();

            if (!records.Any(r => r.Date == DateTime.Today))
            {
                list.Add(new AttendanceListDto
                {
                    EmployeeName = $"{employee?.FirstName} {employee?.LastName}",
                    Date = DateTime.Today,
                    Status = "Not Started"
                });
            }

            foreach (var r in records)
            {
                list.Add(new AttendanceListDto
                {
                    Date = r.Date,
                    CheckIn = r.CheckIn,
                    CheckOut = r.CheckOut,
                    Status = r.Status
                });
            }

            return list;
        }
    }
}