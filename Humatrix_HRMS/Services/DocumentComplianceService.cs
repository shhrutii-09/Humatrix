using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs.Documents;
using Humatrix_HRMS.Models.Documents;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services.Documents;

public class DocumentComplianceService
    : IDocumentComplianceService
{
    private readonly ApplicationDbContext _db;

    public DocumentComplianceService(
        ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<EmployeeComplianceDto>
        GetEmployeeComplianceAsync(
            Guid employeeId,
            Guid organizationId)
    {
        var mandatoryDocumentTypes =
            await _db.DocumentTypes
                .Where(x =>
                    x.OrganizationId == organizationId &&
                    x.IsMandatory &&
                    x.IsActive)
                .ToListAsync();

        var uploadedDocumentTypeIds =
            await _db.EmployeeDocuments
                .Where(x =>
                    x.EmployeeId == employeeId &&
                    x.IsLatestVersion)
                .Select(x => x.DocumentTypeId)
                .ToListAsync();

        var missingDocuments =
            mandatoryDocumentTypes
                .Where(x =>
                    !uploadedDocumentTypeIds.Contains(
                        x.DocumentTypeId))
                .ToList();

        int totalRequired =
            mandatoryDocumentTypes.Count;

        int uploaded =
            totalRequired - missingDocuments.Count;

        int completion =
            totalRequired == 0
                ? 100
                : (int)Math.Round(
                    uploaded * 100.0 /
                    totalRequired);

        return new EmployeeComplianceDto
        {
            EmployeeId = employeeId,

            RequiredDocuments = totalRequired,

            UploadedDocuments = uploaded,

            ProfileCompletionPercentage = completion,

            IsFullyCompliant =
                missingDocuments.Count == 0,

            MissingDocuments = missingDocuments
        };
    }

    public async Task<OrganizationComplianceDto>
        GetOrganizationComplianceAsync(
            Guid organizationId)
    {
        var employees =
            await _db.Employees
                .Where(x =>
                    x.OrganizationId == organizationId &&
                    x.Status == "Active")
                .Select(x => x.EmployeeId)
                .ToListAsync();

        int totalEmployees = employees.Count;

        if (totalEmployees == 0)
        {
            return new OrganizationComplianceDto
            {
                TotalEmployees = 0
            };
        }

        int compliantEmployees = 0;

        foreach (var employeeId in employees)
        {
            var result =
                await GetEmployeeComplianceAsync(
                    employeeId,
                    organizationId);

            if (result.IsFullyCompliant)
            {
                compliantEmployees++;
            }
        }

        return new OrganizationComplianceDto
        {
            TotalEmployees = totalEmployees,

            FullyCompliantEmployees =
                compliantEmployees,

            NonCompliantEmployees =
                totalEmployees -
                compliantEmployees,

            CompliancePercentage =
                Math.Round(
                    compliantEmployees * 100.0 /
                    totalEmployees,
                    2)
        };
    }
}