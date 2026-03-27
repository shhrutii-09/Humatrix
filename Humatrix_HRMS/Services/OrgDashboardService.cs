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

            // ✅ Total Employees (from Employee table)
            var totalEmployees = await _context.Employees
                .CountAsync(e => e.OrganizationId == orgId);

            // ✅ Active Users (Identity)
            var activeEmployees = await _context.Users
                .CountAsync(u => u.OrganizationId == orgId && u.IsActive);

            // ✅ Total HR (from roles)
            var totalHR = await _context.UserRoles
                .Join(_context.Roles,
                      ur => ur.RoleId,
                      r => r.Id,
                      (ur, r) => new { ur.UserId, r.Name })
                .Join(_context.Users,
                      x => x.UserId,
                      u => u.Id,
                      (x, u) => new { x.Name, u.OrganizationId })
                .CountAsync(x =>
                    x.Name == "HR" &&
                    x.OrganizationId == orgId);

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