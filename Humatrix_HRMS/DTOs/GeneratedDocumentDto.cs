using Microsoft.AspNetCore.Components.Forms;

namespace Humatrix_HRMS.DTOs.Documents;

public class GeneratedDocumentDto
{
    public Guid DocumentId { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = default!;
    public string EmployeeCode { get; set; } = default!;
    public string DepartmentName { get; set; } = default!;
    public Guid DocumentTypeId { get; set; }
    public string DocumentTypeName { get; set; } = default!;
    public string? OriginalFileName { get; set; }
    public string? FilePath { get; set; }
    public Guid DepartmentId { get; set; }  // ADD THIS PROPERTY

    public DateTime UploadedAt { get; set; }
    public string UploadedByName { get; set; } = default!;
    public string UploadedByRole { get; set; } = default!;
    public string? Description { get; set; }
    public DateTime? EffectiveDate { get; set; }
}

public class UploadGeneratedDocumentDto
{
    public Guid EmployeeId { get; set; }
    public Guid DocumentTypeId { get; set; }
    public IBrowserFile File { get; set; } = default!;
    public string? Description { get; set; }
    public DateTime? EffectiveDate { get; set; }
}

public class GeneratedDocumentDashboardDto
{
    public int TotalGeneratedDocuments { get; set; }
    public List<GeneratedDocumentDto> RecentDocuments { get; set; } = new();
    public List<GeneratedDocumentDto> AllDocuments { get; set; } = new();
}