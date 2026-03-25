using System.ComponentModel.DataAnnotations;

namespace Humatrix_HRMS.DTOs
{
    public class CreateEmployeeDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        public string FirstName { get; set; }

        [Required]
        public string LastName { get; set; }

        public Guid? DepartmentId { get; set; } // ✅ nullable

        public string Role { get; set; } = "Employee"; // ✅ default
        public Guid? DesignationId { get; set; }
    }
}
