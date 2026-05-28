namespace Humatrix_HRMS.Infrastructure.Constants
{
    // ─────────────────────────────────────────────────────────────────────────
    // Asset Status
    // Valid transitions are enforced by AssetStatusMachine (see Services).
    // ─────────────────────────────────────────────────────────────────────────
    public static class AssetStatus
    {
        public const string Available = "Available";   // In inventory, ready to assign
        public const string Assigned = "Assigned";    // Currently held by an employee/HR
        public const string InRepair = "InRepair";    // Sent for repair (request approved)
        public const string Retired = "Retired";     // Permanently decommissioned
        public const string Lost = "Lost";        // Reported lost

        public static readonly IReadOnlySet<string> All = new HashSet<string>
        {
            Available, Assigned, InRepair, Retired, Lost
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Asset Request Types — raised by Employee or HR
    // ─────────────────────────────────────────────────────────────────────────
    public static class AssetRequestType
    {
        public const string Repair = "Repair";
        public const string Replacement = "Replacement";
        public const string Return = "Return";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Procurement Request Status — raised by HR, approved by OrgAdmin
    // ─────────────────────────────────────────────────────────────────────────
    public static class ProcurementStatus
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Fulfilled = "Fulfilled";  // OrgAdmin marks after assets are created
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Asset Request Status — for Repair / Replacement / Return requests
    // ─────────────────────────────────────────────────────────────────────────
    public static class AssetRequestStatus
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Completed = "Completed";
        public const string Cancelled = "Cancelled";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Who raised the asset request
    // ─────────────────────────────────────────────────────────────────────────
    public static class AssetRequestorRole
    {
        public const string Employee = "Employee";
        public const string HR = "HR";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Notification types for asset events
    // ─────────────────────────────────────────────────────────────────────────
    public static class AssetNotificationTypes
    {
        public const string AssetAssigned = "AssetAssigned";
        public const string AssetReturned = "AssetReturned";
        public const string AssetRequestRaised = "AssetRequestRaised";
        public const string AssetRequestApproved = "AssetRequestApproved";
        public const string AssetRequestRejected = "AssetRequestRejected";
        public const string ProcurementRaised = "ProcurementRaised";
        public const string ProcurementApproved = "ProcurementApproved";
        public const string ProcurementRejected = "ProcurementRejected";
        public const string ProcurementFulfilled = "ProcurementFulfilled";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Activity log module name
    // ─────────────────────────────────────────────────────────────────────────
    public static class AssetModuleName
    {
        public const string Asset = "Asset";
        public const string Procurement = "Procurement";
        public const string AssetRequest = "AssetRequest";
    }
}