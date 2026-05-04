namespace Humatrix_HRMS.DTOs
{
    // ── Attendance correction ─────────────────────────────────────────────────

    public class CorrectionRequestDto
    {
        public Guid CorrectionRequestId { get; set; }
        public string EmployeeName { get; set; } = "";
        public string? Department { get; set; }
        public DateTime Date { get; set; }
        public DateTime? RequestedCheckIn { get; set; }
        public DateTime? RequestedCheckOut { get; set; }
        public string Reason { get; set; } = "";
        public string Status { get; set; } = "Pending";
        public DateTime AppliedAt { get; set; }
        public Guid? AttendanceId { get; set; }
    }

    public class CreateCorrectionRequestDto
    {
        public DateTime Date { get; set; }
        public DateTime? RequestedCheckIn { get; set; }
        public DateTime? RequestedCheckOut { get; set; }
        public string Reason { get; set; } = "";
    }

    public class ReviewCorrectionDto
    {
        public Guid CorrectionRequestId { get; set; }
        public bool Approve { get; set; }
        public string? RejectionReason { get; set; }
    }

    // ── Leave DTOs ─────────────────────────────────────────────────────────────

   

   

    // ── Attendance list (existing, extended) ──────────────────────────────────

   

    // HR manual edit
  
}