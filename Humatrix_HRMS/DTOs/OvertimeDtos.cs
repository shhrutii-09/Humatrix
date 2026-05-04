namespace Humatrix_HRMS.DTOs
{
    public class CreateOvertimeRequestDto
    {
        public Guid AttendanceId { get; set; }

        public DateTime ActualCheckOut { get; set; }  // 🔥 ADD THIS

        public string Reason { get; set; } = string.Empty;
    }

    public class ReviewOvertimeDto
    {
        public Guid OvertimeRequestId { get; set; }
        public bool Approve { get; set; }
    }
}
