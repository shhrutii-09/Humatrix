namespace Humatrix_HRMS.DTOs.AttendanceCorrections
{
    public class HRActionAttendanceCorrectionDto
    {
        public Guid AttendanceCorrectionRequestId { get; set; }

        public bool Approve { get; set; }

        public string? HRRemarks { get; set; }
    }
}