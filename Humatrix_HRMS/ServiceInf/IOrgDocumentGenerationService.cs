using Humatrix_HRMS.DTOs.Documents;
using Humatrix_HRMS.Models;
using Humatrix_HRMS.Models.Documents;
using Humatrix_HRMS.Services.AI;

namespace Humatrix_HRMS.Services.Documents;

/// <summary>
/// Service for generating organization documents (Offer Letters, Warning Letters, etc.)
/// </summary>
public interface IOrgDocumentGenerationService
{
    // Template Management
    Task<OrgDocumentTemplate> CreateTemplateAsync(OrgDocumentTemplateDto dto, Guid organizationId, string userId, string userRole);
    Task<OrgDocumentTemplate> UpdateTemplateAsync(Guid templateId, UpdateOrgDocumentTemplateDto dto, Guid organizationId, string userId);
    Task<bool> DeleteTemplateAsync(Guid templateId, Guid organizationId);
    Task<OrgDocumentTemplate?> GetTemplateByIdAsync(Guid templateId, Guid organizationId);
    Task<List<OrgDocumentTemplate>> GetTemplatesByCategoryAsync(Guid organizationId, string category);
    Task<List<OrgDocumentTemplate>> GetAllTemplatesAsync(Guid organizationId, bool includeInactive = false);

    // Document Generation
    Task<OrgDocumentGenerationResponseDto> GenerateDocumentAsync(OrgDocumentGenerationDto dto, Guid organizationId, string userId, string userRole);

    // AI-Powered Document Generation (NEW)
    Task<OrgDocumentGenerationResponseDto> GenerateDocumentWithAIAsync(
        Guid templateId,
        Guid recipientEmployeeId,
        string userId,
        string userRole,
        Dictionary<string, string>? customData = null,
        bool sendEmail = true);

    // AI Suggestions (NEW)
    Task<List<AIDocumentSuggestion>> GetAIDocumentSuggestionsAsync(Guid employeeId);

    // Auto-Generate Required Documents (NEW)
    Task<int> AutoGenerateRequiredDocumentsAsync(Guid organizationId);

    // Bulk Generation
    Task<List<OrgDocumentGenerationResponseDto>> GenerateDocumentsBulkAsync(
        Guid templateId,
        List<Guid> employeeIds,
        Dictionary<string, string>? commonCustomData,
        Guid organizationId,
        string userId,
        string userRole);

    // Viewing
    Task<List<OrgGeneratedDocument>> GetDocumentsForEmployeeAsync(Guid employeeId, Guid organizationId);
    Task<List<OrgGeneratedDocument>> GetDocumentsGeneratedByUserAsync(string userId, Guid organizationId);
    Task<OrgGeneratedDocument?> GetDocumentByIdAsync(Guid documentId, Guid organizationId);
    Task<List<OrgDocumentHistory>> GetDocumentHistoryAsync(Guid documentId);

    // Acknowledgment & Revoke
    Task<bool> AcknowledgeDocumentAsync(Guid documentId, Guid employeeId, string? remarks = null);
    Task<bool> RevokeDocumentAsync(Guid documentId, string userId, string userRole, string reason);

    // Permission Helpers
    Task<bool> CanGenerateDocumentForEmployeeAsync(Guid employeeId, Guid organizationId, string userId, string userRole);
    Task<List<Employee>> GetEligibleRecipientsForDocumentAsync(Guid organizationId, string userId, string userRole);
}