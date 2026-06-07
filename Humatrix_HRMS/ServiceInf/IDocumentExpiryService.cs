using Humatrix_HRMS.Models.Documents;

namespace Humatrix_HRMS.Services.Documents;

public interface IDocumentExpiryService
{
    Task<List<EmployeeDocument>> GetExpiringDocumentsAsync(
        Guid organizationId,
        int days);

    Task<List<EmployeeDocument>> GetExpiredDocumentsAsync(
        Guid organizationId);

    Task<int> GenerateExpiryAlertsAsync();

    Task<List<DocumentExpiryAlert>>
        GetEmployeeAlertsAsync(Guid employeeId);
}