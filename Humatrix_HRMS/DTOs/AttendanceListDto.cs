namespace Humatrix_HRMS.DTOs
{
    public class AttendanceListDto
    {
        public Guid? AttendanceId { get; set; }
        public string EmployeeName { get; set; } = "";
        public string? Email { get; set; }
        public string OrgTimeZoneId { get; set; } = "UTC";
        public string? Department { get; set; }
        public string? Role { get; set; }
        public DateTime Date { get; set; }
        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }
        public double? TotalHours { get; set; }
        public double? OvertimeHours { get; set; }
        public string Status { get; set; } = "";
        public bool IsManual { get; set; }

        public bool NeedsOvertimeApproval { get; set; }
        public string? OvertimeStatus { get; set; } // Pending / Approved / Rejected
    }
}