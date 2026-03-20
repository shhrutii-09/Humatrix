using Humatrix_HRMS.Data;
using Microsoft.AspNetCore.Identity;

public static class SuperAdminSeeder
{
    public static async Task SeedSuperAdmin(UserManager<ApplicationUser> userManager)
    {
        var email = "superadmin@humatrix.com";
        var password = "Admin@123";

        var user = await userManager.FindByEmailAsync(email);

        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            await userManager.CreateAsync(user, password);
            await userManager.AddToRoleAsync(user, "SuperAdmin");
        }
    }
}