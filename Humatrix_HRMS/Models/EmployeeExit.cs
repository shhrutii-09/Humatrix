// Models/EmployeeExit.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    public class EmployeeExit
    {
        [Key]
        public Guid ExitId { get; set; } = Guid.NewGuid();

        public Guid EmployeeId { get; set; }
        public Guid OrganizationId { get; set; }

        [Required]
        public DateTime ResignationDate { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime LastWorkingDay { get; set; }

        [Required]
        [MaxLength(500)]
        public string Reason { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Remarks { get; set; }

        // Status Flow: Pending → Approved → ClearanceInProgress → Completed
        // Or: Pending → Rejected
        public string Status { get; set; } = ExitStatus.Pending;

        // Approval Details
        public DateTime? ApprovedAt { get; set; }
        public Guid? ApprovedByEmployeeId { get; set; }
        public string? ApprovalRemarks { get; set; }

        // Exit Interview
        public bool ExitInterviewCompleted { get; set; }
        public DateTime? ExitInterviewDate { get; set; }
        public string? ExitInterviewFeedback { get; set; }
        public Guid? ExitInterviewerEmployeeId { get; set; }

        // Clearance Checklist
        public bool AssetsReturned { get; set; }
        public DateTime? AssetsReturnedDate { get; set; }

        public bool AccessRevoked { get; set; }
        public DateTime? AccessRevokedDate { get; set; }

        public bool KnowledgeTransferred { get; set; }
        public string? KnowledgeTransferDetails { get; set; }

        public bool NoDuesCleared { get; set; }
        public DateTime? NoDuesClearedDate { get; set; }
        public decimal? FullFinalAmount { get; set; }

        public bool ExperienceLetterIssued { get; set; }
        public Guid? ExperienceLetterDocumentId { get; set; }

        public bool RelievingLetterIssued { get; set; }
        public Guid? RelievingLetterDocumentId { get; set; }

        public string? ClearanceRemarks { get; set; }

        // Tracking
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public Guid? CompletedByEmployeeId { get; set; }

        public bool TasksCompleted { get; set; }
        public DateTime? TasksCompletedDate { get; set; }

        // Navigation Properties
        [ForeignKey("EmployeeId")]
        public virtual Employee Employee { get; set; } = null!;

        [ForeignKey("OrganizationId")]
        public virtual Organization Organization { get; set; } = null!;

        [ForeignKey("ApprovedByEmployeeId")]
        public virtual Employee? ApprovedBy { get; set; }

        [ForeignKey("ExitInterviewerEmployeeId")]
        public virtual Employee? ExitInterviewer { get; set; }

        [ForeignKey("CompletedByEmployeeId")]
        public virtual Employee? CompletedBy { get; set; }
    }

    public static class ExitStatus
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string ClearanceInProgress = "ClearanceInProgress";
        public const string Completed = "Completed";
    }
}