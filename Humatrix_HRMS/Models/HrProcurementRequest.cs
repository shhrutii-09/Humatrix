// Models/HrProcurementRequest.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Humatrix_HRMS.Data;
using Humatrix_HRMS.Infrastructure.Constants;

namespace Humatrix_HRMS.Models
{
    /// <summary>
    /// HR-raised bulk/new asset procurement request.
    /// Distinct from AssetRequest (employee operational requests).
    /// OrgAdmin approves and fulfills by creating assets in inventory.
    /// </summary>
    public class HrProcurementRequest
    {
        [Key]
        public Guid ProcurementRequestId { get; set; } = Guid.NewGuid();

        [Required]
        public Guid OrganizationId { get; set; }

        /// <summary>Department this procurement is for.</summary>
        public Guid DepartmentId { get; set; }

        /// <summary>HR employee who raised the request.</summary>
        [Required]
        public Guid RequestedByEmployeeId { get; set; }

        /// <summary>BulkAssetRequest | NewAssetDemand</summary>
        [Required]
        [MaxLength(100)]
        public string RequestType { get; set; } = AssetRequestTypeCatalog.BulkAssetRequest;

        [Required]
        [MaxLength(100)]
        public string AssetCategory { get; set; } = default!;

        [Range(1, 1000)]
        public int QuantityRequested { get; set; } = 1;

        public int QuantityFulfilled { get; set; } = 0;

        [Required]
        [MaxLength(2000)]
        public string Reason { get; set; } = default!;

        [MaxLength(1000)]
        public string? Specifications { get; set; }

        [MaxLength(100)]
        public string? PreferredBrand { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? EstimatedBudget { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = HrProcurementStatuses.Pending;

        public string? RejectionReason { get; set; }

        [MaxLength(2000)]
        public string? AdminNotes { get; set; }

        public Guid? ReviewedByEmployeeId { get; set; }

        public DateTime? ReviewedAt { get; set; }

        public Guid? ApprovalRequestId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        [MaxLength(450)]
        public string? CreatedByUserId { get; set; }

        [Timestamp]
        public byte[]? RowVersion { get; set; }

        // ── Navigation ──────────────────────────────────────────────────────

        [ForeignKey(nameof(OrganizationId))]
        public Organization? Organization { get; set; }

        [ForeignKey(nameof(DepartmentId))]
        public Department? Department { get; set; }

        [ForeignKey(nameof(RequestedByEmployeeId))]
        public Employee? RequestedByEmployee { get; set; }

        [ForeignKey(nameof(ReviewedByEmployeeId))]
        public Employee? ReviewedByEmployee { get; set; }

        [ForeignKey(nameof(ApprovalRequestId))]
        public ApprovalRequest? ApprovalRequest { get; set; }

        [ForeignKey(nameof(CreatedByUserId))]
        public ApplicationUser? CreatedByUser { get; set; }

        public ICollection<HrProcurementFulfillment> Fulfillments { get; set; } = new List<HrProcurementFulfillment>();
    }
}
