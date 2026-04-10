namespace Humatrix_HRMS.Models
{
    public class LeaveBalance
    {
        public Guid EmployeeId { get; set; }
        public Guid LeaveTypeId { get; set; }
        public int RemainingDays { get; set; }
    }
}
