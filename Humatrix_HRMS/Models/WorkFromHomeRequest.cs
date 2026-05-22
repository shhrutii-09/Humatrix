namespace Humatrix_HRMS.Models
{
    public class WorkFromHomeRequest
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid EmployeeId { get; set; }
        public Employee Employee { get; set; } = null!;

        public DateTime Date { get; set; }

        public string Reason { get; set; } = string.Empty;

        // Pending | Approved | Rejected | Cancelled
        public string Status { get; set; } = "Pending";
        //public string RequestedByRole { get; set; } = string.Empty;
        //public string ApprovalLevel { get; set; } = string.Empty;

        public string RequestedByRole { get; set; } = "Employee";

        public string ApprovalLevel { get; set; } = "HR";

        public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
        public Guid? ApprovedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }

        public string? RejectionReason { get; set; }
    }
}
