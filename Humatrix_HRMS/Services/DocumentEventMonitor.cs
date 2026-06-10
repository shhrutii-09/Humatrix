using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services.Documents;

/// <summary>
/// Monitors database changes and triggers document generation based on events
/// Runs every 5 minutes to check for new employees, promotions, etc.
/// </summary>
public class DocumentEventMonitor : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentEventMonitor> _logger;

    public DocumentEventMonitor(
        IServiceProvider serviceProvider,
        ILogger<DocumentEventMonitor> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("📄 Document Event Monitor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var documentService = scope.ServiceProvider.GetRequiredService<IOrgDocumentGenerationService>();
                var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

                // Check for various events
                await CheckNewEmployeesAsync(db, documentService, notificationService, userManager);
                await CheckProbationCompletionAsync(db, documentService, notificationService, userManager);
                await CheckWorkAnniversariesAsync(db, documentService, notificationService, userManager);
                await CheckBirthdaysAsync(db, notificationService);
                await CheckPendingAssetReturnsAsync(db, notificationService);

                _logger.LogInformation("Document Event Monitor cycle completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DocumentEventMonitor");
            }

            await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
        }
    }

    private async Task CheckNewEmployeesAsync(
    ApplicationDbContext db,
    IOrgDocumentGenerationService documentService,
    NotificationService notificationService,
    UserManager<ApplicationUser> userManager)
    {
        var fiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);

        var newEmployees = await db.Employees
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .Where(e => e.CreatedAt > fiveMinutesAgo && e.Status == "Active")
            .ToListAsync();

        foreach (var employee in newEmployees)
        {

            var existingDoc = await db.OrgGeneratedDocuments
       .AnyAsync(d => d.EmployeeId == employee.EmployeeId &&
                     d.DocumentName == "Offer Letter" &&
                     d.GeneratedAt > DateTime.UtcNow.AddMinutes(-10));

            if (existingDoc)
            {
                _logger.LogInformation($"Offer Letter already generated for {employee.FirstName}, skipping");
                continue;
            }

            // CHECK FOR DUPLICATE - PREVENT MULTIPLE NOTIFICATIONS
            var existingNotification = await db.Notifications
                .AnyAsync(n => n.UserId == employee.UserId &&
                              n.Title == "📄 New Document" &&
                              n.CreatedAt > DateTime.UtcNow.AddMinutes(-10));


            if (existingNotification)
            {
                _logger.LogInformation($"Skipping duplicate notification for {employee.FirstName}");
                continue;
            }

            _logger.LogInformation($"🎉 New employee detected: {employee.FirstName} {employee.LastName}");

            var orgAdmin = await GetOrgAdminAsync(db, userManager, employee.OrganizationId);
            if (orgAdmin == null) continue;

            var template = await db.OrgDocumentTemplates
                .FirstOrDefaultAsync(t => t.OrganizationId == employee.OrganizationId && t.Name == "Offer Letter");

            if (template != null)
            {
                try
                {
                    await documentService.GenerateDocumentWithAIAsync(
                        templateId: template.TemplateId,
                        recipientEmployeeId: employee.EmployeeId,
                        userId: orgAdmin.Id,
                        userRole: "OrgAdmin",
                        customData: new Dictionary<string, string>
                        {
                        { "JoiningDate", employee.JoiningDate.ToString("dd MMM yyyy") },
                        { "Designation", employee.Designation?.Name ?? "Employee" },
                        { "Department", employee.Department?.Name ?? "General" }
                        });

                    _logger.LogInformation($"✅ Generated Offer Letter for {employee.FirstName} {employee.LastName}");

                    // CLEAN, SHORT NOTIFICATION - NO HTML
                    await notificationService.CreateNotificationAsync(
                        employee.UserId,
                        "📄 New Document",
                        $"Your Offer Letter is ready. View it in My Documents.",
                        "/employee/docu");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to generate Offer Letter for employee {employee.EmployeeId}");
                }
            }
        }
    }

    private async Task CheckProbationCompletionAsync(
        ApplicationDbContext db,
        IOrgDocumentGenerationService documentService,
        NotificationService notificationService,
        UserManager<ApplicationUser> userManager)
    {
        var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);

        var employees = await db.Employees
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .Where(e => e.JoiningDate <= sixMonthsAgo &&
                       e.JoiningDate > sixMonthsAgo.AddDays(-7) &&
                       e.Status == "Active")
            .ToListAsync();

        foreach (var employee in employees)
        {
            var existingDoc = await db.OrgGeneratedDocuments
                .AnyAsync(d => d.EmployeeId == employee.EmployeeId &&
                              d.DocumentName == "Confirmation Letter" &&
                              d.IsLatestVersion);

            if (!existingDoc)
            {
                var orgAdmin = await GetOrgAdminAsync(db, userManager, employee.OrganizationId);
                if (orgAdmin == null) continue;

                var template = await db.OrgDocumentTemplates
                    .FirstOrDefaultAsync(t => t.OrganizationId == employee.OrganizationId && t.Name == "Confirmation Letter");

                if (template != null)
                {
                    await documentService.GenerateDocumentWithAIAsync(
                        templateId: template.TemplateId,
                        recipientEmployeeId: employee.EmployeeId,
                        userId: orgAdmin.Id,
                        userRole: "OrgAdmin",
                        customData: new Dictionary<string, string>
                        {
                            { "ConfirmationDate", DateTime.UtcNow.AddDays(7).ToString("dd MMM yyyy") },
                            { "Designation", employee.Designation?.Name ?? "Employee" },
                            { "Department", employee.Department?.Name ?? "General" }
                        });

                    // SHORT NOTIFICATION
                    await notificationService.CreateNotificationAsync(
                        employee.UserId,
                        "✅ Probation Complete",
                        $"Your confirmation letter is ready. View in Documents.",
                        "/employee/docu");
                }
            }
        }
    }

    private async Task CheckWorkAnniversariesAsync(
        ApplicationDbContext db,
        IOrgDocumentGenerationService documentService,
        NotificationService notificationService,
        UserManager<ApplicationUser> userManager)
    {
        var today = DateTime.UtcNow;

        var employees = await db.Employees
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .Where(e => e.JoiningDate.Month == today.Month &&
                       e.JoiningDate.Day == today.Day &&
                       e.Status == "Active")
            .ToListAsync();

        foreach (var employee in employees)
        {
            var yearsWorked = today.Year - employee.JoiningDate.Year;

            if (employee.JoiningDate.Date > today.Date)
            {
                yearsWorked--;
            }

            if (yearsWorked >= 1)
            {
                // SHORT NOTIFICATION
                await notificationService.CreateNotificationAsync(
                    employee.UserId,
                    $"🎉 {yearsWorked} Year Anniversary!",
                    $"Thank you for your {yearsWorked} years of dedication!",
                    "/employee/dashboard");

                if (yearsWorked % 5 == 0 && yearsWorked > 0)
                {
                    var orgAdmin = await GetOrgAdminAsync(db, userManager, employee.OrganizationId);
                    if (orgAdmin == null) continue;

                    var template = await db.OrgDocumentTemplates
                        .FirstOrDefaultAsync(t => t.OrganizationId == employee.OrganizationId && t.Name == "Appreciation Letter");

                    if (template != null)
                    {
                        await documentService.GenerateDocumentWithAIAsync(
                            templateId: template.TemplateId,
                            recipientEmployeeId: employee.EmployeeId,
                            userId: orgAdmin.Id,
                            userRole: "OrgAdmin",
                            customData: new Dictionary<string, string>
                            {
                                { "Achievement", $"Celebrating {yearsWorked} years of service!" },
                                { "Years", yearsWorked.ToString() }
                            });
                    }
                }
            }
        }
    }

    private async Task CheckBirthdaysAsync(
        ApplicationDbContext db,
        NotificationService notificationService)
    {
        var today = DateTime.UtcNow;

        var employees = await db.Employees
            .Where(e => e.DateOfBirth.HasValue &&
                       e.DateOfBirth.Value.Month == today.Month &&
                       e.DateOfBirth.Value.Day == today.Day &&
                       e.Status == "Active")
            .ToListAsync();

        foreach (var employee in employees)
        {
            // SHORT NOTIFICATION
            await notificationService.CreateNotificationAsync(
                employee.UserId,
                "🎂 Happy Birthday!",
                $"Wishing you a fantastic birthday, {employee.FirstName}!",
                "/employee/dashboard");
        }
    }

    private async Task CheckPendingAssetReturnsAsync(
        ApplicationDbContext db,
        NotificationService notificationService)
    {
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        var pendingReturns = await db.AssetAssignments
            .Include(a => a.Asset)
            .Include(a => a.Employee)
            .Where(a => a.ReturnedAt == null &&
                       a.AssignedAt < thirtyDaysAgo)
            .Take(10)
            .ToListAsync();

        foreach (var assignment in pendingReturns)
        {
            var employee = assignment.Employee;
            var asset = assignment.Asset;

            // SHORT NOTIFICATION
            await notificationService.CreateNotificationAsync(
                employee.UserId,
                "⚠️ Asset Return Due",
                $"Please return '{asset?.Name}' (Code: {asset?.AssetCode}). It's overdue.",
                "/employee/assets");
        }
    }

    private async Task<ApplicationUser?> GetOrgAdminAsync(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        Guid organizationId)
    {
        var orgAdminUser = await (from u in db.Users
                                  join ur in db.UserRoles on u.Id equals ur.UserId
                                  join r in db.Roles on ur.RoleId equals r.Id
                                  where u.OrganizationId == organizationId && r.Name == "OrgAdmin"
                                  select u).FirstOrDefaultAsync();
        return orgAdminUser;
    }
}