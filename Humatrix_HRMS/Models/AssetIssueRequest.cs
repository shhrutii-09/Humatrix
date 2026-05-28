namespace Humatrix_HRMS.Models
{
    public class AssetIssueRequest
    {
        public Guid AssetIssueRequestId { get; set; }

        public Guid OrganizationId { get; set; }

        public Guid AssetId { get; set; }

        public Guid RequestedByEmployeeId { get; set; }

        public string RequestType { get; set; }
        // Repair
        // Return
        // Replacement
        // Damage
        // Lost

        public string Description { get; set; }

        public string Status { get; set; }
        // Pending
        // InProgress
        // Approved
        // Rejected
        // Completed

        public Guid? AssignedToEmployeeId { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? ResolvedAt { get; set; }
    }
}
