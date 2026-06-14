// Models/SupportTicket.cs
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    public class SupportTicket
    {
        public Guid TicketId { get; set; } = Guid.NewGuid();
        public int TicketNumber { get; set; }
        public Guid? EmployeeId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public Guid OrganizationId { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Priority { get; set; } = "Medium";
        public string Status { get; set; } = "Open";
        public Guid? AssignedToEmployeeId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedAt { get; set; }
        public string? Resolution { get; set; }

        // Navigation
        [ForeignKey(nameof(EmployeeId))]
        public Employee? Employee { get; set; }
    }
}