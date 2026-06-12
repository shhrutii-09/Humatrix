namespace Humatrix_HRMS.Models
{
    public class Shift
    {
        public Guid ShiftId { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty; 
        public TimeSpan StartTime { get; set; } 
        public TimeSpan EndTime { get; set; }

        public int LateAllowanceMinutes{ get; set; } = 15; 
        public double MinimumHoursForFullDay { get; set; } = 8.0;
        public double MinimumHoursForHalfDay { get; set; } = 4.0;

        public Guid OrganizationId { get; set; }
    }
}