using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Configuration;

namespace Humatrix_HRMS.Services
{
    public class EmployeeService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly CurrentUserService _currentUser;
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public EmployeeService(
            UserManager<ApplicationUser> userManager,
            CurrentUserService currentUser,
                IDbContextFactory<ApplicationDbContext> contextFactory,
            ApplicationDbContext context,
            IConfiguration config)
        {
            _userManager = userManager;
            _currentUser = currentUser;
            _context = context;
            _contextFactory = contextFactory;
            _config = config;
        }

        #region Dashboard Data

        public async Task<EmployeeDashboardDto?> GetEmployeeDashboardDataAsync()
        {
            var currentUser = await _currentUser.GetUserAsync();
            if (currentUser == null) return null;

            var userId = currentUser.Id;

            // We use a Left Join approach. 
            // If the record isn't in the 'Employees' table, the LINQ FirstOrDefault will return null.
            var dashboardData = await (
                from e in _context.Employees
                where e.UserId == userId

                join d in _context.Departments on e.DepartmentId equals d.DepartmentId into dept
                from d in dept.DefaultIfEmpty()

                join des in _context.Designations on e.DesignationId equals des.DesignationId into desg
                from des in desg.DefaultIfEmpty()

                join s in _context.Shifts on e.ShiftId equals s.ShiftId into sh
                from s in sh.DefaultIfEmpty()

                select new EmployeeDashboardDto
                {
                    FullName = currentUser.FirstName + " " + currentUser.LastName,
                    Email = currentUser.Email ?? "",
                    EmployeeCode = e.EmployeeCode ?? "PENDING",
                    JoiningDate = e.JoiningDate,
                    Status = e.Status ?? "Active",
                    DepartmentName = d != null ? d.Name : "N/A",
                    DesignationName = des != null ? des.Name : "N/A",
                    ShiftName = s != null ? s.Name : "No Shift Assigned"
                }
            ).AsNoTracking().FirstOrDefaultAsync();

            // If dashboardData is null, it means there is no entry in the 'Employees' table for this UserID.
            return dashboardData;
        }

        #endregion

        #region Employee Management

        public async Task<EmployeeListDto?> GetEmployeeByEmailAsync(string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) return null;

            var currentUser = await _currentUser.GetUserAsync();


            var profile = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            var roles = await _userManager.GetRolesAsync(user);

            //var dept = await _context.Departments
            //    .AsNoTracking()
            //    .FirstOrDefaultAsync(d => d.DepartmentId == user.DepartmentId);

            var dept = await _context.Departments
    .FirstOrDefaultAsync(d =>
        d.DepartmentId == user.DepartmentId &&
        d.OrganizationId == currentUser.OrganizationId);


            //var desig = await _context.Designations
            //    .AsNoTracking()
            //    .FirstOrDefaultAsync(d => d.DesignationId == user.DesignationId);

            var desig = await _context.Designations
                .FirstOrDefaultAsync(d =>
                    d.DesignationId == user.DesignationId &&
                    d.OrganizationId == currentUser.OrganizationId);


            //var shift = await _context.Shifts
            //    .AsNoTracking()
            //    .FirstOrDefaultAsync(s => s.ShiftId == profile?.ShiftId);

            var shiftId = profile?.ShiftId;

