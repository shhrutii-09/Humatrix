using Microsoft.AspNetCore.Identity;
using Humatrix.Models;

namespace Humatrix.Data
{
    public static class SuperAdminSeeder
    {
        public static async Task SeedSuperAdminAsync(UserManager<ApplicationUser> userManager)
        {
            var email = "superadmin@humatrix.com";
            var password = "Admin@123"; // change later

            var user = await userManager.FindByEmailAsync(email);

            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    IsActive = true
                };

                await userManager.CreateAsync(user, password);
                await userManager.AddToRoleAsync(user, "SuperAdmin");
            }
        }
    }
}