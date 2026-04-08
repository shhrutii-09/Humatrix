using Humatrix_HRMS.Data;
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

        public OrganizationService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            UserManager<ApplicationUser> userManager)
        {
            _contextFactory = contextFactory;
            _userManager = userManager;
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
                    EmailConfirmed = true
                };

                var result = await _userManager.CreateAsync(user);

                if (!result.Succeeded)
                    throw new Exception(string.Join(", ", result.Errors.Select(e => e.Description)));

                await _userManager.AddToRoleAsync(user, "OrgAdmin");

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

                var baseUrl = "https://localhost:7057";

                return $"{baseUrl}/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";
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

            var employeeCount = await _context.Users
                .CountAsync(x => x.OrganizationId == orgId);

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
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var org = await _context.Organizations.FindAsync(orgId);
                if (org == null)
                    throw new Exception("Organization not found");

                var user = await _userManager.FindByEmailAsync(newAdminEmail.ToLower());

                if (user == null)
                {
                    user = new ApplicationUser
                    {
                        UserName = newAdminEmail,
                        Email = newAdminEmail,
                        OrganizationId = orgId,
                        EmailConfirmed = true
                    };

                    var result = await _userManager.CreateAsync(user);
                    if (!result.Succeeded)
                        throw new Exception(string.Join(", ", result.Errors.Select(e => e.Description)));
                }

                if (!await _userManager.IsInRoleAsync(user, "OrgAdmin"))
                {
                    await _userManager.AddToRoleAsync(user, "OrgAdmin");
                }

                var currentAdmins = await (from u in _context.Users
                                           join ur in _context.UserRoles on u.Id equals ur.UserId
                                           join r in _context.Roles on ur.RoleId equals r.Id
                                           where u.OrganizationId == orgId && r.Name == "OrgAdmin"
                                           select u).ToListAsync();

                foreach (var admin in currentAdmins)
                {
                    if (admin.Email != newAdminEmail)
                    {
                        await _userManager.RemoveFromRoleAsync(admin, "OrgAdmin");
                        admin.IsActive = false;
                    }
                }

                await _context.SaveChangesAsync();

                var token = await _userManager.GeneratePasswordResetTokenAsync(user);

                var invite = new UserInvite
                {
                    Email = user.Email,
                    UserId = user.Id,
                    Token = token,
                    Role = "OrgAdmin",
                    OrganizationId = orgId,
                    IsUsed = false
                };

                _context.UserInvites.Add(invite);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                var baseUrl = "https://localhost:7057";

                return $"{baseUrl}/setup-account?userId={user.Id}&token={Uri.EscapeDataString(token)}";
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<string?> GetOrganizationAdminEmailAsync(Guid orgId)
        {
            using var _context = _contextFactory.CreateDbContext();

            var admin = await (from user in _context.Users
                               join userRole in _context.UserRoles on user.Id equals userRole.UserId
                               join role in _context.Roles on userRole.RoleId equals role.Id
                               where user.OrganizationId == orgId && role.Name == "OrgAdmin"
                               select user.Email)
                               .FirstOrDefaultAsync();

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
    }
}