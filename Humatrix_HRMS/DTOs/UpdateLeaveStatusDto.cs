using System.ComponentModel.DataAnnotations;

namespace Humatrix_HRMS.DTOs
{
    public class UpdateLeaveStatusDto
    {
        [Required]
        public Guid LeaveRequestId { get; set; }

        [Required]
        public string Status { get; set; } = string.Empty; // Approved / Rejected

        public string? RejectionReason { get; set; }
    }
}