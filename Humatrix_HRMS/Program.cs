using Humatrix_HRMS.Components;
using Humatrix_HRMS.Components.Account;
using Humatrix_HRMS.Data;
using Humatrix_HRMS.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Razor + Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

// Web API
builder.Services.AddControllers();

// Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// ✅ Correct HttpContextAccessor registration
builder.Services.AddHttpContextAccessor();

// Custom Services
builder.Services.AddScoped<OrganizationService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<DepartmentService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<OrgDashboardService>();
builder.Services.AddScoped<DesignationService>();
builder.Services.AddScoped<ShiftService>();
builder.Services.AddHostedService<AttendanceBackgroundService>();

builder.Services.AddCascadingAuthenticationState();


var app = builder.Build();

// 🔥 Seed Roles + SuperAdmin
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    await RoleSeeder.SeedRoles(roleManager);

    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    await SuperAdminSeeder.SeedSuperAdmin(userManager);
}

// Middleware Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}   

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting(); // ✅ VERY IMPORTANT

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapControllers(); // ✅ ONLY ONCE

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

app.Run();