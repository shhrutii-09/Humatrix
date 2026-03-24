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

=======
>>>>>>> 78f416305aa7332ecb4231ce726efacb44858935
        public Guid? DepartmentId { get; set; }

        public Guid? DesignationId { get; set; }

        public string Role { get; set; } = "Employee";
<<<<<<< HEAD

       
=======
        //public Guid? DepartmentId { get; set; } 

        //public string Role { get; set; } = "Employee";
        //public Guid? DesignationId { get; set; }
>>>>>>> 78f416305aa7332ecb4231ce726efacb44858935

    }
}
