// Infrastructure/Services/NotificationRecipientResolver.cs
using Humatrix_HRMS.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Infrastructure.Services
{
    /// <summary>
    /// Central service that resolves WHO receives a notification.
    /// All role-based, department-scoped recipient logic lives HERE only.
    /// Business services must NOT contain recipient-resolution logic.
    /// </summary>
    public class NotificationRecipientResolver
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationRecipientResolver(
            IDbContextFactory<ApplicationDbContext> dbFactory,
            UserManager<ApplicationUser> userManager)
        {
            _dbFactory = dbFactory;
            _userManager = userManager;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all HR user-IDs whose employee record is in the given department.
        /// Efficient: single join query, no N+1.
        /// </summary>
        public async Task<IReadOnlyList<string>> GetDepartmentHrUserIdsAsync(
            Guid organizationId,
            Guid departmentId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            // Single query: employees in org+dept whose AspNetUser is in "HR" role
            var candidates = await db.Employees
                .Where(e =>
                    e.OrganizationId == organizationId &&
                    e.DepartmentId == departmentId &&
                    e.Status == "Active")
                .Select(e => e.UserId)
                .ToListAsync();

            if (!candidates.Any())
                return Array.Empty<string>();

            // Batch role check — avoids N+1
            var hrUsers = await db.Users
                .Where(u => candidates.Contains(u.Id))
                .Join(db.UserRoles,
                      u => u.Id,
                      ur => ur.UserId,
                      (u, ur) => new { u.Id, ur.RoleId })
                .Join(db.Roles,
                      x => x.RoleId,
                      r => r.Id,
                      (x, r) => new { x.Id, RoleName = r.Name })
                .Where(x => x.RoleName == "HR")
                .Select(x => x.Id)
                .Distinct()
                .ToListAsync();

            return hrUsers;
        }

        /// <summary>
        /// Returns all OrgAdmin user-IDs for the given organization.
        /// </summary>
        //public async Task<IReadOnlyList<string>> GetOrgAdminUserIdsAsync(Guid organizationId)
        //{
        //    using var db = await _dbFactory.CreateDbContextAsync();

        //    var orgUserIds = await db.Employees
        //        .Where(e => e.OrganizationId == organizationId && e.Status == "Active")
        //        .Select(e => e.UserId)
        //        .ToListAsync();

        //    if (!orgUserIds.Any())
        //        return Array.Empty<string>();

        //    var adminIds = await db.Users
        //        .Where(u => orgUserIds.Contains(u.Id))
        //        .Join(db.UserRoles, u => u.Id, ur => ur.UserId, (u, ur) => new { u.Id, ur.RoleId })
        //        .Join(db.Roles, x => x.RoleId, r => r.Id, (x, r) => new { x.Id, RoleName = r.Name })
        //        .Where(x => x.RoleName == "OrgAdmin")
        //        .Select(x => x.Id)
        //        .Distinct()
        //        .ToListAsync();

        //    return adminIds;
        //}
        public async Task<IReadOnlyList<string>> GetOrgAdminUserIdsAsync(Guid organizationId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var adminIds = await db.Users
                .Where(u => u.OrganizationId == organizationId)
                .Join(
                    db.UserRoles,
                    u => u.Id,
                    ur => ur.UserId,
                    (u, ur) => new { u.Id, ur.RoleId }
                )
                .Join(
                    db.Roles,
                    x => x.RoleId,
                    r => r.Id,
                    (x, r) => new { x.Id, RoleName = r.Name }
                )
                .Where(x => x.RoleName == "OrgAdmin")
                .Select(x => x.Id)
                .Distinct()
                .ToListAsync();

            return adminIds;
        }

        /// <summary>
        /// Resolves approver recipient IDs based on applicant role.
        /// HR applicant → OrgAdmins only.
        /// Employee applicant → same-dept HR + OrgAdmins.
        /// </summary>
        public async Task<IReadOnlyList<string>> GetApproverUserIdsAsync(
            Guid organizationId,
            Guid departmentId,
            string applicantRole)
        {
            var result = new List<string>();

            if (applicantRole == "HR" || applicantRole == "OrgAdmin")
            {
                // HR/OrgAdmin requests go straight to OrgAdmin
                result.AddRange(await GetOrgAdminUserIdsAsync(organizationId));
            }
            else
            {
                // Employee → dept HR + OrgAdmin
                result.AddRange(await GetDepartmentHrUserIdsAsync(organizationId, departmentId));
                result.AddRange(await GetOrgAdminUserIdsAsync(organizationId));
            }

            return result.Distinct().ToList();
        }

        /// <summary>
        /// Returns the employee's own UserId (for approval outcome notifications).
        /// </summary>
        public async Task<string?> GetEmployeeUserIdAsync(Guid employeeId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Employees
                .Where(e => e.EmployeeId == employeeId)
                .Select(e => e.UserId)
                .FirstOrDefaultAsync();
        }
    }
}