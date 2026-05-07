namespace Humatrix_HRMS.DTOs
{
    public class CorrectionRequestDto
    {
        public Guid CorrectionRequestId { get; set; }

        public Guid EmployeeId { get; set; }

        public string? EmployeeName { get; set; }

        public string? Department { get; set; }

        public string OrgTimeZoneId { get; set; } = "UTC";

        // Business date
        public DateOnly Date { get; set; }

        // Local org times for display
        public DateTime? RequestedCheckIn { get; set; }

        public DateTime? RequestedCheckOut { get; set; }

        public DateTime? CurrentCheckIn { get; set; }

        public DateTime? CurrentCheckOut { get; set; }

        public string Reason { get; set; } = string.Empty;

        public string Status { get; set; } = "Pending";

        public string? RejectionReason { get; set; }

        public DateTime AppliedAt { get; set; }

        public DateTime? ReviewedAt { get; set; }

        public Guid? AttendanceId { get; set; }
    }

    public class CreateCorrectionRequestDto
    {
        // IMPORTANT: frontend should send ONLY date
        // Example: "2026-05-07"
        public DateOnly Date { get; set; }

        // Local organization time only
        public TimeSpan? RequestedCheckIn { get; set; }

        public TimeSpan? RequestedCheckOut { get; set; }

        public string Reason { get; set; } = string.Empty;
    }

    namespace Humatrix_HRMS.DTOs
    {
        public class ReviewCorrectionDto
        {
            public Guid CorrectionRequestId { get; set; }

            public bool Approve { get; set; }

            public string? RejectionReason { get; set; }
        }
    }
}