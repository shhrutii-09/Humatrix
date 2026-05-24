// Infrastructure/Constants/WorkflowConstants.cs
namespace Humatrix_HRMS.Infrastructure.Constants
{
    public static class ApprovalRequestTypes
    {
        public const string Leave = "Leave";
        public const string WorkFromHome = "WorkFromHome";
        public const string Overtime = "Overtime";
        public const string AttendanceCorrection = "AttendanceCorrection";
        // Future
        public const string AssetRequest = "AssetRequest";
        public const string HelpDeskTicket = "HelpDeskTicket";
        public const string PayrollAdjustment = "PayrollAdjustment";
    }

    public static class ApprovalStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Cancelled = "Cancelled";
        public const string Escalated = "Escalated"; // future
    }

    public static class ApprovalActions
    {
        public const string Submitted = "Submitted";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Cancelled = "Cancelled";
        public const string Escalated = "Escalated";
        public const string Reassigned = "Reassigned";
        public const string Commented = "Commented";
    }
}