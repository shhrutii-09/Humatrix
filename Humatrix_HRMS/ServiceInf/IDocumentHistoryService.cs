using Humatrix_HRMS.Models.Documents;

namespace Humatrix_HRMS.Services.Documents;

public interface IDocumentHistoryService
{
    Task LogAsync(
        EmployeeDocument document,
        string action,
        string actorUserId,
        string actorRole,
        string? oldStatus = null,
        string? newStatus = null,
        string? remarks = null);

    Task<List<DocumentHistory>> GetDocumentHistoryAsync(Guid documentId);

    Task<List<DocumentHistory>> GetEmployeeHistoryAsync(Guid employeeId);

    Task<List<DocumentHistory>> GetOrganizationHistoryAsync(
        Guid organizationId,
        int take = 100);
}