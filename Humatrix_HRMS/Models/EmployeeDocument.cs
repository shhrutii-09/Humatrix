using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Humatrix_HRMS.Data;

namespace Humatrix_HRMS.Models.Documents
{
    public class EmployeeDocument
    {
        public Guid DocumentId { get; set; } = Guid.NewGuid();

        [Required]
        public Guid OrganizationId { get; set; }

        [Required]
        public Guid EmployeeId { get; set; }

        [Required]
        public Guid DocumentTypeId { get; set; }

        // File metadata
        [Required, MaxLength(500)]
        public string FileName { get; set; } = default!;

        [Required, MaxLength(500)]
        public string OriginalFileName { get; set; } = default!;

        [Required, MaxLength(1000)]
        public string FilePath { get; set; } = default!;

        public long FileSize { get; set; }

        // Add these properties to the EmployeeDocument class (add them near the other properties, around line 50-60)

        [MaxLength(500)]
        public string? Description { get; set; }

        public DateTime? EffectiveDate { get; set; }

        [Required, MaxLength(200)]
        public string MimeType { get; set; } = default!;

        [MaxLength(64)]
        public string? FileHash { get; set; }

        // Document metadata
        [MaxLength(200)]
        public string? DocumentNumber { get; set; }

        public DateOnly? IssueDate { get; set; }
        public DateOnly? ExpiryDate { get; set; }

        [MaxLength(300)]
        public string? IssuingAuthority { get; set; }

        // Workflow status
        [Required, MaxLength(50)]
        public string Status { get; set; } = DocumentStatus.Pending;

        // Upload
        [Required, MaxLength(450)]
        public string UploadedByUserId { get; set; } = default!;

        [Required, MaxLength(50)]
        public string UploadedByRole { get; set; } = default!;

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        // Verification
        [MaxLength(450)]
        public string? VerifiedByUserId { get; set; }

        [MaxLength(50)]
        public string? VerifiedByRole { get; set; }

        public DateTime? VerifiedAt { get; set; }

        // Rejection
        [MaxLength(1000)]
        public string? RejectionRemarks { get; set; }

        [MaxLength(450)]
        public string? RejectedByUserId { get; set; }

        public DateTime? RejectedAt { get; set; }

        // Versioning
        public Guid? PreviousDocumentId { get; set; }
        public int Version { get; set; } = 1;

        // Soft delete
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }

        [MaxLength(450)]
        public string? DeletedByUserId { get; set; }

        // Navigation
        [ForeignKey("OrganizationId")]
        public Organization? Organization { get; set; }

        [ForeignKey("EmployeeId")]
        public Employee? Employee { get; set; }
        //public bool IsCurrentVersion { get; set; } = true;

        [ForeignKey("DocumentTypeId")]
        public DocumentType? DocumentType { get; set; }

        [ForeignKey("PreviousDocumentId")]
        public EmployeeDocument? PreviousDocument { get; set; }

        // Add to EmployeeDocument.cs (around line 60)
        public bool IsOrganizationGenerated { get; set; } = false;

        public bool IsLatestVersion { get; set; } = true;
        public ICollection<DocumentHistory> History { get; set; } = new List<DocumentHistory>();

        public ICollection<DocumentExpiryAlert> ExpiryAlerts { get; set; } = new List<DocumentExpiryAlert>();
    }

    public static class DocumentStatus
    {
        public const string Pending = "Pending";
        public const string UnderReview = "UnderReview";
        public const string Verified = "Verified";
        public const string Rejected = "Rejected";
        public const string Expired = "Expired";
        public const string Superseded = "Superseded";
    }
}