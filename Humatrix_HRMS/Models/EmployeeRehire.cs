using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    public class EmployeeRehire
    {
        [Key]
        public Guid RehireId { get; set; } = Guid.NewGuid();

        public Guid EmployeeId { get; set; }
        public Guid PreviousExitId { get; set; }
        public DateTime RehireDate { get; set; }
        public string RehiredByUserId { get; set; } = string.Empty;
        public string? Remarks { get; set; }
        public string? PreviousStatus { get; set; }
        public string? PreviousExitType { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("EmployeeId")]
        public virtual Employee? Employee { get; set; }

        [ForeignKey("PreviousExitId")]
        public virtual EmployeeExit? PreviousExit { get; set; }
    }
}