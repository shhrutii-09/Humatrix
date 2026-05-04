using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    /// <summary>
    /// When an employee forgets to check out (or has a wrong check-in),
    /// they raise a correction request. HR reviews and approves/rejects it.
    /// </summary>
    public class AttendanceCorrectionRequest
    {
        [Key]
        public Guid CorrectionRequestId { get; set; } = Guid.NewGuid();

        public Guid EmployeeId { get; set; }
        [ForeignKey("EmployeeId")]
        public Employee Employee { get; set; } = null!;

        // The attendance record being corrected (nullable — employee might not have
        // checked in at all, so there is no attendance row yet for that date)
        public Guid? AttendanceId { get; set; }
        [ForeignKey("AttendanceId")]
        public Attendance? Attendance { get; set; }

        public DateTime Date { get; set; }

        // What the employee CLAIMS the correct times were
        public DateTime? RequestedCheckIn { get; set; }
        public DateTime? RequestedCheckOut { get; set; }

        public string Reason { get; set; } = string.Empty;

        // "Pending" | "Approved" | "Rejected"
        public string Status { get; set; } = "Pending";

        public Guid? ReviewedBy { get; set; }   // HR user id
        public DateTime? ReviewedAt { get; set; }
        public string? RejectionReason { get; set; }

        public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
    }
}