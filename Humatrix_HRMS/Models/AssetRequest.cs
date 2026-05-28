// Models/AssetRequest.cs
using Humatrix_HRMS.Infrastructure.Constants;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    /// <summary>
    /// Represents any employee- or HR-raised request touching an asset.
    /// Approval workflow is tracked through the shared ApprovalRequest / ApprovalHistory
    /// tables via the ApprovalRequestId foreign key.
    /// DO NOT duplicate approval logic here — always delegate to ApprovalWorkflowService.
    /// </summary>
    public class AssetRequest
    {
        public Guid AssetRequestId { get; set; } = Guid.NewGuid();

        public Guid OrganizationId { get; set; }

        /// <summary>
        /// Identifies the type of request — see AssetRequestTypes constants.
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string RequestType { get; set; } = default!;

        /// <summary>
        /// The asset this request refers to.
        /// May be null for ProcurementRequest (no existing asset yet).
        /// </summary>
        public Guid? AssetId { get; set; }

        /// <summary>
        /// The employee raising the request (can be an Employee-role or HR-role user).
        /// </summary>
        public Guid EmployeeId { get; set; }

        [Required]
        [MaxLength(2000)]
        public string Reason { get; set; } = default!;

        [MaxLength(50)]
        public string Status { get; set; } = AssetRequestStatuses.Pending;
        public string? RejectionReason { get; set; }

        public string? ProcessedByUserId { get; set; }

        [Timestamp]
        public byte[]? RowVersion { get; set; }

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
        public string RequestCategory { get; set; } = null!;

        public DateTime? ReviewedAt { get; set; }

        public Guid? ReviewedByEmployeeId { get; set; }

        [MaxLength(2000)]
        public string? ReviewComments { get; set; }

        // ── Approval integration ──────────────────────────────────────────────
        // Links to the shared ApprovalRequest created by ApprovalWorkflowService.
        // Do NOT store approval status here; read it from ApprovalRequests table.
        public Guid? ApprovalRequestId { get; set; }

        // ── Procurement-specific fields ───────────────────────────────────────
        [MaxLength(100)]
        public string? RequestedCategory { get; set; }

        [MaxLength(1000)]
        public string? RequestedSpecs { get; set; }

        // ── Navigation ────────────────────────────────────────────────────────

        [ForeignKey(nameof(AssetId))]
        public Asset? Asset { get; set; }

        [ForeignKey(nameof(EmployeeId))]
        public Employee? Employee { get; set; }

        [Range(1, 1000)]
        public int Quantity { get; set; } = 1;
        public DateTime? ProcessedAt { get; set; }
        [Required]
        public string Category { get; set; } = string.Empty;

        [ForeignKey(nameof(ReviewedByEmployeeId))]
        public Employee? ReviewedByEmployee { get; set; }

        [ForeignKey(nameof(ApprovalRequestId))]
        public ApprovalRequest? ApprovalRequest { get; set; }
        public Guid? RequestedByEmployeeId { get;  set; }
        [ForeignKey(nameof(RequestedByEmployeeId))]
        public Employee? RequestedByEmployee { get; set; }
        public DateTime CreatedAt { get;  set; }
    }
}