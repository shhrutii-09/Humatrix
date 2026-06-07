using System.Text.Json;
using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models.Documents;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services.Documents;

public class DocumentHistoryService : IDocumentHistoryService
{
    private readonly ApplicationDbContext _db;

    public DocumentHistoryService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(
        EmployeeDocument document,
        string action,
        string actorUserId,
        string actorRole,
        string? oldStatus = null,
        string? newStatus = null,
        string? remarks = null)
    {
        var history = new DocumentHistory
        {
            DocumentId = document.DocumentId,
            EmployeeId = document.EmployeeId,
            OrganizationId = document.OrganizationId,

            Action = action,

            ActorUserId = actorUserId,
            ActorRole = actorRole,

            OldStatus = oldStatus,
            NewStatus = newStatus,

            Remarks = remarks,

            SnapshotJson = JsonSerializer.Serialize(new
            {
                document.DocumentId,
                document.EmployeeId,
                document.DocumentTypeId,
                document.FileName,
                document.Status,
                document.Version,
                document.UploadedAt
            }),

            OccurredAt = DateTime.UtcNow
        };

        _db.DocumentHistories.Add(history);

        await _db.SaveChangesAsync();
    }

    public async Task<List<DocumentHistory>> GetDocumentHistoryAsync(
        Guid documentId)
    {
        return await _db.DocumentHistories
            .Where(x => x.DocumentId == documentId)
            .OrderByDescending(x => x.OccurredAt)
            .ToListAsync();
    }

    public async Task<List<DocumentHistory>> GetEmployeeHistoryAsync(
        Guid employeeId)
    {
        return await _db.DocumentHistories
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.OccurredAt)
            .ToListAsync();
    }

    public async Task<List<DocumentHistory>> GetOrganizationHistoryAsync(
        Guid organizationId,
        int take = 100)
    {
        return await _db.DocumentHistories
            .Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.OccurredAt)
            .Take(take)
            .ToListAsync();
    }
}