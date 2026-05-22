namespace Humatrix_HRMS.DTOs
{
    public class LeaveRequestDto
    {
        public Guid LeaveRequestId { get; set; }

        // 👤 Employee Info (Flattened for UI)
        public string EmployeeName { get; set; } = string.Empty;
        public string? Department { get; set; }

        // 🏷 Leave Info
        public string LeaveTypeName { get; set; } = string.Empty;

        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        public bool IsHalfDay { get; set; }
        public decimal TotalDays { get; set; }

        // 📝 Details
        public string Reason { get; set; } = string.Empty;

        // 🔄 Status
        public string Status { get; set; } = string.Empty;

        // ⏱ Timeline
        public DateTime AppliedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }

        // ❌ Rejection
        public string? RejectionReason { get; set; }

        public string? ReviewerName { get; set; }
        //public DateTime? ReviewedAt { get; set; }
    }
}