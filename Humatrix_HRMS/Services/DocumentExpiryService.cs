using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models.Documents;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services.Documents;

public class DocumentExpiryService
    : IDocumentExpiryService
{
    private readonly ApplicationDbContext _db;
    private readonly IDocumentHistoryService _historyService;

    public DocumentExpiryService(
        ApplicationDbContext db,
        IDocumentHistoryService historyService)
    {
        _db = db;
        _historyService = historyService;
    }

    public async Task<List<EmployeeDocument>>
        GetExpiringDocumentsAsync(
            Guid organizationId,
            int days)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var future = today.AddDays(days);

        return await _db.EmployeeDocuments
            .Include(x => x.Employee)
            .Include(x => x.DocumentType)
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.IsLatestVersion &&
                x.Status == DocumentStatus.Verified &&
                x.ExpiryDate != null &&
                x.ExpiryDate >= today &&
                x.ExpiryDate <= future)
            .OrderBy(x => x.ExpiryDate)
            .ToListAsync();
    }

    public async Task<List<EmployeeDocument>>
        GetExpiredDocumentsAsync(
            Guid organizationId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return await _db.EmployeeDocuments
            .Include(x => x.Employee)
            .Include(x => x.DocumentType)
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.IsLatestVersion &&
                x.ExpiryDate != null &&
                x.ExpiryDate < today)
            .OrderByDescending(x => x.ExpiryDate)
            .ToListAsync();
    }

    public async Task<List<DocumentExpiryAlert>>
        GetEmployeeAlertsAsync(
            Guid employeeId)
    {
        return await _db.DocumentExpiryAlerts
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.AlertSentAt)
            .ToListAsync();
    }

    public async Task<int>
        GenerateExpiryAlertsAsync()
    {
        int created = 0;

        int[] thresholds = { 90, 60, 30 };

        var documents =
            await _db.EmployeeDocuments
                .Include(x => x.DocumentType)
                .Where(x =>
                    x.IsLatestVersion &&
                    x.Status == DocumentStatus.Verified &&
                    x.ExpiryDate != null &&
                    x.DocumentType!.TrackExpiry)
                .ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var doc in documents)
        {
            var daysRemaining =
                doc.ExpiryDate!.Value.DayNumber -
                today.DayNumber;

            foreach (var threshold in thresholds)
            {
                if (daysRemaining > threshold)
                    continue;

                bool alreadyExists =
                    await _db.DocumentExpiryAlerts
                        .AnyAsync(x =>
                            x.DocumentId == doc.DocumentId &&
                            x.DaysBeforeExpiry == threshold);

                if (alreadyExists)
                    continue;

                var alert = new DocumentExpiryAlert
                {
                    DocumentId = doc.DocumentId,
                    EmployeeId = doc.EmployeeId,
                    OrganizationId = doc.OrganizationId,

                    DaysBeforeExpiry = threshold,

                    AlertType = "InApp"
                };

                _db.DocumentExpiryAlerts.Add(alert);

                await _historyService.LogAsync(
                    doc,
                    DocumentAction.ExpiryWarning,
                    "System",
                    "System",
                    remarks:
                        $"Document expires within {threshold} days");

                created++;
            }

            if (doc.ExpiryDate < today &&
                doc.Status != DocumentStatus.Expired)
            {
                doc.Status = DocumentStatus.Expired;

                await _historyService.LogAsync(
                    doc,
                    DocumentAction.Expired,
                    "System",
                    "System");
            }
        }

        await _db.SaveChangesAsync();

        return created;
    }
}