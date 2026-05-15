namespace Humatrix_HRMS.DTOsA.Auth
{
    public class EmployeeProfileDto
    {
        public Guid EmployeeId { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string EmployeeCode { get; set; } = string.Empty;

        public string Department { get; set; } = string.Empty;

        public string Designation { get; set; } = string.Empty;

        public string Shift { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public DateTime JoiningDate { get; set; }

        public string? Phone { get; set; }

        public string? Address { get; set; }
    }
}
