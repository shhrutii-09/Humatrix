using Humatrix_HRMS.Models.Documents;

namespace Humatrix_HRMS.Services.Documents;

public interface IDocumentVerificationService
{
    Task<EmployeeDocument> VerifyDocumentAsync(
        Guid documentId,
        string reviewerUserId,
        string reviewerRole,
        string? remarks = null);

    Task<EmployeeDocument> RejectDocumentAsync(
        Guid documentId,
        string reviewerUserId,
        string reviewerRole,
        string rejectionReason);

    Task<List<EmployeeDocument>> GetPendingDocumentsAsync(
        Guid organizationId);

    Task<List<EmployeeDocument>> GetRejectedDocumentsAsync(
        Guid organizationId);
}