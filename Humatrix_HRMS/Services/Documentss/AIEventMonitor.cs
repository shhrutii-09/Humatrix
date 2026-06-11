// Services/Documents/AIEventMonitor.cs
// Production-grade background service that monitors database events and auto-generates documents.
//
// Design principles:
//   • Each processor is fully idempotent — safe to re-run on every tick.
//   • A single, consolidated notification is sent per logical event (never one-per-document).
//   • All state flags are written BEFORE notifications so a crash cannot cause double-sends.
//   • No document-level notification is sent from OrgDocumentGenerationService for
//     auto-generated (SYSTEM) documents — the monitor owns that responsibility.
//   • Leave and task document generation are opt-in flags per org, not fired for every event.

using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models;
using Humatrix_HRMS.Models.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Humatrix_HRMS.Services.Documents;

public sealed class AIEventMonitor : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AIEventMonitor> _logger;

    // How frequently the monitor polls the database.
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(30);

    public AIEventMonitor(IServiceProvider serviceProvider, ILogger<AIEventMonitor> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AI Event Monitor started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAllProcessorsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in AI Event Monitor tick. Will retry in 60 s.");
            }

            await Task.Delay(PollingInterval, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("AI Event Monitor stopped.");
    }

    // ─────────────────────────────────────────────────────────
    //  Top-level dispatcher
    // ─────────────────────────────────────────────────────────

    private async Task RunAllProcessorsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var docService = scope.ServiceProvider.GetRequiredService<IOrgDocumentGenerationService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();

        // Order matters: onboarding first so new employees get docs before birthday/anniversary checks
        await ProcessNewEmployeeOnboardingAsync(db, docService, notificationService, ct);
        await ProcessExitEventsAsync(db, docService, notificationService, ct);
        await ProcessBirthdaysAsync(db, docService, notificationService, ct);
        await ProcessWorkAnniversariesAsync(db, docService, notificationService, ct);
        await ProcessProbationCompletionAsync(db, docService, notificationService, ct);
    }

    // ─────────────────────────────────────────────────────────
    //  1. New Employee Onboarding
    //     Generates: Offer Letter + Appointment Letter (batch)
    //     Sends:     ONE consolidated notification to the employee
    //     Guard:     Employee.OnboardingDocumentsProcessed flag
    // ─────────────────────────────────────────────────────────

    private async Task ProcessNewEmployeeOnboardingAsync(
        ApplicationDbContext db,
        IOrgDocumentGenerationService docService,
        NotificationService notificationService,
        CancellationToken ct)
    {
        // Only employees created within the last 7 days who haven't been processed yet.
        var cutoff = DateTime.UtcNow.AddDays(-7);

        var newEmployees = await db.Employees
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .Include(e => e.Organization)
            .Where(e => e.Status == "Active"
                        && !e.OnboardingDocumentsProcessed
                        && e.CreatedAt >= cutoff)
            .ToListAsync(ct);

        foreach (var employee in newEmployees)
        {
            // Set the flag FIRST — prevents re-entry if anything below throws.
            employee.OnboardingDocumentsProcessed = true;
            await db.SaveChangesAsync(ct);

            var orgAdmin = await GetSystemActorAsync(db, employee.OrganizationId, ct);
            if (orgAdmin == null)
            {
                _logger.LogWarning("No OrgAdmin found for org {OrgId}; skipping onboarding docs for {EmpName}.",
                    employee.OrganizationId, employee.FirstName);
                continue;
            }

            var onboardingTemplateNames = new[] { "Offer Letter", "Appointment Letter" };
            var generatedDocNames = new List<string>();

            foreach (var templateName in onboardingTemplateNames)
            {
                var template = await db.OrgDocumentTemplates
                    .FirstOrDefaultAsync(t =>
                        t.OrganizationId == employee.OrganizationId &&
                        t.Name == templateName &&
                        t.IsActive, ct);

                if (template == null) continue;

                // Idempotency: don't re-generate if already exists.
                var alreadyExists = await db.OrgGeneratedDocuments
                    .AnyAsync(d =>
                        d.EmployeeId == employee.EmployeeId &&
                        d.TemplateId == template.TemplateId &&
                        !d.IsDeleted, ct);

                if (alreadyExists) continue;

                try
                {
                    await docService.GenerateDocumentSystemAsync(
                        template.TemplateId,
                        employee.EmployeeId,
                        orgAdmin.Id,
                        "OrgAdmin");

                    generatedDocNames.Add(templateName);
                    _logger.LogInformation("Onboarding doc '{Doc}' generated for {EmpName}.",
                        templateName, employee.FirstName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate '{Doc}' for employee {EmpId}.",
                        templateName, employee.EmployeeId);
                }
            }

            // ONE consolidated notification — only if we actually generated something.
            if (generatedDocNames.Any())
            {
                var docList = string.Join(", ", generatedDocNames);
                var message = $"Welcome to the team! Your onboarding document{(generatedDocNames.Count > 1 ? "s are" : " is")} ready: {docList}. " +
                              $"Please review them in the Documents section.";

                await notificationService.CreateNotificationAsync(
                    employee.UserId,
                    "Welcome — Your Onboarding Documents Are Ready",
                    message,
                    "/employee/docu");
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    //  2. Exit Events
    //     Approved → Experience Letter (one doc, one notification)
    //     Completed → Relieving Letter  (one doc, one notification)
    //     Guard: ExperienceLetterIssued / RelievingLetterIssued on EmployeeExit
    // ─────────────────────────────────────────────────────────

    private async Task ProcessExitEventsAsync(
        ApplicationDbContext db,
        IOrgDocumentGenerationService docService,
        NotificationService notificationService,
        CancellationToken ct)
    {
        // 2a. Approved exits → Experience Letter
        var approvedExits = await db.EmployeeExits
            .Include(e => e.Employee).ThenInclude(emp => emp.Department)
            .Include(e => e.Employee).ThenInclude(emp => emp.Designation)
            .Where(e => e.Status == "Approved" && !e.ExperienceLetterIssued)
            .ToListAsync(ct);

        foreach (var exit in approvedExits)
        {
            // Flag first — idempotency
            exit.ExperienceLetterIssued = true;
            await db.SaveChangesAsync(ct);

            var template = await db.OrgDocumentTemplates
                .FirstOrDefaultAsync(t =>
                    t.OrganizationId == exit.OrganizationId &&
                    t.Name == "Experience Letter" &&
                    t.IsActive, ct);

            if (template == null)
            {
                _logger.LogWarning("No active 'Experience Letter' template for org {OrgId}.", exit.OrganizationId);
                continue;
            }

            var alreadyExists = await db.OrgGeneratedDocuments
                .AnyAsync(d =>
                    d.EmployeeId == exit.EmployeeId &&
                    d.TemplateId == template.TemplateId &&
                    !d.IsDeleted, ct);

            if (alreadyExists) continue;

            var orgAdmin = await GetSystemActorAsync(db, exit.OrganizationId, ct);
            if (orgAdmin == null) continue;

            var tenureYears = Math.Max(0, (exit.LastWorkingDay - exit.Employee.JoiningDate).Days / 365);

            try
            {
                await docService.GenerateDocumentSystemAsync(
                    template.TemplateId,
                    exit.EmployeeId,
                    orgAdmin.Id,
                    "OrgAdmin",
                    customData: new Dictionary<string, string>
                    {
                        { "LastWorkingDay", exit.LastWorkingDay.ToString("dd MMM yyyy") },
                        { "Reason", exit.Reason ?? "Resignation" },
                        { "TenureYears", tenureYears.ToString() },
                        { "JoiningDate", exit.Employee.JoiningDate.ToString("dd MMM yyyy") },
                        { "Designation", exit.Employee.Designation?.Name ?? "N/A" },
                        { "Department", exit.Employee.Department?.Name ?? "N/A" }
                    });

                await notificationService.CreateNotificationAsync(
                    exit.Employee.UserId,
                    "Your Resignation Has Been Approved",
                    $"Your resignation has been approved and your Experience Letter is ready. " +
                    $"Last working day: {exit.LastWorkingDay:dd MMM yyyy}. " +
                    $"Please complete the clearance process.",
                    "/employee/docu");

                _logger.LogInformation("Experience Letter generated for {EmpName}.", exit.Employee.FirstName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate Experience Letter for exit {ExitId}.", exit.ExitId);
                // Revert flag so it retries on the next tick.
                exit.ExperienceLetterIssued = false;
                await db.SaveChangesAsync(ct);
            }
        }

        // 2b. Completed exits → Relieving Letter
        var completedExits = await db.EmployeeExits
            .Include(e => e.Employee).ThenInclude(emp => emp.Designation)
            .Where(e => e.Status == "Completed" && !e.RelievingLetterIssued)
            .ToListAsync(ct);

        foreach (var exit in completedExits)
        {
            exit.RelievingLetterIssued = true;
            await db.SaveChangesAsync(ct);

            var template = await db.OrgDocumentTemplates
                .FirstOrDefaultAsync(t =>
                    t.OrganizationId == exit.OrganizationId &&
                    t.Name == "Relieving Letter" &&
                    t.IsActive, ct);

            if (template == null) continue;

            var alreadyExists = await db.OrgGeneratedDocuments
                .AnyAsync(d =>
                    d.EmployeeId == exit.EmployeeId &&
                    d.TemplateId == template.TemplateId &&
                    !d.IsDeleted, ct);

            if (alreadyExists) continue;

            var orgAdmin = await GetSystemActorAsync(db, exit.OrganizationId, ct);
            if (orgAdmin == null) continue;

            try
            {
                await docService.GenerateDocumentSystemAsync(
                    template.TemplateId,
                    exit.EmployeeId,
                    orgAdmin.Id,
                    "OrgAdmin",
                    customData: new Dictionary<string, string>
                    {
                        { "LastWorkingDay", exit.LastWorkingDay.ToString("dd MMM yyyy") },
                        { "EmployeeCode", exit.Employee.EmployeeCode ?? "N/A" },
                        { "Designation", exit.Employee.Designation?.Name ?? "N/A" }
                    });

                await notificationService.CreateNotificationAsync(
                    exit.Employee.UserId,
                    "Your Exit Process Is Complete",
                    "Your exit formalities are complete. Your Relieving Letter is ready in the Documents section. " +
                    "We wish you all the best in your future endeavours.",
                    "/employee/docu");

                _logger.LogInformation("Relieving Letter generated for {EmpName}.", exit.Employee.FirstName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate Relieving Letter for exit {ExitId}.", exit.ExitId);
                exit.RelievingLetterIssued = false;
                await db.SaveChangesAsync(ct);
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    //  3. Birthdays
    //     Fires ONCE per calendar year per employee.
    //     Guard: Employee.LastBirthdayNotificationSent (set BEFORE notif)
    //     Doc: Birthday Card (only for milestone birthdays: 25, 30, 40, 50...)
    //     Notif: always one clean message, no doc link unless card generated
    // ─────────────────────────────────────────────────────────

    private static readonly HashSet<int> BirthdayMilestones = new() { 25, 30, 35, 40, 45, 50, 55, 60 };

    private async Task ProcessBirthdaysAsync(
        ApplicationDbContext db,
        IOrgDocumentGenerationService docService,
        NotificationService notificationService,
        CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;

        var employeesWithBirthdayToday = await db.Employees
            .Where(e => e.Status == "Active"
                        && e.DateOfBirth.HasValue
                        && e.DateOfBirth.Value.Month == today.Month
                        && e.DateOfBirth.Value.Day == today.Day
                        && (!e.LastBirthdayNotificationSent.HasValue
                            || e.LastBirthdayNotificationSent.Value.Date < today))
            .ToListAsync(ct);

        foreach (var employee in employeesWithBirthdayToday)
        {
            // Set flag FIRST — safe against re-entry on crash after this line.
            employee.LastBirthdayNotificationSent = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var age = today.Year - employee.DateOfBirth!.Value.Year;
            var isMilestone = BirthdayMilestones.Contains(age);

            string message;

            if (isMilestone)
            {
                // Generate a Birthday Card for milestone birthdays only.
                var alreadyHasCard = await db.OrgGeneratedDocuments
                    .AnyAsync(d =>
                        d.EmployeeId == employee.EmployeeId &&
                        d.DocumentName == "Birthday Card" &&
                        d.GeneratedAt.Year == today.Year &&
                        !d.IsDeleted, ct);

                if (!alreadyHasCard)
                {
                    var template = await db.OrgDocumentTemplates
                        .FirstOrDefaultAsync(t =>
                            t.OrganizationId == employee.OrganizationId &&
                            t.Name == "Birthday Card" &&
                            t.IsActive, ct);

                    if (template != null)
                    {
                        var orgAdmin = await GetSystemActorAsync(db, employee.OrganizationId, ct);
                        if (orgAdmin != null)
                        {
                            try
                            {
                                await docService.GenerateDocumentSystemAsync(
                                    template.TemplateId,
                                    employee.EmployeeId,
                                    orgAdmin.Id,
                                    "OrgAdmin",
                                    customData: new Dictionary<string, string>
                                    {
                                        { "Age", age.ToString() }
                                    });
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to generate Birthday Card for {EmpName}.", employee.FirstName);
                            }
                        }
                    }
                }

                message = $"Happy {age}th Birthday, {employee.FirstName}! " +
                          "Wishing you a wonderful day. A special birthday card has been added to your Documents.";
            }
            else
            {
                message = $"Happy Birthday, {employee.FirstName}! " +
                          "The entire team wishes you a fantastic day ahead. Have a wonderful celebration!";
            }

            await notificationService.CreateNotificationAsync(
                employee.UserId,
                $"Happy Birthday, {employee.FirstName}!",
                message,
                isMilestone ? "/employee/docu" : null);

            _logger.LogInformation("Birthday notification sent to {EmpName} (age {Age}).", employee.FirstName, age);
        }
    }

    // ─────────────────────────────────────────────────────────
    //  4. Work Anniversaries
    //     Fires ONCE per calendar year per employee.
    //     Guard: db.Notifications check scoped to current year
    //     Doc: Anniversary Certificate only for milestone years (1, 5, 10, 15, 20, 25...)
    //     Notif: ONE message, no doc link unless cert generated
    // ─────────────────────────────────────────────────────────

    private static readonly HashSet<int> AnniversaryMilestones = new() { 1, 5, 10, 15, 20, 25, 30 };

    private async Task ProcessWorkAnniversariesAsync(
        ApplicationDbContext db,
        IOrgDocumentGenerationService docService,
        NotificationService notificationService,
        CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var yearStart = new DateTime(today.Year, 1, 1);
        var yearEnd = yearStart.AddYears(1);

        var employeesWithAnniversaryToday = await db.Employees
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .Where(e => e.Status == "Active"
                        && e.JoiningDate.Month == today.Month
                        && e.JoiningDate.Day == today.Day
                        && e.JoiningDate.Year < today.Year)
            .ToListAsync(ct);

        foreach (var employee in employeesWithAnniversaryToday)
        {
            var years = today.Year - employee.JoiningDate.Year;

            // Idempotency: already sent this calendar year?
            var alreadySent = await db.Notifications
                .AnyAsync(n =>
                    n.UserId == employee.UserId &&
                    n.Title.StartsWith("Work Anniversary") &&
                    n.CreatedAt >= yearStart &&
                    n.CreatedAt < yearEnd, ct);

            if (alreadySent)
            {
                _logger.LogDebug("Anniversary notification already sent for {EmpName} in {Year}.", employee.FirstName, today.Year);
                continue;
            }

            var isMilestone = AnniversaryMilestones.Contains(years);
            bool certGenerated = false;

            if (isMilestone)
            {
                var alreadyHasCert = await db.OrgGeneratedDocuments
                    .AnyAsync(d =>
                        d.EmployeeId == employee.EmployeeId &&
                        d.DocumentName == "Anniversary Certificate" &&
                        d.GeneratedAt.Year == today.Year &&
                        !d.IsDeleted, ct);

                if (!alreadyHasCert)
                {
                    var template = await db.OrgDocumentTemplates
                        .FirstOrDefaultAsync(t =>
                            t.OrganizationId == employee.OrganizationId &&
                            t.Name == "Anniversary Certificate" &&
                            t.IsActive, ct);

                    if (template != null)
                    {
                        var orgAdmin = await GetSystemActorAsync(db, employee.OrganizationId, ct);
                        if (orgAdmin != null)
                        {
                            try
                            {
                                await docService.GenerateDocumentSystemAsync(
                                    template.TemplateId,
                                    employee.EmployeeId,
                                    orgAdmin.Id,
                                    "OrgAdmin",
                                    customData: new Dictionary<string, string>
                                    {
                                        { "Years", years.ToString() },
                                        { "Designation", employee.Designation?.Name ?? "Employee" },
                                        { "Department", employee.Department?.Name ?? "General" }
                                    });

                                certGenerated = true;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to generate Anniversary Certificate for {EmpName}.", employee.FirstName);
                            }
                        }
                    }
                }
                else
                {
                    certGenerated = true; // cert already existed from before
                }
            }

            var suffix = years == 1 ? "year" : "years";
            var message = certGenerated
                ? $"Congratulations on completing {years} {suffix} with us, {employee.FirstName}! " +
                  "Thank you for your dedication and contribution. " +
                  "A recognition certificate has been added to your Documents."
                : $"Congratulations on completing {years} {suffix} with us, {employee.FirstName}! " +
                  "Thank you for your continued commitment and hard work. We truly appreciate you!";

            await notificationService.CreateNotificationAsync(
                employee.UserId,
                $"Work Anniversary — {years} {suffix.Substring(0, 1).ToUpper() + suffix.Substring(1)} with Us",
                message,
                certGenerated ? "/employee/docu" : null);

            _logger.LogInformation("Work anniversary notification sent to {EmpName} ({Years} years).", employee.FirstName, years);
        }
    }

    // ─────────────────────────────────────────────────────────
    //  5. Probation Completion (6 months)
    //     Guard: existing Confirmation Letter in OrgGeneratedDocuments
    //     Doc: Confirmation Letter
    //     Notif: ONE message
    // ─────────────────────────────────────────────────────────

    private async Task ProcessProbationCompletionAsync(
        ApplicationDbContext db,
        IOrgDocumentGenerationService docService,
        NotificationService notificationService,
        CancellationToken ct)
    {
        // Employees who crossed the 6-month mark in the last 7 days (to handle weekend/holiday gaps).
        var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
        var windowStart = sixMonthsAgo.AddDays(-7);

        var probationEmployees = await db.Employees
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .Where(e => e.Status == "Active"
                        && e.JoiningDate <= sixMonthsAgo
                        && e.JoiningDate > windowStart)
            .ToListAsync(ct);

        foreach (var employee in probationEmployees)
        {
            var alreadyIssued = await db.OrgGeneratedDocuments
                .AnyAsync(d =>
                    d.EmployeeId == employee.EmployeeId &&
                    d.DocumentName == "Confirmation Letter" &&
                    !d.IsDeleted, ct);

            if (alreadyIssued) continue;

            var template = await db.OrgDocumentTemplates
                .FirstOrDefaultAsync(t =>
                    t.OrganizationId == employee.OrganizationId &&
                    t.Name == "Confirmation Letter" &&
                    t.IsActive, ct);

            if (template == null) continue;

            var orgAdmin = await GetSystemActorAsync(db, employee.OrganizationId, ct);
            if (orgAdmin == null) continue;

            try
            {
                var confirmationDate = DateTime.UtcNow.AddDays(7);

                await docService.GenerateDocumentSystemAsync(
                    template.TemplateId,
                    employee.EmployeeId,
                    orgAdmin.Id,
                    "OrgAdmin",
                    customData: new Dictionary<string, string>
                    {
                        { "ConfirmationDate", confirmationDate.ToString("dd MMM yyyy") },
                        { "Designation", employee.Designation?.Name ?? "Employee" },
                        { "Department", employee.Department?.Name ?? "General" }
                    });

                await notificationService.CreateNotificationAsync(
                    employee.UserId,
                    "Congratulations — Probation Period Completed",
                    $"You have successfully completed your probation period, {employee.FirstName}! " +
                    $"Your Confirmation Letter is ready and your employment has been confirmed effective {confirmationDate:dd MMM yyyy}.",
                    "/employee/docu");

                _logger.LogInformation("Confirmation Letter generated for {EmpName}.", employee.FirstName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate Confirmation Letter for {EmpId}.", employee.EmployeeId);
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    //  Shared helpers
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the first OrgAdmin user for the given organization.
    /// Used as the "actor" for system-generated documents.
    /// </summary>
    private static async Task<ApplicationUser?> GetSystemActorAsync(
        ApplicationDbContext db,
        Guid organizationId,
        CancellationToken ct)
    {
        return await (
            from u in db.Users
            join ur in db.UserRoles on u.Id equals ur.UserId
            join r in db.Roles on ur.RoleId equals r.Id
            where u.OrganizationId == organizationId && r.Name == "OrgAdmin"
            select u
        ).FirstOrDefaultAsync(ct);
    }
}