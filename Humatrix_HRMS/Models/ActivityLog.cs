using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    /// <summary>
    /// Immutable append-only audit trail record.
    /// One row per business event — never updated, only inserted and queried.
    ///
    /// DESIGN PRINCIPLES:
    ///   • Tenant-scoped: OrganizationId is always set; never query across orgs.
    ///   • Module-scoped: every record declares its functional area (Leave, WFH, etc.).
    ///   • Actor-scoped: who did it, what role they had at the time.
    ///   • Snapshot-based: OldValues / NewValues are JSON snapshots for forensics.
    ///   • Queryable: indexed for the three most common access patterns:
    ///       1. "What happened to entity X?" → (EntityType, EntityId)
    ///       2. "What did user Y do?" → (PerformedByUserId, OrganizationId)
    ///       3. "Module audit feed" → (OrganizationId, Module, OccurredAt)
    /// </summary>
    public class ActivityLog
    {
        // ── Identity ──────────────────────────────────────────────────────────
        [Key]
        public long ActivityLogId { get; set; } 
        //public Guid ActivityLogId { get; set; } = Guid.NewGuid();

        // ── Tenant ────────────────────────────────────────────────────────────
        /// <summary>Required. Isolates logs per organisation.</summary>
        public Guid OrganizationId { get; set; }

        // ── Module / domain ───────────────────────────────────────────────────
        /// <summary>
        /// Functional module. See <see cref="ActivityModules"/> constants.
        /// Examples: "Leave", "Attendance", "WFH", "Overtime", "Employee", "Payroll"
        /// </summary>
        [Required, MaxLength(100)]
        public string Module { get; set; } = string.Empty;

        // ── Subject entity ────────────────────────────────────────────────────
        /// <summary>
        /// CLR type name or logical entity name.
        /// Examples: "LeaveRequest", "Employee", "Shift", "Holiday"
        /// </summary>
        [Required, MaxLength(100)]
        public string EntityType { get; set; } = string.Empty;

        /// <summary>PK of the entity this event concerns.</summary>
        public Guid EntityId { get; set; }

        // ── Action ────────────────────────────────────────────────────────────
        /// <summary>
        /// Verb. See <see cref="ActivityActions"/> constants.
        /// Examples: "Approved", "Rejected", "Updated", "Deactivated"
        /// </summary>
        [Required, MaxLength(100)]
        public string Action { get; set; } = string.Empty;

        // ── Actor ─────────────────────────────────────────────────────────────
        /// <summary>AspNetUsers.Id of the person who performed the action.</summary>
        [Required, MaxLength(450)]
        public string PerformedByUserId { get; set; } = string.Empty;

        /// <summary>
        /// Role the actor was acting as at the time of the event.
        /// Snapshot — not a FK; role names can change.
        /// </summary>
        [MaxLength(100)]
        public string? PerformedByRole { get; set; }

        /// <summary>
        /// Display name captured at write time. Avoids joins in audit views.
        /// </summary>
        [MaxLength(200)]
        public string? PerformedByName { get; set; }

        // ── Payload ───────────────────────────────────────────────────────────
        /// <summary>
        /// Human-readable event summary. Max 500 chars.
        /// Example: "HR approved leave request for John Doe (3 days, Annual Leave)"
        /// </summary>
        [MaxLength(500)]
        public string Details { get; set; } = string.Empty;

        /// <summary>
        /// JSON snapshot of relevant fields BEFORE the change.
        /// Null for Insert events. Never null for Update/Delete events.
        /// </summary>
        public string? OldValues { get; set; }

        /// <summary>
        /// JSON snapshot of relevant fields AFTER the change.
        /// Null for Delete events.
        /// </summary>
        public string? NewValues { get; set; }

        // ── Optional context ─────────────────────────────────────────────────
        /// <summary>
        /// Department scope at the time of the event.
        /// Allows HR-level filtering in audit views.
        /// </summary>
        public Guid? DepartmentId { get; set; }

        /// <summary>IP address, if available from HttpContext.</summary>
        [MaxLength(50)]
        public string? IpAddress { get; set; }

        // ── Timestamp ─────────────────────────────────────────────────────────
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public string? AdditionalInfo { get; set; }
    }
}