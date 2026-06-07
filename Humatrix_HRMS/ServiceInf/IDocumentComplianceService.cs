using Humatrix_HRMS.DTOs.Documents;

namespace Humatrix_HRMS.Services.Documents;

public interface IDocumentComplianceService
{
    Task<EmployeeComplianceDto> GetEmployeeComplianceAsync(
        Guid employeeId,
        Guid organizationId);

    Task<OrganizationComplianceDto> GetOrganizationComplianceAsync(
        Guid organizationId);
}