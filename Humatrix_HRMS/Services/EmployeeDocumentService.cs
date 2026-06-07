using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs.Documents;
using Humatrix_HRMS.Models.Documents;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services.Documents;

public class EmployeeDocumentService : IEmployeeDocumentService
{
    private readonly ApplicationDbContext _db;
    private readonly IDocumentHistoryService _historyService;

    public EmployeeDocumentService(
        ApplicationDbContext db,
        IDocumentHistoryService historyService)
    {
        _db = db;
        _historyService = historyService;
    }

    public async Task<EmployeeDocument> UploadDocumentAsync( DocumentUploadDto dto,
        Guid organizationId,string userId,string role)
    {
                var documentType = await _db.DocumentTypes
                    .FirstOrDefaultAsync(x =>
                        x.DocumentTypeId == dto.DocumentTypeId &&
                        x.OrganizationId == organizationId &&
                        x.IsActive);

                if (documentType == null)
                    throw new Exception("Document type not found.");

                var latestVersion = await _db.EmployeeDocuments
                    .Where(x =>
                        x.EmployeeId == dto.EmployeeId &&
                        x.DocumentTypeId == dto.DocumentTypeId &&
                        x.IsLatestVersion)
                    .FirstOrDefaultAsync();

                int version = 1;
                Guid? previousId = null;

                if (latestVersion != null)
                {
                    latestVersion.IsLatestVersion = false;

                    version = latestVersion.Version + 1;
                    previousId = latestVersion.DocumentId;
                }

                // File storage service later
                string storedFileName =
                    $"{Guid.NewGuid()}{Path.GetExtension(dto.File.Name)}";

                string folder =
                    Path.Combine(
                        "wwwroot",
                        "uploads",
                        "documents");

                Directory.CreateDirectory(folder);

                string fullPath =
                    Path.Combine(folder, storedFileName);

                await using var stream =
                    new FileStream(fullPath, FileMode.Create);

                await dto.File.OpenReadStream(
                    10 * 1024 * 1024)
                    .CopyToAsync(stream);

                var document = new EmployeeDocument
                {
                    OrganizationId = organizationId,
                    EmployeeId = dto.EmployeeId,
                    DocumentTypeId = dto.DocumentTypeId,

                    FileName = storedFileName,
                    OriginalFileName = dto.File.Name,
                    FilePath = $"/uploads/documents/{storedFileName}",
                    FileSize = dto.File.Size,
                    MimeType = dto.File.ContentType,

                    DocumentNumber = dto.DocumentNumber,
                    IssueDate = dto.IssueDate,
                    ExpiryDate = dto.ExpiryDate,
                    IssuingAuthority = dto.IssuingAuthority,

                    UploadedByUserId = userId,
                    UploadedByRole = role,

                    Status =
                        documentType.RequiresVerification
                        ? DocumentStatus.Pending
                        : DocumentStatus.Verified,

                    PreviousDocumentId = previousId,
                    Version = version,
                    IsLatestVersion = true
                };

                _db.EmployeeDocuments.Add(document);
                await _db.SaveChangesAsync();

                await _historyService.LogAsync(
            document,
            version == 1
                ? DocumentAction.Uploaded
                : DocumentAction.Reuploaded,
            userId,
            role,
            null,
            document.Status);

                return document;


    }

    public async Task<List<EmployeeDocument>>
        GetEmployeeDocumentsAsync(Guid employeeId)
    {
        return await _db.EmployeeDocuments
            .Include(x => x.DocumentType)
            .Where(x =>
                x.EmployeeId == employeeId &&
                x.IsLatestVersion)
            .OrderBy(x => x.DocumentType!.Name)
            .ToListAsync();
    }

    public async Task<EmployeeDocument?>
        GetDocumentAsync(Guid documentId)
    {
        return await _db.EmployeeDocuments
            .Include(x => x.DocumentType)
            .Include(x => x.Employee)
            .FirstOrDefaultAsync(x =>
                x.DocumentId == documentId);
    }

    public async Task<bool> DeleteDocumentAsync(
        Guid documentId,
        string userId)
    {
        var document =
            await _db.EmployeeDocuments
            .FirstOrDefaultAsync(x =>
                x.DocumentId == documentId);

        if (document == null)
            return false;

        document.IsDeleted = true;
        document.DeletedAt = DateTime.UtcNow;
        document.DeletedByUserId = userId;

        await _db.SaveChangesAsync();
        await _historyService.LogAsync(
    document,
    DocumentAction.Deleted,
    userId,
    "System");
        return true;
    }

    public async Task<List<DocumentType>>
        GetMissingMandatoryDocumentsAsync(
            Guid employeeId,
            Guid organizationId)
    {
        var uploadedDocumentTypes =
            await _db.EmployeeDocuments
            .Where(x =>
                x.EmployeeId == employeeId &&
                x.IsLatestVersion)
            .Select(x => x.DocumentTypeId)
            .ToListAsync();

        return await _db.DocumentTypes
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.IsMandatory &&
                x.IsActive &&
                !uploadedDocumentTypes.Contains(
                    x.DocumentTypeId))
            .ToListAsync();
    }

    public async Task<int>
        GetProfileCompletionPercentageAsync(
            Guid employeeId,
            Guid organizationId)
    {
        int totalRequired =
            await _db.DocumentTypes
            .CountAsync(x =>
                x.OrganizationId == organizationId &&
                x.IsMandatory &&
                x.IsActive);

        if (totalRequired == 0)
            return 100;

        int uploaded =
            await _db.EmployeeDocuments
            .CountAsync(x =>
                x.EmployeeId == employeeId &&
                x.IsLatestVersion &&
                x.DocumentType!.IsMandatory);

        return (int)Math.Round(
            (uploaded * 100.0) /
            totalRequired);
    }
}