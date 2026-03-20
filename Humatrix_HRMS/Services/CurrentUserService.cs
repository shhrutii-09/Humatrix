using Humatrix_HRMS.Data;
using Microsoft.AspNetCore.Identity;

namespace Humatrix_HRMS.Services
{
    public class CurrentUserService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserService(UserManager<ApplicationUser> userManager,
                                  IHttpContextAccessor httpContextAccessor)
        {
            _userManager = userManager;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<ApplicationUser?> GetUserAsync()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return await _userManager.GetUserAsync(user);
        }
    }
}
