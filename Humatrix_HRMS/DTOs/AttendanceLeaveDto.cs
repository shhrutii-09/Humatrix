namespace Humatrix_HRMS.DTOs
{
        public class AttendanceRecordDto
        {
            public DateTime Date { get; set; }
            public DateTime? CheckIn { get; set; }
            public DateTime? CheckOut { get; set; }
            public string Status { get; set; } = "Absent";
        }

        public class LeaveRecordDto
        {
            public string Type { get; set; } = string.Empty;
            public DateTime Date { get; set; }
        }
    }
