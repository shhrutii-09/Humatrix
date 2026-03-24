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

            // 🔥 HR restrictions
            if (currentRoles.Contains("HR"))
            {
                dto.Role = "Employee";
                dto.DepartmentId = currentUser.DepartmentId;
            }

<<<<<<< HEAD
=======
            if (dto.DesignationId == null)
                throw new Exception("Designation is required");
            if (string.IsNullOrEmpty(dto.Role))
                throw new Exception("Role is required");

            var validRoles = new[] { "HR", "Employee" };

            if (!validRoles.Contains(dto.Role))
                throw new Exception("Invalid role");

>>>>>>> ae07b0cd972eb059e35f6d866fb42c0d181ee94f
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

<<<<<<< HEAD
        // ✅ LIST (WITH DESIGNATION)
=======
        //        public async Task<List<EmployeeListDto>> GetEmployeesForListAsync(Guid? departmentId = null)
        //        {
        //            var currentUser = await _currentUser.GetUserAsync();

        //            if (currentUser == null || currentUser.OrganizationId == null)
        //                return new List<EmployeeListDto>();

        //            var roles = await _userManager.GetRolesAsync(currentUser);

        //            var usersQuery = _userManager.Users
        //                .Where(u => u.OrganizationId == currentUser.OrganizationId);

        //            if (roles.Contains("HR"))
        //            {
        //                usersQuery = usersQuery
        //                    .Where(u => u.DepartmentId == currentUser.DepartmentId && u.Id != currentUser.Id); 
        //            }
        //            else
        //            {
        //                // 🔥 Org Admin filter
        //                if (departmentId.HasValue)
        //                {
        //                    usersQuery = usersQuery
        //.Where(u => u.DepartmentId.HasValue && u.DepartmentId.Value == departmentId.Value);
        //                }
        //            }

        //            var users = await usersQuery.ToListAsync();

        //            var departments = await _context.Departments.ToListAsync();
        //            var organizations = await _context.Organizations.ToListAsync();

        //            var result = new List<EmployeeListDto>();

        //            foreach (var u in users)
        //            {
        //                var userRoles = await _userManager.GetRolesAsync(u);
        //                var role = userRoles.FirstOrDefault();

        //                result.Add(new EmployeeListDto
        //                {
        //                    Name = $"{u.FirstName} {u.LastName}",
        //                    Email = u.Email,
        //                    Role = role,
        //                    IsHR = role == "HR",
        //                    Department = departments.FirstOrDefault(d => d.DepartmentId == u.DepartmentId)?.Name,
        //                    Organization = organizations.FirstOrDefault(o => o.OrganizationId == u.OrganizationId)?.Name,
        //                    IsActive = u.IsActive
        //                });
        //            }

        //            // 🔥 SORT: HR FIRST
        //            return result
        //                .OrderByDescending(x => x.IsHR)
        //                .ThenBy(x => x.Name)
        //                .ToList();
        //        }


>>>>>>> ae07b0cd972eb059e35f6d866fb42c0d181ee94f
        public async Task<List<EmployeeListDto>> GetEmployeesForListAsync(Guid? departmentId = null)
        {
            var currentUser = await _currentUser.GetUserAsync();
            if (currentUser == null || currentUser.OrganizationId == null)
                return new List<EmployeeListDto>();

            var currentUserRoles = await _userManager.GetRolesAsync(currentUser);

            // 1. Start with users in the same Organization
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
                    .Where(u => u.DepartmentId == departmentId);
=======
            // 2. Security/Role Filter:
            // If the viewer is HR, they can only see people in their own department (and not themselves)
            if (currentUserRoles.Contains("HR"))
            {
                usersQuery = usersQuery.Where(u => u.DepartmentId == currentUser.DepartmentId && u.Id != currentUser.Id);
            }
            // If OrgAdmin, they see the whole org but we should still apply the department filter if selected
            else if (departmentId.HasValue)
            {
                usersQuery = usersQuery.Where(u => u.DepartmentId == departmentId.Value);
>>>>>>> ae07b0cd972eb059e35f6d866fb42c0d181ee94f
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
                var role = userRoles.FirstOrDefault() ?? "Employee";
=======

                // 🔥 CRITICAL CHANGE: Only include if the user is HR or Employee
                // This prevents OrgAdmins or other roles from appearing in the list
                if (!userRoles.Any(r => r == "HR" || r == "Employee"))
                {
                    continue;
                }

                var role = userRoles.FirstOrDefault();
>>>>>>> ae07b0cd972eb059e35f6d866fb42c0d181ee94f

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

        // ✅ ACTIVATE / DEACTIVATE
        public async Task ToggleUserStatusAsync(string email, bool isActive)
        {
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user == null) throw new Exception("User not found");

            user.IsActive = isActive;
            await _userManager.UpdateAsync(user);
        }

        // ✅ UPDATE
        public async Task UpdateEmployeeAsync(string email, EditEmployeeDto dto)
        {
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null) throw new Exception("User not found");

            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
            user.DepartmentId = dto.DepartmentId;
            user.DesignationId = dto.DesignationId; // ✅ Add this line

            await _userManager.UpdateAsync(user);
        }
    }
}
