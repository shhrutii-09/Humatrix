using Humatrix_HRMS.Data;
using Humatrix_HRMS.Data.Seeders;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class OrganizationService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly EmailService _emailService;

        public OrganizationService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            UserManager<ApplicationUser> userManager,
            EmailService emailService)
        {
            _contextFactory = contextFactory;
            _userManager = userManager;
            _emailService = emailService;
        }

        public async Task<string> CreateOrganizationAsync(CreateOrganizationDto dto)
        {
            using var _context = _contextFactory.CreateDbContext();
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var existingOrg = await _context.Organizations
                    .AnyAsync(x => x.Email.ToLower() == dto.Email.ToLower());

                if (existingOrg)
                    throw new Exception("Organization with this email already exists");

                var existingUser = await _userManager.FindByEmailAsync(dto.AdminEmail.ToLower());
                if (existingUser != null)
                    throw new Exception("User with this email already exists");

                var org = new Organization
                {
                    Name = dto.Name,
                    Email = dto.Email,
                    Phone = dto.Phone,
                    Address = dto.Address
                };

                _context.Organizations.Add(org);
                await _context.SaveChangesAsync();

                var user = new ApplicationUser
                {
                    UserName = dto.AdminEmail,
                    Email = dto.AdminEmail,
                    OrganizationId = org.OrganizationId,
                    EmailConfirmed = true,
                    IsActive = false
                };

                var result = await _userManager.CreateAsync(user);

                if (!result.Succeeded)
                    throw new Exception(string.Join(", ", result.Errors.Select(e => e.Description)));

                await _userManager.AddToRoleAsync(user, "OrgAdmin");

                //var token = await _userManager.GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, "AdminInvite");
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
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


                using var seedingContext = _contextFactory.CreateDbContext();
                await Humatrix_HRMS.Data.Seeders.DocumentTypeSeeder.SeedDocumentTypesAsync(seedingContext, org.OrganizationId); 
                
                
                var baseUrl = "https://localhost:7057";
                var setupLink = $"{baseUrl}/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";

                await _emailService.SendOrganizationInviteAsync(
                    dto.AdminEmail,
                    dto.Name,
                    setupLink);

                return setupLink;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<List<Organization>> GetAllOrganizationsAsync()
        {
            using var _context = _contextFactory.CreateDbContext();

            return await _context.Organizations
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task UpdateOrganizationAsync(Organization org)
        {
            using var _context = _contextFactory.CreateDbContext();

            var dbOrg = await _context.Organizations.FindAsync(org.OrganizationId);
            var exists = await _context.Organizations.AnyAsync(x =>
                x.OrganizationId != org.OrganizationId &&
                x.Email.ToLower() == org.Email.ToLower());

            if (exists)
                throw new Exception("Email already used");

            if (dbOrg != null)
            {
                dbOrg.Name = org.Name;
                dbOrg.Email = org.Email;
                dbOrg.Phone = org.Phone;
                dbOrg.Address = org.Address;

                await _context.SaveChangesAsync();
            }
        }

        public async Task ToggleOrganizationStatusAsync(Guid orgId)
        {
            using var _context = _contextFactory.CreateDbContext();

            var dbOrg = await _context.Organizations.FindAsync(orgId);

            if (dbOrg != null)
            {
                dbOrg.IsActive = !dbOrg.IsActive;
                await _context.SaveChangesAsync();
            }
        }

        public async Task<(Organization Organization, int EmployeeCount, int HRCount, int DepartmentCount)>
            GetOrganizationDetailsAsync(Guid orgId)
        {
            using var _context = _contextFactory.CreateDbContext();

            var org = await _context.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.OrganizationId == orgId);

            if (org == null)
                throw new Exception("Organization not found");

            var employeeCount = await (from user in _context.Users
                                       join userRole in _context.UserRoles on user.Id equals userRole.UserId
                                       join role in _context.Roles on userRole.RoleId equals role.Id
                                       where user.OrganizationId == orgId && role.Name == "Employee"
                                       select user)
                                       .CountAsync();

            var hrCount = await (from user in _context.Users
                                 join userRole in _context.UserRoles on user.Id equals userRole.UserId
                                 join role in _context.Roles on userRole.RoleId equals role.Id
                                 where user.OrganizationId == orgId && role.Name == "HR"
                                 select user)
                                 .CountAsync();

            var departmentCount = await _context.Departments
                .CountAsync(x => x.OrganizationId == orgId);

            return (org, employeeCount, hrCount, departmentCount);
        }

        public async Task<string> ChangeOrganizationAdminAsync(Guid orgId, string newAdminEmail)
        {
            using var _context = _contextFactory.CreateDbContext();

            var org = await _context.Organizations.AsNoTracking().FirstOrDefaultAsync(x => x.OrganizationId == orgId);
            if (org == null)
                throw new Exception("Organization not found");

            // Get ONLY the emails of current admins (not the tracked entities)
            var currentAdminEmails = await (from user in _context.Users.AsNoTracking()
                                            join userRole in _context.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
                                            join role in _context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                                            where user.OrganizationId == orgId && role.Name == "OrgAdmin"
                                            select user.Email)
                                             .ToListAsync();

            // Find or create the new admin user
            var newAdminUser = await _userManager.FindByEmailAsync(newAdminEmail.ToLower());

            if (newAdminUser == null)
            {
                // Create new user
                newAdminUser = new ApplicationUser
                {
                    UserName = newAdminEmail,
                    Email = newAdminEmail,
                    OrganizationId = orgId,
                    EmailConfirmed = true,
                    IsActive = false,
                    CreatedAt = DateTime.UtcNow
                };

                var createResult = await _userManager.CreateAsync(newAdminUser);
                if (!createResult.Succeeded)
                    throw new Exception(string.Join(", ", createResult.Errors.Select(e => e.Description)));

                // Re-fetch to ensure we have the user with ID
                newAdminUser = await _userManager.FindByEmailAsync(newAdminEmail.ToLower());
            }
            else
            {
                // User exists - check organization
                if (newAdminUser.OrganizationId != null && newAdminUser.OrganizationId != orgId)
                {
                    throw new Exception("This user email is already registered under another organization.");
                }

                // Update organization ID if needed
                if (newAdminUser.OrganizationId == null)
                {
                    newAdminUser.OrganizationId = orgId;
                    await _userManager.UpdateAsync(newAdminUser);
                }
            }

            // Remove OrgAdmin role from ALL current admins (using their emails, not tracked entities)
            foreach (var adminEmail in currentAdminEmails)
            {
                var admin = await _userManager.FindByEmailAsync(adminEmail);
                if (admin != null)
                {
                    if (await _userManager.IsInRoleAsync(admin, "OrgAdmin"))
                    {
                        await _userManager.RemoveFromRoleAsync(admin, "OrgAdmin");
                    }
                    admin.IsActive = false;
                    await _userManager.UpdateAsync(admin);
                }
            }

            // Add OrgAdmin role to new admin
            if (!await _userManager.IsInRoleAsync(newAdminUser, "OrgAdmin"))
            {
                await _userManager.AddToRoleAsync(newAdminUser, "OrgAdmin");
            }

            // Keep new admin inactive until they set their password
            newAdminUser.IsActive = false;
            await _userManager.UpdateAsync(newAdminUser);

            // Generate password reset token
            var token = await _userManager.GeneratePasswordResetTokenAsync(newAdminUser);

            // Create or update invite (use a fresh context to avoid tracking issues)
            using var inviteContext = _contextFactory.CreateDbContext();

            var existingInvite = await inviteContext.UserInvites
                .FirstOrDefaultAsync(x => x.UserId == newAdminUser.Id);

            if (existingInvite != null)
            {
                existingInvite.Token = token;
                existingInvite.IsUsed = false;
                existingInvite.CreatedAt = DateTime.UtcNow;
                inviteContext.UserInvites.Update(existingInvite);
            }
            else
            {
                var invite = new UserInvite
                {
                    Email = newAdminUser.Email,
                    UserId = newAdminUser.Id,
                    Token = token,
                    Role = "OrgAdmin",
                    OrganizationId = orgId,
                    IsUsed = false,
                    CreatedAt = DateTime.UtcNow
                };
                await inviteContext.UserInvites.AddAsync(invite);
            }

            await inviteContext.SaveChangesAsync();

            var baseUrl = "https://localhost:7057";
            var setupLink = $"{baseUrl}/setup-account?userId={newAdminUser.Id}&token={Uri.EscapeDataString(token)}";

            await _emailService.SendOrganizationInviteAsync(
                newAdminEmail,
                org.Name,
                setupLink);

            return setupLink;
        }
        //public async Task<string?> GetOrganizationAdminEmailAsync(Guid orgId)
        //{
        //    using var _context = _contextFactory.CreateDbContext();

        //    var admin = await (from user in _context.Users
        //                       join userRole in _context.UserRoles on user.Id equals userRole.UserId
        //                       join role in _context.Roles on userRole.RoleId equals role.Id
        //                       where user.OrganizationId == orgId
        //                             && role.Name == "OrgAdmin"
        //                             && user.IsActive == true
        //                       select user.Email)
        //                       .FirstOrDefaultAsync();

        //    if (string.IsNullOrEmpty(admin))
        //    {
        //        admin = await (from user in _context.Users
        //                       join userRole in _context.UserRoles on user.Id equals userRole.UserId
        //                       join role in _context.Roles on userRole.RoleId equals role.Id
        //                       where user.OrganizationId == orgId && role.Name == "OrgAdmin"
        //                       select user.Email)
        //                       .FirstOrDefaultAsync();
        //    }

        //    return admin;
        //}
        public async Task<string?> GetOrganizationAdminEmailAsync(Guid orgId)
        {
            using var _context = _contextFactory.CreateDbContext();

            // First try to get the active admin
            var admin = await (from user in _context.Users
                               join userRole in _context.UserRoles on user.Id equals userRole.UserId
                               join role in _context.Roles on userRole.RoleId equals role.Id
                               where user.OrganizationId == orgId
                                     && role.Name == "OrgAdmin"
                                     && user.IsActive == true
                               select user.Email)
                               .FirstOrDefaultAsync();

            // If no active admin found, get the most recent OrgAdmin (the one that should be activated)
            if (string.IsNullOrEmpty(admin))
            {
                admin = await (from user in _context.Users
                               join userRole in _context.UserRoles on user.Id equals userRole.UserId
                               join role in _context.Roles on userRole.RoleId equals role.Id
                               where user.OrganizationId == orgId && role.Name == "OrgAdmin"
                               orderby user.CreatedAt descending
                               select user.Email)
                               .FirstOrDefaultAsync();
            }

            return admin;
        }
        public async Task<(int totalUsers, int totalEmployees, int totalHRs)> GetUserStatsAsync()
        {
            using var _context = _contextFactory.CreateDbContext();

            var totalUsers = await _context.Users.CountAsync();

            var totalEmployees = await (from u in _context.Users
                                        join ur in _context.UserRoles on u.Id equals ur.UserId
                                        join r in _context.Roles on ur.RoleId equals r.Id
                                        where r.Name == "Employee"
                                        select u).CountAsync();

            var totalHRs = await (from u in _context.Users
                                  join ur in _context.UserRoles on u.Id equals ur.UserId
                                  join r in _context.Roles on ur.RoleId equals r.Id
                                  where r.Name == "HR"
                                  select u).CountAsync();

            return (totalUsers, totalEmployees, totalHRs);
        }

        public async Task<List<UserInvite>> GetAllInvitationsAsync()
        {
            using var _context = _contextFactory.CreateDbContext();

            return await _context.UserInvites
                .Include(i => i.Organization)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync();
        }

        public async Task ResendAdminActivationEmailAsync(Guid orgId, string adminEmail)
        {
            var user = await _userManager.FindByEmailAsync(adminEmail);
            if (user == null)
                throw new Exception("User not found");

            using var _context = _contextFactory.CreateDbContext();

            var org = await _context.Organizations.AsNoTracking().FirstOrDefaultAsync(x => x.OrganizationId == orgId);
            if (org == null)
                throw new Exception("Organization not found");

            // Generate new token
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            // Update or create invite
            var invite = await _context.UserInvites.FirstOrDefaultAsync(x => x.UserId == user.Id);
            if (invite != null)
            {
                invite.Token = token;
                invite.IsUsed = false;
                invite.CreatedAt = DateTime.UtcNow;
                _context.UserInvites.Update(invite);
            }
            else
            {
                invite = new UserInvite
                {
                    Email = user.Email,
                    UserId = user.Id,
                    Token = token,
                    Role = "OrgAdmin",
                    OrganizationId = orgId,
                    IsUsed = false,
                    CreatedAt = DateTime.UtcNow
                };
                await _context.UserInvites.AddAsync(invite);
            }

            await _context.SaveChangesAsync();

            var baseUrl = "https://localhost:7057";
            var setupLink = $"{baseUrl}/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";

            await _emailService.SendOrganizationInviteAsync(adminEmail, org.Name, setupLink);
        }
        public async Task EnsureSingleActiveAdminAsync(Guid orgId)
        {
            using var _context = _contextFactory.CreateDbContext();

            var activeAdmins = await (from user in _context.Users
                                      join userRole in _context.UserRoles on user.Id equals userRole.UserId
                                      join role in _context.Roles on userRole.RoleId equals role.Id
                                      where user.OrganizationId == orgId
                                            && role.Name == "OrgAdmin"
                                            && user.IsActive == true
                                      select user)
                                      .ToListAsync();

            if (activeAdmins.Count > 1)
            {
                // Keep the most recent one, deactivate others
                var adminsOrdered = activeAdmins.OrderByDescending(u => u.CreatedAt).ToList();
                var primaryAdmin = adminsOrdered.First();

                foreach (var extraAdmin in adminsOrdered.Skip(1))
                {
                    await _userManager.RemoveFromRoleAsync(extraAdmin, "OrgAdmin");
                    extraAdmin.IsActive = false;
                    await _userManager.UpdateAsync(extraAdmin);
                }
            }
        }


    }
}