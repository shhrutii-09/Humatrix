// Services/Documents/OrgDocumentGenerationService.cs
using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs.Documents;
using Humatrix_HRMS.Models;
using Humatrix_HRMS.Models.Documents;
using Humatrix_HRMS.Services.AI;
using Microsoft.AspNetCore.Http;
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
    private readonly IAIDocumentService _aiDocumentService; // ONLY for suggestions

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
    //  TEMPLATE MANAGEMENT (Keep as is - optional feature)
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
        return await query
            .OrderBy(x => x.Category)
            .ThenBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();
    }

    // ══════════════════════════════════════════════════════════
    //  MANUAL DOCUMENT UPLOAD (No AI generation, just file upload)
    // ══════════════════════════════════════════════════════════

    public async Task<OrgDocumentGenerationResponseDto> UploadDocumentManuallyAsync(
    ManualDocumentUploadDto dto,
    Guid organizationId,
    string userId,
    string userRole,
    string fileName,
    string contentType,
    byte[] fileContent)
    {
        // ── Permission check ────────────────────────────────────────
        var permissionResult = await CheckPermissionAsync(dto.RecipientEmployeeId, organizationId, userId);
        if (!permissionResult.Allowed)
            throw new UnauthorizedAccessException(permissionResult.Reason);

        if (fileContent == null || fileContent.Length == 0)
            throw new InvalidOperationException("Please select a file to upload.");

        // Validate file type
        var fileExtension = Path.GetExtension(fileName).ToLowerInvariant();
        var allowedExtensions = new[] { ".pdf", ".docx", ".doc", ".txt", ".html", ".jpg", ".png" };
        if (!allowedExtensions.Contains(fileExtension))
            throw new InvalidOperationException("Invalid file type. Allowed: PDF, DOCX, DOC, TXT, HTML, JPG, PNG");

        // Max file size: 10MB
        if (fileContent.Length > 10 * 1024 * 1024)
            throw new InvalidOperationException("File size exceeds 10MB limit.");

        using var db = _dbFactory.CreateDbContext();

        var recipient = await db.Employees
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.EmployeeId == dto.RecipientEmployeeId)
            ?? throw new InvalidOperationException("Recipient employee not found.");

        var actorName = await ResolveActorNameAsync(db, userId, userRole);

        // Generate document number
        var documentNumber = GenerateManualDocumentNumber(dto.DocumentName, recipient.EmployeeCode);

        // Save the uploaded file
        var (savedFileName, filePath, fullPath) = await SaveUploadedFileFromBytesAsync(
            fileContent, fileName, dto.DocumentName,
            recipient.EmployeeCode ?? recipient.EmployeeId.ToString()[..8],
            documentNumber);

        // Build document record - NO TemplateId for manual uploads
        var document = new OrgGeneratedDocument
        {
            DocumentId = Guid.NewGuid(),
            OrganizationId = organizationId,
            EmployeeId = recipient.EmployeeId,
            TemplateId = null,  // Manual upload - no template
            DocumentNumber = documentNumber,
            DocumentName = dto.DocumentName,
            //Description = dto.Description ?? dto.Remarks ?? $"Uploaded by {actorName}",
            Description = string.IsNullOrEmpty(dto.Category)? dto.Description : $"{dto.Category} | {dto.Description}",
            FileName = savedFileName,
            OriginalFileName = fileName,
            FilePath = filePath,
            FileSize = fileContent.Length,
            MimeType = GetMimeType(fileExtension),
            Status = "Issued",
            GeneratedByUserId = userId,
            GeneratedByRole = userRole,
            GeneratedAt = DateTime.UtcNow,
            IsLatestVersion = true,
            Version = 1,
            ContentSnapshot = JsonSerializer.Serialize(new
            {
                dto.DocumentName,
                dto.Category,
                dto.CustomMetadata,
                UploadedBy = actorName,
                OriginalFileName = fileName
            })
        };

        // Add document FIRST
        db.OrgGeneratedDocuments.Add(document);

        // IMPORTANT: Save the document FIRST so it gets a real DocumentId in the database
        await db.SaveChangesAsync();

        // NOW add the history record AFTER document is saved
        var history = new OrgDocumentHistory
        {
            DocumentId = document.DocumentId,  // Now this ID exists in the database
            Action = "Uploaded",
            PerformedByUserId = userId,
            PerformedByRole = userRole,
            PerformedAt = DateTime.UtcNow,
            Remarks = $"Manual document upload: {dto.DocumentName} (File: {fileName})"
        };

        db.OrgDocumentHistories.Add(history);
        await db.SaveChangesAsync();  // Save history separately

        // Send notification to employee
        await _notificationService.CreateNotificationAsync(
            recipient.UserId,
            $"New Document: {dto.DocumentName}",
            $"A document '{dto.DocumentName}' (Ref: {documentNumber}) has been uploaded for you by {actorName}. " +
            "Please review it in the Documents section.",
            "/employee/docu");

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

    public async Task MarkAsViewedAsync(Guid documentId, Guid employeeId)
    {
        using var db = _dbFactory.CreateDbContext();

        var document = await db.OrgGeneratedDocuments
            .FirstOrDefaultAsync(x => x.DocumentId == documentId && x.EmployeeId == employeeId);

        if (document == null)
            throw new InvalidOperationException("Document not found.");

        if (!document.IsViewed)
        {
            document.IsViewed = true;
            document.ViewedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    // Add this new helper method to save from byte array
    private async Task<(string fileName, string filePath, string fullPath)> SaveUploadedFileFromBytesAsync(
        byte[] fileContent,
        string originalFileName,
        string documentName,
        string employeeCode,
        string documentNumber)
    {
        var folder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "documents", "organization");
        Directory.CreateDirectory(folder);

        var safeDocumentName = documentName.Replace(" ", "_").Replace("/", "_");
        var fileExtension = Path.GetExtension(originalFileName);
        var fileName = $"{safeDocumentName}_{employeeCode}_{documentNumber}{fileExtension}";
        var filePath = $"/uploads/documents/organization/{fileName}";
        var fullPath = Path.Combine(folder, fileName);

        await System.IO.File.WriteAllBytesAsync(fullPath, fileContent);

        return (fileName, filePath, fullPath);
    }


    public async Task<Dictionary<Guid, string>> GetEmployeeRolesAsync(Guid organizationId)
    {
        using var db = _dbFactory.CreateDbContext();

        var result = await (
            from emp in db.Employees
            join u in db.Users on emp.UserId equals u.Id
            join ur in db.UserRoles on u.Id equals ur.UserId into urGroup
            from ur in urGroup.DefaultIfEmpty()
            join r in db.Roles on ur.RoleId equals r.Id into rGroup
            from r in rGroup.DefaultIfEmpty()
            where emp.OrganizationId == organizationId
                  && emp.Status == "Active"
            select new
            {
                emp.EmployeeId,
                Role = r == null ? "Employee" : r.Name
            }
        ).ToListAsync();

        return result
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.Any(x => x.Role == "OrgAdmin")
                    ? "OrgAdmin"
                    : g.Any(x => x.Role == "HR")
                        ? "HR"
                        : "Employee");
    }

    // ══════════════════════════════════════════════════════════
    //  VIEWING
    // ══════════════════════════════════════════════════════════

    public async Task<List<OrgGeneratedDocument>> GetDocumentsForEmployeeAsync(
        Guid employeeId, Guid organizationId)
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

    public async Task<List<OrgGeneratedDocument>> GetDocumentsGeneratedByUserAsync(
        string userId, Guid organizationId)
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

    public async Task<List<OrgGeneratedDocument>> GetOrganizationDocumentsAsync(
        Guid organizationId, string userId, string userRole)
    {
        using var db = _dbFactory.CreateDbContext();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return new List<OrgGeneratedDocument>();

        var roles = await _userManager.GetRolesAsync(user);

        IQueryable<OrgGeneratedDocument> query = db.OrgGeneratedDocuments
            .Include(x => x.Template)
            .Include(x => x.Employee).ThenInclude(e => e!.Department)
            .Where(x => x.OrganizationId == organizationId && !x.IsDeleted);

        if (roles.Contains("HR") && !roles.Contains("OrgAdmin"))
        {
            var hrEmployee = await db.Employees.FirstOrDefaultAsync(e => e.UserId == userId);
            if (hrEmployee == null) return new List<OrgGeneratedDocument>();

            var hrUserIds = await GetHrUserIdsInOrgAsync(db, organizationId);

            query = query.Where(x =>
                x.Employee != null &&
                x.Employee.DepartmentId == hrEmployee.DepartmentId &&
                !hrUserIds.Contains(x.Employee.UserId));
        }

        return await query.OrderByDescending(x => x.GeneratedAt).ToListAsync();
    }

    public async Task<OrgGeneratedDocument?> GetDocumentByIdAsync(Guid documentId, Guid organizationId)
    {
        using var db = _dbFactory.CreateDbContext();
        return await db.OrgGeneratedDocuments
            .Include(x => x.Template)
            .Include(x => x.Employee)
            .FirstOrDefaultAsync(x =>
                x.DocumentId == documentId && x.OrganizationId == organizationId);
    }

    // ══════════════════════════════════════════════════════════
    //  ACKNOWLEDGMENT & REVOKE
    // ══════════════════════════════════════════════════════════

    public async Task<bool> AcknowledgeDocumentAsync(
        Guid documentId, Guid employeeId, string? remarks = null)
    {
        using var db = _dbFactory.CreateDbContext();

        var document = await db.OrgGeneratedDocuments.FirstOrDefaultAsync(x =>
            x.DocumentId == documentId && x.EmployeeId == employeeId);

        if (document == null) return false;
        if (document.Status == "Acknowledged") return true;

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId);

        document.Status = "Acknowledged";
        document.AcknowledgedAt = DateTime.UtcNow;
        document.AcknowledgmentRemarks = remarks;

        db.OrgDocumentHistories.Add(BuildHistory(
            document.DocumentId, "Acknowledged",
            employee?.UserId ?? "SYSTEM", "Employee", remarks));

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RevokeDocumentAsync(
        Guid documentId, string userId, string userRole, string reason)
    {
        using var db = _dbFactory.CreateDbContext();

        var document = await db.OrgGeneratedDocuments.FirstOrDefaultAsync(x =>
            x.DocumentId == documentId);
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

    public async Task<bool> CanUploadDocumentForEmployeeAsync(
        Guid employeeId,
        Guid organizationId,
        string userId,
        string userRole)
    {
        var result = await CheckPermissionAsync(employeeId, organizationId, userId);
        return result.Allowed;
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

        // Get ALL user IDs that are HR or OrgAdmin (these should NEVER be recipients)
        var excludedRoleUserIds = await GetHrAndAdminUserIdsInOrgAsync(db, organizationId);

        if (roles.Contains("OrgAdmin"))
        {
            // OrgAdmin can see all NON-HR, NON-ADMIN employees
            return await db.Employees
                .Include(e => e.Department)
                .Include(e => e.Designation)
                .Where(e => e.OrganizationId == organizationId &&
                            e.Status == "Active" &&
                            !excludedRoleUserIds.Contains(e.UserId) &&  // Exclude ALL HR & Admins
                            e.UserId != userId)  // Exclude self
                .OrderBy(e => e.Department!.Name)
                .ThenBy(e => e.FirstName)
                .ToListAsync();
        }

        if (roles.Contains("HR"))
        {
            var hrEmployee = await db.Employees.FirstOrDefaultAsync(e => e.UserId == userId);
            if (hrEmployee == null) return new List<Employee>();

            // HR can see only NON-HR, NON-ADMIN employees in their department
            return await db.Employees
                .Include(e => e.Department)
                .Include(e => e.Designation)
                .Where(e => e.OrganizationId == organizationId &&
                            e.DepartmentId == hrEmployee.DepartmentId &&
                            e.Status == "Active" &&
                            e.UserId != userId &&
                            !excludedRoleUserIds.Contains(e.UserId))  // Exclude ALL HR & Admins
                .OrderBy(e => e.FirstName)
                .ToListAsync();
        }

        return new List<Employee>();
    }
    private static async Task<HashSet<string>> GetHrAndAdminUserIdsInOrgAsync(
      ApplicationDbContext db, Guid organizationId)
    {
        var ids = await (
            from u in db.Users
            join ur in db.UserRoles on u.Id equals ur.UserId
            join r in db.Roles on ur.RoleId equals r.Id
            where u.OrganizationId == organizationId &&
                  (r.Name == "HR" || r.Name == "OrgAdmin")
            select u.Id
        ).ToListAsync();

        return ids.ToHashSet();
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
    //  AI SUGGESTIONS ONLY (Just recommendations, no auto-sending)
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

    public async Task<List<EmployeeWithRoleDto>> GetEligibleRecipientsWithRolesAsync(
    Guid organizationId, string userId, string userRole)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return new List<EmployeeWithRoleDto>();

        var roles = await _userManager.GetRolesAsync(user);
        using var db = _dbFactory.CreateDbContext();

        // Get all users in the organization
        var allUsers = await db.Users
            .Where(u => u.OrganizationId == organizationId)
            .ToListAsync();

        // Get roles for all users
        var userRolesMap = new Dictionary<string, List<string>>();
        foreach (var u in allUsers)
        {
            var userRoles = await _userManager.GetRolesAsync(u);
            userRolesMap[u.Id] = userRoles.ToList();
        }

        // Get all employees
        var employees = await db.Employees
            .Include(e => e.Department)
            .Where(e => e.OrganizationId == organizationId && e.Status == "Active")
            .ToListAsync();

        var result = new List<EmployeeWithRoleDto>();

        foreach (var emp in employees)
        {
            if (!userRolesMap.ContainsKey(emp.UserId)) continue;

            var empRoles = userRolesMap[emp.UserId];
            bool isHR = empRoles.Contains("HR");
            bool isOrgAdmin = empRoles.Contains("OrgAdmin");

            // For HR users: can see everyone in their department (including other HR)
            // For OrgAdmin: can see everyone
            // Skip self
            if (emp.UserId == userId) continue;

            if (roles.Contains("OrgAdmin"))
            {
                // OrgAdmin sees everyone
                result.Add(new EmployeeWithRoleDto
                {
                    EmployeeId = emp.EmployeeId,
                    UserId = emp.UserId,
                    FirstName = emp.FirstName,
                    LastName = emp.LastName,
                    EmployeeCode = emp.EmployeeCode,
                    Role = isHR ? "HR" : (isOrgAdmin ? "OrgAdmin" : "Employee"),
                    IsHR = isHR,
                    IsOrgAdmin = isOrgAdmin,
                    DepartmentName = emp.Department?.Name ?? "N/A"
                });
            }
            else if (roles.Contains("HR"))
            {
                var hrEmployee = await db.Employees.FirstOrDefaultAsync(e => e.UserId == userId);
                if (hrEmployee != null && emp.DepartmentId == hrEmployee.DepartmentId)
                {
                    // HR sees everyone in their department (including other HR)
                    result.Add(new EmployeeWithRoleDto
                    {
                        EmployeeId = emp.EmployeeId,
                        UserId = emp.UserId,
                        FirstName = emp.FirstName,
                        LastName = emp.LastName,
                        EmployeeCode = emp.EmployeeCode,
                        Role = isHR ? "HR" : (isOrgAdmin ? "OrgAdmin" : "Employee"),
                        IsHR = isHR,
                        IsOrgAdmin = isOrgAdmin,
                        DepartmentName = emp.Department?.Name ?? "N/A"
                    });
                }
            }
        }

        return result.OrderBy(e => e.DepartmentName).ThenBy(e => e.FirstName).ToList();
    }

    public async Task<List<DashboardDocumentSuggestion>> GetDashboardSuggestionsAsync(
    Guid organizationId, string userId, string userRole)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return new List<DashboardDocumentSuggestion>();

        var roles = await _userManager.GetRolesAsync(user);
        using var db = _dbFactory.CreateDbContext();

        // Get HR and Admin user IDs to exclude them from suggestions
        var excludedUserIds = await GetHrAndAdminUserIdsInOrgAsync(db, organizationId);

        IQueryable<Employee> employeeQuery = db.Employees
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .Where(e => e.OrganizationId == organizationId &&
                        e.Status == "Active" &&
                        !excludedUserIds.Contains(e.UserId));  // Exclude HR & Admins from suggestions
        if (roles.Contains("HR") && !roles.Contains("OrgAdmin"))
        {
            var hrEmployee = await db.Employees.FirstOrDefaultAsync(e => e.UserId == userId);
            if (hrEmployee == null) return new List<DashboardDocumentSuggestion>();
            var hrUserIds = await GetHrUserIdsInOrgAsync(db, organizationId);
            employeeQuery = employeeQuery.Where(e =>
                e.DepartmentId == hrEmployee.DepartmentId &&
                e.UserId != userId &&
                !hrUserIds.Contains(e.UserId));
        }

        var employees = await employeeQuery.ToListAsync();
        var suggestions = new List<DashboardDocumentSuggestion>();
        var today = DateTime.UtcNow;

        // ── 1. RECENTLY JOINED (≤ 7 days): Offer Letter ──────────────────────────
        foreach (var emp in employees.Where(e => (today - e.JoiningDate).Days <= 7))
        {
            var hasOfferLetter = await db.OrgGeneratedDocuments.AnyAsync(d =>
                d.EmployeeId == emp.EmployeeId &&
                d.DocumentName.Contains("Offer Letter") && !d.IsDeleted);

            if (!hasOfferLetter)
                suggestions.Add(new DashboardDocumentSuggestion
                {
                    EmployeeId = emp.EmployeeId,
                    EmployeeName = $"{emp.FirstName} {emp.LastName}",
                    Department = emp.Department?.Name ?? "N/A",
                    DocumentType = "Offer Letter",
                    Reason = $"🆕 {emp.FirstName} joined {(today - emp.JoiningDate).Days} day(s) ago — upload Offer Letter",
                    Priority = 10,
                    Category = "Onboarding",
                    SuggestedData = new Dictionary<string, string>
                {
                    { "JoiningDate", emp.JoiningDate.ToString("dd MMM yyyy") },
                    { "Designation", emp.Designation?.Name ?? "" },
                    { "Department", emp.Department?.Name ?? "" }
                }
                });
        }

        // ── 2. PROBATION ENDING (173–183 days): Confirmation Letter ──────────────
        foreach (var emp in employees)
        {
            var tenure = (today - emp.JoiningDate).Days;
            if (tenure >= 173 && tenure <= 183)
            {
                var hasCfm = await db.OrgGeneratedDocuments.AnyAsync(d =>
                    d.EmployeeId == emp.EmployeeId &&
                    d.DocumentName.Contains("Confirmation Letter") && !d.IsDeleted);

                if (!hasCfm)
                    suggestions.Add(new DashboardDocumentSuggestion
                    {
                        EmployeeId = emp.EmployeeId,
                        EmployeeName = $"{emp.FirstName} {emp.LastName}",
                        Department = emp.Department?.Name ?? "N/A",
                        DocumentType = "Confirmation Letter",
                        Reason = $"⏳ {emp.FirstName}'s probation ends in {183 - tenure} day(s) — upload Confirmation Letter",
                        Priority = 9,
                        Category = "HR Policies",
                        SuggestedData = new Dictionary<string, string>
                    {
                        { "ConfirmationDate", today.AddDays(183 - tenure).ToString("dd MMM yyyy") }
                    }
                    });
            }
        }

        // ── 3. APPROVED RESIGNATION: Experience + Relieving Letter ────────────────
        var approvedExits = await db.EmployeeExits
            .Include(x => x.Employee).ThenInclude(e => e!.Department)
            .Where(x =>
                x.OrganizationId == organizationId &&
                (x.Status == ExitStatus.Approved || x.Status == ExitStatus.ClearanceInProgress))
            .ToListAsync();

        foreach (var exit in approvedExits)
        {
            if (exit.Employee == null) continue;

            if (!exit.ExperienceLetterIssued)
                suggestions.Add(new DashboardDocumentSuggestion
                {
                    EmployeeId = exit.EmployeeId,
                    EmployeeName = $"{exit.Employee.FirstName} {exit.Employee.LastName}",
                    Department = exit.Employee.Department?.Name ?? "N/A",
                    DocumentType = "Experience Letter",
                    Reason = $"📤 {exit.Employee.FirstName}'s resignation is approved (LWD: {exit.LastWorkingDay:dd MMM yyyy}) — upload Experience Letter",
                    Priority = 8,
                    Category = "Exit",
                    ExitId = exit.ExitId,
                    IsExitRelated = true
                });

            if (!exit.RelievingLetterIssued)
                suggestions.Add(new DashboardDocumentSuggestion
                {
                    EmployeeId = exit.EmployeeId,
                    EmployeeName = $"{exit.Employee.FirstName} {exit.Employee.LastName}",
                    Department = exit.Employee.Department?.Name ?? "N/A",
                    DocumentType = "Relieving Letter",
                    Reason = $"📤 {exit.Employee.FirstName}'s resignation is approved — upload Relieving Letter",
                    Priority = 7,
                    Category = "Exit",
                    ExitId = exit.ExitId,
                    IsExitRelated = true
                });
        }

        // ── 4. CANCELLED RESIGNATION: Notify that exit docs are now void ──────────
        // (We surface a warning, not an upload suggestion)
        var cancelledExits = await db.EmployeeExits
            .Include(x => x.Employee)
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.Status == ExitStatus.Cancelled &&
                x.CancelledAt.HasValue &&
                x.CancelledAt.Value >= DateTime.UtcNow.AddDays(-3)) // Only recent cancellations
            .ToListAsync();

        foreach (var exit in cancelledExits)
        {
            if (exit.Employee == null) continue;

            // Check if any exit docs were issued for this employee
            var exitDocsIssued = await db.OrgGeneratedDocuments.AnyAsync(d =>
                d.EmployeeId == exit.EmployeeId &&
                (d.DocumentName.Contains("Experience Letter") || d.DocumentName.Contains("Relieving Letter")) &&
                d.Status == "Issued" &&
                !d.IsDeleted &&
                d.GeneratedAt >= exit.CancelledAt!.Value.AddDays(-30));

            if (exitDocsIssued)
                suggestions.Add(new DashboardDocumentSuggestion
                {
                    EmployeeId = exit.EmployeeId,
                    EmployeeName = $"{exit.Employee.FirstName} {exit.Employee.LastName}",
                    Department = exit.Employee?.Department?.Name ?? "N/A",
                    DocumentType = "Exit Document",
                    Reason = $"⚠️ {exit.Employee.FirstName}'s resignation was cancelled — review and revoke previously issued exit documents",
                    Priority = 10,
                    Category = "Exit",
                    IsRevocationWarning = true,
                    ExitId = exit.ExitId,
                    IsExitRelated = true
                });
        }

        // ── 5. BIRTHDAY TODAY ────────────────────────────────────────────────────
        foreach (var emp in employees.Where(e =>
            e.DateOfBirth.HasValue &&
            e.DateOfBirth.Value.Month == today.Month &&
            e.DateOfBirth.Value.Day == today.Day))
        {
            suggestions.Add(new DashboardDocumentSuggestion
            {
                EmployeeId = emp.EmployeeId,
                EmployeeName = $"{emp.FirstName} {emp.LastName}",
                Department = emp.Department?.Name ?? "N/A",
                DocumentType = "Birthday Card",
                Reason = $"🎂 {emp.FirstName} has a birthday today — consider sending a greeting",
                Priority = 5,
                Category = "General"
            });
        }

        // ── 6. TERMINATED EMPLOYEE: FnF + Termination Letter ─────────────────────
        var terminatedExits = await db.EmployeeExits
            .Include(x => x.Employee)
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.Status == ExitStatus.Terminated &&
                x.TerminationDate.HasValue &&
                x.TerminationDate.Value >= DateTime.UtcNow.AddDays(-7))
            .ToListAsync();

        foreach (var exit in terminatedExits)
        {
            if (exit.Employee == null) continue;

            var hasTermLetter = await db.OrgGeneratedDocuments.AnyAsync(d =>
                d.EmployeeId == exit.EmployeeId &&
                d.DocumentName.Contains("Termination") && !d.IsDeleted);

            if (!hasTermLetter)
                suggestions.Add(new DashboardDocumentSuggestion
                {
                    EmployeeId = exit.EmployeeId,
                    EmployeeName = $"{exit.Employee.FirstName} {exit.Employee.LastName}",
                    Department = exit.Employee.Department?.Name ?? "N/A",
                    DocumentType = "Termination Letter",
                    Reason = $"🔴 {exit.Employee.FirstName} was terminated on {exit.TerminationDate!.Value:dd MMM yyyy} — upload Termination Letter",
                    Priority = 10,
                    Category = "Exit",
                    ExitId = exit.ExitId,
                    IsExitRelated = true
                });
        }

        return suggestions.OrderByDescending(s => s.Priority).ThenBy(s => s.EmployeeName).ToList();
    }

    // ══════════════════════════════════════════════════════════
    //  PRIVATE HELPERS
    // ══════════════════════════════════════════════════════════

    private async Task<(bool Allowed, string Reason)> CheckPermissionAsync(
        Guid targetEmployeeId,
        Guid organizationId,
        string actorUserId)
    {
        var user = await _userManager.FindByIdAsync(actorUserId);
        if (user == null)
            return (false, "Actor user not found.");

        if (user.OrganizationId != organizationId)
            return (false, "You can only manage documents within your own organization.");

        var roles = await _userManager.GetRolesAsync(user);

        if (roles.Contains("OrgAdmin"))
            return (true, string.Empty);

        if (roles.Contains("HR"))
        {
            using var db = _dbFactory.CreateDbContext();

            var hrEmployee = await db.Employees.FirstOrDefaultAsync(e => e.UserId == actorUserId);
            if (hrEmployee == null)
                return (false, "Your employee profile was not found.");

            var target = await db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == targetEmployeeId);
            if (target == null)
                return (false, "Recipient employee not found.");

            if (target.UserId == actorUserId)
                return (false, "You cannot upload documents for yourself.");

            var targetAppUser = await _userManager.FindByIdAsync(target.UserId);
            if (targetAppUser != null)
            {
                var targetRoles = await _userManager.GetRolesAsync(targetAppUser);
                if (targetRoles.Contains("HR") || targetRoles.Contains("OrgAdmin"))
                    return (false, "You do not have permission to upload documents for this user.");
            }

            if (target.DepartmentId != hrEmployee.DepartmentId)
                return (false, "You can only upload documents for employees in your own department.");

            return (true, string.Empty);
        }

        return (false, "You do not have permission to upload documents.");
    }

    private static async Task<string> ResolveActorNameAsync(
        ApplicationDbContext db, string userId, string fallbackRole)
    {
        var actor = await db.Employees.FirstOrDefaultAsync(e => e.UserId == userId);
        return actor != null ? $"{actor.FirstName} {actor.LastName}" : fallbackRole;
    }

    private async Task<(string fileName, string filePath, string fullPath)> SaveUploadedFileAsync(
        IFormFile file,
        string documentName,
        string employeeCode,
        string documentNumber)
    {
        var folder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "documents", "organization");
        Directory.CreateDirectory(folder);

        var safeDocumentName = documentName.Replace(" ", "_").Replace("/", "_");
        var fileExtension = Path.GetExtension(file.FileName);
        var fileName = $"{safeDocumentName}_{employeeCode}_{documentNumber}{fileExtension}";
        var filePath = $"/uploads/documents/organization/{fileName}";
        var fullPath = Path.Combine(folder, fileName);

        using var stream = new FileStream(fullPath, FileMode.Create);
        await file.CopyToAsync(stream);

        return (fileName, filePath, fullPath);
    }

    private static string GenerateManualDocumentNumber(string documentName, string? employeeCode)
    {
        var prefix = new string(documentName.Split(' ')
            .Where(w => w.Length > 0)
            .Select(w => char.ToUpper(w[0]))
            .ToArray());

        var code = string.IsNullOrEmpty(employeeCode) ? "EMP" : employeeCode;
        return $"MAN-{prefix}-{code}-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..4].ToUpper()}";
    }

    private static string GetMimeType(string extension)
    {
        return extension.ToLower() switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".txt" => "text/plain",
            ".html" => "text/html",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "application/octet-stream"
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



    private static async Task<HashSet<string>> GetHrUserIdsInOrgAsync(
       ApplicationDbContext db, Guid organizationId)
    {
        var ids = await (
            from u in db.Users
            join ur in db.UserRoles on u.Id equals ur.UserId
            join r in db.Roles on ur.RoleId equals r.Id
            where u.OrganizationId == organizationId &&
                  r.Name == "HR"  // Only HR, not OrgAdmin
            select u.Id
        ).ToListAsync();

        return ids.ToHashSet();
    }
}