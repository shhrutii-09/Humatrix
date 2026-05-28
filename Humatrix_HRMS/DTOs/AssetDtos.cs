// DTOs/Asset/AssetDtos.cs
using System.ComponentModel.DataAnnotations;

namespace Humatrix_HRMS.DTOs.Asset
{
    // ═════════════════════════════════════════════════════════════════════════
    // ASSET CRUD DTOs
    // ═════════════════════════════════════════════════════════════════════════

    public class EmployeeDropdownDto
    {
        public Guid EmployeeId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? EmployeeCode { get; set; }
        public string? DepartmentName { get; set; }
    }

    public class CreateAssetDto
    {
        [Required(ErrorMessage = "Asset name is required.")]
        [MaxLength(200)]
        public string AssetName { get; set; } = default!;

        [Required(ErrorMessage = "Category is required.")]
        public string Category { get; set; } = default!;

        [MaxLength(100)]
        public string? Brand { get; set; }

        [MaxLength(100)]
        public string? Model { get; set; }

        [MaxLength(100)]
        public string? SerialNumber { get; set; }

        public DateTime? PurchaseDate { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Purchase cost must be positive.")]
        public decimal? PurchaseCost { get; set; }

        public DateTime? WarrantyExpiry { get; set; }

        [MaxLength(50)]
        public string? Condition { get; set; }

        /// <summary>The department this asset belongs to. Null = org-wide pool.</summary>
        public Guid? DepartmentId { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }
    }

    public class UpdateAssetDto
    {
        public Guid AssetId { get; set; }

        [Required(ErrorMessage = "Asset name is required.")]
        [MaxLength(200)]
        public string AssetName { get; set; } = default!;

        [Required]
        public string Category { get; set; } = default!;

        [MaxLength(100)]
        public string? Brand { get; set; }

        [MaxLength(100)]
        public string? Model { get; set; }

        [MaxLength(100)]
        public string? SerialNumber { get; set; }

        public DateTime? PurchaseDate { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? PurchaseCost { get; set; }

        public DateTime? WarrantyExpiry { get; set; }

        [MaxLength(50)]
        public string? Condition { get; set; }

        public Guid? DepartmentId { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }

        /// <summary>Required for optimistic concurrency — must be sent back from the last read.</summary>
        public byte[]? RowVersion { get; set; }
    }

