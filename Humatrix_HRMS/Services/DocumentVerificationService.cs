using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models.Documents;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services.Documents;

public class DocumentVerificationService
    : IDocumentVerificationService
{
    private readonly ApplicationDbContext _db;
    private readonly IDocumentHistoryService _historyService;

    public DocumentVerificationService(
        ApplicationDbContext db,
        IDocumentHistoryService historyService)
    {
        _db = db;
        _historyService = historyService;
    }

    public async Task<EmployeeDocument>
        VerifyDocumentAsync(
            Guid documentId,
            string reviewerUserId,
            string reviewerRole,
            string? remarks = null)
    {
        var document =
            await _db.EmployeeDocuments
                .Include(x => x.DocumentType)
                .FirstOrDefaultAsync(x =>
                    x.DocumentId == documentId);

        if (document == null)
            throw new Exception("Document not found.");

        if (document.Status == DocumentStatus.Verified)
            return document;

        var oldStatus = document.Status;

        document.Status = DocumentStatus.Verified;
        document.VerifiedAt = DateTime.UtcNow;
        document.VerifiedByUserId = reviewerUserId;
        document.VerifiedByRole = reviewerRole;

        await _db.SaveChangesAsync();

        await _historyService.LogAsync(
            document,
            DocumentAction.Verified,
            reviewerUserId,
            reviewerRole,
            oldStatus,
            DocumentStatus.Verified,
            remarks);

        return document;
    }

    public async Task<EmployeeDocument>
        RejectDocumentAsync(
            Guid documentId,
            string reviewerUserId,
            string reviewerRole,
            string rejectionReason)
    {
        var document =
            await _db.EmployeeDocuments
                .Include(x => x.DocumentType)
                .FirstOrDefaultAsync(x =>
                    x.DocumentId == documentId);

        if (document == null)
            throw new Exception("Document not found.");

        var oldStatus = document.Status;

        document.Status = DocumentStatus.Rejected;

        document.RejectionRemarks =
            rejectionReason;

        document.RejectedAt =
            DateTime.UtcNow;

        document.RejectedByUserId =
            reviewerUserId;

        await _db.SaveChangesAsync();

        await _historyService.LogAsync(
            document,
            DocumentAction.Rejected,
            reviewerUserId,
            reviewerRole,
            oldStatus,
            DocumentStatus.Rejected,
            rejectionReason);

        return document;
    }

    public async Task<List<EmployeeDocument>>
        GetPendingDocumentsAsync(
            Guid organizationId)
    {
        return await _db.EmployeeDocuments
            .Include(x => x.Employee)
            .Include(x => x.DocumentType)
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.Status == DocumentStatus.Pending &&
                x.IsLatestVersion)
            .OrderBy(x => x.UploadedAt)
            .ToListAsync();
    }

    public async Task<List<EmployeeDocument>>
        GetRejectedDocumentsAsync(
            Guid organizationId)
    {
        return await _db.EmployeeDocuments
            .Include(x => x.Employee)
            .Include(x => x.DocumentType)
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.Status == DocumentStatus.Rejected &&
                x.IsLatestVersion)
            .OrderByDescending(x => x.RejectedAt)
            .ToListAsync();
    }
}