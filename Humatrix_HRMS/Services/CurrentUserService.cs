using Humatrix_HRMS.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
//using Humatrix_HRMS.DTOs.Dashboard;
using Humatrix_HRMS.DTOs.Dashboard;

namespace Humatrix_HRMS.Services
{
    public class CurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;


        public CurrentUserService(
            IHttpContextAccessor httpContextAccessor,
            IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _httpContextAccessor = httpContextAccessor;
            _contextFactory = contextFactory;
        }

        public async Task<ApplicationUser?> GetUserAsync()
        {
            var principal = _httpContextAccessor.HttpContext?.User;

            if (principal == null)
                return null;

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
                return null;

            using var context = _contextFactory.CreateDbContext();

            return await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<HrDashboardContextDto?> GetHrDashboardContextAsync()
        {
            var user = await GetUserAsync();

            if (user == null)
                return null;

            return new HrDashboardContextDto
            {
                EmployeeId = user.Id,
                OrganizationId = user.OrganizationId ?? Guid.Empty,
                DepartmentId = user.DepartmentId ?? Guid.Empty,
                FullName = $"{user.FirstName} {user.LastName}",
                Email = user.Email ?? ""
            };
        }
        public async Task<OrgDashboardContextDto?> GetOrgDashboardContextAsync()
        {
            var user = await GetUserAsync();

            if (user == null)
                return null;

            return new OrgDashboardContextDto
            {
                UserId = user.Id,
                OrganizationId = user.OrganizationId ?? Guid.Empty,
                FullName = $"{user.FirstName} {user.LastName}",
                Email = user.Email ?? ""
            };
        }
    }
}