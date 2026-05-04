using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    public class LeaveBalance
    {
        public Guid LeaveBalanceId { get; set; } = Guid.NewGuid();

        public Guid EmployeeId { get; set; }
        [ForeignKey("EmployeeId")]
        public Employee Employee { get; set; } = null!;

        public Guid LeaveTypeId { get; set; }
        [ForeignKey("LeaveTypeId")]
        public LeaveType LeaveType { get; set; } = null!;

        public int Year { get; set; }           // which year this balance is for

        public int Allocated { get; set; }      // set at year start from LeaveType.MaxDaysPerYear
        public decimal Used { get; set; }       // decimal to support half-days (0.5)
        public decimal Pending { get; set; }    // days in "Pending" requests (not yet approved)

        public decimal Remaining => Allocated - Used - Pending;

        // Carry-forward from previous year
        public decimal CarriedForward { get; set; } = 0;
    }
}