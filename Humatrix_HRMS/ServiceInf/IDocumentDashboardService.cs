using Humatrix_HRMS.DTOs.Documents;

namespace Humatrix_HRMS.Services.Documents;

public interface IDocumentDashboardService
{
    Task<DocumentDashboardDto> GetEmployeeDashboardAsync(
        Guid employeeId,
        Guid organizationId);

    Task<HrDocumentDashboardDto> GetHrDashboardAsync(
        Guid organizationId);
}