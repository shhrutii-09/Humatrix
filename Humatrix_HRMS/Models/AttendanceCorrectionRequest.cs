using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    /// <summary>
    /// Workflow / audit entity representing an employee's request to correct
    /// their attendance record.
    ///
    /// CONTRACT:
    ///   • This entity is APPEND-ONLY in spirit: once a field is set it should
    ///     only change through the defined state machine (Submit → Review → Apply).
    ///   • All DateTime fields are UTC (stored as Kind=Utc).
    ///   • WorkDate is the org-local calendar date for which the correction applies.
    ///   • The Attendance table is the authoritative source; this is the workflow
    ///     record that drives approval before any attendance mutation happens.
    ///   • Original* fields are captured at submission time and are NEVER changed.
    ///   • Approved* fields are written only when status transitions to Approved.
    ///
    /// STATE MACHINE:
    ///   Pending → Approved  (HR approves; triggers Apply step)
    ///   Pending → Rejected  (HR rejects)
    ///   Pending → Cancelled (Employee cancels before review)
    /// </summary>
    public class AttendanceCorrectionRequest
    {
        // ── Identity ──────────────────────────────────────────────────────────────

        [Key]
        public Guid AttendanceCorrectionRequestId { get; set; } = Guid.NewGuid();

        public Guid OrganizationId { get; set; }
        [ForeignKey(nameof(OrganizationId))]
        public Organization Organization { get; set; } = null!;
        public Guid EmployeeId { get; set; }

        [ForeignKey(nameof(EmployeeId))]
        public Employee Employee { get; set; } = null!;

        // ── Linked attendance record (may be null for AbsentButWorked) ───────────

        /// <summary>
        /// FK to the existing Attendance row being corrected.
        /// Null when CorrectionType == AbsentButWorked and no row existed yet.
        /// </summary>
        public Guid? AttendanceId { get; set; }

        [ForeignKey(nameof(AttendanceId))]
        public Attendance? Attendance { get; set; }

        // ── Business date ─────────────────────────────────────────────────────────

        /// <summary>
        /// Org-local date for which the correction applies.
        /// Stored as a DATE-only value (time component is always 00:00:00).
        /// </summary>
        public DateTime WorkDate { get; set; }

        // ── Correction type ───────────────────────────────────────────────────────

        /// <summary>
        /// See <see cref="CorrectionTypes"/> for valid values.
        /// Drives validation rules and determines which fields are required.
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string CorrectionType { get; set; } = null!;

        // ── Snapshot of attendance state at submission time ───────────────────────
        // These are FROZEN at submission. HR sees them for context. Never mutated.

        /// <summary>UTC. Snapshot of Attendance.CheckIn at the time of submission.</summary>
        public DateTime? OriginalCheckIn { get; set; }

        /// <summary>UTC. Snapshot of Attendance.CheckOut at the time of submission.</summary>
        public DateTime? OriginalCheckOut { get; set; }

        /// <summary>Snapshot of Attendance.Status at the time of submission.</summary>
        [MaxLength(50)]
        public string? OriginalStatus { get; set; }

        /// <summary>Snapshot of Attendance.TotalHours at the time of submission.</summary>
        public double? OriginalTotalHours { get; set; }

        // ── What the employee is requesting ───────────────────────────────────────

        /// <summary>UTC. What the employee believes the correct check-in time is.</summary>
        public DateTime? RequestedCheckIn { get; set; }

        /// <summary>UTC. What the employee believes the correct check-out time is.</summary>
        public DateTime? RequestedCheckOut { get; set; }

        /// <summary>Optional override of the attendance status.</summary>
        [MaxLength(50)]
        public string? RequestedStatus { get; set; }

        // ── What HR actually approved (may differ from requested) ─────────────────

        /// <summary>
        /// UTC. The check-in time HR decided to apply.
        /// HR may modify RequestedCheckIn before approving.
        /// Populated only when Status == Approved.
        /// </summary>
        public DateTime? ApprovedCheckIn { get; set; }

        /// <summary>UTC. The check-out time HR decided to apply.</summary>
        public DateTime? ApprovedCheckOut { get; set; }

        /// <summary>
        /// Status HR decided to apply. May override RequestedStatus or be
        /// computed by AttendanceCalculationService after applying times.
        /// </summary>
        [MaxLength(50)]
        public string? ApprovedStatus { get; set; }

        // ── Reason and remarks ────────────────────────────────────────────────────

        [Required]
        [MaxLength(1000)]
        public string Reason { get; set; } = null!;

        [MaxLength(1000)]
        public string? HrNote { get; set; }

        [MaxLength(1000)]
        public string? RejectionReason { get; set; }

        // ── Workflow state ────────────────────────────────────────────────────────

        /// <summary>See <see cref="CorrectionStatuses"/> for valid values.</summary>
        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = CorrectionStatuses.Pending;

        /// <summary>
        /// True once the correction has been written to the Attendance table.
        /// Set to true by the Apply step inside the approval transaction.
        /// </summary>
        public bool IsApplied { get; set; } = false;

        /// <summary>UTC timestamp when the correction was applied to Attendance.</summary>
        public DateTime? AppliedAt { get; set; }

        // ── HR reviewer ───────────────────────────────────────────────────────────

        public Guid? ReviewedByEmployeeId { get; set; }

        [ForeignKey(nameof(ReviewedByEmployeeId))]
        public Employee? ReviewedByEmployee { get; set; }

        public DateTime? ReviewedAt { get; set; }

        // ── Initiator (HR-submitted corrections) ──────────────────────────────────

        /// <summary>
        /// When HrManualCorrection: the HR employee who submitted on behalf of the employee.
        /// Null for employee-initiated requests.
        /// </summary>
        public Guid? InitiatedByHrEmployeeId { get; set; }

        [ForeignKey(nameof(InitiatedByHrEmployeeId))]
        public Employee? InitiatedByHrEmployee { get; set; }

        // ── Optional attachment ───────────────────────────────────────────────────

        [MaxLength(500)]
        public string? AttachmentPath { get; set; }

        // ── Audit timestamps ──────────────────────────────────────────────────────

        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // ── Convenience ───────────────────────────────────────────────────────────

        [NotMapped]
        public bool IsEditable => Status == CorrectionStatuses.Pending && !IsApplied;

        [NotMapped]
        public bool IsHrInitiated => CorrectionType == CorrectionTypes.HrManualCorrection
                                     || InitiatedByHrEmployeeId.HasValue;
        //public ICollection<CorrectionAuditLog> AuditLogs { get; set; } = new();
        public ICollection<CorrectionAuditLog> AuditLogs { get; set; }
            = new List<CorrectionAuditLog>();
        //public ICollection<CorrectionAuditLog> AuditLogs { get; set; } = new List<CorrectionAuditLog>();
    }

    /// <summary>
    /// Immutable append-only audit log for every state transition on a
    /// correction request. One row per event, never updated.
    /// </summary>
   
}