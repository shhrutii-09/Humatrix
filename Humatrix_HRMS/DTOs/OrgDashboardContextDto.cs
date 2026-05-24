namespace Humatrix_HRMS.DTOs.Dashboard
{
    public class OrgDashboardContextDto
    {
        public string UserId { get; set; } = "";

        public Guid OrganizationId { get; set; }

        public string FullName { get; set; } = "";

        public string Email { get; set; } = "";
    }
}