using Humatrix_HRMS.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models.Documents;

/// <summary>
/// Templates for documents that ORGANIZATION generates (Offer Letters, Warning Letters, etc.)
/// This is SEPARATE from DocumentType which is for employee-uploaded documents
/// </summary>
public class OrgDocumentTemplate
{
    [Key]
    public Guid TemplateId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid OrganizationId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = default!;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required, MaxLength(100)]
    public string Category { get; set; } = default!; // Onboarding, Disciplinary, Recognition, Offboarding, Verification, Operational

    [Required]
    public string TemplateContent { get; set; } = default!; // HTML content with placeholders

    public string? PlaceholderSchema { get; set; } // JSON of available placeholders

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; } = 0;

    public bool RequiresAcknowledgment { get; set; } = false;

    // Tracking
    [Required, MaxLength(450)]
    public string CreatedByUserId { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }

    public DateTime? UpdatedAt { get; set; }

    // Navigation
    [ForeignKey("OrganizationId")]
    public Organization? Organization { get; set; }

    [ForeignKey("CreatedByUserId")]
    public ApplicationUser? CreatedBy { get; set; }

    // Generated documents using this template
    public ICollection<OrgGeneratedDocument> GeneratedDocuments { get; set; } = new List<OrgGeneratedDocument>();
}