using System.ComponentModel.DataAnnotations;

namespace Humatrix_HRMS.DTOs.Documents;

/// <summary>
/// Request DTO for generating an organization document
/// </summary>
public class OrgDocumentGenerationDto
{
    /// <summary>
    /// The document template ID to use
    /// </summary>
    [Required]
    public Guid TemplateId { get; set; }

    /// <summary>
    /// Employee ID who will receive this document
    /// </summary>
    [Required]
    public Guid RecipientEmployeeId { get; set; }

    /// <summary>
    /// Optional custom data to override placeholders
    /// Example: {"JoiningDate": "2024-01-15", "Salary": "50000"}
    /// </summary>
    public Dictionary<string, string>? CustomData { get; set; }

    /// <summary>
    /// Optional remarks to add to the document
    /// </summary>
    [MaxLength(500)]
    public string? Remarks { get; set; }

    /// <summary>
    /// Should the employee receive an email notification?
    /// </summary>
    public bool SendEmailNotification { get; set; } = true;
}