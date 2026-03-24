namespace Humatrix_HRMS.DTOs
{
    public class AttendanceDto
    {
        public DateTime Date { get; set; }
        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }
    }
}
