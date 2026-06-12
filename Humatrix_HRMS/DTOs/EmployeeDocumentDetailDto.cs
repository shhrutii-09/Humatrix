namespace Humatrix_HRMS.DTOs.Documents;

public class EmployeeDocumentDetailDto
{
    public Guid DocumentId { get; set; }
    public Guid DocumentTypeId { get; set; }
    public string DocumentTypeName { get; set; } = default!;
    public string Category { get; set; } = default!;
    public bool IsMandatory { get; set; }
    public bool RequiresVerification { get; set; }

    // Document instance data
    public string? OriginalFileName { get; set; }
    public string? FilePath { get; set; }
    public string Status { get; set; } = default!;
    public string? RejectionRemarks { get; set; }
    public int Version { get; set; }
    public DateTime UploadedAt { get; set; }
    public string UploadedByName { get; set; } = default!;
    public string UploadedByRole { get; set; } = default!;
    public DateTime? VerifiedAt { get; set; }
    public string? VerifiedByName { get; set; }
    public Guid? PreviousDocumentId { get; set; }
    public int? PreviousVersion { get; set; }

    // For display
    public string StatusBadgeClass => Status switch
    {
        "Verified" => "bg-success",
        "Pending" => "bg-warning text-dark",
        "Rejected" => "bg-danger",
        "Superseded" => "bg-info",
        _ => "bg-secondary"
    };

    public string StatusIcon => Status switch
    {
        "Verified" => "✓",
        "Pending" => "⏳",
        "Rejected" => "✗",
        "Superseded" => "↺",
        _ => "📄"
    };
}

public class EmployeeDocumentSummaryDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = default!;
    public string EmployeeCode { get; set; } = default!;
    public string DepartmentName { get; set; } = default!;
    public string DesignationName { get; set; } = default!;
    public int? ProfileCompletionPercentage { get; set; }
    public int TotalRequired { get; set; }
    public int VerifiedCount { get; set; }
    public int PendingCount { get; set; }
    public int RejectedCount { get; set; }
    public int MissingCount { get; set; }
    public List<EmployeeDocumentDetailDto> Documents { get; set; } = new();
}