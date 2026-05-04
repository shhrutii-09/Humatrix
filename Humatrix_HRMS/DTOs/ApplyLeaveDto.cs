using System.ComponentModel.DataAnnotations;

namespace Humatrix_HRMS.DTOs
{
    public class ApplyLeaveDto
    {
        [Required]
        public Guid LeaveTypeId { get; set; }

        [Required]
        public DateTime FromDate { get; set; }

        [Required]
        public DateTime ToDate { get; set; }

        public bool IsHalfDay { get; set; } = false;

        [Required]
        [StringLength(500)]
        public string Reason { get; set; } = string.Empty;
    }
}