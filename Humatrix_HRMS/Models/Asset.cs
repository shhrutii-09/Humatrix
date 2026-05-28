// Models/Asset.cs  (UPDATED — adds Reserved status support + FulfillmentId)
using Humatrix_HRMS.Data;
using Humatrix_HRMS.Infrastructure.Constants;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    public class Asset
    {
        public Guid AssetId { get; set; } = Guid.NewGuid();

        [Required]
        public Guid OrganizationId { get; set; }

        public Guid? DepartmentId { get; set; }

        [Required]
        [MaxLength(50)]
        public string AssetCode { get; set; } = default!;

        [Required]
        [MaxLength(200)]
        public string AssetName { get; set; } = default!;

        [Required]
        [MaxLength(100)]
        public string Category { get; set; } = AssetCategories.Other;

        [MaxLength(100)]
        public string? Brand { get; set; }

        [MaxLength(100)]
        public string? Model { get; set; }

        [MaxLength(100)]
        public string? SerialNumber { get; set; }

        public DateTime? PurchaseDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? PurchaseCost { get; set; }

        public DateTime? WarrantyExpiry { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = AssetStatuses.Available;

        [MaxLength(50)]
        public string? Condition { get; set; } = AssetConditions.Good;

        [MaxLength(2000)]
        public string? Notes { get; set; }

        public Guid? CurrentEmployeeId { get; set; }

        /// <summary>
        /// If this asset was created via a procurement fulfillment, records which one.
        /// </summary>
        public Guid? FulfillmentId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        [MaxLength(450)]
        public string? CreatedByUserId { get; set; }

        public DateTime? AssignedAt { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }

        [Timestamp]
        public byte[]? RowVersion { get; set; }

        // ── Navigation ──────────────────────────────────────────────────────

        [ForeignKey(nameof(OrganizationId))]
        public Organization? Organization { get; set; }

        [ForeignKey(nameof(DepartmentId))]
        public Department? Department { get; set; }

        [ForeignKey(nameof(CurrentEmployeeId))]
        public Employee? CurrentEmployee { get; set; }

        [ForeignKey(nameof(CreatedByUserId))]
        public ApplicationUser? CreatedByUser { get; set; }

        [ForeignKey(nameof(FulfillmentId))]
        public HrProcurementFulfillment? Fulfillment { get; set; }

        public ICollection<AssetAssignmentHistory> AssignmentHistory { get; set; } = new List<AssetAssignmentHistory>();
        public ICollection<AssetRequest> Requests { get; set; } = new List<AssetRequest>();
        public ICollection<EmployeeAssetRequest> EmployeeRequests { get; set; } = new List<EmployeeAssetRequest>();
    }
}
