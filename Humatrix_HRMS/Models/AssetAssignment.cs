using System.ComponentModel.DataAnnotations;

namespace Humatrix_HRMS.Models
{
    public class AssetAssignment
    {
        public Guid AssignmentId { get; set; }

        public Guid AssetId { get; set; }
        public Asset Asset { get; set; }

        public Guid EmployeeId { get; set; }
        public Employee Employee { get; set; }

        public Guid? AssignedByEmployeeId { get; set; }
        public Employee? AssignedByEmployee { get; set; }

        [Timestamp]
        public byte[]? RowVersion { get; set; } 

        public DateTime AssignedAt { get; set; }

        public DateTime? ReturnedAt { get; set; }

        public string Status { get; set; } = "Active";

        public string? AssignmentNotes { get; set; }

        public string? ReturnCondition { get; set; }

        public bool IsActive => ReturnedAt == null;
    }
}
