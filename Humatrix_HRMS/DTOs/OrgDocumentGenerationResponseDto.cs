namespace Humatrix_HRMS.DTOs.Documents;

/// <summary>
/// Response after generating a document
/// </summary>
public class OrgDocumentGenerationResponseDto
{
    public Guid DocumentId { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string DocumentTypeName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public string GeneratedBy { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool EmailSent { get; set; }
}