namespace Humatrix_HRMS.DTOs
{
    public class EmployeeListDto
    {
        public string Email { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Name { get; set; } = "";
        public string Role { get; set; } = "";
        public string Department { get; set; } = "";
        public string Designation { get; set; } = "";
        public string ShiftName { get; set; } = "Unassigned"; // Add this
        public Guid? ShiftId { get; set; } // ✅ Add this line
        public bool IsActive { get; set; }
        public Guid? DepartmentId { get; set; }
        public Guid? DesignationId { get; set; }
    }
}
