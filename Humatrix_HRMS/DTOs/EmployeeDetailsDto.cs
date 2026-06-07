using Humatrix_HRMS.DTOs;

namespace Humatrix_HRMS.DTOs
{
    public class EmployeeDetailsDto : EmployeeDashboardDto
    {
        public Guid EmployeeId { get; set; }
        public Guid OrganizationId { get; set; } // Added to resolve compilation mapping errors
        public int TotalDays { get; set; }
        public int PresentDays { get; set; }

        public decimal AttendancePercentage =>
            TotalDays == 0 ? 0 : Math.Round((decimal)PresentDays / TotalDays * 100, 2);

        // FIXED: Updated types to match what the service produces
        public List<AttendanceRecordDto> RecentAttendance { get; set; } = new();
        public List<LeaveRecordDto> Leaves { get; set; } = new();

        public string Role { get; set; } = "Employee";
    }
}