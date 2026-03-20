using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;

namespace Humatrix_HRMS.Services
{
    public class OrganizationService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public OrganizationService(ApplicationDbContext context,
                                   UserManager<ApplicationUser> userManager)
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

            // 2. Create Org Admin (no password yet)
            var user = new ApplicationUser
            {
                UserName = dto.AdminEmail,
                Email = dto.AdminEmail,
                OrganizationId = org.OrganizationId,
                EmailConfirmed = true
            };

            await _userManager.CreateAsync(user);

            await _userManager.AddToRoleAsync(user, "OrgAdmin");

            // 3. Generate token
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            //var link = $"https://localhost:5001/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";
            var link = $"https://localhost:7057/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";
            return link;
        }
    }
}