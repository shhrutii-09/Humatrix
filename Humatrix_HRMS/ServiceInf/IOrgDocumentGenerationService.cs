// Services/Documents/IOrgDocumentGenerationService.cs
using Humatrix_HRMS.DTOs.Documents;
using Humatrix_HRMS.Models;
using Humatrix_HRMS.Models.Documents;
using Humatrix_HRMS.Services.AI;
using Microsoft.AspNetCore.Http;

namespace Humatrix_HRMS.Services.Documents;

public interface IOrgDocumentGenerationService
{
    // ══════════════════════════════════════════════════════════
    //  TEMPLATE MANAGEMENT (Optional - can keep or remove)
    // ══════════════════════════════════════════════════════════
    Task<OrgDocumentTemplate> CreateTemplateAsync(OrgDocumentTemplateDto dto, Guid organizationId, string userId, string userRole);
    Task<OrgDocumentTemplate> UpdateTemplateAsync(Guid templateId, UpdateOrgDocumentTemplateDto dto, Guid organizationId, string userId);
    Task<bool> DeleteTemplateAsync(Guid templateId, Guid organizationId);
    Task<OrgDocumentTemplate?> GetTemplateByIdAsync(Guid templateId, Guid organizationId);
    Task<List<OrgDocumentTemplate>> GetTemplatesByCategoryAsync(Guid organizationId, string category);
    Task<List<OrgDocumentTemplate>> GetAllTemplatesAsync(Guid organizationId, bool includeInactive = false);

    Task<List<EmployeeWithRoleDto>> GetEligibleRecipientsWithRolesAsync(
    Guid organizationId, string userId, string userRole);

    Task<Dictionary<Guid, string>> GetEmployeeRolesAsync(Guid organizationId);

    // ══════════════════════════════════════════════════════════
    //  MANUAL DOCUMENT UPLOAD (New - replaces AI generation)
    // ══════════════════════════════════════════════════════════
    Task<OrgDocumentGenerationResponseDto> UploadDocumentManuallyAsync(
        ManualDocumentUploadDto dto,
        Guid organizationId,
        string userId,
        string userRole,
        string fileName,
        string contentType,
        byte[] fileContent);

    // ══════════════════════════════════════════════════════════
    //  VIEWING
    // ══════════════════════════════════════════════════════════

    Task MarkAsViewedAsync(Guid documentId, Guid employeeId);
    Task<List<OrgGeneratedDocument>> GetDocumentsForEmployeeAsync(Guid employeeId, Guid organizationId);
    Task<List<OrgGeneratedDocument>> GetDocumentsGeneratedByUserAsync(string userId, Guid organizationId);
    Task<List<OrgGeneratedDocument>> GetOrganizationDocumentsAsync(Guid organizationId, string userId, string userRole);
    Task<OrgGeneratedDocument?> GetDocumentByIdAsync(Guid documentId, Guid organizationId);

    // ══════════════════════════════════════════════════════════
    //  ACKNOWLEDGMENT & REVOKE
    // ══════════════════════════════════════════════════════════
    Task<bool> AcknowledgeDocumentAsync(Guid documentId, Guid employeeId, string? remarks = null);
    Task<bool> RevokeDocumentAsync(Guid documentId, string userId, string userRole, string reason);

    // ══════════════════════════════════════════════════════════
    //  PERMISSIONS
    // ══════════════════════════════════════════════════════════
    Task<bool> CanUploadDocumentForEmployeeAsync(Guid employeeId, Guid organizationId, string userId, string userRole);
    Task<List<Employee>> GetEligibleRecipientsForDocumentAsync(Guid organizationId, string userId, string userRole);
    Task<List<OrgDocumentHistory>> GetDocumentHistoryAsync(Guid documentId);

    // ══════════════════════════════════════════════════════════
    //  AI SUGGESTIONS ONLY (No generation, just recommendations)
    // ══════════════════════════════════════════════════════════
    Task<List<AIDocumentSuggestion>> GetAIDocumentSuggestionsAsync(Guid employeeId);
    Task<List<DashboardDocumentSuggestion>> GetDashboardSuggestionsAsync(Guid organizationId, string userId, string userRole);
}