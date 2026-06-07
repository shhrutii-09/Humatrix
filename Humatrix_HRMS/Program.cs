using Humatrix_HRMS.Components;
using Humatrix_HRMS.Components.Account;
using Humatrix_HRMS.Data;
using Humatrix_HRMS.Hubs;
using Humatrix_HRMS.Infrastructure.Services;
using Humatrix_HRMS.Services;
using Humatrix_HRMS.Services.Assets;
using Humatrix_HRMS.Services.Documents;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

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

// Identity Setup
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;

    // ✅ Fixes token tracking discrepancies for custom setups
    options.Tokens.ProviderMap["AdminInvite"] = new TokenProviderDescriptor(typeof(DataProtectorTokenProvider<ApplicationUser>));
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

var jwtSettings = builder.Configuration.GetSection("Jwt");

builder.Services
    .AddAuthentication()
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings["Key"]!)
            ),

            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString),
    ServiceLifetime.Scoped); // ✅ THIS LINE FIXES ERROR

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// ✅ Correct HttpContextAccessor registration
builder.Services.AddHttpContextAccessor();

builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("EmailSettings"));

builder.Services.AddScoped<EmailService>();

// Custom Services
builder.Services.AddScoped<OrganizationService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<DepartmentService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<OrgDashboardService>();
builder.Services.AddScoped<DesignationService>();
builder.Services.AddScoped<ShiftService>();
builder.Services.AddScoped<HolidayService>();
builder.Services.AddScoped<WorkWeekService>();
builder.Services.AddScoped<LeaveService>();
builder.Services.AddHostedService<YearlyBalanceService>();
builder.Services.AddScoped<OvertimeService>();
builder.Services.AddScoped<WorkFromHomeService>();
builder.Services.AddScoped<AttendanceCorrectionService>();
builder.Services.AddScoped<CorrectionValidationEngine>();
builder.Services.AddHostedService<AttendanceBackgroundService>();
builder.Services.AddScoped<AttendanceCalculationService>();
builder.Services.AddScoped<HRPolicyValidationService>();
builder.Services.AddScoped<DepartmentEventService>();
builder.Services.AddScoped<EmployeeEventService>();
builder.Services.AddScoped<NotificationRecipientResolver>();
builder.Services.AddScoped<NotificationEngine>();
builder.Services.AddScoped<ApprovalWorkflowService>();
builder.Services.AddScoped<ActivityLogService>();
builder.Services.AddScoped<DashboardBroadcastService>();
// Services

builder.Services.AddScoped<IOrgGeneratedDocumentService, OrgGeneratedDocumentService>();

builder.Services.AddScoped<IDocumentTypeService,
    DocumentTypeService>();
builder.Services.AddScoped<IEmployeeDocumentService,
    EmployeeDocumentService>();

builder.Services.AddScoped<IDocumentHistoryService,
                           DocumentHistoryService>();

builder.Services.AddScoped<
    IDocumentComplianceService,
    DocumentComplianceService>();

builder.Services.AddScoped<
    IDocumentVerificationService,
    DocumentVerificationService>();

builder.Services.AddScoped<
    IDocumentExpiryService,
    DocumentExpiryService>();

builder.Services.AddScoped<
    IDocumentDashboardService,
    DocumentDashboardService>();
// Configuration
// appsettings.json:
// "DocumentStorage": { "BasePath": "D:/app-data/documents" }
builder.Services.AddScoped<AssetService>();

builder.Services.AddScoped<OfficeLocationService>();

builder.Services.AddScoped<TaskService>();

builder.Services.AddScoped<NotificationService>();
builder.Services.AddSignalR();

builder.Services.AddHttpClient();

var app = builder.Build();

//// 🔥 Seed Roles + SuperAdmin
//using (var scope = app.Services.CreateScope())
//{
//    var services = scope.ServiceProvider;

//    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
//    await RoleSeeder.SeedRoles(roleManager);

//    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
//    await SuperAdminSeeder.SeedSuperAdmin(userManager);
//}
// 🔥 Seed Roles, SuperAdmin and Migrations
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    // 1. DATABASE MIGRATION: Yeh line automatic update karegi
    var db = services.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();

    // 2. SEEDING: Yeh pehle se tha
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

//app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting(); // ✅ VERY IMPORTANT

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapControllers(); // ✅ ONLY ONCE

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();
app.MapHub<NotificationHub>("/notificationHub");
app.Run();