namespace Humatrix_HRMS.DTOs.Dashboard
{
    public class HrDashboardContextDto
    {
        //public Guid EmployeeId { get; set; }

        public string EmployeeId { get; set; } = "";
        public Guid OrganizationId { get; set; }

        public Guid DepartmentId { get; set; }

        public string FullName { get; set; } = "";

        public string Email { get; set; } = "";
    }
}