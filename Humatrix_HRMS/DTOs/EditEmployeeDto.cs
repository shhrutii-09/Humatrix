namespace Humatrix_HRMS.DTOs
{
    public class EditEmployeeDto
    {
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public Guid? DepartmentId { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public Guid? DesignationId { get; set; }
        public Guid? ShiftId { get; set; } // ✅ Add this line
        public string Role { get; set; } = ""; // Added this to fix CS0117
    }
}