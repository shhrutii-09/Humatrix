//using Humatrix_HRMS.Infrastructure.Constants;
//using System.ComponentModel.DataAnnotations;
//using System.ComponentModel.DataAnnotations.Schema;

//namespace Humatrix_HRMS.Models
//{
//    /// <summary>
//    /// Central approval workflow record.
//    ///
//    /// PURPOSE:
//    ///   Decouples the "is this approved?" concern from every domain entity.
//    ///   LeaveRequest, WFH, Overtime, etc. no longer manage their own approval state
//    ///   beyond a denormalised Status field for fast UI queries.
//    ///
//    /// DESIGN:
//    ///   • One ApprovalRequest per domain request (1:1 via RequestType + RequestId).
//    ///   • RequestType + RequestId form a unique business key — enforced by DB index.
//    ///   • Approval state lives here; domain entities mirror it as a cached field.
//    ///   • CurrentApproverEmployeeId tracks the *current* pending approver.
//    ///     For multi-level: each level transitions this FK forward.
//    ///   • History is the append-only audit trail of every transition.
//    ///
//    /// STATE MACHINE:
//    ///   Pending → Approved
//    ///   Pending → Rejected
//    ///   Pending → Cancelled  (by requester)
//    ///   Pending → Escalated  (future: SLA timer breach)
//    ///
//    /// FUTURE:
//    ///   • CurrentLevel + TotalLevels support multi-level approval chains.
//    ///   • EscalatedAt + SlaDeadline support SLA timer enforcement.
//    ///   • DelegatedToEmployeeId supports approver delegation/out-of-office.
//    /// </summary>
//    public class ApprovalRequest
//    {
//        // ── Identity ──────────────────────────────────────────────────────────
//        [Key]
//        public Guid ApprovalRequestId { get; set; } = Guid.NewGuid();

//        // ── Tenant ────────────────────────────────────────────────────────────
//        public Guid OrganizationId { get; set; }

//        // ── Domain link ───────────────────────────────────────────────────────
//        /// <summary>
//        /// Which module/entity type this approval is for.
//        /// See <see cref="ApprovalRequestTypes"/>.
//        /// </summary>
//        [Required, MaxLength(100)]
//        public string RequestType { get; set; } = string.Empty;

//        /// <summary>
//        /// PK of the domain entity (LeaveRequestId, WorkFromHomeRequest.Id, etc.).
//        /// </summary>
//        public Guid RequestId { get; set; }

//        // ── Requester ─────────────────────────────────────────────────────────
//        public Guid RequestedByEmployeeId { get; set; }

//        [ForeignKey(nameof(RequestedByEmployeeId))]
//        public Employee RequestedByEmployee { get; set; } = null!;

//        /// <summary>
//        /// Role snapshot at time of submission.
//        /// Drives routing: "Employee" → HR; "HR" → OrgAdmin.
//        /// </summary>
//        [MaxLength(100)]
//        public string RequesterRole { get; set; } = "Employee";

//        // ── Workflow state ────────────────────────────────────────────────────
//        /// <summary>See <see cref="ApprovalStatuses"/>.</summary>
//        [Required, MaxLength(50)]
//        public string Status { get; set; } = ApprovalStatuses.Pending;

//        /// <summary>
//        /// FK to the Employee currently expected to act.
//        /// Null after final decision.
//        /// </summary>
//        public Guid? CurrentApproverEmployeeId { get; set; }

//        [ForeignKey(nameof(CurrentApproverEmployeeId))]
//        public Employee? CurrentApprover { get; set; }

//        // ── Multi-level support (future) ──────────────────────────────────────
//        /// <summary>Which level the workflow is currently at (1-based).</summary>
//        public int CurrentLevel { get; set; } = 1;

//        /// <summary>Total levels required. 1 = single-level (current default).</summary>
//        public int TotalLevels { get; set; } = 1;

//        // ── Priority ─────────────────────────────────────────────────────────
//        /// <summary>See <see cref="ApprovalPriorities"/>.</summary>
//        [MaxLength(20)]
//        public string Priority { get; set; } = ApprovalPriorities.Normal;

//        // ── SLA (future) ──────────────────────────────────────────────────────
//        /// <summary>When this request must be acted on by.</summary>
//        public DateTime? SlaDeadline { get; set; }

//        /// <summary>Set when the SLA timer fires and escalation occurs.</summary>
//        public DateTime? EscalatedAt { get; set; }

//        // ── Timestamps ────────────────────────────────────────────────────────
//        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
//        public DateTime? CompletedAt { get; set; }

//        // ── Comments ─────────────────────────────────────────────────────────
//        [MaxLength(1000)]
//        public string? FinalComments { get; set; }

//        // ── Navigation ───────────────────────────────────────────────────────
//        public ICollection<ApprovalHistory> History { get; set; }
//            = new List<ApprovalHistory>();
//    }

//    /// <summary>
//    /// Immutable, append-only record of every action taken on an ApprovalRequest.
//    /// Never update a row here — always insert a new one.
//    ///
//    /// One row per event: Submitted, Approved, Rejected, Cancelled, Escalated, etc.
//    /// </summary>
//    public class ApprovalHistory
//    {
//        [Key]
//        public Guid ApprovalHistoryId { get; set; } = Guid.NewGuid();

//        // ── FK to parent ──────────────────────────────────────────────────────
//        public Guid ApprovalRequestId { get; set; }

//        [ForeignKey(nameof(ApprovalRequestId))]
//        public ApprovalRequest ApprovalRequest { get; set; } = null!;

//        // ── What happened ─────────────────────────────────────────────────────
//        /// <summary>See <see cref="ApprovalActions"/>.</summary>
//        [Required, MaxLength(100)]
//        public string Action { get; set; } = string.Empty;

//        /// <summary>From state before this action.</summary>
//        [MaxLength(50)]
//        public string? FromStatus { get; set; }

//        /// <summary>To state after this action.</summary>
//        [MaxLength(50)]
//        public string? ToStatus { get; set; }

//        // ── Actor ─────────────────────────────────────────────────────────────
//        public Guid PerformedByEmployeeId { get; set; }

//        [ForeignKey(nameof(PerformedByEmployeeId))]
//        public Employee PerformedByEmployee { get; set; } = null!;

//        /// <summary>Role snapshot at time of action.</summary>
//        [MaxLength(100)]
//        public string? PerformedByRole { get; set; }

//        // ── Approval level context ────────────────────────────────────────────
//        public int Level { get; set; } = 1;

//        // ── Remarks ───────────────────────────────────────────────────────────
//        [MaxLength(1000)]
//        public string? Comments { get; set; }

//        // ── Timestamp ─────────────────────────────────────────────────────────
//        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
//    }
//}