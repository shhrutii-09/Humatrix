using Microsoft.AspNetCore.Components.Forms;

namespace Humatrix_HRMS.DTOs.Documents;

public class DocumentUploadDto
{
    public Guid EmployeeId { get; set; }

    public Guid DocumentTypeId { get; set; }

    public IBrowserFile File { get; set; } = default!;

    public string? DocumentNumber { get; set; }

    public DateOnly? IssueDate { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public string? IssuingAuthority { get; set; }
}