namespace Humatrix_HRMS.Models
{
    public class LeaveType
    {
        public Guid LeaveTypeId { get; set; } = Guid.NewGuid();
        public Guid OrganizationId { get; set; }

        public string Name { get; set; } = string.Empty;       // "Sick Leave", "Casual Leave", etc.
        public int MaxDaysPerYear { get; set; }                  // e.g. 12
        public bool IsPaid { get; set; } = true;                 // affects payroll deduction logic

        // How many days can be carried forward to next year (0 = no carry-forward)
        public int MaxCarryForwardDays { get; set; } = 0;

        // Minimum days notice required before applying (0 = no restriction)
        public int MinNoticeRequiredDays { get; set; } = 0;

        // Whether the employee can apply for half-day leave of this type
        public bool AllowHalfDay { get; set; } = true;

        public bool IsActive { get; set; } = true;
    }
}