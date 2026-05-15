using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    public class TaskItem
    {
        [Key]
        public Guid TaskId { get; set; } = Guid.NewGuid();

        public Guid OrganizationId { get; set; }

        public Guid AssignedTo { get; set; }
        [ForeignKey("AssignedTo")]
        public Employee AssignedToEmployee { get; set; } = null!;

        public Guid AssignedBy { get; set; }

        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public string Priority { get; set; } = "Medium"; // Low / Medium / High

        public DateTime? DueDate { get; set; }

        public string Status { get; set; } = "Pending";
        // Pending | In Progress | Completed

        public int Progress { get; set; } = 0; // 0–100

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}