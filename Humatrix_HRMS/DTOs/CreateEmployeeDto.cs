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

<<<<<<< HEAD
        public Guid? DepartmentId { get; set; }

        public Guid? DesignationId { get; set; }

        public string Role { get; set; } = "Employee";
=======
        public Guid? DepartmentId { get; set; } 

        public string Role { get; set; } = "Employee";
        public Guid? DesignationId { get; set; }

>>>>>>> ae07b0cd972eb059e35f6d866fb42c0d181ee94f
    }
}
