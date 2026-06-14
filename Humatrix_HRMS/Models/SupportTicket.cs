// Models/SupportTicket.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    public class SupportTicket
    {
        [Key]
        public Guid TicketId { get; set; } = Guid.NewGuid();

        /// <summary>Auto-incrementing human-readable ticket number (e.g. TKT-0042).</summary>
        public int TicketNumber { get; set; }

        // ── Ownership ─────────────────────────────────────────────────────────
        public Guid OrganizationId { get; set; }

        /// <summary>ApplicationUser.Id of the employee who raised the ticket.</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>Employee record of the raiser (nullable for users without an employee profile).</summary>
        public Guid? EmployeeId { get; set; }

        [ForeignKey(nameof(EmployeeId))]
        public Employee? Employee { get; set; }

        // ── Assignment ────────────────────────────────────────────────────────
        /// <summary>
        /// ApplicationUser.Id of the HR/OrgAdmin who is handling this ticket.
        /// Null = unassigned.
        /// </summary>
        public string? AssignedToUserId { get; set; }

        /// <summary>Employee record of the assignee (for display).</summary>
        public Guid? AssignedToEmployeeId { get; set; }

        [ForeignKey(nameof(AssignedToEmployeeId))]
        public Employee? AssignedTo { get; set; }

        // ── Content ───────────────────────────────────────────────────────────
        [Required, MaxLength(100)]
        public string Category { get; set; } = string.Empty;

        [Required, MaxLength(4000)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Priority { get; set; } = "Medium"; // Low | Medium | High | Urgent

        [MaxLength(50)]
        public string Status { get; set; } = "Open"; // Open | InProgress | Resolved | Closed

        /// <summary>Source that created the ticket (Manual | AI).</summary>
        [MaxLength(20)]
        public string Source { get; set; } = "Manual";

        // ── Resolution ────────────────────────────────────────────────────────
        [MaxLength(4000)]
        public string? Resolution { get; set; }

        public DateTime? ResolvedAt { get; set; }

        /// <summary>Internal note visible only to HR/OrgAdmin — not shown to the employee.</summary>
        [MaxLength(4000)]
        public string? InternalNote { get; set; }

        // ── Timestamps ────────────────────────────────────────────────────────
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // ── Navigation ────────────────────────────────────────────────────────
        public ICollection<TicketReply> Replies { get; set; } = new List<TicketReply>();
    }

    /// <summary>Well-known ticket categories — kept as constants so both service and UI agree.</summary>
    public static class TicketCategories
    {
        public const string LeaveIssue = "Leave Issue";
        public const string AttendanceIssue = "Attendance Issue";
        public const string Payroll = "Payroll";
        public const string AssetRequest = "Asset Request";
        public const string ITSupport = "IT Support";
        public const string PolicyQuery = "Policy Query";
        public const string Other = "Other";

        public static readonly string[] All =
        {
            LeaveIssue, AttendanceIssue, Payroll,
            AssetRequest, ITSupport, PolicyQuery, Other
        };
    }
}