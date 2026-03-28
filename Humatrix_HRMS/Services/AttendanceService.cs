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
            var employee = await _context.Employees
                .Include(e => e.Shift)
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            if (employee == null) throw new Exception("Profile not found.");

            var today = DateTime.Now.Date;
            var exists = await _context.Attendances
                .FirstOrDefaultAsync(a => a.UserId == user.Id && a.Date == today);

            if (exists?.CheckIn != null) throw new Exception("Already checked in.");

            var attendance = exists ?? new Attendance
            {
                UserId = user.Id,
                EmployeeId = employee.EmployeeId,
                Date = today,
                OrganizationId = user.OrganizationId ?? Guid.Empty
            };

            attendance.CheckIn = DateTime.UtcNow;
            attendance.IsPresent = true;

            if (exists == null) _context.Attendances.Add(attendance);
            await _context.SaveChangesAsync();
        }

        public async Task CheckOutAsync()
        {
            var user = await _currentUser.GetUserAsync();
            var today = DateTime.Now.Date;

            var attendance = await _context.Attendances
                .FirstOrDefaultAsync(a => a.UserId == user.Id && a.Date == today);

            if (attendance == null || attendance.CheckIn == null)
                throw new Exception("You must check in first.");

            if (attendance.CheckOut != null)
                throw new Exception("Already checked out.");

            attendance.CheckOut = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task<List<AttendanceListDto>> GetAllAttendanceAsync(DateTime? date = null, Guid? departmentId = null, string? role = null)
        {
            var currentUser = await _currentUser.GetUserAsync();
            if (currentUser == null) throw new Exception("Unauthorized");

            var userRoles = await _userManager.GetRolesAsync(currentUser);
            bool isHR = userRoles.Contains("HR");
            bool isOrgAdmin = userRoles.Contains("OrgAdmin");

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
            var allDepartments = await _context.Departments.ToListAsync();

            foreach (var a in results)
            {
                var roles = await _userManager.GetRolesAsync(a.User);
                var displayRole = roles.FirstOrDefault() ?? "Employee";

                if (!string.IsNullOrEmpty(role) && displayRole != role) continue;

                list.Add(new AttendanceListDto
                {
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

        // Shared Helper for Status Logic
        private string GetStatus(Attendance a)
        {
            // 1. If today and no check-in yet
            if (a.Date.Date == DateTime.Today && a.CheckIn == null) return "Not Started";

            // 2. If it's a past date and no check-in (The Background Service marks this)
            if (a.CheckIn == null) return "Absent";

            // 3. Standard Shift Logic
            var shift = a.Employee?.Shift;
            if (shift == null) return "Present";

            var checkInLocal = a.CheckIn.Value.ToLocalTime().TimeOfDay;
            var lateThreshold = shift.StartTime.Add(TimeSpan.FromMinutes(shift.LateAllowanceMinutes));
            bool isLate = checkInLocal > lateThreshold;

            if (a.CheckOut != null)
            {
                var hours = (a.CheckOut.Value - a.CheckIn.Value).TotalHours;
                if (hours < shift.MinimumHoursForHalfDay) return "Short Hours";
                if (hours < shift.MinimumHoursForFullDay) return "Half Day";
                return isLate ? "Late" : "Present";
            }

            return isLate ? "Late (Active)" : "Checked In";
        }

        public async Task<Attendance?> GetTodayStatusAsync()
        {
            var user = await _currentUser.GetUserAsync();
            var today = DateTime.Now.Date;
            return await _context.Attendances.FirstOrDefaultAsync(a => a.UserId == user.Id && a.Date == today);
        }

        // UPDATED: Now returns DTOs and handles "Virtual" rows for today
        public async Task<List<AttendanceListDto>> GetMyAttendanceAsync()
        {
            var user = await _currentUser.GetUserAsync();
            var employee = await _context.Employees
                .Include(e => e.Shift)
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            // Get actual records from DB (includes the 'Absent' rows from background service)
            var dbRecords = await _context.Attendances
                .Include(a => a.Employee).ThenInclude(e => e.Shift)
                .Where(a => a.UserId == user.Id)
                .OrderByDescending(a => a.Date)
                .Take(30) // Show last 30 days
                .ToListAsync();

            var dtoList = new List<AttendanceListDto>();

            // Check if today exists. If not, add a 'Virtual' Not Started row
            if (!dbRecords.Any(r => r.Date == DateTime.Today))
            {
                dtoList.Add(new AttendanceListDto
                {
                    EmployeeName = $"{employee?.FirstName} {employee?.LastName}",
                    Date = DateTime.Today,
                    Status = "Not Started"
                });
            }

            foreach (var rec in dbRecords)
            {
                dtoList.Add(new AttendanceListDto
                {
                    Date = rec.Date,
                    CheckIn = rec.CheckIn,
                    CheckOut = rec.CheckOut,
                    Status = GetStatus(rec)
                });
            }

            return dtoList;
        }
    }
}