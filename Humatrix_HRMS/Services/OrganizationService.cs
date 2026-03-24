using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class OrganizationService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public OrganizationService(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<string> CreateOrganizationAsync(CreateOrganizationDto dto)
        {
            // 1. Create Organization
            var org = new Organization
            {
                Name = dto.Name,
                Email = dto.Email,
                Phone = dto.Phone,
                Address = dto.Address
            };

            _context.Organizations.Add(org);
            await _context.SaveChangesAsync();

            // 2. Create Org Admin
            var user = new ApplicationUser
            {
                UserName = dto.AdminEmail, 
                Email = dto.AdminEmail,
                OrganizationId = org.OrganizationId,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user);
            if (!result.Succeeded)
            {
                throw new Exception(string.Join(", ", result.Errors.Select(e => e.Description)));
            }

            await _userManager.AddToRoleAsync(user, "OrgAdmin");

            // 3. Generate Token
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            // 4. STORE INVITE (Matches your Employee logic exactly)
            var invite = new UserInvite
            {
                Email = dto.AdminEmail,
                UserId = user.Id,
                Token = token,
                Role = "OrgAdmin",
                OrganizationId = org.OrganizationId,
                CreatedAt = DateTime.UtcNow,
                IsUsed = false
            };

            _context.UserInvites.Add(invite);
            await _context.SaveChangesAsync();

            // 5. Return Link
            return $"https://localhost:7057/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";
        }

        
    }


}