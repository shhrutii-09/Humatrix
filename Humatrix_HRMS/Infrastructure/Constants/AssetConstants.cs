// Infrastructure/Constants/AssetConstants.cs  (UPDATED)
namespace Humatrix_HRMS.Infrastructure.Constants
{
    // ── Asset lifecycle statuses ───────────────────────────────────────────────
    public static class AssetStatuses
    {
        public const string Available = "Available";
        public const string Assigned = "Assigned";
        public const string Reserved = "Reserved";   // NEW: reserved for a replacement/approved request
        public const string InRepair = "InRepair";
        public const string Lost = "Lost";
        public const string Retired = "Retired";
        public const string Disposed = "Disposed";

        public static readonly string[] All =
            { Available, Assigned, Reserved, InRepair, Lost, Retired, Disposed };

        public static readonly string[] Inactive = { Retired, Disposed };
    }

    // ── Hardware / accessory categories ──────────────────────────────────────
    public static class AssetCategories
    {
        public const string Laptop = "Laptop";
        public const string Monitor = "Monitor";
        public const string Keyboard = "Keyboard";
        public const string Mouse = "Mouse";
        public const string Mobile = "Mobile";
        public const string SimCard = "SIM Card";
        public const string IdCard = "ID Card";
        public const string Headphones = "Headphones";
        public const string Other = "Other";

        public static readonly string[] All =
            { Laptop, Monitor, Keyboard, Mouse, Mobile, SimCard, IdCard, Headphones, Other };
    }

    // ── Physical condition labels ─────────────────────────────────────────────
    public static class AssetConditions
    {
        public const string New = "New";
        public const string Good = "Good";
        public const string Fair = "Fair";
        public const string Poor = "Poor";
        public const string Damaged = "Damaged";

        public static readonly string[] All = { New, Good, Fair, Poor, Damaged };
    }

    // ── Existing AssetRequest statuses (Operational / legacy Procurement) ──────
    public static class AssetRequestStatuses
    {
        public const string Pending = "Pending";
        public const string InReview = "InReview";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string PartiallyFulfilled = "PartiallyFulfilled";
        public const string Completed = "Completed";
        public const string Cancelled = "Cancelled";
    }

    // ── HR Procurement Request statuses ──────────────────────────────────────
    public static class HrProcurementStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string PartiallyFulfilled = "PartiallyFulfilled";
        public const string Fulfilled = "Fulfilled";
        public const string Cancelled = "Cancelled";
    }

    // ── Employee Asset Request statuses ──────────────────────────────────────
    public static class EmployeeAssetRequestStatuses
    {
        public const string Pending = "Pending";
        public const string UnderReview = "UnderReview";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string InProgress = "InProgress";
        public const string Completed = "Completed";
        public const string Cancelled = "Cancelled";
    }

    // ── Employee Request types ────────────────────────────────────────────────
    public static class EmployeeAssetRequestTypes
    {
        public const string ReturnRequest = "ReturnRequest";
        public const string ReplacementRequest = "ReplacementRequest";
        public const string RepairRequest = "RepairRequest";
        public const string AccessoryRequest = "AccessoryRequest";
        public const string NewAssetRequest = "NewAssetRequest";

        public static readonly HashSet<string> All = new()
        {
            ReturnRequest, ReplacementRequest, RepairRequest,
            AccessoryRequest, NewAssetRequest
        };

        /// <summary>Request types that require an existing asset.</summary>
        public static readonly HashSet<string> RequiresExistingAsset = new()
        {
            ReturnRequest, ReplacementRequest, RepairRequest
        };
    }

    // ── HR Procurement request types ─────────────────────────────────────────
    //public static class AssetRequestTypeCatalog
    //{
    //    // Employee operational
    //    public const string Repair = "Repair";
    //    public const string Return = "Return";
    //    public const string Replacement = "Replacement";
    //    public const string AccessoryRequest = "AccessoryRequest";
    //    public const string DamageReport = "DamageReport";

    //    // HR procurement
    //    public const string BulkAssetRequest = "BulkAssetRequest";
    //    public const string NewAssetDemand = "NewAssetDemand";
    //}

    public static class AssetRequestTypes
    {
        public static readonly HashSet<string> OperationalTypes = new()
        {
            AssetRequestTypeCatalog.Repair,
            AssetRequestTypeCatalog.Return,
            AssetRequestTypeCatalog.Replacement,
            AssetRequestTypeCatalog.AccessoryRequest,
            AssetRequestTypeCatalog.DamageReport
        };

        public static readonly HashSet<string> ProcurementTypes = new()
        {
            AssetRequestTypeCatalog.BulkAssetRequest,
            AssetRequestTypeCatalog.NewAssetDemand
        };
    }

    //public static class AssetRequestCategories
    //{
    //    public const string Operational = "Operational";
    //    public const string Procurement = "Procurement";
    //}

    //public static class AssetAssignmentStatuses
    //{
    //    public const string Active = "Active";
    //    public const string Returned = "Returned";
    //}

    // ── Notification-type constants ───────────────────────────────────────────
    public static class AssetNotificationTypes
    {
        public const string AssetAssigned = "asset.assigned";
        public const string AssetReturned = "asset.returned";
        public const string AssetRepairRequested = "asset.repair.requested";
        public const string AssetRepairApproved = "asset.repair.approved";
        public const string AssetRepairRejected = "asset.repair.rejected";
        public const string AssetRepairCompleted = "asset.repair.completed";
        public const string AssetLostReported = "asset.lost.reported";
        public const string AssetReplacementApproved = "asset.replacement.approved";
        public const string AssetReplacementRejected = "asset.replacement.rejected";
        public const string AssetReturnRequested = "asset.return.requested";
        public const string AssetReturnApproved = "asset.return.approved";
        public const string AssetDisposalRequested = "asset.disposal.requested";
        public const string AssetProcurementRequested = "asset.procurement.requested";
        public const string AssetProcurementApproved = "asset.procurement.approved";
        public const string AssetProcurementRejected = "asset.procurement.rejected";
        public const string AssetProcurementFulfilled = "asset.procurement.fulfilled";
        public const string AssetOverdue = "asset.overdue";
        public const string AssetWarrantyExpiring = "asset.warranty.expiring";
        public const string EmployeeRequestSubmitted = "asset.employee.request.submitted";
        public const string EmployeeRequestApproved = "asset.employee.request.approved";
        public const string EmployeeRequestRejected = "asset.employee.request.rejected";
        public const string EmployeeRequestCompleted = "asset.employee.request.completed";
    }
}
