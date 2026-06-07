using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models.Documents;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services.Documents;

public class DocumentTypeService : IDocumentTypeService
{
    private readonly ApplicationDbContext _db;

    public DocumentTypeService(
        ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<DocumentType>> GetOrganizationDocumentTypesAsync(
        Guid organizationId)
    {
        return await _db.DocumentTypes
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();
    }

    public async Task<DocumentType?> GetByIdAsync(
        Guid documentTypeId,
        Guid organizationId)
    {
        return await _db.DocumentTypes
            .FirstOrDefaultAsync(x =>
                x.DocumentTypeId == documentTypeId &&
                x.OrganizationId == organizationId);
    }

    public async Task<DocumentType> CreateAsync(
        DocumentType model)
    {
        bool exists = await _db.DocumentTypes
            .AnyAsync(x =>
                x.OrganizationId == model.OrganizationId &&
                x.Name == model.Name);

        if (exists)
            throw new Exception(
                "Document type already exists.");

        _db.DocumentTypes.Add(model);

        await _db.SaveChangesAsync();

        return model;
    }

    public async Task<DocumentType> UpdateAsync(
        DocumentType model)
    {
        var existing = await _db.DocumentTypes
            .FirstOrDefaultAsync(x =>
                x.DocumentTypeId == model.DocumentTypeId);

        if (existing == null)
            throw new Exception("Document type not found.");

        existing.Name = model.Name;
        existing.Description = model.Description;
        existing.Category = model.Category;
        existing.IsMandatory = model.IsMandatory;
        existing.RequiresVerification = model.RequiresVerification;
        existing.TrackExpiry = model.TrackExpiry;
        existing.AllowedFileTypes = model.AllowedFileTypes;
        existing.MaxFileSizeMB = model.MaxFileSizeMB;
        existing.IsEmployeeUploadAllowed = model.IsEmployeeUploadAllowed;
        existing.IsHRUploadAllowed = model.IsHRUploadAllowed;
        existing.DisplayOrder = model.DisplayOrder;
        existing.UpdatedAt = DateTime.UtcNow;
        existing.UpdatedByUserId = model.UpdatedByUserId;

        await _db.SaveChangesAsync();

        return existing;
    }

    public async Task<bool> ToggleStatusAsync(
        Guid documentTypeId,
        Guid organizationId)
    {
        var documentType = await _db.DocumentTypes
            .FirstOrDefaultAsync(x =>
                x.DocumentTypeId == documentTypeId &&
                x.OrganizationId == organizationId);

        if (documentType == null)
            return false;

        documentType.IsActive =
            !documentType.IsActive;

        await _db.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteAsync(
        Guid documentTypeId,
        Guid organizationId)
    {
        bool inUse = await _db.EmployeeDocuments
            .AnyAsync(x =>
                x.DocumentTypeId == documentTypeId);

        if (inUse)
            throw new Exception(
                "Document type is already used.");

        var documentType = await _db.DocumentTypes
            .FirstOrDefaultAsync(x =>
                x.DocumentTypeId == documentTypeId &&
                x.OrganizationId == organizationId);

        if (documentType == null)
            return false;

        _db.DocumentTypes.Remove(documentType);

        await _db.SaveChangesAsync();

        return true;
    }

    // Add these methods to DocumentTypeService class

    public async Task<List<DocumentType>> GetMandatoryDocumentTypesAsync(Guid organizationId)
    {
        return await _db.DocumentTypes
            .Where(x => x.OrganizationId == organizationId
                        && x.IsMandatory
                        && x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();
    }

    public async Task<List<DocumentType>> GetDocumentTypesForEmployeeAsync(Guid organizationId)
    {
        return await _db.DocumentTypes
            .Where(x => x.OrganizationId == organizationId && x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();
    }
}