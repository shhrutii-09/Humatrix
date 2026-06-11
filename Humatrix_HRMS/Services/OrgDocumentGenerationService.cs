// Services/Documents/OrgDocumentGenerationService.cs
// Production-grade document generation service.
//
// Key design decisions:
//   • Uses IDbContextFactory<ApplicationDbContext> throughout — no scoped DbContext leak.
//   • GenerateDocumentSystemAsync is a dedicated method for background/auto-generation:
//     it does NOT send any notification. The caller (AIEventMonitor) owns the notification.
//   • GenerateDocumentAsync (manual, HR/OrgAdmin-initiated) sends ONE in-app notification.
//   • GenerateDocumentWithAIAsync (manual, AI-enhanced) sends ONE in-app notification.
//   • No duplicate document-number generation inside GenerateDocumentFileAsync.
//   • CanGenerateDocumentForEmployeeAsync is pure and does not mutate state.

using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs.Documents;
using Humatrix_HRMS.Models;
using Humatrix_HRMS.Models.Documents;
using Humatrix_HRMS.Services.AI;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Humatrix_HRMS.Services.Documents;

public class OrgDocumentGenerationService : IOrgDocumentGenerationService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly NotificationService _notificationService;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly IAIDocumentService _aiDocumentService;

    public OrgDocumentGenerationService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        UserManager<ApplicationUser> userManager,
        NotificationService notificationService,
        IWebHostEnvironment webHostEnvironment,
        IAIDocumentService aiDocumentService)
    {
        _dbFactory = dbFactory;
        _userManager = userManager;
        _notificationService = notificationService;
        _webHostEnvironment = webHostEnvironment;
        _aiDocumentService = aiDocumentService;
    }

    // ══════════════════════════════════════════════════════════
    //  TEMPLATE MANAGEMENT
    // ══════════════════════════════════════════════════════════

    public async Task<OrgDocumentTemplate> CreateTemplateAsync(
        OrgDocumentTemplateDto dto,
        Guid organizationId,
        string userId,
        string userRole)
    {
        using var db = _dbFactory.CreateDbContext();

        var exists = await db.OrgDocumentTemplates.AnyAsync(x =>
            x.OrganizationId == organizationId && x.Name == dto.Name);

        if (exists)
            throw new InvalidOperationException($"A template named '{dto.Name}' already exists.");

        var template = new OrgDocumentTemplate
        {
            TemplateId = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = dto.Name,
            Description = dto.Description,
            Category = dto.Category,
            TemplateContent = dto.TemplateContent,
            PlaceholderSchema = dto.PlaceholderDescriptions != null
                ? JsonSerializer.Serialize(dto.PlaceholderDescriptions) : null,
            IsActive = true,
            DisplayOrder = dto.DisplayOrder,
            RequiresAcknowledgment = dto.RequiresAcknowledgment,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId
        };

        db.OrgDocumentTemplates.Add(template);
        await db.SaveChangesAsync();
        return template;
    }

    public async Task<OrgDocumentTemplate> UpdateTemplateAsync(
        Guid templateId,
        UpdateOrgDocumentTemplateDto dto,
        Guid organizationId,
        string userId)
    {
        using var db = _dbFactory.CreateDbContext();

        var template = await db.OrgDocumentTemplates.FirstOrDefaultAsync(x =>
            x.TemplateId == templateId && x.OrganizationId == organizationId)
            ?? throw new InvalidOperationException("Template not found.");

        var nameConflict = await db.OrgDocumentTemplates.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.TemplateId != templateId &&
            x.Name == dto.Name);

        if (nameConflict)
            throw new InvalidOperationException($"A template named '{dto.Name}' already exists.");

        template.Name = dto.Name;
        template.Description = dto.Description;
        template.Category = dto.Category;
        template.TemplateContent = dto.TemplateContent;
        template.PlaceholderSchema = dto.PlaceholderDescriptions != null
            ? JsonSerializer.Serialize(dto.PlaceholderDescriptions) : null;
        template.DisplayOrder = dto.DisplayOrder;
        template.IsActive = dto.IsActive;
        template.RequiresAcknowledgment = dto.RequiresAcknowledgment;
        template.UpdatedAt = DateTime.UtcNow;
        template.UpdatedByUserId = userId;

        await db.SaveChangesAsync();
        return template;
    }

    public async Task<bool> DeleteTemplateAsync(Guid templateId, Guid organizationId)
    {
        using var db = _dbFactory.CreateDbContext();

        var template = await db.OrgDocumentTemplates.FirstOrDefaultAsync(x =>
            x.TemplateId == templateId && x.OrganizationId == organizationId);

        if (template == null) return false;

        var isUsed = await db.OrgGeneratedDocuments.AnyAsync(x => x.TemplateId == templateId);
        if (isUsed)
            throw new InvalidOperationException("Cannot delete a template that has generated documents.");

        db.OrgDocumentTemplates.Remove(template);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<OrgDocumentTemplate?> GetTemplateByIdAsync(Guid templateId, Guid organizationId)
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.OrgDocumentTemplates.FirstOrDefaultAsync(x =>
            x.TemplateId == templateId &&
            x.OrganizationId == organizationId &&
            x.IsActive);
    }

    public async Task<List<OrgDocumentTemplate>> GetTemplatesByCategoryAsync(Guid organizationId, string category)
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.OrgDocumentTemplates
            .Where(x => x.OrganizationId == organizationId && x.Category == category && x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .ToListAsync();
    }

    public async Task<List<OrgDocumentTemplate>> GetAllTemplatesAsync(Guid organizationId, bool includeInactive = false)
    {
        using var db = _dbFactory.CreateDbContext();
        var query = db.OrgDocumentTemplates.Where(x => x.OrganizationId == organizationId);
        if (!includeInactive) query = query.Where(x => x.IsActive);
        return await query.OrderBy(x => x.Category).ThenBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync();
    }

    // ══════════════════════════════════════════════════════════
    //  DOCUMENT GENERATION — MANUAL (HR / OrgAdmin initiated)
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Manual generation from UI. Validates permissions, replaces placeholders from
    /// the stored template, saves the document, and sends ONE in-app notification.
    /// </summary>
    public async Task<OrgDocumentGenerationResponseDto> GenerateDocumentAsync(
        OrgDocumentGenerationDto dto,
        Guid organizationId,
        string userId,
        string userRole)
    {
        var canGenerate = await CanGenerateDocumentForEmployeeAsync(
            dto.RecipientEmployeeId, organizationId, userId, userRole);

        if (!canGenerate)
            throw new UnauthorizedAccessException(
                "You do not have permission to generate documents for this employee.");

        using var db = _dbFactory.CreateDbContext();

        var template = await db.OrgDocumentTemplates.FirstOrDefaultAsync(x =>
            x.TemplateId == dto.TemplateId &&
            x.OrganizationId == organizationId &&
            x.IsActive)
            ?? throw new InvalidOperationException("Template not found or inactive.");

        var recipient = await db.Employees
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.EmployeeId == dto.RecipientEmployeeId)
            ?? throw new InvalidOperationException("Recipient employee not found.");

        var actor = await db.Employees.FirstOrDefaultAsync(e => e.UserId == userId);
        var actorName = actor != null ? $"{actor.FirstName} {actor.LastName}" : userRole;

        var placeholderData = BuildPlaceholderData(recipient, actorName, dto.CustomData);
        var processedContent = ReplacePlaceholders(template.TemplateContent, placeholderData);

        var documentNumber = GenerateDocumentNumber(template.Name, recipient.EmployeeCode);
        var (fileName, filePath, fullPath) = await GenerateDocumentFileAsync(
            processedContent, template.Name, recipient.EmployeeCode ?? recipient.EmployeeId.ToString()[..8], documentNumber);

        var document = BuildDocumentRecord(
            organizationId, recipient.EmployeeId, template.TemplateId,
            documentNumber, template.Name, dto.Remarks ?? $"Generated by {actorName}",
            fileName, recipient, filePath, fullPath, userId, userRole, placeholderData);

        db.OrgGeneratedDocuments.Add(document);
        db.OrgDocumentHistories.Add(BuildHistory(document.DocumentId, "Generated", userId, userRole,
            $"Document generated using template '{template.Name}'"));
        await db.SaveChangesAsync();

        // ONE in-app notification to the employee.
        await _notificationService.CreateNotificationAsync(
            recipient.UserId,
            $"New Document Issued: {template.Name}",
            $"A new document '{template.Name}' (Ref: {documentNumber}) has been issued to you by {actorName}. " +
            $"Please review it in the Documents section.",
            "/employee/docu");

        return MapToResponseDto(document, recipient, actorName);
    }

    /// <summary>
    /// Manual generation from UI, with AI-generated content.
    /// Sends ONE in-app notification.
    /// </summary>
    public async Task<OrgDocumentGenerationResponseDto> GenerateDocumentWithAIAsync(
        Guid templateId,
        Guid recipientEmployeeId,
        string userId,
        string userRole,
        Dictionary<string, string>? customData = null,
        bool sendEmail = false)
    {
        using var db = _dbFactory.CreateDbContext();

        var recipient = await db.Employees
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.EmployeeId == recipientEmployeeId)
            ?? throw new InvalidOperationException("Recipient employee not found.");

        var template = await db.OrgDocumentTemplates.FirstOrDefaultAsync(x =>
            x.TemplateId == templateId &&
            x.OrganizationId == recipient.OrganizationId &&
            x.IsActive)
            ?? throw new InvalidOperationException("Template not found or inactive.");

        // Idempotency guard: prevent duplicate generation within 5 minutes.
        var recentExists = await db.OrgGeneratedDocuments.AnyAsync(d =>
            d.EmployeeId == recipientEmployeeId &&
            d.TemplateId == templateId &&
            d.GeneratedAt > DateTime.UtcNow.AddMinutes(-5));

        if (recentExists)
            throw new InvalidOperationException("This document was already generated recently. Please wait a few minutes.");

        var actor = await db.Employees.FirstOrDefaultAsync(e => e.UserId == userId);
        var actorName = actor != null ? $"{actor.FirstName} {actor.LastName}" : userRole;

        var aiContent = await _aiDocumentService.GenerateDocumentContentAsync(
            template.Name, template.Category, recipient, customData, actorName);

        var placeholderData = BuildPlaceholderData(recipient, actorName, customData);
        var processedContent = ReplacePlaceholders(aiContent, placeholderData);

        var documentNumber = GenerateDocumentNumber(template.Name, recipient.EmployeeCode);
        var (fileName, filePath, fullPath) = await GenerateDocumentFileAsync(
            processedContent, template.Name, recipient.EmployeeCode ?? recipient.EmployeeId.ToString()[..8], documentNumber);

        var document = BuildDocumentRecord(
            recipient.OrganizationId, recipient.EmployeeId, template.TemplateId,
            documentNumber, template.Name, $"AI-generated by {actorName}",
            fileName, recipient, filePath, fullPath, userId, userRole, placeholderData,
            originalFileNameSuffix: "_AI");

        db.OrgGeneratedDocuments.Add(document);
        db.OrgDocumentHistories.Add(BuildHistory(document.DocumentId, "Generated (AI)", userId, userRole,
            $"AI-generated document using template '{template.Name}'"));
        await db.SaveChangesAsync();

        // ONE notification.
        await _notificationService.CreateNotificationAsync(
            recipient.UserId,
            $"New Document Issued: {template.Name}",
            $"A new document '{template.Name}' (Ref: {documentNumber}) has been issued to you by {actorName}. " +
            $"Please review it in the Documents section.",
            "/employee/docu");

        return MapToResponseDto(document, recipient, actorName);
    }

    // ══════════════════════════════════════════════════════════
    //  DOCUMENT GENERATION — SYSTEM (Background / AIEventMonitor)
    //
    //  IMPORTANT: This method does NOT send any notification.
    //  The caller (AIEventMonitor) is responsible for sending
    //  a single consolidated notification after all docs are done.
    // ══════════════════════════════════════════════════════════

    public async Task<OrgDocumentGenerationResponseDto> GenerateDocumentSystemAsync(
        Guid templateId,
        Guid recipientEmployeeId,
        string actorUserId,
        string actorRole,
        Dictionary<string, string>? customData = null)
    {
        using var db = _dbFactory.CreateDbContext();

        var recipient = await db.Employees
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.EmployeeId == recipientEmployeeId)
            ?? throw new InvalidOperationException($"Employee {recipientEmployeeId} not found.");

        var template = await db.OrgDocumentTemplates.FirstOrDefaultAsync(x =>
            x.TemplateId == templateId &&
            x.OrganizationId == recipient.OrganizationId &&
            x.IsActive)
            ?? throw new InvalidOperationException($"Template {templateId} not found or inactive.");

        // Idempotency: exact same template + employee combo already exists?
        var alreadyExists = await db.OrgGeneratedDocuments.AnyAsync(d =>
            d.EmployeeId == recipientEmployeeId &&
            d.TemplateId == templateId &&
            !d.IsDeleted);

        if (alreadyExists)
            throw new InvalidOperationException(
                $"Document '{template.Name}' already exists for this employee.");

        // Resolve actor name from the database rather than using a raw "SYSTEM" string.
        var actor = await db.Employees.FirstOrDefaultAsync(e => e.UserId == actorUserId);
        var actorName = actor != null ? $"{actor.FirstName} {actor.LastName}" : "HR Department";

        // Use AI to generate content for a richer output.
        string processedContent;
        try
        {
            var aiContent = await _aiDocumentService.GenerateDocumentContentAsync(
                template.Name, template.Category, recipient, customData, actorName);
            var placeholderData = BuildPlaceholderData(recipient, actorName, customData);
            processedContent = ReplacePlaceholders(aiContent, placeholderData);
        }
        catch
        {
            // Fallback to template-based generation if AI is unavailable.
            var placeholderData = BuildPlaceholderData(recipient, actorName, customData);
            processedContent = ReplacePlaceholders(template.TemplateContent, placeholderData);
        }

        var documentNumber = GenerateDocumentNumber(template.Name, recipient.EmployeeCode);
        var (fileName, filePath, fullPath) = await GenerateDocumentFileAsync(
            processedContent, template.Name, recipient.EmployeeCode ?? recipient.EmployeeId.ToString()[..8], documentNumber);

        var finalPlaceholders = BuildPlaceholderData(recipient, actorName, customData);
        var document = BuildDocumentRecord(
            recipient.OrganizationId, recipient.EmployeeId, template.TemplateId,
            documentNumber, template.Name, $"Auto-generated by system",
            fileName, recipient, filePath, fullPath, actorUserId, actorRole, finalPlaceholders);

        db.OrgGeneratedDocuments.Add(document);
        db.OrgDocumentHistories.Add(BuildHistory(document.DocumentId, "Generated (System)", actorUserId, actorRole,
            $"System auto-generated using template '{template.Name}'"));
        await db.SaveChangesAsync();

        return MapToResponseDto(document, recipient, actorName);
    }

    // ══════════════════════════════════════════════════════════
    //  BULK GENERATION
    // ══════════════════════════════════════════════════════════

    public async Task<List<OrgDocumentGenerationResponseDto>> GenerateDocumentsBulkAsync(
        Guid templateId,
        List<Guid> employeeIds,
        Dictionary<string, string>? commonCustomData,
        Guid organizationId,
        string userId,
        string userRole)
    {
        var results = new List<OrgDocumentGenerationResponseDto>();
        var errors = new List<string>();

        foreach (var employeeId in employeeIds)
        {
            try
            {
                var result = await GenerateDocumentAsync(
                    new OrgDocumentGenerationDto
                    {
                        TemplateId = templateId,
                        RecipientEmployeeId = employeeId,
                        CustomData = commonCustomData,
                        SendEmailNotification = false // bulk: no per-doc notifications
                    },
                    organizationId, userId, userRole);

                results.Add(result);
            }
            catch (Exception ex)
            {
                errors.Add($"Employee {employeeId}: {ex.Message}");
            }
        }

        if (!results.Any() && errors.Any())
            throw new InvalidOperationException($"Bulk generation failed for all employees: {string.Join("; ", errors)}");

        return results;
    }

    // ══════════════════════════════════════════════════════════
    //  VIEWING
    // ══════════════════════════════════════════════════════════

    public async Task<List<OrgGeneratedDocument>> GetDocumentsForEmployeeAsync(Guid employeeId, Guid organizationId)
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.OrgGeneratedDocuments
            .Include(x => x.Template)
            .Where(x => x.EmployeeId == employeeId &&
                        x.OrganizationId == organizationId &&
                        !x.IsDeleted &&
                        x.IsLatestVersion)
            .OrderByDescending(x => x.GeneratedAt)
            .ToListAsync();
    }

    public async Task<List<OrgGeneratedDocument>> GetDocumentsGeneratedByUserAsync(string userId, Guid organizationId)
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.OrgGeneratedDocuments
            .Include(x => x.Template)
            .Include(x => x.Employee)
            .Where(x => x.GeneratedByUserId == userId &&
                        x.OrganizationId == organizationId &&
                        !x.IsDeleted)
            .OrderByDescending(x => x.GeneratedAt)
            .ToListAsync();
    }

    public async Task<OrgGeneratedDocument?> GetDocumentByIdAsync(Guid documentId, Guid organizationId)
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.OrgGeneratedDocuments
            .Include(x => x.Template)
            .Include(x => x.Employee)
            .FirstOrDefaultAsync(x => x.DocumentId == documentId && x.OrganizationId == organizationId);
    }

    // ══════════════════════════════════════════════════════════
    //  ACKNOWLEDGMENT
    // ══════════════════════════════════════════════════════════

    public async Task<bool> AcknowledgeDocumentAsync(Guid documentId, Guid employeeId, string? remarks = null)
    {
        using var db = _dbFactory.CreateDbContext();

        var document = await db.OrgGeneratedDocuments.FirstOrDefaultAsync(x =>
            x.DocumentId == documentId && x.EmployeeId == employeeId);

        if (document == null) return false;
        if (document.Status == "Acknowledged") return true; // idempotent

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId);

        document.Status = "Acknowledged";
        document.AcknowledgedAt = DateTime.UtcNow;
        document.AcknowledgmentRemarks = remarks;

        db.OrgDocumentHistories.Add(BuildHistory(document.DocumentId, "Acknowledged",
            employee?.UserId ?? "SYSTEM", "Employee", remarks: remarks));

        await db.SaveChangesAsync();
        return true;
    }

    // ══════════════════════════════════════════════════════════
    //  REVOKE
    // ══════════════════════════════════════════════════════════

    public async Task<bool> RevokeDocumentAsync(Guid documentId, string userId, string userRole, string reason)
    {
        using var db = _dbFactory.CreateDbContext();

        var document = await db.OrgGeneratedDocuments.FirstOrDefaultAsync(x => x.DocumentId == documentId);
        if (document == null) return false;

        document.Status = "Revoked";
        document.IsLatestVersion = false;

        db.OrgDocumentHistories.Add(BuildHistory(document.DocumentId, "Revoked", userId, userRole, reason));
        await db.SaveChangesAsync();
        return true;
    }

    // ══════════════════════════════════════════════════════════
    //  PERMISSIONS
    // ══════════════════════════════════════════════════════════

    public async Task<bool> CanGenerateDocumentForEmployeeAsync(
        Guid employeeId,
        Guid organizationId,
        string userId,
        string userRole)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        var roles = await _userManager.GetRolesAsync(user);

        if (roles.Contains("OrgAdmin")) return true;

        if (roles.Contains("HR"))
        {
            using var db = _dbFactory.CreateDbContext();

            var currentEmployee = await db.Employees.FirstOrDefaultAsync(e => e.UserId == userId);
            if (currentEmployee == null) return false;

            var targetEmployee = await db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId);
            if (targetEmployee == null) return false;

            // HR cannot generate docs for other HR members.
            var targetUser = await _userManager.FindByIdAsync(targetEmployee.UserId);
            if (targetUser != null)
            {
                var targetRoles = await _userManager.GetRolesAsync(targetUser);
                if (targetRoles.Contains("HR")) return false;
            }

            return targetEmployee.DepartmentId == currentEmployee.DepartmentId;
        }

        return false;
    }

    public async Task<List<Employee>> GetEligibleRecipientsForDocumentAsync(
        Guid organizationId,
        string userId,
        string userRole)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return new List<Employee>();

        var roles = await _userManager.GetRolesAsync(user);
        using var db = _dbFactory.CreateDbContext();

        if (roles.Contains("OrgAdmin"))
        {
            return await db.Employees
                .Include(e => e.Department)
                .Where(e => e.OrganizationId == organizationId && e.Status == "Active")
                .OrderBy(e => e.FirstName)
                .ToListAsync();
        }

        if (roles.Contains("HR"))
        {
            var currentEmployee = await db.Employees.FirstOrDefaultAsync(e => e.UserId == userId);
            if (currentEmployee == null) return new List<Employee>();

            var hrUserIds = await GetHrUserIdsInOrgAsync(db, organizationId);

            return await db.Employees
                .Include(e => e.Department)
                .Where(e => e.OrganizationId == organizationId &&
                            e.DepartmentId == currentEmployee.DepartmentId &&
                            e.Status == "Active" &&
                            !hrUserIds.Contains(e.UserId))
                .OrderBy(e => e.FirstName)
                .ToListAsync();
        }

        return new List<Employee>();
    }

    public async Task<List<OrgDocumentHistory>> GetDocumentHistoryAsync(Guid documentId)
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.OrgDocumentHistories
            .Where(x => x.DocumentId == documentId)
            .OrderByDescending(x => x.PerformedAt)
            .ToListAsync();
    }

    // ══════════════════════════════════════════════════════════
    //  AI SUGGESTIONS (used by the UI)
    // ══════════════════════════════════════════════════════════

    public async Task<List<AIDocumentSuggestion>> GetAIDocumentSuggestionsAsync(Guid employeeId)
    {
        using var db = _dbFactory.CreateDbContext();

        var employee = await db.Employees
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);

        if (employee == null) return new List<AIDocumentSuggestion>();

        return await _aiDocumentService.SuggestDocumentsAsync(employee);
    }

    // ══════════════════════════════════════════════════════════
    //  PRIVATE HELPERS
    // ══════════════════════════════════════════════════════════

    private static Dictionary<string, string> BuildPlaceholderData(
        Employee employee,
        string actorName,
        Dictionary<string, string>? customData)
    {
        var data = new Dictionary<string, string>
        {
            { "EmployeeName",    $"{employee.FirstName} {employee.LastName}" },
            { "FirstName",       employee.FirstName },
            { "LastName",        employee.LastName },
            { "EmployeeCode",    employee.EmployeeCode ?? "N/A" },
            { "Department",      employee.Department?.Name ?? "N/A" },
            { "Designation",     employee.Designation?.Name ?? "N/A" },
            { "JoiningDate",     employee.JoiningDate.ToString("dd MMM yyyy") },
            { "CurrentDate",     DateTime.UtcNow.ToString("dd MMM yyyy") },
            { "CurrentYear",     DateTime.UtcNow.Year.ToString() },
            { "GeneratedBy",     actorName },
            { "OrganizationName", employee.Organization?.Name ?? "Our Organization" }
        };

        if (customData != null)
            foreach (var kvp in customData)
                data[kvp.Key] = kvp.Value;

        return data;
    }

    private static string ReplacePlaceholders(string content, Dictionary<string, string> data)
    {
        var result = content;
        foreach (var kvp in data)
            result = result.Replace($"{{{{{kvp.Key}}}}}", kvp.Value);
        return result;
    }

    private async Task<(string fileName, string filePath, string fullPath)> GenerateDocumentFileAsync(
        string htmlContent,
        string templateName,
        string employeeCode,
        string documentNumber)
    {
        var folder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "documents", "organization");
        Directory.CreateDirectory(folder);

        var safeTemplateName = templateName.Replace(" ", "_");
        var fileName = $"{safeTemplateName}_{employeeCode}_{DateTime.UtcNow:yyyyMMddHHmmss}.html";
        var filePath = $"/uploads/documents/organization/{fileName}";
        var fullPath = Path.Combine(folder, fileName);

        var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <title>{templateName}</title>
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; margin: 0; padding: 40px; line-height: 1.7; color: #333; background: #fff; }}
        .doc-wrapper {{ max-width: 800px; margin: 0 auto; }}
        .doc-meta {{ font-size: 12px; color: #888; text-align: right; margin-bottom: 30px; border-bottom: 1px solid #eee; padding-bottom: 10px; }}
        .doc-content {{ margin: 20px 0; }}
        .doc-footer {{ text-align: center; margin-top: 60px; padding-top: 15px; border-top: 1px solid #eee; font-size: 11px; color: #aaa; }}
        @media print {{ body {{ padding: 20px; }} .doc-footer {{ position: fixed; bottom: 0; width: 100%; }} }}
    </style>
</head>
<body>
    <div class=""doc-wrapper"">
        <div class=""doc-meta"">
            Document Number: {documentNumber} | Generated: {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC
        </div>
        <div class=""doc-content"">
            {htmlContent}
        </div>
        <div class=""doc-footer"">
            This is a system-generated document. For queries, please contact HR.
        </div>
    </div>
</body>
</html>";

        await File.WriteAllTextAsync(fullPath, html);
        return (fileName, filePath, fullPath);
    }

    private static string GenerateDocumentNumber(string templateName, string? employeeCode)
    {
        var prefix = new string(templateName.Split(' ')
            .Where(w => w.Length > 0)
            .Select(w => char.ToUpper(w[0]))
            .ToArray()); // e.g. "Offer Letter" → "OL"

        var code = string.IsNullOrEmpty(employeeCode) ? "EMP" : employeeCode;
        return $"{prefix}-{code}-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..4].ToUpper()}";
    }

    private static OrgGeneratedDocument BuildDocumentRecord(
        Guid organizationId,
        Guid employeeId,
        Guid templateId,
        string documentNumber,
        string documentName,
        string description,
        string fileName,
        Employee recipient,
        string filePath,
        string fullPath,
        string generatedByUserId,
        string generatedByRole,
        Dictionary<string, string> placeholderData,
        string originalFileNameSuffix = "")
    {
        return new OrgGeneratedDocument
        {
            DocumentId = Guid.NewGuid(),
            OrganizationId = organizationId,
            EmployeeId = employeeId,
            TemplateId = templateId,
            DocumentNumber = documentNumber,
            DocumentName = documentName,
            Description = description,
            FileName = fileName,
            OriginalFileName = $"{documentName.Replace(" ", "_")}_{recipient.FirstName}_{recipient.LastName}{originalFileNameSuffix}.html",
            FilePath = filePath,
            FileSize = new FileInfo(fullPath).Length,
            MimeType = "text/html",
            Status = "Issued",
            GeneratedByUserId = generatedByUserId,
            GeneratedByRole = generatedByRole,
            GeneratedAt = DateTime.UtcNow,
            IsLatestVersion = true,
            Version = 1,
            ContentSnapshot = JsonSerializer.Serialize(placeholderData)
        };
    }

    private static OrgDocumentHistory BuildHistory(
        Guid documentId,
        string action,
        string userId,
        string role,
        string? remarks = null)
    {
        return new OrgDocumentHistory
        {
            DocumentId = documentId,
            Action = action,
            PerformedByUserId = userId,
            PerformedByRole = role,
            PerformedAt = DateTime.UtcNow,
            Remarks = remarks
        };
    }

    private static OrgDocumentGenerationResponseDto MapToResponseDto(
        OrgGeneratedDocument document,
        Employee recipient,
        string actorName)
    {
        return new OrgDocumentGenerationResponseDto
        {
            DocumentId = document.DocumentId,
            EmployeeId = recipient.EmployeeId,
            EmployeeName = $"{recipient.FirstName} {recipient.LastName}",
            DocumentTypeName = document.DocumentName,
            FilePath = document.FilePath,
            OriginalFileName = document.OriginalFileName,
            GeneratedAt = document.GeneratedAt,
            GeneratedBy = actorName,
            Status = document.Status,
            EmailSent = false
        };
    }

    private static async Task<HashSet<string>> GetHrUserIdsInOrgAsync(ApplicationDbContext db, Guid organizationId)
    {
        var ids = await (
            from u in db.Users
            join ur in db.UserRoles on u.Id equals ur.UserId
            join r in db.Roles on ur.RoleId equals r.Id
            where u.OrganizationId == organizationId && r.Name == "HR"
            select u.Id
        ).ToListAsync();

        return ids.ToHashSet();
    }
}