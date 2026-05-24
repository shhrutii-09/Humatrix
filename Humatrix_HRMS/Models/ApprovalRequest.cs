// Models/ApprovalRequest.cs
using Humatrix_HRMS.Infrastructure.Constants;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    /// <summary>
    /// Generic approval workflow record.
    /// One row per request (leave, WFH, overtime, attendance correction, etc.).
    /// The business entity (e.g. LeaveRequest) continues to hold its own Status
    /// for query convenience — this entity drives the workflow audit trail.
    /// </summary>
    public class ApprovalRequest
    {
        [Key]
        public Guid ApprovalRequestId { get; set; } = Guid.NewGuid();

        // ── Tenant + Module ────────────────────────────────────────────────────
        public Guid OrganizationId { get; set; }

        /// <summary>See ApprovalRequestTypes constants.</summary>
        [Required, MaxLength(50)]
        public string RequestType { get; set; } = null!;

        /// <summary>PK of the business entity (LeaveRequestId, etc.).</summary>
        public Guid RequestId { get; set; }

        // ── Participants ───────────────────────────────────────────────────────
        /// <summary>EmployeeId of the person who submitted the request.</summary>
        public Guid RequestedByEmployeeId { get; set; }

        [ForeignKey(nameof(RequestedByEmployeeId))]
        public Employee RequestedByEmployee { get; set; } = null!;

        /// <summary>
        /// EmployeeId of the current approver.
        /// Updated on escalation / reassignment.
        /// Null = broadcast to role group.
        /// </summary>
        public Guid? CurrentApproverEmployeeId { get; set; }

        [ForeignKey(nameof(CurrentApproverEmployeeId))]
        public Employee? CurrentApprover { get; set; }

        /// <summary>EmployeeId of who ultimately actioned the request.</summary>
        public Guid? ActionedByEmployeeId { get; set; }

        // ── State ──────────────────────────────────────────────────────────────
        /// <summary>See ApprovalStatuses constants.</summary>
        [Required, MaxLength(20)]
        public string Status { get; set; } = ApprovalStatuses.Pending;

        /// <summary>
        /// Approval level routing: "HR" | "OrgAdmin" | "MultiLevel".
        /// Defaults to HR. OrgAdmin requests go straight to OrgAdmin.
        /// </summary>
        [MaxLength(20)]
        public string ApprovalLevel { get; set; } = "HR";

        /// <summary>
        /// Role of the applicant at submission time.
        /// Drives routing logic.
        /// </summary>
        [MaxLength(30)]
        public string ApplicantRole { get; set; } = "Employee";

        [MaxLength(20)]
        public string Priority { get; set; } = "Normal";

        // ── Timestamps ─────────────────────────────────────────────────────────
        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }

        // ── Comments ──────────────────────────────────────────────────────────
        [MaxLength(1000)]
        public string? RejectionReason { get; set; }

        [MaxLength(1000)]
        public string? ApproverComments { get; set; }

        // ── Navigation ─────────────────────────────────────────────────────────
        public ICollection<ApprovalHistory> History { get; set; } = new List<ApprovalHistory>();
    }
}