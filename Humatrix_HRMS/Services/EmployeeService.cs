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

        // ✅ CREATE
        public async Task<string> CreateEmployeeAsync(CreateEmployeeDto dto)
        {
            var currentUser = await _currentUser.GetUserAsync();

            if (currentUser == null || currentUser.OrganizationId == null)
                throw new Exception("Unauthorized");

            var roles = await _userManager.GetRolesAsync(currentUser);

            if (roles.Contains("HR"))
            {
                dto.Role = "Employee";
                dto.DepartmentId = currentUser.DepartmentId;
            }

            if (dto.DesignationId == null)
                throw new Exception("Designation required");

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
                throw new Exception(string.Join(",", result.Errors.Select(e => e.Description)));

            await _userManager.AddToRoleAsync(user, dto.Role);

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            return $"https://localhost:7057/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";
        }

        // ✅ LIST
        public async Task<List<EmployeeListDto>> GetEmployeesForListAsync(Guid? departmentId = null)
        {
            var currentUser = await _currentUser.GetUserAsync();

            if (currentUser == null || currentUser.OrganizationId == null)
                return new List<EmployeeListDto>();

            var roles = await _userManager.GetRolesAsync(currentUser);

            var usersQuery = _userManager.Users
                .Where(u => u.OrganizationId == currentUser.OrganizationId);

            if (roles.Contains("HR"))
            {
                usersQuery = usersQuery
                    .Where(u => u.DepartmentId == currentUser.DepartmentId && u.Id != currentUser.Id);
            }
            else if (departmentId.HasValue)
            {
                usersQuery = usersQuery
                    .Where(u => u.DepartmentId == departmentId.Value);
            }

            var users = await usersQuery.ToListAsync();
            var departments = await _context.Departments.ToListAsync();
            var organizations = await _context.Organizations.ToListAsync();
            var designations = await _context.Designations.ToListAsync();

            var result = new List<EmployeeListDto>();

            foreach (var u in users)
            {
                var userRoles = await _userManager.GetRolesAsync(u);

                if (!userRoles.Any(r => r == "HR" || r == "Employee"))
                    continue;

                var role = userRoles.FirstOrDefault() ?? "Employee";

                result.Add(new EmployeeListDto
                {
                    Name = $"{u.FirstName} {u.LastName}",
                    Email = u.Email ?? "",
                    Role = role,
                    IsHR = role == "HR",
                    Department = departments.FirstOrDefault(d => d.DepartmentId == u.DepartmentId)?.Name ?? "",
                    Organization = organizations.FirstOrDefault(o => o.OrganizationId == u.OrganizationId)?.Name ?? "",
                    Designation = designations.FirstOrDefault(d => d.DesignationId == u.DesignationId)?.Name ?? "",
                    IsActive = u.IsActive
                });
            }

            return result
                .OrderByDescending(x => x.IsHR)
                .ThenBy(x => x.Name)
                .ToList();
        }

        // ✅ DELETE
        public async Task DeleteUserAsync(string email)
        {
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user == null)
                throw new Exception("User not found");

            await _userManager.DeleteAsync(user);
        }

        // ✅ UPDATE
        public async Task UpdateEmployeeAsync(string email, EditEmployeeDto dto)
        {
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user == null)
                throw new Exception("User not found");

            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
            user.DepartmentId = dto.DepartmentId;
            user.DesignationId = dto.DesignationId;

            await _userManager.UpdateAsync(user);
        }
    }
}
