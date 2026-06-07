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

        //public async Task<HrDashboardContextDto?> GetHrDashboardContextAsync()
        //{
        //    var user = await GetUserAsync();

        //    if (user == null)
        //        return null;

        //    return new HrDashboardContextDto
        //    {
        //        EmployeeId = user.Id,
        //        OrganizationId = user.OrganizationId ?? Guid.Empty,
        //        DepartmentId = user.DepartmentId ?? Guid.Empty,
        //        FullName = $"{user.FirstName} {user.LastName}",
        //        Email = user.Email ?? ""
        //    };
        //}
        public async Task<HrDashboardContextDto?> GetHrDashboardContextAsync()
        {
            var user = await GetUserAsync();

            if (user == null || user.OrganizationId == null)
                return null;

            using var context = _contextFactory.CreateDbContext();

            var employee = await context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            if (employee == null)
                return null;

            return new HrDashboardContextDto
            {
                EmployeeId = employee.EmployeeId,
                OrganizationId = employee.OrganizationId,
                DepartmentId = employee.DepartmentId,
                FullName = $"{employee.FirstName} {employee.LastName}",
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

        public async Task<EmployeeDashboardContextDto?> GetEmployeeDashboardContextAsync()
        {
            var user = await GetUserAsync();

            if (user == null || user.OrganizationId == null)
                return null;

            using var context = _contextFactory.CreateDbContext();

            var employee = await context.Employees
                        .Include(e => e.Department)  // Add this to get Department
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            if (employee == null)
                return null;

            return new EmployeeDashboardContextDto
            {
                UserId = user.Id,
                EmployeeId = employee.EmployeeId,
                OrganizationId = employee.OrganizationId,
                DepartmentId = employee.DepartmentId,
                DepartmentName = employee.Department?.Name ?? "N/A",  // Add this line
                FullName = $"{employee.FirstName} {employee.LastName}",
                Email = user.Email ?? "",

                Role = _httpContextAccessor.HttpContext?.User
         .Claims
         .FirstOrDefault(x => x.Type == ClaimTypes.Role)
         ?.Value ?? "Employee"
            };
        }

        public Task<bool> IsInRoleAsync(string role)
        {
            var user = _httpContextAccessor.HttpContext?.User;

            if (user == null)
                return Task.FromResult(false);

            return Task.FromResult(user.IsInRole(role));
        }
    }
}