    public class AssetDto
    {
        public Guid AssetId { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid? DepartmentId { get; set; }
        public string? DepartmentName { get; set; }
        public string AssetCode { get; set; } = default!;
        public string AssetName { get; set; } = default!;
        public string Category { get; set; } = default!;
        public string? Brand { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public decimal? PurchaseCost { get; set; }
        public DateTime? WarrantyExpiry { get; set; }
        public string Status { get; set; } = default!;
        public string? Condition { get; set; }
        public string? Notes { get; set; }
        public Guid? CurrentEmployeeId { get; set; }
        public string? CurrentEmployeeName { get; set; }
        public string? CurrentEmployeeCode { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>Sent back on every write to enable optimistic concurrency.</summary>
        public byte[]? RowVersion { get; set; }

        public bool IsWarrantyExpiringSoon =>
            WarrantyExpiry.HasValue
            && WarrantyExpiry.Value > DateTime.UtcNow
            && WarrantyExpiry.Value <= DateTime.UtcNow.AddDays(30);

        public bool IsWarrantyExpired =>
            WarrantyExpiry.HasValue
            && WarrantyExpiry.Value < DateTime.UtcNow;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ASSIGNMENT DTOs
    // ═════════════════════════════════════════════════════════════════════════

    public class AssignAssetDto
    {
        [Required]
        public Guid AssetId { get; set; }

        [Required(ErrorMessage = "Please select an employee.")]
        public Guid EmployeeId { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }
    }

    public class ReturnAssetDto
    {
        [Required]
        public Guid AssetId { get; set; }

        [Required(ErrorMessage = "Return condition is required.")]
        [MaxLength(50)]
        public string ReturnCondition { get; set; } = default!;

        [MaxLength(2000)]
        public string? Notes { get; set; }
    }

    public class RetireDisposeAssetDto
    {
        [Required]
        public Guid AssetId { get; set; }

        /// <summary>"Retired" or "Disposed"</summary>
        [Required]
        public string NewStatus { get; set; } = default!;

        [Required(ErrorMessage = "Reason is required.")]
        [MaxLength(2000)]
        public string Reason { get; set; } = default!;
    }

    public class AssetAssignmentHistoryDto
    {
        public Guid HistoryId { get; set; }
        public Guid AssetId { get; set; }
        public string AssetCode { get; set; } = default!;
        public string AssetName { get; set; } = default!;
        public Guid EmployeeId { get; set; }
        public string EmployeeName { get; set; } = default!;
        public string EmployeeCode { get; set; } = default!;
        public string AssignedByName { get; set; } = default!;
        public DateTime AssignedAt { get; set; }
        public DateTime? ReturnedAt { get; set; }
        public string? ReturnCondition { get; set; }
        public string? AssignmentNotes { get; set; }
        public bool IsActive => ReturnedAt == null;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LIFECYCLE DTOs  (MarkInRepair, CompleteRepair, ReportLost, RecoverAsset)
    // ═════════════════════════════════════════════════════════════════════════

    public class MarkAssetRepairDto
    {
        [Required]
        public Guid AssetId { get; set; }

        [MaxLength(2000)]
        public string? RepairReason { get; set; }

        /// <summary>Required for optimistic concurrency.</summary>
        public byte[]? RowVersion { get; set; }
    }

    public class CompleteAssetRepairDto
    {
        [Required]
        public Guid AssetId { get; set; }

        [MaxLength(50)]
        public string? FinalCondition { get; set; }

        [MaxLength(2000)]
        public string? RepairNotes { get; set; }

        public byte[]? RowVersion { get; set; }
    }

    public class ReportAssetLostDto
    {
        [Required]
        public Guid AssetId { get; set; }

        [Required(ErrorMessage = "Please describe how the asset was lost.")]
        [MaxLength(2000)]
        public string LostDescription { get; set; } = default!;

        public byte[]? RowVersion { get; set; }
    }

    public class RecoverAssetDto
    {
        [Required]
        public Guid AssetId { get; set; }

        [MaxLength(2000)]
        public string? RecoveryNotes { get; set; }

        [MaxLength(50)]
        public string? Condition { get; set; }

        public byte[]? RowVersion { get; set; }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ASSET REQUEST DTOs
    // ═════════════════════════════════════════════════════════════════════════

    public class SubmitAssetRequestDto
    {
        [Required]
        public string RequestType { get; set; } = default!;

        /// <summary>"Operational" or "Procurement" — see AssetRequestCategories.</summary>
        [Required]
        public string RequestCategory { get; set; } = default!;

        /// <summary>Required for all types except procurement (no existing asset yet).</summary>
        public Guid? AssetId { get; set; }

        [Required(ErrorMessage = "Please provide a reason.")]
        [MaxLength(2000)]
        public string Reason { get; set; } = default!;

        // Procurement-specific
        [MaxLength(100)]
        public string? RequestedCategory { get; set; }

        [MaxLength(1000)]
        public string? RequestedSpecs { get; set; }

        [Range(1, 100)]
        public int Quantity { get; set; } = 1;
    }

    public class ReviewAssetRequestDto
    {
        [Required]
        public Guid AssetRequestId { get; set; }

        public Guid? ApprovalRequestId { get; set; }

        /// <summary>"Approved" or "Rejected"</summary>
        [Required]
        public string Decision { get; set; } = default!;

        [MaxLength(2000)]
        public string? Comments { get; set; }

        /// <summary>Required when Decision = "Rejected".</summary>
        [MaxLength(2000)]
        public string? RejectionReason { get; set; }
    }

    public class AssetRequestDto
    {
        public Guid AssetRequestId { get; set; }
        public Guid OrganizationId { get; set; }

        /// <summary>Repair / Return / Replacement / etc.</summary>
        public string RequestType { get; set; } = default!;

        /// <summary>Operational or Procurement.</summary>
        public string RequestCategory { get; set; } = default!;

        public Guid? AssetId { get; set; }
        public string? AssetCode { get; set; }
        public string? AssetName { get; set; }

        public Guid EmployeeId { get; set; }
        public string EmployeeName { get; set; } = default!;
        public string EmployeeCode { get; set; } = default!;
        public string? DepartmentName { get; set; }

        public string Reason { get; set; } = default!;
        public string Status { get; set; } = default!;

        public int Quantity { get; set; }

        public DateTime RequestedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }

        public string? RejectionReason { get; set; }

        public DateTime? ReviewedAt { get; set; }
        public string? ReviewedByName { get; set; }
        public string? ReviewComments { get; set; }

        // Procurement-specific
        public string? RequestedCategory { get; set; }
        public string? RequestedSpecs { get; set; }

        // From linked ApprovalRequest
        public Guid? ApprovalRequestId { get; set; }
        public string? ApprovalStatus { get; set; }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ANALYTICS DTOs
    // ═════════════════════════════════════════════════════════════════════════

    public class AssetAnalyticsDto
    {
        public int TotalAssets { get; set; }
        public int Available { get; set; }
        public int Assigned { get; set; }
        public int InRepair { get; set; }
        public int Lost { get; set; }
        public int Retired { get; set; }
        public int Disposed { get; set; }
        public decimal TotalPurchaseCost { get; set; }
        public int WarrantyExpiringSoon { get; set; }
        public int WarrantyExpired { get; set; }
        public int PendingRequests { get; set; }
        public List<AssetCategoryBreakdownDto> ByCategory { get; set; } = new();
        public List<AssetDepartmentBreakdownDto> ByDepartment { get; set; } = new();
        public List<AssetDto> RecentlyAssigned { get; set; } = new();
    }

    public class AssetCategoryBreakdownDto
    {
        public string Category { get; set; } = default!;
        public int Count { get; set; }
        public decimal TotalValue { get; set; }
    }

    public class AssetDepartmentBreakdownDto
    {
        public Guid? DepartmentId { get; set; }
        public string DepartmentName { get; set; } = "Unallocated";
        public int Count { get; set; }
        public int Assigned { get; set; }
        public int Available { get; set; }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // FILTER / PAGING
    // ═════════════════════════════════════════════════════════════════════════

    public class AssetFilterDto
    {
        public Guid OrganizationId { get; set; }
        public Guid? DepartmentId { get; set; }
        public string? Status { get; set; }
        public string? Category { get; set; }
        /// <summary>Fuzzy search across AssetCode, Name, Serial, Brand.</summary>
        public string? SearchTerm { get; set; }
        /// <summary>When true, includes Retired and Disposed assets.</summary>
        public bool IncludeInactive { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    public class AssetRequestFilterDto
    {
        public Guid OrganizationId { get; set; }
        public string? Status { get; set; }
        public string? RequestType { get; set; }
        public string? RequestCategory { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }
}