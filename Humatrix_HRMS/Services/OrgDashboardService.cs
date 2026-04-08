using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class OrgDashboardService
    {
        //private readonly ApplicationDbContext _context;
        //private readonly CurrentUserService _currentUser;

        //public OrgDashboardService(
        //    ApplicationDbContext context,
        //    CurrentUserService currentUser)
        //{
        //    _context = context;
        //    _currentUser = currentUser;
        //}

        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly CurrentUserService _currentUser;

        public OrgDashboardService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            CurrentUserService currentUser)
        {
            _contextFactory = contextFactory;
            _currentUser = currentUser;
        }

        public async Task<OrgDashboardDto> GetDashboardDataAsync()
        {
            using var _context = _contextFactory.CreateDbContext();
            var user = await _currentUser.GetUserAsync();

            if (user == null || user.OrganizationId == null)
                throw new Exception("Unauthorized");

            var orgId = user.OrganizationId.Value;

            // 1. Get Active User IDs for this Org
            var activeUserIds = await _context.Users
                .Where(u => u.OrganizationId == orgId && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync();

            // 2. Count Active Employees (linked to active users)
            var activeEmployees = await _context.Employees
                .CountAsync(e => e.OrganizationId == orgId && activeUserIds.Contains(e.UserId));

            // 3. Count Active HRs
            var totalHR = await (
                from ur in _context.UserRoles
                join r in _context.Roles on ur.RoleId equals r.Id
                join u in _context.Users on ur.UserId equals u.Id
                where r.Name == "HR" && u.OrganizationId == orgId && u.IsActive
                select u.Id
            ).CountAsync();

            // 4. FIX: Count ACTIVE Departments (Removed !d.IsActive)
            var totalDepartments = await _context.Departments
                .CountAsync(d => d.OrganizationId == orgId && d.IsActive);

            return new OrgDashboardDto
            {
                TotalEmployees = activeEmployees + totalHR,
                ActiveEmployees = activeEmployees,
                TotalHR = totalHR,
                TotalDepartments = totalDepartments
            };
        }
    }
}