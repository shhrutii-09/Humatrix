using System.ComponentModel.DataAnnotations;

namespace Humatrix_HRMS.DTOs.Assets
{
    // =========================================================================
    // Asset DTOs
    // =========================================================================

    public class CreateAssetDto
    {
        [Required, MaxLength(100)] public string Name { get; set; } = default!;
        [Required, MaxLength(100)] public string Category { get; set; } = default!;
        [MaxLength(100)] public string? Brand { get; set; }
        [MaxLength(100)] public string? Model { get; set; }
        [MaxLength(100)] public string? SerialNumber { get; set; }
        [Range(0, double.MaxValue)] public decimal? PurchasePrice { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public DateTime? WarrantyExpiryDate { get; set; }
        public Guid? DepartmentId { get; set; }
        [MaxLength(500)] public string? Notes { get; set; }
    }

    public class UpdateAssetDto
    {
        [MaxLength(100)] public string? Name { get; set; }
        [MaxLength(100)] public string? Brand { get; set; }
        [MaxLength(100)] public string? Model { get; set; }
        [MaxLength(100)] public string? SerialNumber { get; set; }
        [Range(0, double.MaxValue)] public decimal? PurchasePrice { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public DateTime? WarrantyExpiryDate { get; set; }
        public Guid? DepartmentId { get; set; }

        [MaxLength(500)] public string? Notes { get; set; }
    }

    public class AssetDto
    {
        public Guid AssetId { get; set; }
        public Guid OrganizationId { get; set; }
        public string Name { get; set; } = default!;
        public string Category { get; set; } = default!;
        public string AssetCode { get; set; } = default!;
        public string? Brand { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public string Status { get; set; } = default!;
        public Guid? CurrentEmployeeId { get; set; }
        public string? CurrentEmployeeName { get; set; }
        public decimal? PurchasePrice { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public Guid? DepartmentId { get; set; }

        public DateTime? WarrantyExpiryDate { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AssetListDto
    {
        public Guid AssetId { get; set; }
        public string Name { get; set; } = default!;
        public string Category { get; set; } = default!;
        public string AssetCode { get; set; } = default!;
        public string Status { get; set; } = default!;
        public Guid? DepartmentId { get; set; }

        public string? CurrentEmployeeName { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // =========================================================================
    // Assignment DTOs
    // =========================================================================

    public class AssignAssetDto
    {
        [Required] public Guid AssetId { get; set; }
        [Required] public Guid EmployeeId { get; set; }
        [MaxLength(500)] public string? Notes { get; set; }
    }

    public class ReturnAssetDto
    {
        [Required] public Guid AssetId { get; set; }
        [MaxLength(500)] public string? Notes { get; set; }
    }

    public class AssignmentDto
    {
        public Guid AssetAssignmentId { get; set; }
        public Guid AssetId { get; set; }
        public string AssetName { get; set; } = default!;
        public string AssetCode { get; set; } = default!;
        public Guid EmployeeId { get; set; }
        public string EmployeeName { get; set; } = default!;
        public DateTime AssignedAt { get; set; }
        public DateTime? ReturnedAt { get; set; }
        public string? AssignmentNotes { get; set; }
        public string? ReturnNotes { get; set; }
        public bool IsActive => ReturnedAt == null;
    }

    // =========================================================================
    // Asset Request DTOs
    // =========================================================================

    public class CreateAssetRequestDto
    {
        [Required] public Guid AssetId { get; set; }

        /// <summary>"Repair", "Replacement", or "Return"</summary>
        [Required] public string RequestType { get; set; } = default!;

        [Required, MaxLength(1000)] public string Reason { get; set; } = default!;
    }

    public class ReviewAssetRequestDto
    {
        [Required] public Guid AssetRequestId { get; set; }

        /// <summary>true = Approved, false = Rejected</summary>
        [Required] public bool Approve { get; set; }

        [MaxLength(1000)] public string? Notes { get; set; }

        /// <summary>
        /// Required when Approve = true and RequestType = "Replacement".
        /// The AssetId of the replacement unit.
        /// </summary>
        public Guid? ReplacementAssetId { get; set; }
    }

    public class AssetRequestDto
    {
        public Guid AssetRequestId { get; set; }
        public Guid AssetId { get; set; }
        public string AssetName { get; set; } = default!;
        public string AssetCode { get; set; } = default!;
        public Guid RequestedByEmployeeId { get; set; }
        public string RequestedByEmployeeName { get; set; } = default!;
        public string RequestorRole { get; set; } = default!;
        public string RequestType { get; set; } = default!;
        public string Status { get; set; } = default!;
        public string Reason { get; set; } = default!;
        public DateTime RequestedAt { get; set; }
        public string? ReviewedByEmployeeName { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewNotes { get; set; }
    }

    // =========================================================================
    // Procurement DTOs
    // =========================================================================

    public class CreateProcurementRequestDto
    {
        [Required, MaxLength(100)] public string AssetCategory { get; set; } = default!;
        [MaxLength(100)] public string? AssetName { get; set; }
        [Required, Range(1, 10000)] public int QuantityRequested { get; set; }
        [Required, MaxLength(1000)] public string Justification { get; set; } = default!;
    }

    public class ReviewProcurementDto
    {
        [Required] public Guid ProcurementRequestId { get; set; }
        [Required] public bool Approve { get; set; }
        [MaxLength(1000)] public string? Notes { get; set; }
    }

    /// <summary>
    /// OrgAdmin submits this to mark a procurement as fulfilled and auto-create assets.
    /// </summary>
    public class FulfilProcurementDto
    {
        [Required] public Guid ProcurementRequestId { get; set; }

        /// <summary>
        /// Template for asset creation. The service will stamp each asset
        /// with an auto-generated AssetCode and sequential naming.
        /// </summary>
        [Required, MaxLength(100)] public string AssetName { get; set; } = default!;
        [MaxLength(100)] public string? Brand { get; set; }
        [MaxLength(100)] public string? Model { get; set; }
        [Range(0, double.MaxValue)] public decimal? PurchasePrice { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public DateTime? WarrantyExpiryDate { get; set; }

        /// <summary>How many units to actually create (≤ QuantityRequested).</summary>
        [Required, Range(1, 10000)] public int QuantityToCreate { get; set; }
    }

    public class ProcurementRequestDto
    {
        public Guid ProcurementRequestId { get; set; }
        public Guid? DepartmentId { get; set; }
        public string DepartmentName { get; set; } = default!;
        public string RequestedByEmployeeName { get; set; } = default!;
        public string AssetCategory { get; set; } = default!;
        public string? AssetName { get; set; }
        public int QuantityRequested { get; set; }
        public int QuantityFulfilled { get; set; }
        public string Justification { get; set; } = default!;
        public string Status { get; set; } = default!;
        public DateTime RequestedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewNotes { get; set; }
        public DateTime? FulfilledAt { get; set; }
    }

    // =========================================================================
    // Query / filter DTOs
    // =========================================================================

    public class AssetFilterDto
    {
        public string? Category { get; set; }
        public string? Status { get; set; }
        public Guid? EmployeeId { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public Guid? DepartmentId { get; set; }

    }

    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }
}