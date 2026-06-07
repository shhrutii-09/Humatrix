using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs.Documents;
using Humatrix_HRMS.Models.Documents;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services.Documents;

public class DocumentDashboardService
    : IDocumentDashboardService
{
    private readonly ApplicationDbContext _db;

    public DocumentDashboardService(
        ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<DocumentDashboardDto>
        GetEmployeeDashboardAsync(
            Guid employeeId,
            Guid organizationId)
    {
        int mandatoryCount =
            await _db.DocumentTypes
                .CountAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.IsMandatory &&
                    x.IsActive);

        int uploadedMandatory =
            await _db.EmployeeDocuments
                .CountAsync(x =>
                    x.EmployeeId == employeeId &&
                    x.IsLatestVersion &&
                    x.DocumentType!.IsMandatory);

        int completion =
            mandatoryCount == 0
            ? 100
            : (int)Math.Round(
                uploadedMandatory * 100m /
                mandatoryCount);

        int pending =
            await _db.EmployeeDocuments
                .CountAsync(x =>
                    x.EmployeeId == employeeId &&
                    x.IsLatestVersion &&
                    x.Status == DocumentStatus.Pending);

        int rejected =
            await _db.EmployeeDocuments
                .CountAsync(x =>
                    x.EmployeeId == employeeId &&
                    x.IsLatestVersion &&
                    x.Status == DocumentStatus.Rejected);

        DateOnly today =
            DateOnly.FromDateTime(DateTime.UtcNow);

        DateOnly threshold =
            today.AddDays(30);

        int expiringSoon =
            await _db.EmployeeDocuments
                .CountAsync(x =>
                    x.EmployeeId == employeeId &&
                    x.IsLatestVersion &&
                    x.Status == DocumentStatus.Verified &&
                    x.ExpiryDate != null &&
                    x.ExpiryDate <= threshold);

        return new DocumentDashboardDto
        {
            ProfileCompletionPercentage = completion,
            MandatoryDocuments = mandatoryCount,
            UploadedMandatoryDocuments = uploadedMandatory,
            MissingDocuments =
                mandatoryCount - uploadedMandatory,
            PendingVerification = pending,
            RejectedDocuments = rejected,
            ExpiringSoonDocuments = expiringSoon
        };
    }

    public async Task<HrDocumentDashboardDto>
        GetHrDashboardAsync(
            Guid organizationId)
    {
        DateOnly today =
            DateOnly.FromDateTime(DateTime.UtcNow);

        DateOnly threshold =
            today.AddDays(30);

        int pending =
            await _db.EmployeeDocuments
                .CountAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.IsLatestVersion &&
                    x.Status == DocumentStatus.Pending);

        int rejected =
            await _db.EmployeeDocuments
                .CountAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.IsLatestVersion &&
                    x.Status == DocumentStatus.Rejected);

        int expiringSoon =
            await _db.EmployeeDocuments
                .CountAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.IsLatestVersion &&
                    x.Status == DocumentStatus.Verified &&
                    x.ExpiryDate != null &&
                    x.ExpiryDate <= threshold);

        int employeeCount =
            await _db.Employees
                .CountAsync(x =>
                    x.OrganizationId == organizationId &&
                    x.Status == "Active");

        int employeesMissingDocs = 0;

        var employees =
            await _db.Employees
                .Where(x =>
                    x.OrganizationId == organizationId &&
                    x.Status == "Active")
                .Select(x => x.EmployeeId)
                .ToListAsync();

        foreach (var employeeId in employees)
        {
            int required =
                await _db.DocumentTypes
                    .CountAsync(x =>
                        x.OrganizationId == organizationId &&
                        x.IsMandatory &&
                        x.IsActive);

            int uploaded =
                await _db.EmployeeDocuments
                    .CountAsync(x =>
                        x.EmployeeId == employeeId &&
                        x.IsLatestVersion &&
                        x.DocumentType!.IsMandatory);

            if (uploaded < required)
                employeesMissingDocs++;
        }

        decimal compliance = employeeCount == 0
            ? 100
            : Math.Round(
                ((employeeCount - employeesMissingDocs)
                * 100m) / employeeCount,
                2);

        return new HrDocumentDashboardDto
        {
            EmployeesMissingDocuments =
                employeesMissingDocs,

            PendingVerificationCount =
                pending,

            ExpiringSoonCount =
                expiringSoon,

            RejectedDocumentsCount =
                rejected,

            CompliancePercentage =
                compliance
        };
    }
}