namespace Humatrix_HRMS.DTOs
{
    public class CreateOrganizationDto
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }

        public string AdminEmail { get; set; }
    }
}