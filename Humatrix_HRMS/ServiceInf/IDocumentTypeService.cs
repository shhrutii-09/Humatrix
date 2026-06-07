using Humatrix_HRMS.Models.Documents;

namespace Humatrix_HRMS.Services.Documents;

public interface IDocumentTypeService
{
    Task<List<DocumentType>> GetOrganizationDocumentTypesAsync(
        Guid organizationId);

    Task<DocumentType?> GetByIdAsync(
        Guid documentTypeId,
        Guid organizationId);

    Task<DocumentType> CreateAsync(
        DocumentType model);

    Task<DocumentType> UpdateAsync(
        DocumentType model);

    Task<bool> ToggleStatusAsync(
        Guid documentTypeId,
        Guid organizationId);

    Task<bool> DeleteAsync(
        Guid documentTypeId,
        Guid organizationId);

    // Add these methods to IDocumentTypeService interface
    Task<List<DocumentType>> GetMandatoryDocumentTypesAsync(Guid organizationId);
    Task<List<DocumentType>> GetDocumentTypesForEmployeeAsync(Guid organizationId);
}