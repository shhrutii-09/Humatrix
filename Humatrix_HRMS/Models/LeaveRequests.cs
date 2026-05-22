using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    public class LeaveRequest
    {
        public Guid LeaveRequestId { get; set; } = Guid.NewGuid();

        public Guid EmployeeId { get; set; }
        [ForeignKey("EmployeeId")]
        public Employee Employee { get; set; } = null!;

        public Guid LeaveTypeId { get; set; }
        [ForeignKey("LeaveTypeId")]
        public LeaveType LeaveType { get; set; } = null!;

        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        // If true, only half a day on FromDate is taken
        public bool IsHalfDay { get; set; } = false;

        // Calculated and stored on apply — (ToDate - FromDate + 1) excluding weekends/holidays
        public decimal TotalDays { get; set; }

        public string Reason { get; set; } = string.Empty;

        // "Pending" | "Approved" | "Rejected" | "Cancelled"
        public string Status { get; set; } = "Pending";
        // LeaveRequest.cs — ADD THIS FIELD after the Status field

        /// <summary>
        /// Role of the person who submitted this leave ("Employee", "HR", "OrgAdmin").
        /// Used for routing notifications to the correct approver.
        /// </summary>
        public string ApplicantRole { get; set; } = "Employee";
        public Guid? ApprovedBy { get; set; }
        public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReviewedAt { get; set; }
        public string? RejectionReason { get; set; }
    }
}