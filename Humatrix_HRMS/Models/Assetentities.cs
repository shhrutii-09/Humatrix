using Humatrix_HRMS.Infrastructure.Constants;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    // =========================================================================
    // Asset — the core inventory item
    // =========================================================================

    /// <summary>
    /// Represents a single physical asset in the organisation's inventory.
    /// Status is managed exclusively through AssetService — never set directly.
    /// </summary>
    public class Asset
    {
        public Guid AssetId { get; set; } = Guid.NewGuid();

        [Required]
        public Guid OrganizationId { get; set; }

        // ── Identity ──────────────────────────────────────────────────────────
        [Required, MaxLength(100)]
        public string Name { get; set; } = default!;

        /// <summary>e.g. "Laptop", "Monitor", "Mouse"</summary>
        [Required, MaxLength(100)]
        public string Category { get; set; } = default!;

        /// <summary>Human-readable unique code: e.g. AST-2024-001</summary>
        [Required, MaxLength(50)]
        public string AssetCode { get; set; } = default!;

        [MaxLength(100)]
        public string? Brand { get; set; }

        public Guid? DepartmentId { get; set; }


        [MaxLength(100)]
        public string? Model { get; set; }

        [MaxLength(100)]
        public string? SerialNumber { get; set; }

        // ── Status ────────────────────────────────────────────────────────────
        /// <summary>
        /// Controlled field. Only AssetService may change this.
        /// Valid values defined in AssetStatus constants.
        /// </summary>
        [Required, MaxLength(20)]
        public string Status { get; set; } = AssetStatus.Available;

        // ── Current holder (denormalised for fast queries) ────────────────────
        /// <summary>
        /// Null when Available/InRepair/Retired. Set to the EmployeeId when
        /// Assigned. Cleared on Return. Kept in sync by AssetService.
        /// </summary>
        public Guid? CurrentEmployeeId { get; set; }

        [ForeignKey("CurrentEmployeeId")]
        public Employee? CurrentEmployee { get; set; }

        // ── Purchase / lifecycle ──────────────────────────────────────────────
        [Column(TypeName = "decimal(18,2)")]
        public decimal? PurchasePrice { get; set; }

        public DateTime? PurchaseDate { get; set; }
        public DateTime? WarrantyExpiryDate { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        // ── Audit ─────────────────────────────────────────────────────────────
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public string CreatedByUserId { get; set; } = default!;

        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedByUserId { get; set; }

        // ── Navigation ────────────────────────────────────────────────────────
        [ForeignKey("OrganizationId")]
        public Organization? Organization { get; set; }

        [ForeignKey("DepartmentId")]
        public Department? Department { get; set; }
        public ICollection<AssetAssignment> Assignments { get; set; } = new List<AssetAssignment>();
        public ICollection<AssetRequest> Requests { get; set; } = new List<AssetRequest>();
    }

    // =========================================================================
    // AssetAssignment — one record per assignment period
    // =========================================================================

    /// <summary>
    /// Tracks every assignment lifecycle for an asset.
    /// ReturnedAt = null means the assignment is currently active.
    /// Only one active assignment (ReturnedAt == null) is permitted per asset
    /// — enforced by a filtered unique index in DbContext configuration.
    /// </summary>
    public class AssetAssignment
    {
        public Guid AssetAssignmentId { get; set; } = Guid.NewGuid();

        [Required]
        public Guid AssetId { get; set; }

        [Required]
        public Guid EmployeeId { get; set; }

        [Required]
        public Guid OrganizationId { get; set; }

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public string AssignedByUserId { get; set; } = default!;

        /// <summary>Null while assignment is active.</summary>
        public DateTime? ReturnedAt { get; set; }

        public string? ReturnedByUserId { get; set; }

        [MaxLength(500)]
        public string? AssignmentNotes { get; set; }

        [MaxLength(500)]
        public string? ReturnNotes { get; set; }

        // ── Navigation ────────────────────────────────────────────────────────
        [ForeignKey("AssetId")]
        public Asset Asset { get; set; } = default!;

        [ForeignKey("EmployeeId")]
        public Employee Employee { get; set; } = default!;
    }

    // =========================================================================
    // AssetRequest — Repair / Replacement / Return raised by Employee or HR
    // =========================================================================

    /// <summary>
    /// Raised by an Employee (against their own assigned asset) or by HR.
    /// HR requests are reviewed by OrgAdmin.
    /// Employee requests are reviewed by their department HR or OrgAdmin.
    /// </summary>
    public class AssetRequest
    {
        public Guid AssetRequestId { get; set; } = Guid.NewGuid();

        [Required]
        public Guid AssetId { get; set; }

        [Required]
        public Guid OrganizationId { get; set; }

        /// <summary>The EmployeeId of whoever raised this request.</summary>
        [Required]
        public Guid RequestedByEmployeeId { get; set; }

        /// <summary>"Employee" or "HR" — from AssetRequestorRole constants.</summary>
        [Required, MaxLength(20)]
        public string RequestorRole { get; set; } = default!;

        /// <summary>"Repair", "Replacement", or "Return" — from AssetRequestType constants.</summary>
        [Required, MaxLength(20)]
        public string RequestType { get; set; } = default!;

        /// <summary>From AssetRequestStatus constants.</summary>
        [Required, MaxLength(20)]
        public string Status { get; set; } = AssetRequestStatus.Pending;

        [Required, MaxLength(1000)]
        public string Reason { get; set; } = default!;

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        // ── Review ────────────────────────────────────────────────────────────
        public Guid? ReviewedByEmployeeId { get; set; }
        public DateTime? ReviewedAt { get; set; }

        [MaxLength(1000)]
        public string? ReviewNotes { get; set; }

        /// <summary>
        /// For Replacement: the new asset assigned after approval.
        /// Null for Repair and Return requests.
        /// </summary>
        public Guid? ReplacementAssetId { get; set; }

        // ── Navigation ────────────────────────────────────────────────────────
        [ForeignKey("AssetId")]
        public Asset Asset { get; set; } = default!;

        [ForeignKey("RequestedByEmployeeId")]
        public Employee RequestedByEmployee { get; set; } = default!;

        [ForeignKey("ReviewedByEmployeeId")]
        public Employee? ReviewedByEmployee { get; set; }

        [ForeignKey("ReplacementAssetId")]
        public Asset? ReplacementAsset { get; set; }
    }

    // =========================================================================
    // ProcurementRequest — HR requests bulk assets from OrgAdmin
    // =========================================================================

    /// <summary>
    /// HR raises this when their department needs new assets.
    /// OrgAdmin approves/rejects. On fulfilment, assets are created automatically.
    /// </summary>
    public class ProcurementRequest
    {
        public Guid ProcurementRequestId { get; set; } = Guid.NewGuid();

        [Required]
        public Guid OrganizationId { get; set; }

        [Required]
        public Guid DepartmentId { get; set; }

        /// <summary>The HR employee who raised this request.</summary>
        [Required]
        public Guid RequestedByEmployeeId { get; set; }

        /// <summary>e.g. "Laptop", "Monitor"</summary>
        [Required, MaxLength(100)]
        public string AssetCategory { get; set; } = default!;

        [MaxLength(100)]
        public string? AssetName { get; set; }

        [Required, Range(1, 10000)]
        public int QuantityRequested { get; set; }

        public int QuantityFulfilled { get; set; } = 0;

        [Required, MaxLength(1000)]
        public string Justification { get; set; } = default!;

        /// <summary>From ProcurementStatus constants.</summary>
        [Required, MaxLength(20)]
        public string Status { get; set; } = ProcurementStatus.Pending;

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        // ── Review ────────────────────────────────────────────────────────────
        public string? ReviewedByUserId { get; set; }
        public DateTime? ReviewedAt { get; set; }

        [MaxLength(1000)]
        public string? ReviewNotes { get; set; }

        public DateTime? FulfilledAt { get; set; }

        // ── Navigation ────────────────────────────────────────────────────────
        [ForeignKey("OrganizationId")]
        public Organization? Organization { get; set; }

        [ForeignKey("DepartmentId")]
        public Department? Department { get; set; }

        [ForeignKey("RequestedByEmployeeId")]
        public Employee? RequestedByEmployee { get; set; }
    }
}