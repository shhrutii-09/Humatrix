// DTOs/Documents/ManualDocumentUploadDto.cs
namespace Humatrix_HRMS.DTOs.Documents;

public class ManualDocumentUploadDto
{
    public Guid RecipientEmployeeId { get; set; }
    public string DocumentName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Remarks { get; set; }
    public bool RequiresAcknowledgment { get; set; } = true;
    public Dictionary<string, string>? CustomMetadata { get; set; }
}