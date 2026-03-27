using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class EmployeeService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly CurrentUserService _currentUser;
        private readonly ApplicationDbContext _context;

        public EmployeeService(
            UserManager<ApplicationUser> userManager,
            CurrentUserService currentUser,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _currentUser = currentUser;
            _context = context;
        }

        public async Task<string> CreateEmployeeAsync(CreateEmployeeDto dto)
        {
            var currentUser = await _currentUser.GetUserAsync();
            if (currentUser?.OrganizationId == null)
                throw new Exception("Unauthorized: Organization context missing.");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var existingUser = await _userManager.FindByEmailAsync(dto.Email);
                if (existingUser != null) throw new Exception("A user with this email already exists.");

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

                //return $"https://localhost:7057/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";

                var baseUrl = _config["AppBaseUrl"];

                return $"{baseUrl}/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception(ex.InnerException?.Message ?? ex.Message);
            }
        }

        public async Task<List<EmployeeListDto>> GetEmployeesForListAsync(string? search = null, Guid? departmentId = null)
        {
            var currentUser = await _currentUser.GetUserAsync();
            if (currentUser?.OrganizationId == null) return new();

            var roles = await _userManager.GetRolesAsync(currentUser);
            var query = _userManager.Users.Where(u => u.OrganizationId == currentUser.OrganizationId && u.Id != currentUser.Id);

            if (roles.Contains("HR"))
                query = query.Where(u => u.DepartmentId == currentUser.DepartmentId);

            if (departmentId.HasValue)
                query = query.Where(u => u.DepartmentId == departmentId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.ToLower();
                query = query.Where(u => u.FirstName.ToLower().Contains(s) || u.LastName.ToLower().Contains(s) || u.Email.ToLower().Contains(s));
            }

            var users = await query.ToListAsync();
            var depts = await _context.Departments.AsNoTracking().ToListAsync();
            var desigs = await _context.Designations.AsNoTracking().ToListAsync();
            var list = new List<EmployeeListDto>();

            foreach (var u in users)
            {
                var userRoles = await _userManager.GetRolesAsync(u);
                var role = userRoles.FirstOrDefault() ?? "Employee";

                if (roles.Contains("HR") && (role == "HR" || role == "OrgAdmin")) continue;

                list.Add(new EmployeeListDto
                {
                    Email = u.Email!,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    Name = $"{u.FirstName} {u.LastName}",
                    Role = role,
                    Department = depts.FirstOrDefault(d => d.DepartmentId == u.DepartmentId)?.Name ?? "N/A",
                    Designation = desigs.FirstOrDefault(d => d.DesignationId == u.DesignationId)?.Name ?? "N/A",
                    DepartmentId = u.DepartmentId,
                    DesignationId = u.DesignationId,
                    IsActive = u.IsActive
                });
            }
            return list.OrderBy(x => x.Name).ToList();
        }

        public async Task UpdateEmployeeAsync(string email, EditEmployeeDto dto)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) throw new Exception("User not found");

            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
            user.DepartmentId = dto.DepartmentId;
            user.DesignationId = dto.DesignationId;

            await _userManager.UpdateAsync(user);

            var profile = await _context.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id);
            if (profile != null)
            {
                profile.FirstName = dto.FirstName;
                profile.LastName = dto.LastName;
                profile.DepartmentId = dto.DepartmentId ?? Guid.Empty;
                profile.DesignationId = dto.DesignationId ?? Guid.Empty;
                await _context.SaveChangesAsync();
            }
        }

        public async Task ToggleUserStatusAsync(string email, bool isActive)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) throw new Exception("User not found");

            user.IsActive = isActive;
            await _userManager.UpdateAsync(user);

            var profile = await _context.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id);
            if (profile != null)
            {
                profile.Status = isActive ? "Active" : "Inactive";
                await _context.SaveChangesAsync();
            }
        }

        private async Task<string> GenerateEmployeeCodeAsync()
        {
            var count = await _context.Employees.IgnoreQueryFilters().CountAsync();
            return $"EMP{(count + 1).ToString("D4")}";
        }

        private readonly IConfiguration _config;

        public EmployeeService(
            UserManager<ApplicationUser> userManager,
            CurrentUserService currentUser,
            ApplicationDbContext context,
            IConfiguration config) // 👈 ADD THIS
        {
            _userManager = userManager;
            _currentUser = currentUser;
            _context = context;
            _config = config; // 👈 ADD THIS
        }
    }
}