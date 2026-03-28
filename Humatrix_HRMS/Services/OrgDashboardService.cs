using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class OrgDashboardService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;

        public OrgDashboardService(
            ApplicationDbContext context,
            CurrentUserService currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        public async Task<OrgDashboardDto> GetDashboardDataAsync()
        {
            var user = await _currentUser.GetUserAsync();

            if (user == null || user.OrganizationId == null)
                throw new Exception("Unauthorized");

            var orgId = user.OrganizationId.Value;

            // ✅ Get active user IDs first (from Identity)
            var activeUserIds = await _context.Users
                .Where(u => u.OrganizationId == orgId && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync();

            // ✅ Active Employees (match with active users)
            var activeEmployees = await _context.Employees
                .CountAsync(e =>
                    e.OrganizationId == orgId &&
                    activeUserIds.Contains(e.UserId));

            // ✅ Active HR
            var totalHR = await _context.UserRoles
                .Join(_context.Roles,
                      ur => ur.RoleId,
                      r => r.Id,
                      (ur, r) => new { ur.UserId, r.Name })
                .Join(_context.Users,
                      x => x.UserId,
                      u => u.Id,
                      (x, u) => new { x.Name, u.OrganizationId, u.IsActive })
                .CountAsync(x =>
                    x.Name == "HR" &&
                    x.OrganizationId == orgId &&
                    x.IsActive);

            // ✅ Total Employees = Active Employees + Active HR
            var totalEmployees = activeEmployees + totalHR;

            // ✅ Departments
            var totalDepartments = await _context.Departments
                .CountAsync(d => d.OrganizationId == orgId && !d.IsDeleted);

            return new OrgDashboardDto
            {
                TotalEmployees = totalEmployees,
                ActiveEmployees = activeEmployees,
                TotalHR = totalHR,
                TotalDepartments = totalDepartments
            };
        }
    }
}