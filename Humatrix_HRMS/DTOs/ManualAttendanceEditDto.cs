namespace Humatrix_HRMS.DTOs
{
    public class ManualAttendanceEditDto
    {
        public Guid AttendanceId { get; set; }
        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }
        public string? StatusOverride { get; set; }   // optional — let service recalculate if null
    }
}

