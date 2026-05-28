// DTOs/Asset/AssetExtendedDtos.cs
// Contains DTOs for HrProcurementRequest, EmployeeAssetRequest, and analytics extensions.
using System.ComponentModel.DataAnnotations;

namespace Humatrix_HRMS.DTOs.Asset
{
    // ═════════════════════════════════════════════════════════════════════════
    // HR PROCUREMENT REQUEST DTOs
    // ═════════════════════════════════════════════════════════════════════════

    public class CreateHrProcurementRequestDto
    {
        [Required]
        public Guid DepartmentId { get; set; }

        [Required]
        [MaxLength(100)]
        public string RequestType { get; set; } = "BulkAssetRequest"; // BulkAssetRequest | NewAssetDemand

        [Required]
        [MaxLength(100)]
        public string AssetCategory { get; set; } = default!;

        [Required]
        [Range(1, 1000)]
        public int QuantityRequested { get; set; } = 1;

        [Required]
        [MaxLength(2000)]
        public string Reason { get; set; } = default!;

        [MaxLength(1000)]
        public string? Specifications { get; set; }

        [MaxLength(100)]
        public string? PreferredBrand { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? EstimatedBudget { get; set; }
    }

    public class ReviewHrProcurementRequestDto
    {
        [Required]
        public Guid ProcurementRequestId { get; set; }

        /// <summary>"Approved" or "Rejected"</summary>
        [Required]
        public string Decision { get; set; } = default!;

        [MaxLength(2000)]
        public string? AdminNotes { get; set; }

        [MaxLength(2000)]
        public string? RejectionReason { get; set; }

        public byte[]? RowVersion { get; set; }
    }

    public class FulfillHrProcurementRequestDto
    {
        [Required]
        public Guid ProcurementRequestId { get; set; }

        /// <summary>
        /// List of assets to create. Count must not exceed remaining quantity.
        /// </summary>
        [Required]
        [MinLength(1)]
        public List<CreateAssetDto> Assets { get; set; } = new();

        [MaxLength(2000)]
        public string? Notes { get; set; }

        public byte[]? RowVersion { get; set; }
    }

