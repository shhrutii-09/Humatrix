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

<<<<<<< HEAD
=======
            var validRoles = new[] { "HR", "Employee" };

            if (!validRoles.Contains(dto.Role))
                throw new Exception("Invalid role");

>>>>>>> 78f416305aa7332ecb4231ce726efacb44858935
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

<<<<<<< HEAD
        // ✅ LIST
=======
        // ✅ LIST (WITH DESIGNATION)
>>>>>>> 78f416305aa7332ecb4231ce726efacb44858935
        public async Task<List<EmployeeListDto>> GetEmployeesForListAsync(Guid? departmentId = null)
        {
            var currentUser = await _currentUser.GetUserAsync();

            if (currentUser == null || currentUser.OrganizationId == null)
                return new List<EmployeeListDto>();

            var roles = await _userManager.GetRolesAsync(currentUser);

            var usersQuery = _userManager.Users
                .Where(u => u.OrganizationId == currentUser.OrganizationId);

<<<<<<< HEAD
            if (roles.Contains("HR"))
            {
                usersQuery = usersQuery
                    .Where(u => u.DepartmentId == currentUser.DepartmentId && u.Id != currentUser.Id);
            }
            else if (departmentId.HasValue)
            {
                usersQuery = usersQuery
                    .Where(u => u.DepartmentId == departmentId.Value);
=======
            // 2. Security/Role Filter
            if (currentUserRoles.Contains("HR"))
            {
                // HR can only see their own department, excluding themselves
                usersQuery = usersQuery.Where(u => u.DepartmentId == currentUser.DepartmentId && u.Id != currentUser.Id);
            }
            else if (departmentId.HasValue)
            {
                // OrgAdmin can filter by department
                usersQuery = usersQuery.Where(u => u.DepartmentId == departmentId.Value);
>>>>>>> 78f416305aa7332ecb4231ce726efacb44858935
            }

            var users = await usersQuery.ToListAsync();
            var departments = await _context.Departments.ToListAsync();
            var organizations = await _context.Organizations.ToListAsync();
            var designations = await _context.Designations.ToListAsync();

            var result = new List<EmployeeListDto>();

            foreach (var u in users)
            {
                var userRoles = await _userManager.GetRolesAsync(u);
<<<<<<< HEAD

                if (!userRoles.Any(r => r == "HR" || r == "Employee"))
                    continue;

                var role = userRoles.FirstOrDefault() ?? "Employee";
=======
                var role = userRoles.FirstOrDefault() ?? "Employee";

                // 🔥 Only include HR or Employee
                if (!userRoles.Any(r => r == "HR" || r == "Employee"))
                    continue;
>>>>>>> 78f416305aa7332ecb4231ce726efacb44858935

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
<<<<<<< HEAD
            user.DesignationId = dto.DesignationId;
=======
            user.DesignationId = dto.DesignationId; // ✅ Added
>>>>>>> 78f416305aa7332ecb4231ce726efacb44858935

            await _userManager.UpdateAsync(user);
        }
    }
}