namespace Humatrix_HRMS.Models
{
    public class LeaveType
    {
        public Guid LeaveTypeId { get; set; }
        public string Name { get; set; } // Sick, Casual
        public int MaxDaysPerYear { get; set; }
        public bool IsPaid { get; set; }
        public Guid OrganizationId { get; set; }
    }
}
