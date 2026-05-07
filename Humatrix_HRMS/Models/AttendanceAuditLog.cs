using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    public class AttendanceAuditLog
    {
        [Key]
        public Guid AuditId { get; set; } = Guid.NewGuid();

        public Guid OrganizationId { get; set; }

        public Guid AttendanceId { get; set; }

        [ForeignKey(nameof(AttendanceId))]
        public Attendance Attendance { get; set; } = null!;

        // User who performed action
        public string ChangedByUserId { get; set; } = string.Empty;

        // Employee / HR / OrgAdmin / System
        [MaxLength(50)]
        public string ChangedByRole { get; set; } = string.Empty;

        // CorrectionApproved / ManualEdit / AutoCheckout / etc.
        [MaxLength(100)]
        public string Action { get; set; } = string.Empty;

        // JSON snapshot before change
        public string? OldValues { get; set; }

        // JSON snapshot after change
        public string? NewValues { get; set; }

        [MaxLength(1000)]
        public string? Remarks { get; set; }

        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    }
}