            var shift = shiftId == null
                ? null
                : await _context.Shifts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.ShiftId == shiftId);

            return new EmployeeListDto
            {
                Email = user.Email ?? string.Empty,
                FirstName = user.FirstName ?? string.Empty,
                LastName = user.LastName ?? string.Empty,
                Name = $"{user.FirstName} {user.LastName}",
                Role = roles.FirstOrDefault() ?? "Employee",
                Department = dept?.Name ?? "N/A",
                Designation = desig?.Name ?? "N/A",
                DepartmentId = user.DepartmentId,
                DesignationId = user.DesignationId,
                ShiftId = profile?.ShiftId,
                ShiftName = shift?.Name ?? "No Shift",
                IsActive = user.IsActive
            };
        }

        public async Task UpdateEmployeeAsync(string email, EditEmployeeDto dto)
        {

            if (dto.DepartmentId == null)
                throw new Exception("Department required");

            if (dto.DesignationId == null)
                throw new Exception("Designation required");

            var currentUser = await _currentUser.GetUserAsync();

            var validCombo = await _context.Designations.AnyAsync(d =>
                d.DesignationId == dto.DesignationId &&
                d.DepartmentId == dto.DepartmentId &&
                d.OrganizationId == currentUser.OrganizationId);

            if (!validCombo)
                throw new Exception("Designation does not belong to selected department");

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) throw new Exception("User not found");

            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
            user.DepartmentId = dto.DepartmentId;
            user.DesignationId = dto.DesignationId;

            await _userManager.UpdateAsync(user);

            var profile = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            if (profile != null)
            {
                profile.FirstName = dto.FirstName;
                profile.LastName = dto.LastName;
                profile.DepartmentId = dto.DepartmentId ?? Guid.Empty;
                profile.DesignationId = dto.DesignationId ?? Guid.Empty;
                profile.ShiftId = dto.ShiftId;

                await _context.SaveChangesAsync();
            }
        }

        public async Task ToggleUserStatusAsync(string email, bool isActive)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) throw new Exception("User not found");

            user.IsActive = isActive;
            await _userManager.UpdateAsync(user);

            var profile = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            if (profile != null)
            {
                profile.Status = isActive ? "Active" : "Inactive";
                await _context.SaveChangesAsync();
            }
        }

        #endregion

        #region List Retrieval

        public async Task<(List<EmployeeListDto> Items, int TotalCount)> GetEmployeesForListAsync(
     string? search = null,
     Guid? departmentId = null,
     int pageNumber = 1,
     int pageSize = 10)
        {
            var currentUser = await _currentUser.GetUserAsync();
            if (currentUser?.OrganizationId == null) return new();

            // 1. Identify current user's role
            var currentUserRoles = await _userManager.GetRolesAsync(currentUser);
            bool isOrgAdmin = currentUserRoles.Contains("OrgAdmin");
            bool isHR = currentUserRoles.Contains("HR");

            // 2. Fetch all users in organization (excluding self)
            var usersQuery = _userManager.Users
                .Where(u => u.OrganizationId == currentUser.OrganizationId && u.Id != currentUser.Id);


            // 🔒 SECURITY FIX: If user is HR (and NOT OrgAdmin), restrict to their department
            if (isHR && !isOrgAdmin)
            {
                usersQuery = usersQuery.Where(u => u.DepartmentId == currentUser.DepartmentId);
            }

            var users = await usersQuery.ToListAsync();

            // 3. Prepare metadata (Departments, Designations, etc.)
            var depts = await _context.Departments
                .Where(d => d.OrganizationId == currentUser.OrganizationId)
                .AsNoTracking().ToListAsync();

            var desigs = await _context.Designations
                .Where(d => d.OrganizationId == currentUser.OrganizationId)
                .AsNoTracking().ToListAsync();

            var shifts = await _context.Shifts
        .Where(s => s.OrganizationId == currentUser.OrganizationId)
        .AsNoTracking().ToListAsync();

            var profiles = await _context.Employees
                .Where(e => e.OrganizationId == currentUser.OrganizationId)
                .AsNoTracking().ToListAsync();

            var list = new List<EmployeeListDto>();

            foreach (var u in users)
            {
                var userRoles = await _userManager.GetRolesAsync(u);
                var role = userRoles.FirstOrDefault() ?? "Employee";

                // HR should not see/manage OrgAdmins or other HRs usually, 
                // but even if they do, they are now restricted by DepartmentId above.
                if (isHR && !isOrgAdmin && (role == "HR" || role == "OrgAdmin"))
                    continue;

                var profile = profiles.FirstOrDefault(p => p.UserId == u.Id);

                list.Add(new EmployeeListDto
                {
                    EmployeeId = profile?.EmployeeId ?? Guid.Empty,
                    Email = u.Email ?? string.Empty,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    Name = $"{u.FirstName} {u.LastName}",
                    Role = role,
                    Department = depts.FirstOrDefault(d => d.DepartmentId == u.DepartmentId)?.Name ?? "N/A",
                    Designation = desigs.FirstOrDefault(d => d.DesignationId == u.DesignationId)?.Name ?? "N/A",
                    DepartmentId = u.DepartmentId,
                    DesignationId = u.DesignationId,
                    ShiftId = profile?.ShiftId,
                    ShiftName = shifts.FirstOrDefault(s => s.ShiftId == profile?.ShiftId)?.Name ?? "No Shift Assigned",
                    IsActive = u.IsActive
                });
            }

            // Apply Search and Manual Filters
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.ToLower();
                list = list.Where(x => x.Name.ToLower().Contains(search) || x.Email.ToLower().Contains(search)).ToList();
            }

            if (departmentId.HasValue)
            {
                list = list.Where(x => x.DepartmentId == departmentId).ToList();
            }

            int totalCount = list.Count;
            var items = list
                .OrderBy(x => x.Name)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return (items, totalCount);

            //return list.OrderBy(x => x.Name).ToList();
        }
        #endregion

        #region Creation

        public async Task<string> CreateEmployeeAsync(CreateEmployeeDto dto)
        {
            var currentUser = await _currentUser.GetUserAsync();
            if (currentUser?.OrganizationId == null)
                throw new Exception("Unauthorized");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var existingUser = await _userManager.FindByEmailAsync(dto.Email);
                if (existingUser != null)
                    throw new Exception("User already exists");

                if (string.IsNullOrWhiteSpace(dto.Email))
                    throw new Exception("Email is required");

                var deptExists = await _context.Departments.AnyAsync(d =>
                    d.DepartmentId == dto.DepartmentId &&
                    d.OrganizationId == currentUser.OrganizationId);

                if (!deptExists)
                    throw new Exception("Invalid department");

                var desigExists = await _context.Designations.AnyAsync(d =>
                    d.DesignationId == dto.DesignationId &&
                    d.OrganizationId == currentUser.OrganizationId);

                if (!desigExists)
                    throw new Exception("Invalid designation");

                var designation = await _context.Designations
.FirstOrDefaultAsync(d =>
   d.DesignationId == dto.DesignationId &&
   d.OrganizationId == currentUser.OrganizationId &&
   d.IsActive); // ✅ IMPORTANT

                if (designation == null)
                    throw new Exception("Selected designation is inactive or invalid");


                var user = new ApplicationUser
                {
                    UserName = dto.Email,
                    Email = dto.Email,
                    FirstName = dto.FirstName,
                    LastName = dto.LastName,
                    OrganizationId = currentUser.OrganizationId,
                    DepartmentId = dto.DepartmentId,
                    DesignationId = dto.DesignationId,
                    EmailConfirmed = true,
                    IsActive = true
                };

                var result = await _userManager.CreateAsync(user);
                if (!result.Succeeded)
                    throw new Exception(string.Join(", ", result.Errors.Select(x => x.Description)));

                await _userManager.AddToRoleAsync(user, dto.Role ?? "Employee");

                var employee = new Employee
                {
                    UserId = user.Id,
                    OrganizationId = currentUser.OrganizationId.Value,
                    FirstName = dto.FirstName,
                    LastName = dto.LastName,
                    DepartmentId = dto.DepartmentId ?? Guid.Empty,
                    DesignationId = dto.DesignationId ?? Guid.Empty,
                    ShiftId = dto.ShiftId,
                    EmployeeCode = await GenerateEmployeeCodeAsync(),
                    JoiningDate = DateTime.UtcNow,
                    Status = "Active"
                };
           


                    

                _context.Employees.Add(employee);
                await _context.SaveChangesAsync();

                var token = await _userManager.GeneratePasswordResetTokenAsync(user);

                var invite = new UserInvite
                {
                    Email = dto.Email,
                    UserId = user.Id,
                    Token = token,
                    Role = dto.Role ?? "Employee",
                    OrganizationId = currentUser.OrganizationId,
                    IsUsed = false,
                    CreatedAt = DateTime.UtcNow
                };

                _context.UserInvites.Add(invite);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                var baseUrl = _config["AppBaseUrl"] ?? "https://localhost:7057";
                return $"{baseUrl}/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task<string> GenerateEmployeeCodeAsync()
        {
            var count = await _context.Employees.CountAsync();
            return $"EMP{(count + 1).ToString("D4")}";
        }

        #endregion

        public async Task<EmployeeDetailsDto?> GetEmployeeDetailsByIdAsync(Guid employeeId)
        {
            if (employeeId == Guid.Empty)
                return null;

            using var context = await _contextFactory.CreateDbContextAsync();

            var result = await (
                from e in context.Employees
                where e.EmployeeId == employeeId

                join d in context.Departments on e.DepartmentId equals d.DepartmentId into dept
                from d in dept.DefaultIfEmpty()

                join des in context.Designations on e.DesignationId equals des.DesignationId into desg
                from des in desg.DefaultIfEmpty()

                join s in context.Shifts on e.ShiftId equals s.ShiftId into sh
                from s in sh.DefaultIfEmpty()

                select new
                {
                    Employee = e,
                    Department = d,
                    Designation = des,
                    Shift = s
                }
            ).AsNoTracking().FirstOrDefaultAsync();

            if (result == null)
                return null;

            var user = await _userManager.FindByIdAsync(result.Employee.UserId);
            if (user == null)
                return null;

            // 🔥 Attendance calculation (CURRENT MONTH)
            var startDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var endDate = startDate.AddMonths(1);

            var attendance = await context.Attendances
                .Where(a => a.EmployeeId == employeeId &&
                            a.Date >= startDate &&
                            a.Date < endDate)
                .ToListAsync();

            var totalDays = attendance.Count;
            var presentDays = attendance.Count(a => a.IsPresent);

            // ADD THIS inside your existing method (after attendance calc)

            var recentAttendance = await context.Attendances
                .Where(a => a.EmployeeId == employeeId)
                .OrderByDescending(a => a.Date)
                .Take(7)
                .Select(a => new AttendanceListDto
                {
                    Date = a.Date,
                    CheckIn = a.CheckIn,
                    CheckOut = a.CheckOut,
                    Status = a.IsPresent ? "Present" : "Absent"
                })
                .ToListAsync();

            return new EmployeeDetailsDto
            {
                FullName = $"{user.FirstName} {user.LastName}".Trim(),
                Email = user.Email ?? "",
                EmployeeCode = result.Employee.EmployeeCode ?? "N/A",
                JoiningDate = result.Employee.JoiningDate,
                Status = result.Employee.Status ?? "Active",
                DepartmentName = result.Department?.Name ?? "N/A",
                DesignationName = result.Designation?.Name ?? "N/A",
                ShiftName = result.Shift?.Name ?? "No Shift Assigned",

                // ✅ dynamic attendance
                TotalDays = totalDays,
                PresentDays = presentDays,

                    RecentAttendance = recentAttendance,

                    Leaves = new List<LeaveDto>
{
    new LeaveDto { Date = DateTime.UtcNow.AddDays(-3), Type = "Sick Leave" },
    new LeaveDto { Date = DateTime.UtcNow.AddDays(-10), Type = "Casual Leave" }
}
            };
        }


    }
}

