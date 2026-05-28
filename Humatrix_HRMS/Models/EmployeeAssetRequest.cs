// Models/EmployeeAssetRequest.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Humatrix_HRMS.Data;
using Humatrix_HRMS.Infrastructure.Constants;

namespace Humatrix_HRMS.Models
{
    /// <summary>
    /// Employee-raised operational asset requests.
    /// Types: ReturnRequest, ReplacementRequest, RepairRequest, AccessoryRequest, NewAssetRequest.
    /// HR reviews and approves; asset status stays in sync.
    /// Completely separate from HrProcurementRequest.
    /// </summary>
    public class EmployeeAssetRequest
    {
        [Key]
        public Guid EmployeeAssetRequestId { get; set; } = Guid.NewGuid();

        [Required]
        public Guid OrganizationId { get; set; }

        [Required]
        public Guid EmployeeId { get; set; }

        /// <summary>
        /// ReturnRequest | ReplacementRequest | RepairRequest | AccessoryRequest | NewAssetRequest
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string RequestType { get; set; } = default!;

        /// <summary>The asset this request relates to. Null for NewAssetRequest / AccessoryRequest.</summary>
        public Guid? AssetId { get; set; }

        [Required]
        [MaxLength(2000)]
        public string Reason { get; set; } = default!;

        [MaxLength(1000)]
        public string? AdditionalDetails { get; set; }

        /// <summary>For AccessoryRequest / NewAssetRequest: what category is needed.</summary>
        [MaxLength(100)]
        public string? RequestedAssetCategory { get; set; }

        [MaxLength(1000)]
        public string? RequestedSpecs { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = EmployeeAssetRequestStatuses.Pending;

        public string? RejectionReason { get; set; }

        [MaxLength(2000)]
        public string? ReviewComments { get; set; }

        public Guid? ReviewedByEmployeeId { get; set; }

        public DateTime? ReviewedAt { get; set; }

        public Guid? ProcessedByEmployeeId { get; set; }

        public DateTime? ProcessedAt { get; set; }

        [MaxLength(2000)]
        public string? ResolutionNotes { get; set; }

        /// <summary>For ReplacementRequest: the new asset assigned to the employee after approval.</summary>
        public Guid? ReplacementAssetId { get; set; }

        public Guid? ApprovalRequestId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        [Timestamp]
        public byte[]? RowVersion { get; set; }

        // ── Navigation ──────────────────────────────────────────────────────

        [ForeignKey(nameof(OrganizationId))]
        public Organization? Organization { get; set; }

        [ForeignKey(nameof(EmployeeId))]
        public Employee? Employee { get; set; }

        [ForeignKey(nameof(AssetId))]
        public Asset? Asset { get; set; }

        [ForeignKey(nameof(ReplacementAssetId))]
        public Asset? ReplacementAsset { get; set; }

        [ForeignKey(nameof(ReviewedByEmployeeId))]
        public Employee? ReviewedByEmployee { get; set; }

        [ForeignKey(nameof(ProcessedByEmployeeId))]
        public Employee? ProcessedByEmployee { get; set; }

        [ForeignKey(nameof(ApprovalRequestId))]
        public ApprovalRequest? ApprovalRequest { get; set; }
    }
}
