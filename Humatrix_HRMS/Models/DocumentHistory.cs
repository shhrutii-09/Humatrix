using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models.Documents
{
    public class DocumentHistory
    {
        public Guid HistoryId { get; set; } = Guid.NewGuid();

        public Guid DocumentId { get; set; }

        [Required]
        public Guid EmployeeId { get; set; }

        [Required]
        public Guid OrganizationId { get; set; }

        [Required, MaxLength(50)]
        public string Action { get; set; } = default!;

        [Required, MaxLength(450)]
        public string ActorUserId { get; set; } = default!;

        [Required, MaxLength(50)]
        public string ActorRole { get; set; } = default!;

        [MaxLength(50)]
        public string? OldStatus { get; set; }

        [MaxLength(50)]
        public string? NewStatus { get; set; }

        [MaxLength(1000)]
        public string? Remarks { get; set; }

        public string? SnapshotJson { get; set; }

        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

        // Navigation
        [ForeignKey("DocumentId")]
        public EmployeeDocument? Document { get; set; }

        [ForeignKey(nameof(EmployeeId))]
        public Employee? Employee { get; set; }
    }

    public static class DocumentAction
    {
        public const string Uploaded = "Uploaded";
        public const string Verified = "Verified";
        public const string Rejected = "Rejected";
        public const string Reuploaded = "Reuploaded";
        public const string Expired = "Expired";
        public const string Deleted = "Deleted";
        public const string ExpiryWarning = "ExpiryWarning";
    }
}