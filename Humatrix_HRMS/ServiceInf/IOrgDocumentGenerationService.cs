// Services/Documents/IOrgDocumentGenerationService.cs
using Humatrix_HRMS.DTOs.Documents;
using Humatrix_HRMS.Models;
using Humatrix_HRMS.Models.Documents;
using Humatrix_HRMS.Services.AI;

namespace Humatrix_HRMS.Services.Documents;

public interface IOrgDocumentGenerationService
{
    // ── Template management ───────────────────────────────────
    Task<OrgDocumentTemplate> CreateTemplateAsync(OrgDocumentTemplateDto dto, Guid organizationId, string userId, string userRole);
    Task<OrgDocumentTemplate> UpdateTemplateAsync(Guid templateId, UpdateOrgDocumentTemplateDto dto, Guid organizationId, string userId);
    Task<bool> DeleteTemplateAsync(Guid templateId, Guid organizationId);
    Task<OrgDocumentTemplate?> GetTemplateByIdAsync(Guid templateId, Guid organizationId);
    Task<List<OrgDocumentTemplate>> GetTemplatesByCategoryAsync(Guid organizationId, string category);
    Task<List<OrgDocumentTemplate>> GetAllTemplatesAsync(Guid organizationId, bool includeInactive = false);

    // ── Document generation — manual (from UI) ────────────────
    Task<OrgDocumentGenerationResponseDto> GenerateDocumentAsync(OrgDocumentGenerationDto dto, Guid organizationId, string userId, string userRole);
    Task<OrgDocumentGenerationResponseDto> GenerateDocumentWithAIAsync(Guid templateId, Guid recipientEmployeeId, string userId, string userRole, Dictionary<string, string>? customData = null, bool sendEmail = false);
    Task<List<OrgDocumentGenerationResponseDto>> GenerateDocumentsBulkAsync(Guid templateId, List<Guid> employeeIds, Dictionary<string, string>? commonCustomData, Guid organizationId, string userId, string userRole);

    // ── Document generation — system (background jobs only) ───
    /// <summary>
    /// Generates a document without sending any notification.
    /// Used exclusively by background services (AIEventMonitor).
    /// The caller is responsible for sending a consolidated notification.
    /// </summary>
    Task<OrgDocumentGenerationResponseDto> GenerateDocumentSystemAsync(Guid templateId, Guid recipientEmployeeId, string actorUserId, string actorRole, Dictionary<string, string>? customData = null);

    // ── Viewing ───────────────────────────────────────────────
    Task<List<OrgGeneratedDocument>> GetDocumentsForEmployeeAsync(Guid employeeId, Guid organizationId);
    Task<List<OrgGeneratedDocument>> GetDocumentsGeneratedByUserAsync(string userId, Guid organizationId);
    Task<OrgGeneratedDocument?> GetDocumentByIdAsync(Guid documentId, Guid organizationId);
    Task<List<OrgDocumentHistory>> GetDocumentHistoryAsync(Guid documentId);

    // ── State transitions ─────────────────────────────────────
    Task<bool> AcknowledgeDocumentAsync(Guid documentId, Guid employeeId, string? remarks = null);
    Task<bool> RevokeDocumentAsync(Guid documentId, string userId, string userRole, string reason);

    // ── Permissions ───────────────────────────────────────────
    Task<bool> CanGenerateDocumentForEmployeeAsync(Guid employeeId, Guid organizationId, string userId, string userRole);
    Task<List<Employee>> GetEligibleRecipientsForDocumentAsync(Guid organizationId, string userId, string userRole);

    // ── AI ────────────────────────────────────────────────────
    Task<List<AIDocumentSuggestion>> GetAIDocumentSuggestionsAsync(Guid employeeId);
}