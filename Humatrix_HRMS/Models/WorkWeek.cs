namespace Humatrix_HRMS.Models
{
    public class WorkWeek
    {
        public Guid WorkWeekId { get; set; }

        public Guid OrganizationId { get; set; }

        // Example:
        public bool IsMondayWorking { get; set; } = true;
        public bool IsTuesdayWorking { get; set; } = true;
        public bool IsWednesdayWorking { get; set; } = true;
        public bool IsThursdayWorking { get; set; } = true;
        public bool IsFridayWorking { get; set; } = true;
        public bool IsSaturdayWorking { get; set; } = false;
        public bool IsSundayWorking { get; set; } = false;
    }
}
