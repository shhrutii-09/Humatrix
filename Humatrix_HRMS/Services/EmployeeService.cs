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
    }
}