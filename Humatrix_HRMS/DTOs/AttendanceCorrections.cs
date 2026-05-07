namespace Humatrix_HRMS.DTOs.AttendanceCorrections
{
    public class CreateAttendanceCorrectionDto
    {
        public DateTime WorkDate { get; set; }

        public string RequestType { get; set; } = null!;

        public DateTime? RequestedCheckIn { get; set; }

        public DateTime? RequestedCheckOut { get; set; }

        public string Reason { get; set; } = null!;
    }
}