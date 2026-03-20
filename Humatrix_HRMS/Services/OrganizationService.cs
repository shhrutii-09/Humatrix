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

        public OrganizationService(ApplicationDbContext context,
                                   UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<string> CreateOrganizationAsync(CreateOrganizationDto dto)
        {
            // 🔒 Check duplicate organization email
            if (await _context.Organizations.AnyAsync(x => x.Email == dto.Email))
            {
                throw new Exception("Organization email already exists.");
            }

            // 🔒 Check duplicate admin user email
            var existingUser = await _userManager.FindByEmailAsync(dto.AdminEmail);
            if (existingUser != null)
            {
                throw new Exception("Admin user already exists.");
            }

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

            // 2. Create Org Admin (without password)
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

            // Assign Role
            if (!await _userManager.IsInRoleAsync(user, "OrgAdmin"))
            {
                await _userManager.AddToRoleAsync(user, "OrgAdmin");
            }

            // 3. Generate password setup token
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            // ⚠️ Later move this to config (appsettings)
            var link = $"https://localhost:7057/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";

            return link;
        }
    }
}
