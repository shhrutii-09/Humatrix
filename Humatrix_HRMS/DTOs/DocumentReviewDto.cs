namespace Humatrix_HRMS.DTOs.Documents;

public class DocumentReviewDto
{
    public Guid DocumentId { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = default!;
    public string? EmployeeCode { get; set; }
    public string DepartmentName { get; set; } = default!;
    public Guid DocumentTypeId { get; set; }
    public string DocumentTypeName { get; set; } = default!;
    public bool IsMandatory { get; set; }
    public string Status { get; set; } = default!;
    public string? RejectionRemarks { get; set; }
    public int Version { get; set; }
    public Guid DepartmentId { get; set; }  // Add this property

    public DateTime UploadedAt { get; set; }
    public string UploadedByName { get; set; } = default!;
    public string? OriginalFileName { get; set; }
    public string? FilePath { get; set; }
}