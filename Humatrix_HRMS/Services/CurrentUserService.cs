using Humatrix_HRMS.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

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
    }
}