using Humatrix_HRMS.DTOs.Documents;
using Humatrix_HRMS.Models.Documents;

namespace Humatrix_HRMS.Services.Documents;

public interface IEmployeeDocumentService
{
    Task<EmployeeDocument> UploadDocumentAsync(
        DocumentUploadDto dto,
        Guid organizationId,
        string userId,
        string role);

    Task<List<EmployeeDocument>> GetEmployeeDocumentsAsync(
        Guid employeeId);

    Task<EmployeeDocument?> GetDocumentAsync(
        Guid documentId);

    Task<EmployeeDocumentDashboardDto> GetEmployeeDocumentDashboardAsync(
       Guid employeeId,
       Guid organizationId);

    Task<bool> DeleteDocumentAsync(
        Guid documentId,
        string userId);

    Task<List<DocumentType>> GetMissingMandatoryDocumentsAsync(
        Guid employeeId,
        Guid organizationId);

    Task<int?> GetProfileCompletionPercentageAsync(
        Guid employeeId,
        Guid organizationId);
    // Add to IEmployeeDocumentService.cs
    Task<List<EmployeeDocument>> GetAllEmployeeDocumentsWithHistoryAsync(Guid employeeId);

    Task<CompanyComplianceDto> GetCompanyComplianceDashboardAsync(Guid organizationId);



}