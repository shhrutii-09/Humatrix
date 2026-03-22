using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class OrgDashboardService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly CurrentUserService _currentUser;
        private readonly ApplicationDbContext _context;

        public OrgDashboardService(
            UserManager<ApplicationUser> userManager,
            CurrentUserService currentUser,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _currentUser = currentUser;
            _context = context;
        }

        public async Task<OrgDashboardDto> GetDashboardDataAsync()
        {
            var user = await _currentUser.GetUserAsync();

            if (user == null || user.OrganizationId == null)
                throw new Exception("Unauthorized");

            var users = _userManager.Users
                .Where(u => u.OrganizationId == user.OrganizationId);

            var totalEmployees = await users.CountAsync();
            var activeEmployees = await users.CountAsync(u => u.IsActive);

            int totalHR = 0;
            foreach (var u in await users.ToListAsync())
            {
                var roles = await _userManager.GetRolesAsync(u);
                if (roles.Contains("HR"))
                    totalHR++;
            }

            var totalDepartments = await _context.Departments
                .CountAsync(d => d.OrganizationId == user.OrganizationId && !d.IsDeleted);

            return new OrgDashboardDto
            {
                TotalEmployees = totalEmployees,
                TotalHR = totalHR,
                ActiveEmployees = activeEmployees,
                TotalDepartments = totalDepartments
            };
        }
    }
}
