namespace Humatrix_HRMS.DTOs.AttendanceCorrections
{
    public class AttendanceCorrectionListDto
    {
        public Guid AttendanceCorrectionRequestId { get; set; }

        public string EmployeeName { get; set; } = null!;

        public string Department { get; set; } = null!;

        public DateTime WorkDate { get; set; }

        public string RequestType { get; set; } = null!;

        public DateTime? ExistingCheckIn { get; set; }

        public DateTime? ExistingCheckOut { get; set; }

        public DateTime? RequestedCheckIn { get; set; }

        public DateTime? RequestedCheckOut { get; set; }
        public string Email { get; set; } = string.Empty;

        public string Reason { get; set; } = null!;

        public string Status { get; set; } = null!;

        public string? HRRemarks { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}