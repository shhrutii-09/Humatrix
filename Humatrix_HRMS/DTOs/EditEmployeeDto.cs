namespace Humatrix_HRMS.DTOs
{
    public class EditEmployeeDto
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public Guid? DepartmentId { get; set; }

        // ✅ Add this line to fix the red error
        public Guid? DesignationId { get; set; }
    }
}