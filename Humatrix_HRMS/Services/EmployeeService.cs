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


            if (currentUser == null || currentUser.OrganizationId == null)
                throw new Exception("Unauthorized");


            var currentUserRoles = await _userManager.GetRolesAsync(currentUser);
            if (currentUserRoles.Contains("HR"))
            {
                // 1. Force the role to Employee
                dto.Role = "Employee";

                // 2. Force the DepartmentId to be the same as the HR's department
                dto.DepartmentId = currentUser.DepartmentId;

                if (dto.DepartmentId == null)
                    throw new Exception("HR user must be assigned to a department to create employees.");
            }


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
                EmailConfirmed = true
            };

            // 1. CREATE USER
            var result = await _userManager.CreateAsync(user);

            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                throw new Exception(errors);
            }

            // 2. ASSIGN ROLE (Uses the role selected in the UI: HR or Employee)
            await _userManager.AddToRoleAsync(user, dto.Role);

            // 3. GENERATE TOKEN
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            // 4. SAVE INVITE (FIXED: Added Email, Role, and OrganizationId)
            var invite = new UserInvite
            {
                Email = dto.Email, // This fixes the SQL NULL error
                UserId = user.Id,
                Token = token,
                Role = dto.Role, // Stores whether they are HR or Employee
                OrganizationId = currentUser.OrganizationId,
                CreatedAt = DateTime.UtcNow,
                IsUsed = false
            };

            _context.UserInvites.Add(invite);
            await _context.SaveChangesAsync();

            // 5. RETURN LINK
            var link = $"https://localhost:7057/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";

            return link;
        }

        public async Task<List<ApplicationUser>> GetEmployeesAsync()
        {
            var currentUser = await _currentUser.GetUserAsync();

            if (currentUser == null || currentUser.OrganizationId == null)
                return new List<ApplicationUser>();

            return await _userManager.Users
                .Where(u => u.OrganizationId == currentUser.OrganizationId)
                .ToListAsync();
        }

        public async Task<List<EmployeeListDto>> GetEmployeesForListAsync(Guid? departmentId = null)
        {
            var currentUser = await _currentUser.GetUserAsync();

            if (currentUser == null || currentUser.OrganizationId == null)
                return new List<EmployeeListDto>();

            var roles = await _userManager.GetRolesAsync(currentUser);

            var usersQuery = _userManager.Users
                .Where(u => u.OrganizationId == currentUser.OrganizationId);

            // 🔥 HR logic
            if (roles.Contains("HR"))
            {
                usersQuery = usersQuery
                    .Where(u => u.DepartmentId == currentUser.DepartmentId && u.Id != currentUser.Id); // ❌ exclude self
            }
            else
            {
                // 🔥 Org Admin filter
                if (departmentId.HasValue)
                {
                    usersQuery = usersQuery
.Where(u => u.DepartmentId.HasValue && u.DepartmentId.Value == departmentId.Value);
                }
            }

            var users = await usersQuery.ToListAsync();

            var departments = await _context.Departments.ToListAsync();
            var organizations = await _context.Organizations.ToListAsync();

            var result = new List<EmployeeListDto>();

            foreach (var u in users)
            {
                var userRoles = await _userManager.GetRolesAsync(u);
                var role = userRoles.FirstOrDefault();

                result.Add(new EmployeeListDto
                {
                    Name = $"{u.FirstName} {u.LastName}",
                    Email = u.Email,
                    Role = role,
                    IsHR = role == "HR",
                    Department = departments.FirstOrDefault(d => d.DepartmentId == u.DepartmentId)?.Name,
                    Organization = organizations.FirstOrDefault(o => o.OrganizationId == u.OrganizationId)?.Name,
                    IsActive = u.IsActive
                });
            }

            // 🔥 SORT: HR FIRST
            return result
                .OrderByDescending(x => x.IsHR)
                .ThenBy(x => x.Name)
                .ToList();
        }

        public async Task ToggleUserStatusAsync(string email, bool isActive)
        {
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user == null) throw new Exception("User not found");

            user.IsActive = isActive;
            await _userManager.UpdateAsync(user);
        }

        public async Task UpdateEmployeeAsync(string email, EditEmployeeDto dto)
        {
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user == null) throw new Exception("User not found");

            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
            user.DepartmentId = dto.DepartmentId;

            await _userManager.UpdateAsync(user);
        }

        public async Task<string> ResendInviteAsync(string email)
        {
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user == null) throw new Exception("User not found");

            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault();

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            var invite = new UserInvite
            {
                Email = user.Email,
                UserId = user.Id,
                Token = token,
                Role = role,
                OrganizationId = user.OrganizationId,
                CreatedAt = DateTime.UtcNow
            };

            _context.UserInvites.Add(invite);
            await _context.SaveChangesAsync();

            return $"https://localhost:7057/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";
        }
    }
}