    public class HrProcurementRequestDto
    {
        public Guid ProcurementRequestId { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid DepartmentId { get; set; }
        public string DepartmentName { get; set; } = default!;
        public Guid RequestedByEmployeeId { get; set; }
        public string RequestedByEmployeeName { get; set; } = default!;
        public string RequestedByEmployeeCode { get; set; } = default!;
        public string RequestType { get; set; } = default!;
        public string AssetCategory { get; set; } = default!;
        public int QuantityRequested { get; set; }
        public int QuantityFulfilled { get; set; }
        public int QuantityRemaining => QuantityRequested - QuantityFulfilled;
        public string Reason { get; set; } = default!;
        public string? Specifications { get; set; }
        public string? PreferredBrand { get; set; }
        public decimal? EstimatedBudget { get; set; }
        public string Status { get; set; } = default!;
        public string? RejectionReason { get; set; }
        public string? AdminNotes { get; set; }
        public string? ReviewedByEmployeeName { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public byte[]? RowVersion { get; set; }
        public Guid? ApprovalRequestId { get; set; }
        public List<HrProcurementFulfillmentDto> Fulfillments { get; set; } = new();
    }

    public class HrProcurementFulfillmentDto
    {
        public Guid FulfillmentId { get; set; }
        public Guid ProcurementRequestId { get; set; }
        public string FulfilledByEmployeeName { get; set; } = default!;
        public int QuantityFulfilled { get; set; }
        public string? Notes { get; set; }
        public DateTime FulfilledAt { get; set; }
        public List<AssetDto> CreatedAssets { get; set; } = new();
    }

    public class HrProcurementFilterDto
    {
        public Guid OrganizationId { get; set; }
        public Guid? DepartmentId { get; set; }
        public string? Status { get; set; }
        public string? RequestType { get; set; }
        public string? AssetCategory { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // EMPLOYEE ASSET REQUEST DTOs
    // ═════════════════════════════════════════════════════════════════════════

    public class SubmitEmployeeAssetRequestDto
    {
        /// <summary>ReturnRequest | ReplacementRequest | RepairRequest | AccessoryRequest | NewAssetRequest</summary>
        [Required]
        [MaxLength(100)]
        public string RequestType { get; set; } = default!;

        /// <summary>Required for ReturnRequest, ReplacementRequest, RepairRequest.</summary>
        public Guid? AssetId { get; set; }

        [Required]
        [MaxLength(2000)]
        public string Reason { get; set; } = default!;

        [MaxLength(1000)]
        public string? AdditionalDetails { get; set; }

        /// <summary>For AccessoryRequest / NewAssetRequest: what category needed.</summary>
        [MaxLength(100)]
        public string? RequestedAssetCategory { get; set; }

        [MaxLength(1000)]
        public string? RequestedSpecs { get; set; }
    }

    public class ReviewEmployeeAssetRequestDto
    {
        [Required]
        public Guid EmployeeAssetRequestId { get; set; }

        /// <summary>"Approved" | "Rejected" | "UnderReview"</summary>
        [Required]
        public string Decision { get; set; } = default!;

        [MaxLength(2000)]
        public string? ReviewComments { get; set; }

        [MaxLength(2000)]
        public string? RejectionReason { get; set; }

        public byte[]? RowVersion { get; set; }
    }

    public class CompleteEmployeeAssetRequestDto
    {
        [Required]
        public Guid EmployeeAssetRequestId { get; set; }

        [MaxLength(2000)]
        public string? ResolutionNotes { get; set; }

        /// <summary>For ReplacementRequest: the new asset being assigned.</summary>
        public Guid? ReplacementAssetId { get; set; }

        public byte[]? RowVersion { get; set; }
    }

    public class EmployeeAssetRequestDto
    {
        public Guid EmployeeAssetRequestId { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid EmployeeId { get; set; }
        public string EmployeeName { get; set; } = default!;
        public string EmployeeCode { get; set; } = default!;
        public string? DepartmentName { get; set; }
        public string RequestType { get; set; } = default!;
        public Guid? AssetId { get; set; }
        public string? AssetCode { get; set; }
        public string? AssetName { get; set; }
        public string Reason { get; set; } = default!;
        public string? AdditionalDetails { get; set; }
        public string? RequestedAssetCategory { get; set; }
        public string? RequestedSpecs { get; set; }
        public string Status { get; set; } = default!;
        public string? RejectionReason { get; set; }
        public string? ReviewComments { get; set; }
        public string? ReviewedByEmployeeName { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ResolutionNotes { get; set; }
        public Guid? ReplacementAssetId { get; set; }
        public string? ReplacementAssetCode { get; set; }
        public string? ReplacementAssetName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public byte[]? RowVersion { get; set; }
        public Guid? ApprovalRequestId { get; set; }
    }

    public class EmployeeAssetRequestFilterDto
    {
        public Guid OrganizationId { get; set; }
        public Guid? DepartmentId { get; set; }
        public string? Status { get; set; }
        public string? RequestType { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // EXTENDED ANALYTICS DTO
    // ═════════════════════════════════════════════════════════════════════════

    public class AssetAnalyticsExtendedDto : AssetAnalyticsDto
    {
        public int Reserved { get; set; }
        public int PendingEmployeeRequests { get; set; }
        public int PendingProcurementRequests { get; set; }
        public int OpenRepairRequests { get; set; }
        public int OpenReturnRequests { get; set; }
        public int OpenReplacementRequests { get; set; }
        public List<HrProcurementSummaryDto> RecentProcurements { get; set; } = new();
    }

    public class HrProcurementSummaryDto
    {
        public Guid ProcurementRequestId { get; set; }
        public string DepartmentName { get; set; } = default!;
        public string AssetCategory { get; set; } = default!;
        public int QuantityRequested { get; set; }
        public int QuantityFulfilled { get; set; }
        public string Status { get; set; } = default!;
        public DateTime CreatedAt { get; set; }
    }
}
