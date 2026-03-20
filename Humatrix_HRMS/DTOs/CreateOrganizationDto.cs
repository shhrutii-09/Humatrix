using System.ComponentModel.DataAnnotations;

namespace Humatrix_HRMS.DTOs
{
    public class CreateOrganizationDto
    {
<<<<<<< HEAD
        // Removed 'required' to stop CS9035 errors
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string AdminEmail { get; set; } = string.Empty;
=======
        [Required(ErrorMessage = "Organization name is required")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Organization email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Phone number is required")]
        [RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Phone number must be exactly 10 digits")]
        public string Phone { get; set; }

        [Required(ErrorMessage = "Address is required")]
        public string Address { get; set; }

        [Required(ErrorMessage = "Admin email is required")]
        [EmailAddress(ErrorMessage = "Invalid admin email")]
        public string AdminEmail { get; set; }
>>>>>>> 87e968b2f568150e4c704f94f305a3189b963a9e
    }
}
