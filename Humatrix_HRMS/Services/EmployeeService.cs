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

        // ✅ CREATE EMPLOYEE / HR
        public async Task<string> CreateEmployeeAsync(CreateEmployeeDto dto)
        {
            var currentUser = await _currentUser.GetUserAsync();

            if (currentUser == null || currentUser.OrganizationId == null)
                throw new Exception("Unauthorized");

            var currentRoles = await _userManager.GetRolesAsync(currentUser);

            // 🔥 HR restriction
            if (currentRoles.Contains("HR"))
            {
                dto.Role = "Employee";
                dto.DepartmentId = currentUser.DepartmentId;
            }

            if (dto.DesignationId == null)
                throw new Exception("Designation is required");

            if (string.IsNullOrEmpty(dto.Role))
                throw new Exception("Role is required");

            var validRoles = new[] { "HR", "Employee" };

            if (!validRoles.Contains(dto.Role))
                throw new Exception("Invalid role");

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

            // 🔥 Invite link
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            var invite = new UserInvite
            {
                Email = dto.Email,
                UserId = user.Id,
                Token = token,
                Role = dto.Role,
                OrganizationId = currentUser.OrganizationId,
                CreatedAt = DateTime.UtcNow
            };

            _context.UserInvites.Add(invite);
            await _context.SaveChangesAsync();

            return $"https://localhost:7057/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";
        }

        // ✅ GET EMPLOYEES LIST (🔥 FULL FIXED WITH IDs)
        public async Task<List<EmployeeListDto>> GetEmployeesForListAsync(Guid? departmentId = null)
        {
            var currentUser = await _currentUser.GetUserAsync();

            if (currentUser == null || currentUser.OrganizationId == null)
                return new List<EmployeeListDto>();

            var currentUserRoles = await _userManager.GetRolesAsync(currentUser);

            var usersQuery = _userManager.Users
                .Where(u => u.OrganizationId == currentUser.OrganizationId);

            // 🔥 Role based filter
            if (currentUserRoles.Contains("HR"))
            {
                usersQuery = usersQuery.Where(u =>
                    u.DepartmentId == currentUser.DepartmentId &&
                    u.Id != currentUser.Id);
            }
            else if (departmentId.HasValue)
            {
                usersQuery = usersQuery.Where(u =>
                    u.DepartmentId == departmentId.Value);
            }

            var users = await usersQuery.ToListAsync();

            // 🔥 Load related data
            var departments = await _context.Departments.ToListAsync();
            var organizations = await _context.Organizations.ToListAsync();
            var designations = await _context.Designations.ToListAsync();

            var result = new List<EmployeeListDto>();

            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                var role = roles.FirstOrDefault() ?? "Employee";

                if (!roles.Any(r => r == "HR" || r == "Employee"))
                    continue;

                result.Add(new EmployeeListDto
                {
                    Name = $"{u.FirstName} {u.LastName}",
                    Email = u.Email ?? "",

                    Role = role,
                    IsHR = role == "HR",

                    // 🔥 IMPORTANT (ID + NAME BOTH)
                    DepartmentId = u.DepartmentId,
                    Department = departments
                        .FirstOrDefault(d => d.DepartmentId == u.DepartmentId)?.Name ?? "",

                    DesignationId = u.DesignationId,
                    Designation = designations
                        .FirstOrDefault(d => d.DesignationId == u.DesignationId)?.Name ?? "",

                    Organization = organizations
                        .FirstOrDefault(o => o.OrganizationId == u.OrganizationId)?.Name ?? "",

                    IsActive = u.IsActive
                });
            }

            return result
                .OrderByDescending(x => x.IsHR)
                .ThenBy(x => x.Name)
                .ToList();
        }

        // ✅ ACTIVATE / DEACTIVATE
        public async Task ToggleUserStatusAsync(string email, bool isActive)
        {
            var user = await _userManager.Users
                .FirstOrDefaultAsync(u => u.Email == email);

            if (user == null)
                throw new Exception("User not found");

            user.IsActive = isActive;
            await _userManager.UpdateAsync(user);
        }

        // ✅ UPDATE EMPLOYEE (🔥 SAFE UPDATE)
        public async Task UpdateEmployeeAsync(string email, EditEmployeeDto dto)
        {
            var user = await _userManager.Users
                .FirstOrDefaultAsync(u => u.Email == email);

            if (user == null)
                throw new Exception("User not found");

            // BASIC UPDATE
            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
            user.DepartmentId = dto.DepartmentId;
            user.DesignationId = dto.DesignationId;

            await _userManager.UpdateAsync(user);

            // 🔥 ROLE UPDATE
            var currentRoles = await _userManager.GetRolesAsync(user);

            // remove old roles
            if (currentRoles.Any())
                await _userManager.RemoveFromRolesAsync(user, currentRoles);

            // add new role
            if (!string.IsNullOrEmpty(dto.Role))
                await _userManager.AddToRoleAsync(user, dto.Role);
        }


        // ✅ GET SINGLE USER
        public async Task<ApplicationUser?> GetEmployeeByEmailAsync(string email)
        {
            return await _userManager.Users
                .FirstOrDefaultAsync(u => u.Email == email);
        }

        // ✅ DELETE (optional)
        public async Task DeleteUserAsync(string email)
        {
            var user = await _userManager.Users
                .FirstOrDefaultAsync(u => u.Email == email);

            if (user != null)
            {
                await _userManager.DeleteAsync(user);
            }
        }
    }
}