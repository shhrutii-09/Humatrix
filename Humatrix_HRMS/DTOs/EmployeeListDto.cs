namespace Humatrix_HRMS.DTOs
{
    public class EmployeeListDto
    {
<<<<<<< HEAD
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }
=======
        public string Name { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
        public string? Department { get; set; }
        public string? Organization { get; set; }
        public string? Designation { get; set; }
        public bool IsActive { get; set; }
>>>>>>> ae07b0cd972eb059e35f6d866fb42c0d181ee94f

        public string? Department { get; set; }
        public string? Designation { get; set; }   // ✅ NEW

        public string? Organization { get; set; }

        public bool IsActive { get; set; }
        public bool IsHR { get; set; }
    }
}
