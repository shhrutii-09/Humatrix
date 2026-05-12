using System.ComponentModel.DataAnnotations;

namespace Humatrix_HRMS.DTOs
{
    // ═══════════════════════════════════════════════════════════════════════════
    // EMPLOYEE → SYSTEM  :  SUBMIT
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sent from the employee's correction form.
    /// RequestedCheckIn / RequestedCheckOut MUST be org-local DateTime values
    /// with Kind = Unspecified. The service converts to UTC.
    /// </summary>
    public class SubmitCorrectionRequestDto
    {
        [Required]
        public DateTime WorkDate { get; set; }

        [Required]
        [MaxLength(50)]
        public string CorrectionType { get; set; } = null!;

        /// <summary>Org-local (Kind=Unspecified). Service converts to UTC.</summary>
        public DateTime? RequestedCheckIn { get; set; }

        /// <summary>Org-local (Kind=Unspecified). Service converts to UTC.</summary>
        public DateTime? RequestedCheckOut { get; set; }

        [MaxLength(50)]
        public string? RequestedStatus { get; set; }

        [Required]
        [MinLength(10, ErrorMessage = "Please provide a detailed reason (at least 10 characters).")]
        [MaxLength(1000)]
        public string Reason { get; set; } = null!;

        [MaxLength(500)]
        public string? AttachmentPath { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HR → SYSTEM  :  REVIEW (APPROVE / REJECT)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sent when HR approves or rejects a correction request.
    /// HrOverrideCheckIn / HrOverrideCheckOut are org-local (Kind=Unspecified).
    /// Service converts them to UTC.
    /// </summary>
    public class ReviewCorrectionDto
    {
        [Required]
        public Guid AttendanceCorrectionRequestId { get; set; }

        [Required]
        public bool Approve { get; set; }

        /// <summary>
        /// HR may override the employee's requested check-in before approving.
        /// Org-local (Kind=Unspecified). Null = use the employee's RequestedCheckIn.
        /// </summary>
        public DateTime? HrOverrideCheckIn { get; set; }

        /// <summary>Org-local (Kind=Unspecified). Null = use the employee's RequestedCheckOut.</summary>
        public DateTime? HrOverrideCheckOut { get; set; }

        [MaxLength(1000)]
        public string? HrNote { get; set; }

        /// <summary>Required when Approve == false.</summary>
        [MaxLength(1000)]
        public string? RejectionReason { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EMPLOYEE → SYSTEM  :  CANCEL
    // ═══════════════════════════════════════════════════════════════════════════

    public class CancelCorrectionDto
    {
        [Required]
        public Guid AttendanceCorrectionRequestId { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HR → SYSTEM  :  MANUAL CORRECTION ON BEHALF OF EMPLOYEE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// HR initiates a direct correction without the normal employee Pending flow.
    ///
    /// TIMEZONE CONTRACT:
    ///   NewCheckIn / NewCheckOut are org-local (Kind=Unspecified).
    ///   The service converts them to UTC before persisting.
    ///   The Razor page binds <input type="time"> to a TimeSpan? and then
    ///   combines it with WorkDate to produce Kind=Unspecified DateTimes.
    /// </summary>
    public class HrManualCorrectionDto
    {
        [Required]
        public Guid EmployeeId { get; set; }

        [Required]
        public DateTime WorkDate { get; set; }

        /// <summary>Org-local (Kind=Unspecified). Null = do not change check-in.</summary>
        public DateTime? NewCheckIn { get; set; }

        /// <summary>Org-local (Kind=Unspecified). Null = do not change check-out.</summary>
        public DateTime? NewCheckOut { get; set; }

        [MaxLength(50)]
        public string? OverrideStatus { get; set; }

        [Required]
        [MinLength(5)]
        [MaxLength(1000)]
        public string HrNote { get; set; } = null!;

        /// <summary>
        /// When true: the correction is created, auto-approved, and applied
        /// in a single transaction.
        /// When false: it is created as Pending for a second HR to review.
        /// </summary>
        public bool AutoApply { get; set; } = true;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // READ  :  LIST VIEW  (employee "my requests" + HR queue)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// All DateTime fields are UTC — display via TimeHelper.FormatOrgTime().
    /// </summary>
    public class CorrectionRequestListDto
    {
        public Guid AttendanceCorrectionRequestId { get; set; }
        public Guid EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string? Department { get; set; }
        public string? EmployeeCode { get; set; }

        /// <summary>Org-local date only (time component = 00:00:00).</summary>
        public DateTime WorkDate { get; set; }

        public string CorrectionType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        /// <summary>UTC — display via TimeHelper.</summary>
        public DateTime? RequestedCheckIn { get; set; }
        public DateTime? RequestedCheckOut { get; set; }
        public DateTime? OriginalCheckIn { get; set; }
        public DateTime? OriginalCheckOut { get; set; }
        public string? OriginalStatus { get; set; }
        public double? OriginalTotalHours { get; set; }

        public string Reason { get; set; } = string.Empty;
        public string? HrNote { get; set; }
        public string? RejectionReason { get; set; }

        public DateTime SubmittedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewedByName { get; set; }

        public bool IsApplied { get; set; }
        public bool IsHrInitiated { get; set; }
        public bool HasAttachment { get; set; }

        /// <summary>True when the employee may still cancel this request.</summary>
        //public bool CanCancel => Status == "Pending";
        public bool CanCancel { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // READ  :  DETAIL VIEW  (HR review modal / audit trail page)
    // ═══════════════════════════════════════════════════════════════════════════

    public class CorrectionRequestDetailDto : CorrectionRequestListDto
    {
        public DateTime? ApprovedCheckIn { get; set; }
        public DateTime? ApprovedCheckOut { get; set; }
        public string? ApprovedStatus { get; set; }
        public DateTime? AppliedAt { get; set; }
        public Guid? AttendanceId { get; set; }
        public string OrgTimeZoneId { get; set; } = "UTC";

        //public List<CorrectionAuditLogDto> AuditLog { get; set; } = new();
        public List<CorrectionAuditLogDto> AuditLogs { get; set; } = new();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // READ  :  AUDIT LOG ENTRY
    // ═══════════════════════════════════════════════════════════════════════════

    public class CorrectionAuditLogDto
    {
        public string Action { get; set; } = string.Empty;
        public string? ActorName { get; set; }
        public string? Notes { get; set; }
        public DateTime OccurredAt { get; set; }

        public DateTime? PreviousCheckIn { get; set; }
        public DateTime? PreviousCheckOut { get; set; }
        public DateTime? NewCheckIn { get; set; }
        public DateTime? NewCheckOut { get; set; }
        public string? PreviousStatus { get; set; }
        public string? NewStatus { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // READ  :  HR DASHBOARD SUMMARY CARDS
    // ═══════════════════════════════════════════════════════════════════════════

    public class CorrectionQueueSummaryDto
    {
        public int TotalPending { get; set; }
        public int PendingOlderThan2Days { get; set; }
        public int ApprovedThisWeek { get; set; }
        public int RejectedThisWeek { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // FILTER  :  HR QUEUE
    // ═══════════════════════════════════════════════════════════════════════════

    public class CorrectionQueueFilterDto
    {
        public Guid? DepartmentId { get; set; }
        public string? Status { get; set; }  // null = all statuses
        public string? CorrectionType { get; set; }  // null = all types
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? SearchName { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PAGING WRAPPER
    // ═══════════════════════════════════════════════════════════════════════════

    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }

        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => Page < TotalPages;
    }
}