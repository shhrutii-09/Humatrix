// Models/ApprovalHistory.cs
using Humatrix_HRMS.Infrastructure.Constants;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    /// <summary>
    /// Immutable audit log for every state transition on an ApprovalRequest.
    /// Append-only. Never updated.
    /// </summary>
    public class ApprovalHistory
    {
        [Key]
        public Guid ApprovalHistoryId { get; set; } = Guid.NewGuid();

        public Guid ApprovalRequestId { get; set; }

        [ForeignKey(nameof(ApprovalRequestId))]
        public ApprovalRequest ApprovalRequest { get; set; } = null!;

        // ── Action ─────────────────────────────────────────────────────────────
        /// <summary>See ApprovalActions constants.</summary>
        [Required, MaxLength(30)]
        public string Action { get; set; } = null!;

        /// <summary>Status before this action.</summary>
        [MaxLength(20)]
        public string? FromStatus { get; set; }

        /// <summary>Status after this action.</summary>
        [MaxLength(20)]
        public string? ToStatus { get; set; }

        // ── Actor ──────────────────────────────────────────────────────────────
        public Guid PerformedByEmployeeId { get; set; }

        [ForeignKey(nameof(PerformedByEmployeeId))]
        public Employee PerformedByEmployee { get; set; } = null!;

        [MaxLength(30)]
        public string PerformedByRole { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Comments { get; set; }

        // ── Timestamp ─────────────────────────────────────────────────────────
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }
}