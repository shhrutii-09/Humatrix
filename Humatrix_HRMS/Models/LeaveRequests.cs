using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{

    public class LeaveRequest
    {
        public Guid LeaveRequestId { get; set; }

        public Guid EmployeeId { get; set; }

        [ForeignKey("EmployeeId")]
        public Employee Employee { get; set; } = null!; // ✅ ADD THIS

        public Guid LeaveTypeId { get; set; }

        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        public string Reason { get; set; } = "";

        public string Status { get; set; } = "Pending";

        public Guid? ApprovedBy { get; set; }

        public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ReviewedAt { get; set; }
    }
}
