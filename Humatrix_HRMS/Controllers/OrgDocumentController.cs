using Humatrix_HRMS.Data;
using Humatrix_HRMS.Data.SeedData;
using Humatrix_HRMS.DTOs.Documents;
using Humatrix_HRMS.Models;
using Humatrix_HRMS.Services;
using Humatrix_HRMS.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class OrgDocumentController : ControllerBase
{
    private readonly IOrgDocumentGenerationService _documentService;
    private readonly CurrentUserService _currentUserService;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<OrgDocumentController> _logger;

    public OrgDocumentController(
        IOrgDocumentGenerationService documentService,
        CurrentUserService currentUserService,
        ApplicationDbContext db,
        ILogger<OrgDocumentController> logger)
    {
        _documentService = documentService;
        _currentUserService = currentUserService;
        _db = db;
        _logger = logger;
    }

    // ==================== TEMPLATE MANAGEMENT ====================

    /// <summary>
    /// Get all document templates (OrgAdmin and HR can view)
    /// </summary>
    [HttpGet("templates")]
    public async Task<IActionResult> GetAllTemplates([FromQuery] bool includeInactive = false)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        var templates = await _documentService.GetAllTemplatesAsync(user.OrganizationId.Value, includeInactive);
        return Ok(templates);
    }

    /// <summary>
    /// Get templates by category
    /// </summary>
    [HttpGet("templates/category/{category}")]
    public async Task<IActionResult> GetTemplatesByCategory(string category)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        var templates = await _documentService.GetTemplatesByCategoryAsync(user.OrganizationId.Value, category);
        return Ok(templates);
    }

    /// <summary>
    /// Get a single template by ID
    /// </summary>
    [HttpGet("templates/{templateId}")]
    public async Task<IActionResult> GetTemplateById(Guid templateId)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        var template = await _documentService.GetTemplateByIdAsync(templateId, user.OrganizationId.Value);
        if (template == null)
            return NotFound();

        return Ok(template);
    }

    /// <summary>
    /// Create a new document template (OrgAdmin only)
    /// </summary>
    [HttpPost("templates")]
    [Authorize(Roles = "OrgAdmin")]
    public async Task<IActionResult> CreateTemplate([FromBody] OrgDocumentTemplateDto dto)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        var roles = await GetUserRolesAsync(user.Id);
        var primaryRole = roles.FirstOrDefault() ?? "OrgAdmin";

        var template = await _documentService.CreateTemplateAsync(
            dto,
            user.OrganizationId.Value,
            user.Id,
            primaryRole);

        return Ok(template);
    }

    /// <summary>
    /// Update an existing template (OrgAdmin only)
    /// </summary>
    [HttpPut("templates/{templateId}")]
    [Authorize(Roles = "OrgAdmin")]
    public async Task<IActionResult> UpdateTemplate(Guid templateId, [FromBody] UpdateOrgDocumentTemplateDto dto)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        var template = await _documentService.UpdateTemplateAsync(
            templateId,
            dto,
            user.OrganizationId.Value,
            user.Id);

        return Ok(template);
    }

    /// <summary>
    /// Delete a template (OrgAdmin only)
    /// </summary>
    [HttpDelete("templates/{templateId}")]
    [Authorize(Roles = "OrgAdmin")]
    public async Task<IActionResult> DeleteTemplate(Guid templateId)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        try
        {
            var result = await _documentService.DeleteTemplateAsync(templateId, user.OrganizationId.Value);
            if (!result)
                return NotFound();

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }




    // ==================== SEED TEMPLATES ====================

    /// <summary>
    /// Seed default document templates (OrgAdmin only)
    /// </summary>
    // ==================== SEED TEMPLATES ====================

    /// <summary>
    /// Seed default document templates (OrgAdmin only)
    /// </summary>
    [HttpPost("seed-templates")]
    [Authorize(Roles = "OrgAdmin")]
    public async Task<IActionResult> SeedTemplates()
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        // Seed the templates for this organization
        await OrgDocumentTemplateSeeder.SeedTemplatesForOrganizationAsync(_db, user.OrganizationId.Value);

        // Get all seeded templates
        var templates = await _db.OrgDocumentTemplates
            .Where(x => x.OrganizationId == user.OrganizationId.Value && x.IsActive)
            .Select(x => new { x.TemplateId, x.Name, x.Category, x.DisplayOrder })
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync();

        return Ok(new
        {
            message = "Templates seeded successfully!",
            count = templates.Count,
            templates = templates
        });
    }
    // ==================== DOCUMENT GENERATION ====================

    /// <summary>
    /// Generate a document for an employee (OrgAdmin or Department HR)
    /// </summary>
    [HttpPost("generate")]
    public async Task<IActionResult> GenerateDocument([FromBody] OrgDocumentGenerationDto dto)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        var roles = await GetUserRolesAsync(user.Id);
        var primaryRole = roles.FirstOrDefault() ?? "Employee";

        // Only OrgAdmin and HR can generate documents
        if (primaryRole != "OrgAdmin" && primaryRole != "HR")
            return Forbid();

        try
        {
            var result = await _documentService.GenerateDocumentAsync(
                dto,
                user.OrganizationId.Value,
                user.Id,
                primaryRole);

            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Bulk generate documents for multiple employees (OrgAdmin only)
    /// </summary>
    [HttpPost("generate/bulk")]
    [Authorize(Roles = "OrgAdmin")]
    public async Task<IActionResult> GenerateDocumentsBulk(
        [FromQuery] Guid templateId,
        [FromBody] List<Guid> employeeIds,
        [FromQuery] string? customDataJson = null)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        var roles = await GetUserRolesAsync(user.Id);
        var primaryRole = roles.FirstOrDefault() ?? "Employee";

        Dictionary<string, string>? customData = null;
        if (!string.IsNullOrEmpty(customDataJson))
        {
            try
            {
                customData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(customDataJson);
            }
            catch { }
        }

        var results = await _documentService.GenerateDocumentsBulkAsync(
            templateId,
            employeeIds,
            customData,
            user.OrganizationId.Value,
            user.Id,
            primaryRole);

        return Ok(new { total = results.Count, documents = results });
    }

    // ==================== VIEWING DOCUMENTS ====================

    /// <summary>
    /// Get all organization documents for the logged-in employee
    /// </summary>
    [HttpGet("my-documents")]
    [Authorize(Roles = "Employee,HR")]
    public async Task<IActionResult> GetMyDocuments()
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        var employee = await GetEmployeeByUserIdAsync(user.Id);
        if (employee == null)
            return NotFound("Employee profile not found.");

        var documents = await _documentService.GetDocumentsForEmployeeAsync(employee.EmployeeId, user.OrganizationId.Value);
        return Ok(documents);
    }




    /// <summary>
    /// Get all documents generated by me (OrgAdmin/HR)
    /// </summary>
    [HttpGet("my-generated")]
    [Authorize(Roles = "OrgAdmin,HR")]
    public async Task<IActionResult> GetMyGeneratedDocuments()
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        var documents = await _documentService.GetDocumentsGeneratedByUserAsync(user.Id, user.OrganizationId.Value);
        return Ok(documents);
    }

    /// <summary>
    /// Get documents for a specific employee (OrgAdmin or Department HR)
    /// </summary>
    [HttpGet("employee/{employeeId}")]
    public async Task<IActionResult> GetEmployeeDocuments(Guid employeeId)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        var roles = await GetUserRolesAsync(user.Id);
        var isOrgAdmin = roles.Contains("OrgAdmin");
        var isHR = roles.Contains("HR");

        if (!isOrgAdmin && !isHR)
            return Forbid();

        // Check if HR has access to this employee
        if (isHR && !isOrgAdmin)
        {
            var canAccess = await _documentService.CanGenerateDocumentForEmployeeAsync(
                employeeId, user.OrganizationId.Value, user.Id, "HR");

            if (!canAccess)
                return Forbid();
        }

        var documents = await _documentService.GetDocumentsForEmployeeAsync(employeeId, user.OrganizationId.Value);
        return Ok(documents);
    }

    /// <summary>
    /// Download a document by ID
    /// </summary>
    [HttpGet("download/{documentId}")]
    public async Task<IActionResult> DownloadDocument(Guid documentId)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        var document = await _documentService.GetDocumentByIdAsync(documentId, user.OrganizationId.Value);
        if (document == null)
            return NotFound();

        // Check if user has access to this document
        var roles = await GetUserRolesAsync(user.Id);
        var isOrgAdmin = roles.Contains("OrgAdmin");
        var isHR = roles.Contains("HR");
        var employee = await GetEmployeeByUserIdAsync(user.Id);

        if (!isOrgAdmin && !isHR && document.EmployeeId != employee?.EmployeeId)
            return Forbid();

        if (isHR && !isOrgAdmin && employee != null)
        {
            var canAccess = await _documentService.CanGenerateDocumentForEmployeeAsync(
                document.EmployeeId, user.OrganizationId.Value, user.Id, "HR");

            if (!canAccess)
                return Forbid();
        }

        var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var fullPath = Path.Combine(webRoot, document.FilePath.TrimStart('/'));

        if (!System.IO.File.Exists(fullPath))
            return NotFound("File not found.");

        var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
        return File(bytes, document.MimeType, document.OriginalFileName);
    }

    // ==================== ELIGIBLE RECIPIENTS ====================

    /// <summary>
    /// Get employees eligible to receive documents (based on user role)
    /// </summary>
    [HttpGet("eligible-recipients")]
    public async Task<IActionResult> GetEligibleRecipients()
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        var roles = await GetUserRolesAsync(user.Id);
        var primaryRole = roles.FirstOrDefault() ?? "Employee";

        if (primaryRole != "OrgAdmin" && primaryRole != "HR")
            return Forbid();

        var recipients = await _documentService.GetEligibleRecipientsForDocumentAsync(
            user.OrganizationId.Value, user.Id, primaryRole);

        return Ok(recipients.Select(e => new
        {
            e.EmployeeId,
            e.EmployeeCode,
            FullName = $"{e.FirstName} {e.LastName}",
            DepartmentName = e.Department != null ? e.Department.Name : "N/A",
            DesignationName = e.Designation != null ? e.Designation.Name : "N/A",
            e.Status
        }));
    }

    // ==================== ACKNOWLEDGMENT ====================

    /// <summary>
    /// Acknowledge receipt of a document (Employee only)
    /// </summary>
    [HttpPost("{documentId}/acknowledge")]
    [Authorize(Roles = "Employee")]
    public async Task<IActionResult> AcknowledgeDocument(Guid documentId, [FromQuery] string? remarks = null)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        var employee = await GetEmployeeByUserIdAsync(user.Id);
        if (employee == null)
            return NotFound("Employee profile not found.");

        var result = await _documentService.AcknowledgeDocumentAsync(documentId, employee.EmployeeId, remarks);

        if (!result)
            return NotFound();

        return Ok(new { message = "Document acknowledged successfully." });
    }


    /// <summary>
    /// Generate document using AI (OrgAdmin only)
    /// </summary>
    [HttpPost("generate-ai")]
    [Authorize(Roles = "OrgAdmin")]
    public async Task<IActionResult> GenerateDocumentWithAI([FromBody] OrgDocumentGenerationDto dto)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        var roles = await GetUserRolesAsync(user.Id);
        var primaryRole = roles.FirstOrDefault() ?? "Employee";

        var result = await _documentService.GenerateDocumentWithAIAsync(
            dto.TemplateId,
            dto.RecipientEmployeeId,
            user.Id,
            primaryRole,
            dto.CustomData,
            dto.SendEmailNotification);

        return Ok(result);
    }

    /// <summary>
    /// Get AI suggestions for an employee
    /// </summary>
    [HttpGet("suggestions/{employeeId}")]
    [Authorize(Roles = "OrgAdmin,HR")]
    public async Task<IActionResult> GetAIDocumentSuggestions(Guid employeeId)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        var canAccess = await _documentService.CanGenerateDocumentForEmployeeAsync(
            employeeId, user.OrganizationId.Value, user.Id, "HR");

        if (!canAccess)
            return Forbid();

        var suggestions = await _documentService.GetAIDocumentSuggestionsAsync(employeeId);
        return Ok(suggestions);
    }

    /// <summary>
    /// Auto-generate required documents (background job)
    /// </summary>
    [HttpPost("auto-generate")]
    [Authorize(Roles = "OrgAdmin")]
    public async Task<IActionResult> AutoGenerateRequiredDocuments()
    {
        var user = await _currentUserService.GetUserAsync();
        if (user?.OrganizationId == null)
            return Unauthorized();

        var generatedCount = await _documentService.AutoGenerateRequiredDocumentsAsync(user.OrganizationId.Value);

        return Ok(new { message = $"Auto-generated {generatedCount} documents", count = generatedCount });
    }

    // ==================== PRIVATE HELPERS ====================

    private async Task<List<string>> GetUserRolesAsync(string userId)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user == null) return new List<string>();

        var roles = new List<string>();

        if (await _currentUserService.IsInRoleAsync("OrgAdmin"))
            roles.Add("OrgAdmin");

        if (await _currentUserService.IsInRoleAsync("HR"))
            roles.Add("HR");

        if (!roles.Any())
            roles.Add("Employee");

        return roles;
    }

    private async Task<Employee?> GetEmployeeByUserIdAsync(string userId)
    {
        return await _db.Employees.FirstOrDefaultAsync(e => e.UserId == userId);
    }
}