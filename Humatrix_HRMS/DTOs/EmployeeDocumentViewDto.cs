using Humatrix_HRMS.Models.Documents;

namespace Humatrix_HRMS.DTOs.Documents;

public class EmployeeDocumentViewDto
{
    public Guid DocumentId { get; set; }
    public Guid DocumentTypeId { get; set; }
    public string DocumentTypeName { get; set; } = default!;
    public string Category { get; set; } = default!;
    public bool IsMandatory { get; set; }
    public bool RequiresVerification { get; set; }
    public bool TrackExpiry { get; set; }

    // These are needed for the upload modal validation
    public string AllowedFileTypes { get; set; } = ".pdf,.jpg,.png";
    public int MaxFileSizeMB { get; set; } = 5;

    // Document instance data
    public string? OriginalFileName { get; set; }
    public string? FilePath { get; set; }
    public string Status { get; set; } = "NotUploaded";
    public string? RejectionRemarks { get; set; }
    public int Version { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public DateTime? UploadedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? VerifiedByName { get; set; }

    // For display
    public string StatusBadgeClass => Status switch
    {
        "Verified" => "bg-success",
        "Pending" => "bg-warning text-dark",
        "Rejected" => "bg-danger",
        "Expired" => "bg-secondary",
        "Superseded" => "bg-info",
        "NotUploaded" => "bg-light text-dark border",
        _ => "bg-secondary"
    };

    public string StatusIcon => Status switch
    {
        "Verified" => "✓",
        "Pending" => "⏳",
        "Rejected" => "✗",
        "Expired" => "⌛",
        "NotUploaded" => "📄",
        _ => "📄"
    };
}

public class EmployeeDocumentDashboardDto
{
    public int ProfileCompletionPercentage { get; set; }
    public int TotalRequiredDocuments { get; set; }
    public int UploadedDocuments { get; set; }
    public int VerifiedDocuments { get; set; }
    public int PendingDocuments { get; set; }
    public int RejectedDocuments { get; set; }
    public int MissingMandatoryCount { get; set; }
    public List<DocumentType> MissingMandatoryDocuments { get; set; } = new();
    public List<EmployeeDocumentViewDto> Documents { get; set; } = new();
}