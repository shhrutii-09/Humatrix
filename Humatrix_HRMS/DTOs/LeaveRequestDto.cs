namespace Humatrix_HRMS.DTOs
{
    public class LeaveRequestDto
    {
        public Guid LeaveRequestId { get; set; }

        public string EmployeeName { get; set; } = string.Empty;
        public string? Department { get; set; }

        public string RequestedByRole { get; set; } = "Employee";

        public string LeaveTypeName { get; set; } = string.Empty;

        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        public bool IsHalfDay { get; set; }
        public decimal TotalDays { get; set; }

        public string Reason { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public DateTime AppliedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public Guid? ApprovedBy { get; set; }
        public string? RejectionReason { get; set; }

        public string? ReviewerName { get; set; }
    }
}