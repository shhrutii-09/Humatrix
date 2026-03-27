using System.ComponentModel.DataAnnotations;

namespace Humatrix_HRMS.DTOs
{
    public class CreateEmployeeDto
    {
        [Required(ErrorMessage = "First Name is required")]
        public string FirstName { get; set; } = "";

        [Required(ErrorMessage = "Last Name is required")]
        public string LastName { get; set; } = "";

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Please select a department")]
        public Guid? DepartmentId { get; set; }

        [Required(ErrorMessage = "Please select a designation")]
        public Guid? DesignationId { get; set; }

        public string Role { get; set; } = "Employee";
    }
}