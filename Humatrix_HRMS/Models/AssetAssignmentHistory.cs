using Humatrix_HRMS.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class AssetAssignmentHistory
{
    [Key]
    public Guid HistoryId { get; set; } = Guid.NewGuid();

    public Guid AssetId { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid? AssignedByEmployeeId { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ReturnedAt { get; set; }

    [MaxLength(50)]
    public string? ReturnCondition { get; set; }

    [MaxLength(2000)]
    public string? AssignmentNotes { get; set; }

    [ForeignKey(nameof(AssetId))]
    public Asset? Asset { get; set; }

    [ForeignKey(nameof(EmployeeId))]
    public Employee? Employee { get; set; }

    [ForeignKey(nameof(AssignedByEmployeeId))]
    public Employee? AssignedByEmployee { get; set; }
}