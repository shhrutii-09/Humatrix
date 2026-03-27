using System.ComponentModel.DataAnnotations;

public class CreateOrganizationDto
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, RegularExpression(@"^[0-9]{10}$")]
    public string Phone { get; set; } = string.Empty;

    [Required]
    public string Address { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string AdminEmail { get; set; } = string.Empty;
}