namespace Humatrix_HRMS.DTOs
{
    public class EmployeeDetailsDto : EmployeeDashboardDto
    {
        public int TotalDays { get; set; }
        public int PresentDays { get; set; }

        public double AttendancePercentage =>
            TotalDays == 0 ? 0 : (double)PresentDays / TotalDays * 100;

        public List<AttendanceListDto> RecentAttendance { get; set; } = new();

        public List<LeaveDto> Leaves { get; set; } = new();
    }
}
