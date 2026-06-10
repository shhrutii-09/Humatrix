using System.ComponentModel.DataAnnotations;

namespace Humatrix_HRMS.DTOs.Documents;

/// <summary>
/// DTO for creating/updating document templates
/// </summary>
public class OrgDocumentTemplateDto
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required]
    public string Category { get; set; } = "Onboarding"; // Onboarding, Disciplinary, Recognition, Offboarding

    [Required]
    public string TemplateContent { get; set; } = string.Empty; // HTML with placeholders

    public Dictionary<string, string>? PlaceholderDescriptions { get; set; }

    public bool RequiresAcknowledgment { get; set; } = false;

    public int DisplayOrder { get; set; } = 0;
}

/// <summary>
/// DTO for updating an existing template
/// </summary>
public class UpdateOrgDocumentTemplateDto : OrgDocumentTemplateDto
{
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// DTO for viewing a template
/// </summary>
public class OrgDocumentTemplateViewDto
{
    public Guid TemplateId { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public string TemplateContent { get; set; } = string.Empty;
    public Dictionary<string, string>? PlaceholderDescriptions { get; set; }
    public bool RequiresAcknowledgment { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByName { get; set; }
}