namespace Humatrix_HRMS.DTOs
{
    public class CreateOrganizationDto
    {
        // Removed 'required' to stop CS9035 errors
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string AdminEmail { get; set; } = string.Empty;
    }
}