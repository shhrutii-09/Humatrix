namespace Humatrix_HRMS.DTOs
{
    public class EmployeeListDto
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }

        public string? Department { get; set; }
        public string? Designation { get; set; }   // ✅ NEW

        public string? Organization { get; set; }

        public bool IsActive { get; set; }
        public bool IsHR { get; set; }
    }
}
