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
        public async Task CheckInAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var userId = user.Id;
            var orgId = user.OrganizationId ?? Guid.Empty;

            var employee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == userId);

            if (employee == null)
                throw new Exception("Profile not found.");

            var today = DateTime.UtcNow.Date;

            var attendance = await _context.Attendances
                .FirstOrDefaultAsync(a => a.UserId == userId && a.Date == today);

            if (attendance?.CheckIn != null)
                throw new Exception("Already checked in.");

            if (attendance == null)
            {
                attendance = new Attendance
                {
                    UserId = userId,
                    EmployeeId = employee.EmployeeId,
                    Date = today,
                    OrganizationId = orgId
                };

                _context.Attendances.Add(attendance);
            }

            attendance.CheckIn = DateTime.UtcNow;
            attendance.IsPresent = true;

            await _context.SaveChangesAsync();
        }

        // =========================
        // CHECK OUT
        // =========================
        public async Task CheckOutAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var today = DateTime.UtcNow.Date;

            var attendance = await _context.Attendances
                .FirstOrDefaultAsync(a => a.UserId == user.Id && a.Date == today);

            if (attendance == null || attendance.CheckIn == null)
                throw new Exception("You must check in first.");

            if (attendance.CheckOut != null)
                throw new Exception("Already checked out.");

            attendance.CheckOut = DateTime.UtcNow;

            await _context.SaveChangesAsync();
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
                    Status = GetStatus(a)
                });
            }

            return list;
        }

        // =========================
        // STATUS LOGIC
        // =========================
        private string GetStatus(Attendance a)
        {
            if (a.Date.Date == DateTime.Today && a.CheckIn == null)
                return "Not Started";

            if (a.CheckIn == null)
                return "Absent";

            var shift = a.Employee?.Shift;
            if (shift == null)
                return "Present";

            var checkInTime = a.CheckIn.Value.ToLocalTime().TimeOfDay;
            var lateTime = shift.StartTime.Add(TimeSpan.FromMinutes(shift.LateAllowanceMinutes));

            bool isLate = checkInTime > lateTime;

            if (a.CheckOut != null)
            {
                var hours = (a.CheckOut.Value - a.CheckIn.Value).TotalHours;

                if (hours < shift.MinimumHoursForHalfDay) return "Short Hours";
                if (hours < shift.MinimumHoursForFullDay) return "Half Day";

                return isLate ? "Late" : "Present";
            }

            return isLate ? "Late (Active)" : "Checked In";
        }

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
                    Status = GetStatus(r)
                });
            }

            return list;
        }
    }
}