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

        public OrganizationService(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<string> CreateOrganizationAsync(CreateOrganizationDto dto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // ✅ 1. Check duplicate org email
                var existingOrg = await _context.Organizations
                    //.AnyAsync(x => x.Email == dto.Email);
                    .AnyAsync(x => x.Email.ToLower() == dto.Email.ToLower());

                if (existingOrg)
                    throw new Exception("Organization with this email already exists");

                // ✅ 2. Check duplicate user email
                var existingUser = await _userManager.FindByEmailAsync(dto.AdminEmail.ToLower());
                if (existingUser != null)
                    throw new Exception("User with this email already exists");

                // ✅ 3. Create Organization
                var org = new Organization
                {
                    Name = dto.Name,
                    Email = dto.Email,
                    Phone = dto.Phone,
                    Address = dto.Address
                };

                _context.Organizations.Add(org);
                await _context.SaveChangesAsync();

                // ✅ 4. Create Org Admin User (NO PASSWORD YET)
                var user = new ApplicationUser
                {
                    UserName = dto.AdminEmail,
                    Email = dto.AdminEmail,
                    OrganizationId = org.OrganizationId,
                    EmailConfirmed = true
                };

                var result = await _userManager.CreateAsync(user);

                if (!result.Succeeded)
                    throw new Exception(string.Join(", ", result.Errors.Select(e => e.Description)));

                await _userManager.AddToRoleAsync(user, "OrgAdmin");

                // ✅ 5. Generate setup token
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);

                // ✅ 6. Save invite
                var invite = new UserInvite
                {
                    Email = dto.AdminEmail,
                    UserId = user.Id,
                    Token = token,
                    Role = "OrgAdmin",
                    OrganizationId = org.OrganizationId,
                    IsUsed = false
                };

                _context.UserInvites.Add(invite);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                // ✅ 7. Return link
                //return $"https://localhost:7057/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";

                var baseUrl = "https://localhost:7057"; // later move to config

                return $"{baseUrl}/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";

            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private readonly IConfiguration _config;

        public OrganizationService(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IConfiguration config) // 👈 ADD THIS
        {
            _context = context;
            _userManager = userManager;
            _config = config; // 👈 ADD THIS
        }
    }
}