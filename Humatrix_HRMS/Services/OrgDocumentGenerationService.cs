using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs.Documents;
using Humatrix_HRMS.Models;
using Humatrix_HRMS.Models.Documents;
using Humatrix_HRMS.Services.AI;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Humatrix_HRMS.Services.Documents;

public class OrgDocumentGenerationService : IOrgDocumentGenerationService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly NotificationService _notificationService;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAIDocumentService _aiDocumentService;


    public OrgDocumentGenerationService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        NotificationService notificationService,
        IWebHostEnvironment webHostEnvironment,
        IHttpContextAccessor httpContextAccessor,
        IAIDocumentService aiDocumentService)
    {
        _db = db;
        _userManager = userManager;
        _notificationService = notificationService;
        _webHostEnvironment = webHostEnvironment;
        _httpContextAccessor = httpContextAccessor;
        _aiDocumentService = aiDocumentService;
    }

    // ==================== TEMPLATE MANAGEMENT ====================

    public async Task<OrgDocumentTemplate> CreateTemplateAsync(
        OrgDocumentTemplateDto dto,
        Guid organizationId,
        string userId,
        string userRole)
    {
        var exists = await _db.OrgDocumentTemplates.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.Name == dto.Name);

        if (exists)
            throw new InvalidOperationException($"Template with name '{dto.Name}' already exists.");

        var template = new OrgDocumentTemplate
        {
            TemplateId = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = dto.Name,
            Description = dto.Description,
            Category = dto.Category,
            TemplateContent = dto.TemplateContent,
            PlaceholderSchema = dto.PlaceholderDescriptions != null ? JsonSerializer.Serialize(dto.PlaceholderDescriptions) : null,
            IsActive = true,
            DisplayOrder = dto.DisplayOrder,
            RequiresAcknowledgment = dto.RequiresAcknowledgment,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId
        };

        _db.OrgDocumentTemplates.Add(template);
        await _db.SaveChangesAsync();

        return template;
    }

    public async Task<OrgDocumentTemplate> UpdateTemplateAsync(
        Guid templateId,
        UpdateOrgDocumentTemplateDto dto,
        Guid organizationId,
        string userId)
    {
        var template = await _db.OrgDocumentTemplates.FirstOrDefaultAsync(x =>
            x.TemplateId == templateId &&
            x.OrganizationId == organizationId);

        if (template == null)
            throw new InvalidOperationException("Template not found.");

        var nameExists = await _db.OrgDocumentTemplates.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.TemplateId != templateId &&
            x.Name == dto.Name);

        if (nameExists)
            throw new InvalidOperationException($"Template with name '{dto.Name}' already exists.");

        template.Name = dto.Name;
        template.Description = dto.Description;
        template.Category = dto.Category;
        template.TemplateContent = dto.TemplateContent;
        template.PlaceholderSchema = dto.PlaceholderDescriptions != null ? JsonSerializer.Serialize(dto.PlaceholderDescriptions) : null;
        template.DisplayOrder = dto.DisplayOrder;
        template.IsActive = dto.IsActive;
        template.RequiresAcknowledgment = dto.RequiresAcknowledgment;
        template.UpdatedAt = DateTime.UtcNow;
        template.UpdatedByUserId = userId;

        await _db.SaveChangesAsync();
        return template;
    }

    public async Task<bool> DeleteTemplateAsync(Guid templateId, Guid organizationId)
    {
        var template = await _db.OrgDocumentTemplates.FirstOrDefaultAsync(x =>
            x.TemplateId == templateId &&
            x.OrganizationId == organizationId);

        if (template == null)
            return false;

        var isUsed = await _db.OrgGeneratedDocuments.AnyAsync(x =>
            x.TemplateId == templateId);

        if (isUsed)
            throw new InvalidOperationException("Cannot delete template that has generated documents.");

        _db.OrgDocumentTemplates.Remove(template);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<OrgDocumentTemplate?> GetTemplateByIdAsync(Guid templateId, Guid organizationId)
    {
        return await _db.OrgDocumentTemplates.FirstOrDefaultAsync(x =>
            x.TemplateId == templateId &&
            x.OrganizationId == organizationId &&
            x.IsActive);
    }

    public async Task<List<OrgDocumentTemplate>> GetTemplatesByCategoryAsync(Guid organizationId, string category)
    {
        return await _db.OrgDocumentTemplates
            .Where(x => x.OrganizationId == organizationId &&
                        x.Category == category &&
                        x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();
    }

    public async Task<List<OrgDocumentTemplate>> GetAllTemplatesAsync(Guid organizationId, bool includeInactive = false)
    {
        var query = _db.OrgDocumentTemplates.Where(x => x.OrganizationId == organizationId);

        if (!includeInactive)
            query = query.Where(x => x.IsActive);

        return await query
            .OrderBy(x => x.Category)
            .ThenBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();
    }

    // ==================== DOCUMENT GENERATION ====================

    public async Task<OrgDocumentGenerationResponseDto> GenerateDocumentAsync(
        OrgDocumentGenerationDto dto,
        Guid organizationId,
        string userId,
        string userRole)
    {
        // 1. Validate permissions
        var canGenerate = await CanGenerateDocumentForEmployeeAsync(
            dto.RecipientEmployeeId, organizationId, userId, userRole);

        if (!canGenerate)
            throw new UnauthorizedAccessException("You don't have permission to generate documents for this employee.");

        // 2. Get template
        var template = await GetTemplateByIdAsync(dto.TemplateId, organizationId);
        if (template == null)
            throw new InvalidOperationException("Template not found or inactive.");

        // 3. Get recipient employee
        var recipient = await _db.Employees
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.EmployeeId == dto.RecipientEmployeeId);

        if (recipient == null)
            throw new InvalidOperationException("Recipient employee not found.");

        // 4. Get the actor (who is generating)
        var actor = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == userId);
        var actorName = actor != null ? $"{actor.FirstName} {actor.LastName}" : userRole;

        // 5. Process template content and replace placeholders
        var placeholderData = BuildPlaceholderData(recipient, actorName, dto.CustomData);
        var processedContent = ReplacePlaceholders(template.TemplateContent, placeholderData);

        // 6. Generate HTML file
        var (fileName, filePath, fullPath) = await GenerateDocumentFileAsync(
            processedContent,
            template.Name,
            recipient.EmployeeCode ?? recipient.EmployeeId.ToString().Substring(0, 8));

        // 7. Create document record in OrgGeneratedDocument table
        var documentNumber = GenerateDocumentNumber(template.Name, recipient.EmployeeCode);

        var document = new OrgGeneratedDocument
        {
            DocumentId = Guid.NewGuid(),
            OrganizationId = organizationId,
            EmployeeId = recipient.EmployeeId,
            TemplateId = template.TemplateId,
            DocumentNumber = documentNumber,
            DocumentName = template.Name,
            Description = dto.Remarks ?? $"Generated by {actorName}",
            FileName = fileName,
            OriginalFileName = $"{template.Name}_{recipient.FirstName}_{recipient.LastName}.html",
            FilePath = filePath,
            FileSize = new FileInfo(fullPath).Length,
            MimeType = "text/html",
            Status = "Issued",
            GeneratedByUserId = userId,
            GeneratedByRole = userRole,
            GeneratedAt = DateTime.UtcNow,
            IsLatestVersion = true,
            Version = 1,
            ContentSnapshot = JsonSerializer.Serialize(placeholderData)
        };

        _db.OrgGeneratedDocuments.Add(document);
        await _db.SaveChangesAsync();

        // 8. Add history record
        var history = new OrgDocumentHistory
        {
            DocumentId = document.DocumentId,
            Action = "Generated",
            PerformedByUserId = userId,
            PerformedByRole = userRole,
            PerformedAt = DateTime.UtcNow,
            Remarks = $"Document generated using template '{template.Name}'"
        };
        _db.OrgDocumentHistories.Add(history);
        await _db.SaveChangesAsync();

        // 9. Send notifications
        bool emailSent = false;
        if (dto.SendEmailNotification)
        {
            await SendDocumentNotificationAsync(recipient, document, template.Name);
            emailSent = true;
        }

        await SendInAppNotificationAsync(recipient, document, template.Name);

        return new OrgDocumentGenerationResponseDto
        {
            DocumentId = document.DocumentId,
            EmployeeId = recipient.EmployeeId,
            EmployeeName = $"{recipient.FirstName} {recipient.LastName}",
            DocumentTypeName = template.Name,
            FilePath = document.FilePath,
            OriginalFileName = document.OriginalFileName,
            GeneratedAt = document.GeneratedAt,
            GeneratedBy = actorName,
            Status = document.Status,
            EmailSent = emailSent
        };
    }

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
                        SendEmailNotification = true
                    },
                    organizationId,
                    userId,
                    userRole);

                results.Add(result);
            }
            catch (Exception ex)
            {
                errors.Add($"Employee {employeeId}: {ex.Message}");
            }
        }

        if (errors.Any() && !results.Any())
            throw new InvalidOperationException($"Bulk generation failed: {string.Join("; ", errors)}");

        return results;
    }

    // ==================== VIEWING ====================

    public async Task<List<OrgGeneratedDocument>> GetDocumentsForEmployeeAsync(Guid employeeId, Guid organizationId)
    {
        return await _db.OrgGeneratedDocuments
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
        return await _db.OrgGeneratedDocuments
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
        return await _db.OrgGeneratedDocuments
            .Include(x => x.Template)
            .Include(x => x.Employee)
            .FirstOrDefaultAsync(x => x.DocumentId == documentId &&
                                      x.OrganizationId == organizationId);
    }

    // ==================== ACKNOWLEDGMENT ====================

    public async Task<bool> AcknowledgeDocumentAsync(Guid documentId, Guid employeeId, string? remarks = null)
    {
        var document = await _db.OrgGeneratedDocuments
            .FirstOrDefaultAsync(x => x.DocumentId == documentId &&
                                      x.EmployeeId == employeeId);

        if (document == null)
            return false;

        document.Status = "Acknowledged";
        document.AcknowledgedAt = DateTime.UtcNow;
        document.AcknowledgmentRemarks = remarks;

        var history = new OrgDocumentHistory
        {
            DocumentId = document.DocumentId,
            Action = "Acknowledged",
            PerformedByUserId = (await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId))?.UserId ?? "SYSTEM",
            PerformedByRole = "Employee",
            PerformedAt = DateTime.UtcNow,
            Remarks = remarks
        };
        _db.OrgDocumentHistories.Add(history);

        await _db.SaveChangesAsync();
        return true;
    }

    // ==================== REVOKE DOCUMENT ====================

    public async Task<bool> RevokeDocumentAsync(Guid documentId, string userId, string userRole, string reason)
    {
        var document = await _db.OrgGeneratedDocuments
            .FirstOrDefaultAsync(x => x.DocumentId == documentId);

        if (document == null)
            return false;

        document.Status = "Revoked";
        document.IsLatestVersion = false;

        var history = new OrgDocumentHistory
        {
            DocumentId = document.DocumentId,
            Action = "Revoked",
            PerformedByUserId = userId,
            PerformedByRole = userRole,
            PerformedAt = DateTime.UtcNow,
            Remarks = reason
        };
        _db.OrgDocumentHistories.Add(history);
        await _db.SaveChangesAsync();

        return true;
    }

    // ==================== PERMISSION HELPERS ====================

    public async Task<bool> CanGenerateDocumentForEmployeeAsync(
        Guid employeeId,
        Guid organizationId,
        string userId,
        string userRole)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        var roles = await _userManager.GetRolesAsync(user);

        if (roles.Contains("OrgAdmin"))
            return true;

        if (roles.Contains("HR"))
        {
            var currentEmployee = await _db.Employees
                .FirstOrDefaultAsync(e => e.UserId == userId);

            if (currentEmployee == null) return false;

            var targetEmployee = await _db.Employees
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);

            if (targetEmployee == null) return false;

            var targetRoles = await _userManager.GetRolesAsync(
                await _userManager.FindByIdAsync(targetEmployee.UserId));

            if (targetRoles.Contains("HR"))
                return false;

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

        if (roles.Contains("OrgAdmin"))
        {
            return await _db.Employees
                .Include(e => e.Department)
                .Where(e => e.OrganizationId == organizationId && e.Status == "Active")
                .OrderBy(e => e.FirstName)
                .ToListAsync();
        }

        if (roles.Contains("HR"))
        {
            var currentEmployee = await _db.Employees
                .FirstOrDefaultAsync(e => e.UserId == userId);

            if (currentEmployee == null) return new List<Employee>();

            var hrUserIds = await GetHrUserIdsInOrgAsync(organizationId);

            return await _db.Employees
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
        return await _db.OrgDocumentHistories
            .Where(x => x.DocumentId == documentId)
            .OrderByDescending(x => x.PerformedAt)
            .ToListAsync();
    }

    // ==================== PRIVATE HELPER METHODS ====================

    private Dictionary<string, string> BuildPlaceholderData(
        Employee employee,
        string actorName,
        Dictionary<string, string>? customData)
    {
        

        var data = new Dictionary<string, string>
        {
            { "EmployeeName", $"{employee.FirstName} {employee.LastName}" },
            { "FirstName", employee.FirstName },
            { "LastName", employee.LastName },
            { "EmployeeCode", employee.EmployeeCode ?? "N/A" },
            { "Department", employee.Department?.Name ?? "N/A" },
            { "Designation", employee.Designation?.Name ?? "N/A" },
            { "JoiningDate", employee.JoiningDate.ToString("dd MMM yyyy") },
            { "CurrentDate", DateTime.UtcNow.ToString("dd MMM yyyy") },
            { "CurrentYear", DateTime.UtcNow.Year.ToString() },
            { "GeneratedBy", actorName },
            { "OrganizationName", employee.Organization?.Name ?? "Our Organization" }
        };

        if (customData != null)
        {
            foreach (var kvp in customData)
            {
                data[kvp.Key] = kvp.Value;
            }
        }

        return data;
    }

    private string ReplacePlaceholders(string content, Dictionary<string, string> data)
    {
        var result = content;
        foreach (var kvp in data)
        {
            result = result.Replace($"{{{{{kvp.Key}}}}}", kvp.Value);
        }
        return result;
    }

    private async Task<(string fileName, string filePath, string fullPath)> GenerateDocumentFileAsync(
        string htmlContent,
        string templateName,
        string employeeCode)
    {
        var folder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "documents", "organization");
        Directory.CreateDirectory(folder);

        var fileName = $"{templateName.Replace(" ", "_")}_{employeeCode}_{DateTime.UtcNow:yyyyMMddHHmmss}.html";
        var filePath = $"/uploads/documents/organization/{fileName}";
        var fullPath = Path.Combine(folder, fileName);

        var html = $@"
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset='utf-8'>
            <title>{templateName}</title>
            <style>
                body {{ font-family: Arial, sans-serif; margin: 40px; line-height: 1.6; }}
                .header {{ text-align: center; margin-bottom: 30px; }}
                .content {{ margin: 20px 0; }}
                .footer {{ text-align: center; margin-top: 50px; font-size: 12px; color: #666; }}
                @media print {{
                    body {{ margin: 0; }}
                    .footer {{ position: fixed; bottom: 0; }}
                }}
            </style>
        </head>
        <body>
            <div class='header'>
                <h2>{templateName}</h2>
                <p>Document Number: {GenerateDocumentNumber(templateName, employeeCode)}</p>
            </div>
            <div class='content'>
                {htmlContent}
            </div>
            <div class='footer'>
                <p>This is a system-generated document. Generated on {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC</p>
            </div>
        </body>
        </html>";

        await File.WriteAllTextAsync(fullPath, html);
        return (fileName, filePath, fullPath);
    }

    private string GenerateDocumentNumber(string templateName, string? employeeCode)
    {
        var prefix = templateName.Length >= 3 ? templateName.Substring(0, 3).ToUpper() : templateName.ToUpper();
        var code = string.IsNullOrEmpty(employeeCode) ? "EMP" : employeeCode;
        return $"{prefix}-{code}-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 4).ToUpper()}";
    }

    private async Task SendDocumentNotificationAsync(Employee recipient, OrgGeneratedDocument document, string documentTypeName)
    {
        // Implement email notification here if needed
        await Task.CompletedTask;
    }

    private async Task SendInAppNotificationAsync(Employee recipient, OrgGeneratedDocument document, string documentTypeName)
    {
        var appUser = await _userManager.FindByIdAsync(recipient.UserId);
        if (appUser == null) return;

        var message = $"📄 A new document '{documentTypeName}' has been issued to you.\n\n" +
                      $"Document Number: {document.DocumentNumber}\n" +
                      $"Issue Date: {document.GeneratedAt:dd MMM yyyy}\n\n" +
                      $"Please review this document in your Documents section.";

        await _notificationService.CreateNotificationAsync(
            recipient.UserId,
            $"New Organization Document: {documentTypeName}",
            message,
            $"/employee/docu");
    }

    private async Task<HashSet<string>> GetHrUserIdsInOrgAsync(Guid organizationId)
    {
        var hrUserIds = await (
            from u in _db.Users
            join ur in _db.UserRoles on u.Id equals ur.UserId
            join r in _db.Roles on ur.RoleId equals r.Id
            where u.OrganizationId == organizationId && r.Name == "HR"
            select u.Id
        ).ToListAsync();

        return hrUserIds.ToHashSet();
    }

    // Add AI-powered document generation
    public async Task<OrgDocumentGenerationResponseDto> GenerateDocumentWithAIAsync(
        Guid templateId,
        Guid recipientEmployeeId,
        string userId,
        string userRole,
        Dictionary<string, string>? customData = null,
        bool sendEmail = true)
    {
        var recentDocument = await _db.OrgGeneratedDocuments
       .AnyAsync(d => d.EmployeeId == recipientEmployeeId &&
                     d.TemplateId == templateId &&
                     d.GeneratedAt > DateTime.UtcNow.AddMinutes(-5));

        if (recentDocument)
        {
            throw new InvalidOperationException("Document already generated recently. Please wait.");
        }
        // 1. Get recipient employee first
        var recipient = await _db.Employees
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.EmployeeId == recipientEmployeeId);

        if (recipient == null)
            throw new InvalidOperationException("Recipient employee not found.");

        // 2. Get template using recipient's organization ID
        var template = await GetTemplateByIdAsync(templateId, recipient.OrganizationId);
        if (template == null)
            throw new InvalidOperationException("Template not found or inactive.");

        // 3. Get actor
        var actor = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == userId);
        var actorName = actor != null ? $"{actor.FirstName} {actor.LastName}" : userRole;

        // 4. Generate AI content
        var aiContent = await _aiDocumentService.GenerateDocumentContentAsync(
            template.Name,
            template.Category,
            recipient,
            customData,
            actorName);

        var placeholderData = BuildPlaceholderData(recipient, actorName, customData);
        var processedContent = ReplacePlaceholders(aiContent, placeholderData);

        // 5. Generate file
        var (fileName, filePath, fullPath) = await GenerateDocumentFileAsync(
            processedContent,
            template.Name,
            recipient.EmployeeCode ?? recipient.EmployeeId.ToString().Substring(0, 8));

        var documentNumber = GenerateDocumentNumber(template.Name, recipient.EmployeeCode);

        var document = new OrgGeneratedDocument
        {
            DocumentId = Guid.NewGuid(),
            OrganizationId = recipient.OrganizationId,
            EmployeeId = recipient.EmployeeId,
            TemplateId = template.TemplateId,
            DocumentNumber = documentNumber,
            DocumentName = template.Name,
            Description = $"AI-generated document by {actorName}",
            FileName = fileName,
            OriginalFileName = $"{template.Name}_{recipient.FirstName}_{recipient.LastName}_AI.html",
            FilePath = filePath,
            FileSize = new FileInfo(fullPath).Length,
            MimeType = "text/html",
            Status = "Issued",
            GeneratedByUserId = userId,
            GeneratedByRole = userRole,
            GeneratedAt = DateTime.UtcNow,
            IsLatestVersion = true,
            Version = 1,
            ContentSnapshot = JsonSerializer.Serialize(placeholderData)
        };

        _db.OrgGeneratedDocuments.Add(document);
        await _db.SaveChangesAsync();

        // Add history
        var history = new OrgDocumentHistory
        {
            DocumentId = document.DocumentId,
            Action = "Generated (AI)",
            PerformedByUserId = userId,
            PerformedByRole = userRole,
            PerformedAt = DateTime.UtcNow,
            Remarks = $"AI-generated document using template '{template.Name}'"
        };
        _db.OrgDocumentHistories.Add(history);
        await _db.SaveChangesAsync();

        // Send AI-personalized notification
        var notificationMessage = await _aiDocumentService.GenerateNotificationMessageAsync(
            template.Name, recipient, documentNumber);

        await _notificationService.CreateNotificationAsync(
            recipient.UserId,
            $"✨ New Document: {template.Name}",
            notificationMessage,
            $"/employee/docu");

        return new OrgDocumentGenerationResponseDto
        {
            DocumentId = document.DocumentId,
            EmployeeId = recipient.EmployeeId,
            EmployeeName = $"{recipient.FirstName} {recipient.LastName}",
            DocumentTypeName = template.Name,
            FilePath = document.FilePath,
            OriginalFileName = document.OriginalFileName,
            GeneratedAt = document.GeneratedAt,
            GeneratedBy = actorName,
            Status = document.Status,
            EmailSent = false
        };
    }

    // Add method to get AI suggestions
    public async Task<List<AIDocumentSuggestion>> GetAIDocumentSuggestionsAsync(Guid employeeId)
    {
        var employee = await _db.Employees
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);

        if (employee == null)
            return new List<AIDocumentSuggestion>();

        return await _aiDocumentService.SuggestDocumentsAsync(employee);
    }

    // Add auto-trigger method
    public async Task<int> AutoGenerateRequiredDocumentsAsync(Guid organizationId)
    {
        var generatedCount = 0;
        var employees = await _db.Employees
            .Where(e => e.OrganizationId == organizationId && e.Status == "Active")
            .ToListAsync();

        foreach (var employee in employees)
        {
            var suggestions = await GetAIDocumentSuggestionsAsync(employee.EmployeeId);

            foreach (var suggestion in suggestions.Where(s => s.Priority >= 8))
            {
                // Check if document already exists
                var existingDoc = await _db.OrgGeneratedDocuments
                    .AnyAsync(d => d.EmployeeId == employee.EmployeeId &&
                                  d.DocumentName == suggestion.DocumentType &&
                                  d.IsLatestVersion);

                if (!existingDoc)
                {
                    // Get template ID for this document type
                    var template = await _db.OrgDocumentTemplates
                        .FirstOrDefaultAsync(t => t.OrganizationId == organizationId &&
                                                  t.Name == suggestion.DocumentType);

                    if (template != null)
                    {
                        // Find an OrgAdmin to be the generator
                        var orgAdmin = await (from u in _db.Users
                                              join ur in _db.UserRoles on u.Id equals ur.UserId
                                              join r in _db.Roles on ur.RoleId equals r.Id
                                              where u.OrganizationId == organizationId && r.Name == "OrgAdmin"
                                              select u).FirstOrDefaultAsync();

                        if (orgAdmin != null)
                        {
                            await GenerateDocumentWithAIAsync(
                                template.TemplateId,
                                employee.EmployeeId,
                                orgAdmin.Id,
                                "OrgAdmin",
                                suggestion.SuggestedData);

                            generatedCount++;
                        }
                    }
                }
            }
        }

        return generatedCount;
    }
}