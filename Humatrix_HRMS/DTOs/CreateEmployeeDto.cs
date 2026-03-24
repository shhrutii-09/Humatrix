namespace Humatrix_HRMS.DTOs
{
    public class CreateEmployeeDto
    {
        public string Email { get; set; }

        public string FirstName { get; set; }
        public string LastName { get; set; }

        public Guid? DepartmentId { get; set; } 

        public string Role { get; set; } = "Employee";
        public Guid? DesignationId { get; set; }

    }
}