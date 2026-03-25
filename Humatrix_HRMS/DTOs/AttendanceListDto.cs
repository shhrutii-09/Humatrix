namespace Humatrix_HRMS.DTOs
{
    public class AttendanceListDto
    {
        public string EmployeeName { get; set; }
        public string Email { get; set; }
        public string? Department { get; set; }
        public string Role { get; set; }
        public DateTime Date { get; set; }
        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }

        public string Status { get; set; } // Present / Late / Half Day
    }
}