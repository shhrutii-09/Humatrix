using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    public class CorrectionAuditLog
    {
        [Key]
        public Guid CorrectionAuditLogId { get; set; } = Guid.NewGuid();
        public Guid OrganizationId { get; set; }
        public Guid AttendanceCorrectionRequestId { get; set; }

        [ForeignKey(nameof(AttendanceCorrectionRequestId))]
        public AttendanceCorrectionRequest AttendanceCorrectionRequest { get; set; } = null!;

        public Guid? ActorEmployeeId { get; set; }

        [ForeignKey(nameof(ActorEmployeeId))]
        public Employee? ActorEmployee { get; set; }

        public const string AutoApplied = "AutoApplied";

        [MaxLength(200)]
        public string? ActorName { get; set; }

        [MaxLength(50)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? Notes { get; set; }

        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

        // Before values
        public DateTime? PreviousCheckIn { get; set; }
        public DateTime? PreviousCheckOut { get; set; }

        // After values
        public DateTime? NewCheckIn { get; set; }
        public DateTime? NewCheckOut { get; set; }

        [MaxLength(50)]
        public string? PreviousStatus { get; set; }

        [MaxLength(50)]
        public string? NewStatus { get; set; }
    }
}