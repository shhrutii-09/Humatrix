using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    public class OvertimeRequest
    {
        public Guid OvertimeRequestId { get; set; } = Guid.NewGuid();

        public Guid EmployeeId { get; set; }
        [ForeignKey("EmployeeId")]
        public Employee Employee { get; set; } = null!;

        public Guid AttendanceId { get; set; }
        [ForeignKey("AttendanceId")]
        public Attendance Attendance { get; set; } = null!;

        public DateTime Date { get; set; }

        public double RequestedHours { get; set; }
        public DateTime? ActualCheckOut { get; set; }

        public string Reason { get; set; } = string.Empty;

        // Pending | Approved | Rejected
        public string Status { get; set; } = "Pending";

        public Guid? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }

        public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
    }
}