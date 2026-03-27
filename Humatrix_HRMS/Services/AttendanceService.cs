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

        public AttendanceService(ApplicationDbContext context, CurrentUserService currentUser, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _currentUser = currentUser;
            _userManager = userManager;
        }

        public async Task CheckInAsync()
        {
            var user = await _currentUser.GetUserAsync();

        //    var employee = await _context.Employees
        //.FirstOrDefaultAsync(e => e.UserId == user.Id);

            var employee = await _context.Employees
        .FirstOrDefaultAsync(e => e.UserId == user.Id);

            if (employee == null)
                throw new Exception("Employee profile not found. Cannot check in.");

            //var today = DateTime.UtcNow.Date;
            var today = DateTime.Now.Date;

            var exists = await _context.Attendances
                .FirstOrDefaultAsync(a => a.UserId == user.Id && a.Date == today);

            if (exists != null && exists.CheckIn != null)
                throw new Exception("Already checked in today.");

            if (exists == null)
            {
                var attendance = new Attendance
                {
                    UserId = user.Id,
                    EmployeeId = employee.EmployeeId,
                    Date = today,
                    //CheckIn = DateTime.UtcNow,
                    CheckIn = DateTime.Now,
                    IsPresent = true,
                    OrganizationId = user.OrganizationId ?? Guid.Empty
                };
                _context.Attendances.Add(attendance);
            }
            else
            {
                exists.EmployeeId = employee.EmployeeId;
                exists.CheckIn = DateTime.Now;
                exists.IsPresent = true;
            }

            await _context.SaveChangesAsync();
        }

        public async Task CheckOutAsync()
        {
            var user = await _currentUser.GetUserAsync();
            //var today = DateTime.UtcNow.Date;
            var today = DateTime.Now.Date;

            //var attendance = await _context.Attendances
            //    .FirstOrDefaultAsync(a => a.UserId == user.Id && a.Date == today);
            var attendance = await _context.Attendances
    .FirstOrDefaultAsync(a =>
        a.UserId == user.Id &&
        a.Date == today &&
        a.OrganizationId == user.OrganizationId);

            if (attendance == null || attendance.CheckIn == null)
                throw new Exception("You must check in first.");

            if (attendance.CheckOut != null)
                throw new Exception("Already checked out.");

            //attendance.CheckOut = DateTime.UtcNow;
            attendance.CheckOut = DateTime.Now;
            await _context.SaveChangesAsync();
        }

        public async Task<List<AttendanceListDto>> GetAllAttendanceAsync(DateTime? date = null, Guid? departmentId = null, string? role = null)
        {
            var currentUser = await _currentUser.GetUserAsync();
            if (currentUser == null) throw new Exception("Unauthorized");

            var userRoles = await _userManager.GetRolesAsync(currentUser);
            bool isHR = userRoles.Contains("HR");
            bool isOrgAdmin = userRoles.Contains("OrgAdmin");

            // ✅ Optimized Query: Include both Identity User and Employee Profile
            var query = _context.Attendances
                .Include(a => a.User)
                .Include(a => a.Employee)
                .ThenInclude(e => e.Shift)
                .Where(a => a.OrganizationId == currentUser.OrganizationId);

            if (date.HasValue)
            {
                var d = date.Value.Date;
                query = query.Where(a => a.Date == d);
            }

            // Security Boundaries
            if (isHR && !isOrgAdmin)
            {
                query = query.Where(a => a.Employee.DepartmentId == currentUser.DepartmentId || a.UserId == currentUser.Id);
            }

            if (departmentId.HasValue)
            {
                query = query.Where(a => a.Employee.DepartmentId == departmentId);
            }

            var results = await query.ToListAsync();
            var list = new List<AttendanceListDto>();

            // Pre-fetch departments to avoid "N+1" query issues in the loop
            var allDepartments = await _context.Departments.ToListAsync();
    //        var deptMap = await _context.Departments
    //.ToDictionaryAsync(d => d.DepartmentId, d => d.Name);
    //        Department = deptMap.ContainsKey(a.Employee?.DepartmentId ?? Guid.Empty)
    //? deptMap[a.Employee.DepartmentId.Value]
    //: "N/A";


            var users = results.Select(a => a.User).Distinct().ToList();
            var roleMap = new Dictionary<string, string>();
            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                roleMap[u.Id] = roles.FirstOrDefault() ?? "Employee";
            }



            foreach (var a in results)
            {
                var roles = await _userManager.GetRolesAsync(a.User);
                var displayRole = roles.FirstOrDefault() ?? "Employee";

                if (!string.IsNullOrEmpty(role) && displayRole != role) continue;

                list.Add(new AttendanceListDto
                {
                    // ✅ Pull name from Employee table; fallback to User table if profile missing
                    EmployeeName = a.Employee != null
                        ? $"{a.Employee.FirstName} {a.Employee.LastName}"
                        : $"{a.User.FirstName} {a.User.LastName}",
                    Email = a.User.Email,
                    Department = allDepartments.FirstOrDefault(d => d.DepartmentId == a.Employee?.DepartmentId)?.Name ?? "N/A",
                    Role = displayRole,
                    Date = a.Date,
                    CheckIn = a.CheckIn,
                    CheckOut = a.CheckOut,
                    Status = GetStatus(a)
                });
            }

            return list;
        }

        //private string GetStatus(Attendance a)
        //{
        //    if (a.CheckIn == null) return "Absent";

        //    // Note: Ensure your server/app handles local time vs UTC consistently
        //    var checkInTime = a.CheckIn.Value.ToLocalTime().TimeOfDay;
        //    if (checkInTime > new TimeSpan(10, 30, 0)) return "Late";

        //    if (a.CheckOut != null)
        //    {
        //        var hours = (a.CheckOut.Value - a.CheckIn.Value).TotalHours;
        //        if (hours < 4) return "Half Day";
        //    }
        //    return "Present";
        //}

        private string GetStatus(Attendance a)
        {
            if (a.CheckIn == null) return "Absent";

            var shift = a.Employee?.Shift;

            if (shift == null)
            {
                return a.CheckOut != null ? "Present" : "Checked In";
            }

            var checkInTime = a.CheckIn.Value.TimeOfDay;
            var lateThreshold = shift.StartTime.Add(TimeSpan.FromMinutes(shift.LateAllowanceMinutes));
            bool isLate = checkInTime > lateThreshold;

            if (a.CheckOut != null)
            {
                var hoursWorked = (a.CheckOut.Value - a.CheckIn.Value).TotalHours;

                if (hoursWorked < shift.MinimumHoursForHalfDay) return "Short Hours";
                if (hoursWorked < shift.MinimumHoursForFullDay) return "Half Day";

                return isLate ? "Late" : "Present";
            }

            // If they haven't checked out yet, show their morning status
            return isLate ? "Late (Active)" : "Checked In";
        }

        public async Task<Attendance?> GetTodayStatusAsync()
        {
            var user = await _currentUser.GetUserAsync();
            //var today = DateTime.UtcNow.Date;
            var today = DateTime.Now.Date;
            return await _context.Attendances.FirstOrDefaultAsync(a => a.UserId == user.Id && a.Date == today);
        }

        public async Task<List<Attendance>> GetMyAttendanceAsync()
        {
            var user = await _currentUser.GetUserAsync();
            return await _context.Attendances
                .Where(a => a.UserId == user.Id)
                .OrderByDescending(a => a.Date)
                .ToListAsync();
        }
    }
}