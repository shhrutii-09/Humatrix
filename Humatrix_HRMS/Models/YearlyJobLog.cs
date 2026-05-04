namespace Humatrix_HRMS.Models
{
    public class YearlyJobLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid OrganizationId { get; set; }

        public int Year { get; set; }

        public string JobName { get; set; } = "LeaveBalanceInit";

        public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    }
}
