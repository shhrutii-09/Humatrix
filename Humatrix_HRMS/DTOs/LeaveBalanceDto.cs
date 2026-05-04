namespace Humatrix_HRMS.DTOs
{
    public class LeaveBalanceDto
    {
        public string LeaveTypeName { get; set; } = "";
        public bool IsPaid { get; set; }
        public int Allocated { get; set; }
        public decimal Used { get; set; }
        public decimal Pending { get; set; }
        public decimal Remaining { get; set; }
        public decimal CarriedForward { get; set; }
    }
}

