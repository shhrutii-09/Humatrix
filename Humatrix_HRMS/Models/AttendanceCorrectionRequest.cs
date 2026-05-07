using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    /// <summary>
    /// Employee attendance correction request.
    /// All timestamps stored in UTC.
    /// Business date stored separately as DateOnly.
    /// </summary>
    public class AttendanceCorrectionRequest
    {
        [Key]
        public Guid CorrectionRequestId { get; set; } = Guid.NewGuid();

        public Guid EmployeeId { get; set; }

        [ForeignKey(nameof(EmployeeId))]
        public Employee Employee { get; set; } = null!;

        public Guid? AttendanceId { get; set; }

        [ForeignKey(nameof(AttendanceId))]
        public Attendance? Attendance { get; set; }

        // Business date ONLY
        public DateOnly Date { get; set; }

        // Stored in UTC
        public DateTime? RequestedCheckIn { get; set; }

        // Stored in UTC
        public DateTime? RequestedCheckOut { get; set; }

        public string Reason { get; set; } = string.Empty;

        // Pending | Approved | Rejected | Cancelled
        public string Status { get; set; } = "Pending";

        public Guid? ReviewedBy { get; set; }

        // Stored in UTC
        public DateTime? ReviewedAt { get; set; }

        public string? RejectionReason { get; set; }

        // Stored in UTC
        public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
    }
}