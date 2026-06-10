using Humatrix_HRMS.Components;
using Humatrix_HRMS.Components.Account;
using Humatrix_HRMS.Data;
using Humatrix_HRMS.Data.SeedData;
using Humatrix_HRMS.Hubs;
using Humatrix_HRMS.Infrastructure.Services;
using Humatrix_HRMS.Services;
using Humatrix_HRMS.Services.AI;
using Humatrix_HRMS.Services.Assets;
using Humatrix_HRMS.Services.Documents;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
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

// Add Swagger for API documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Humatrix HRMS API",
        Version = "v1",
        Description = "HRMS API for Organization Document Generation and Management"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer' followed by your token"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

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
    ServiceLifetime.Scoped);

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("EmailSettings"));

builder.Services.AddScoped<EmailService>();
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
builder.Services.AddHostedService<ExitBackgroundService>();
builder.Services.AddScoped<EmployeeExitService>();
builder.Services.AddScoped<IOrgDocumentGenerationService, OrgDocumentGenerationService>();
builder.Services.AddScoped<IDocumentTypeService, DocumentTypeService>();
builder.Services.AddScoped<IEmployeeDocumentService, EmployeeDocumentService>();
builder.Services.AddScoped<IDocumentHistoryService, DocumentHistoryService>();
builder.Services.AddScoped<IDocumentComplianceService, DocumentComplianceService>();
builder.Services.AddScoped<IDocumentVerificationService, DocumentVerificationService>();
builder.Services.AddScoped<IDocumentExpiryService, DocumentExpiryService>();
builder.Services.AddScoped<IDocumentDashboardService, DocumentDashboardService>();
builder.Services.AddScoped<AssetService>();
builder.Services.AddScoped<OfficeLocationService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddHostedService<DocumentEventMonitor>();
// Add AI Service
builder.Services.AddHttpClient();
builder.Services.AddScoped<IAIDocumentService, AIDocumentService>();


builder.Services.AddSignalR();
builder.Services.AddHttpClient();

var app = builder.Build();

// ==========================================
// SEED ORGANIZATION DOCUMENT TEMPLATES FOR ALL ORGS
// ==========================================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var db = services.GetRequiredService<ApplicationDbContext>();

        // This will seed templates for ALL organizations that don't have them
        await OrgDocumentTemplateSeeder.SeedTemplatesForAllOrganizationsAsync(db);
        Console.WriteLine("✅ Organization document templates seeded for all organizations!");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error seeding templates: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
    }
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Humatrix HRMS API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAdditionalIdentityEndpoints();
app.MapHub<NotificationHub>("/notificationHub");

app.Run();