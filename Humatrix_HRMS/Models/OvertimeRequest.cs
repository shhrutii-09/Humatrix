using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    public class OvertimeRequest
    {
        public Guid OvertimeRequestId { get; set; } = Guid.NewGuid();

        public Guid EmployeeId { get; set; }

        [ForeignKey(nameof(EmployeeId))]
        public Employee Employee { get; set; } = null!;

        public Guid AttendanceId { get; set; }

        [ForeignKey(nameof(AttendanceId))]
        public Attendance Attendance { get; set; } = null!;

        public DateTime Date { get; set; }

        public double RequestedHours { get; set; }

        public DateTime? ActualCheckOut { get; set; }

        public string Reason { get; set; } = string.Empty;

        // Pending | Approved | Rejected | Cancelled
        public string Status { get; set; } = "Pending";

        // HR/Admin remarks
        public string? RejectionReason { get; set; }

        public string? HRRemarks { get; set; }

        public Guid? ReviewedBy { get; set; }

        public DateTime? ReviewedAt { get; set; }

        public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
    }
}