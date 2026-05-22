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
    
        // =========================================================================
        // SUBMIT (Employee)
        // =========================================================================

        /// <summary>
        /// Passed from the Razor page to CorrectionService.SubmitAsync().
        /// Time fields must be org-local, Kind=Unspecified.
        /// The service converts them to UTC before storing.
        /// </summary>
        public class SubmitCorrectionRequestDto
        {
            public DateTime WorkDate { get; set; }

            /// <summary>See CorrectionTypes constants.</summary>
            public string CorrectionType { get; set; } = string.Empty;

            /// <summary>Org-local DateTime, Kind=Unspecified. Service converts to UTC.</summary>
            public DateTime? RequestedCheckIn { get; set; }

            /// <summary>Org-local DateTime, Kind=Unspecified. Service converts to UTC.</summary>
            public DateTime? RequestedCheckOut { get; set; }

            /// <summary>Optional status override (rarely used by employees).</summary>
            public string? RequestedStatus { get; set; }

            public string Reason { get; set; } = string.Empty;

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
        public Guid AttendanceCorrectionRequestId { get; set; }

        public bool Approve { get; set; }

        /// <summary>
        /// Org-local DateTime, Kind=Unspecified. Used only when HR wants to
        /// override the employee's requested check-in before approving.
        /// Null = use employee's requested value.
        /// </summary>
        public DateTime? HrOverrideCheckIn { get; set; }

        /// <summary>Org-local DateTime, Kind=Unspecified. Null = use requested value.</summary>
        public DateTime? HrOverrideCheckOut { get; set; }

        /// <summary>Optional status override by HR (e.g. force "Present").</summary>
        public string? HrOverrideStatus { get; set; }

        public string? HrNote { get; set; }

        /// <summary>Required when Approve=false.</summary>
        public string? RejectionReason { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EMPLOYEE → SYSTEM  :  CANCEL
    // ═══════════════════════════════════════════════════════════════════════════

    public class CancelCorrectionDto
    {
        public Guid AttendanceCorrectionRequestId { get; set; }
        public string? CancelReason { get; set; }
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
        /// <summary>Target employee (not the HR submitting).</summary>
        public Guid EmployeeId { get; set; }

        public DateTime WorkDate { get; set; }

        /// <summary>Org-local DateTime, Kind=Unspecified.</summary>
        public DateTime? NewCheckIn { get; set; }

        /// <summary>Org-local DateTime, Kind=Unspecified.</summary>
        public DateTime? NewCheckOut { get; set; }

        /// <summary>Optional status override. Null = let CalcService decide.</summary>
        public string? OverrideStatus { get; set; }

        public string HrNote { get; set; } = string.Empty;

        /// <summary>
        /// When true: request is auto-approved and applied immediately in one
        /// transaction. When false: creates a Pending request that another HR or
        /// OrgAdmin must review (four-eyes principle).
        /// </summary>
        public bool AutoApply { get; set; } = false;
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
        public DateTime WorkDate { get; set; }
        public string CorrectionType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ReviewLevel { get; set; }

        // Timestamps stored as UTC — convert for display in Razor
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

        // Computed flags for UI — eliminates status checks in Razor
        public bool CanCancel { get; set; }
        public bool IsOverdue { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // READ  :  DETAIL VIEW  (HR review modal / audit trail page)
    // ═══════════════════════════════════════════════════════════════════════════

    public class CorrectionRequestDetailDto : CorrectionRequestListDto
    {
        public List<CorrectionAuditLogDto> AuditLogs { get; set; } = new();

        // Approved values (for display after review)
        public DateTime? ApprovedCheckIn { get; set; }
        public DateTime? ApprovedCheckOut { get; set; }
        public string? ApprovedStatus { get; set; }

        public string? AppliedByName { get; set; }
        public DateTime? AppliedAt { get; set; }
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
    //    public int PendingOlderThan2Days { get; set; } // Add this line
    //public int ApprovedThisWeek { get; set; }      // Add this line
    public int RejectedThisWeek { get; set; }
        //public int RejectedThisWeek { get; set; }

        /// <summary>Count of HR-initiated corrections awaiting OrgAdmin review.</summary>
        public int HrRequestsPendingOrgAdminReview { get; set; }
    }


    public class CorrectionPreValidationResult
    {
        public bool IsAllowed { get; set; }
        public string? BlockReason { get; set; }
        public bool IsHoliday { get; set; }
        public bool IsWeeklyOff { get; set; }
        public bool IsOnLeave { get; set; }
        public bool IsOnWfh { get; set; }
        public bool HasExistingPending { get; set; }
        public bool AttendanceRecordExists { get; set; }
        public string? ExistingStatus { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // FILTER  :  HR QUEUE
    // ═══════════════════════════════════════════════════════════════════════════

    public class CorrectionQueueFilterDto
    {
        public string? Status { get; set; }
        public string? CorrectionType { get; set; }
        public Guid? DepartmentId { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? SearchName { get; set; }

        private int _page = 1;
        public int Page
        {
            get => _page;
            set => _page = value < 1 ? 1 : value;
        }

        private int _pageSize = 20;
        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = value < 1 ? 20 : (value > 100 ? 100 : value);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PAGING WRAPPER
    // ═══════════════════════════════════════════════════════════════════════════

    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        public int TotalPages => PageSize > 0
            ? (int)Math.Ceiling(TotalCount / (double)PageSize)
            : 0;

        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => Page < TotalPages;
    }
}