namespace Humatrix_HRMS.DTOs
{
    public class ShiftDto
    {
        public Guid ShiftId { get; set; }
        public string Name { get; set; } = "";
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public int LateAllowanceMinutes { get; set; }
    }
}