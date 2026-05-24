// Models/ActivityLog.cs
using System.ComponentModel.DataAnnotations;

namespace Humatrix_HRMS.Models
{
    /// <summary>
    /// Immutable append-only audit log for all business actions.
    /// Never updated. Partitioned by OrganizationId for multi-tenant safety.
    /// </summary>
    public class ActivityLog
    {
        [Key]
        public long ActivityLogId { get; set; }   // long for high-volume write performance

        // ── Tenant ─────────────────────────────────────────────────────────────
        public Guid OrganizationId { get; set; }

        // ── Module ─────────────────────────────────────────────────────────────
        /// <summary>Module name: "Leave", "WFH", "Overtime", "Attendance", "Employee", etc.</summary>
        [MaxLength(50)]
        public string Module { get; set; } = string.Empty;

        /// <summary>Action: "Applied", "Approved", "Rejected", "Cancelled", "Updated", "Created", "Deleted".</summary>
        [MaxLength(50)]
        public string Action { get; set; } = string.Empty;

        // ── Target entity ──────────────────────────────────────────────────────
        [MaxLength(100)]
        public string EntityType { get; set; } = string.Empty;

        public Guid EntityId { get; set; }

        // ── Actor ──────────────────────────────────────────────────────────────
        /// <summary>AspNetUsers.Id of the person who performed the action.</summary>
        [MaxLength(450)]
        public string PerformedByUserId { get; set; } = string.Empty;

        [MaxLength(30)]
        public string PerformedByRole { get; set; } = string.Empty;

        // ── Change capture ────────────────────────────────────────────────────
        /// <summary>JSON serialized old values (null for Creates).</summary>
        public string? OldValues { get; set; }

        /// <summary>JSON serialized new values (null for Deletes).</summary>
        public string? NewValues { get; set; }

        // ── Context ────────────────────────────────────────────────────────────
        [MaxLength(45)]
        public string? IpAddress { get; set; }

        [MaxLength(500)]
        public string? AdditionalInfo { get; set; }

        // ── Timestamp ─────────────────────────────────────────────────────────
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }
}