namespace Humatrix_HRMS.DTOs.Dashboard
{
    public class EmployeeDashboardContextDto
    {
        public string UserId { get; set; } = default!;

        public Guid EmployeeId { get; set; }

        public Guid OrganizationId { get; set; }

        public Guid DepartmentId { get; set; }
        public string DepartmentName { get; set; } = default!;  // Add this property

        public string FullName { get; set; } = default!;
        public string Role { get; set; } = default!;

        public string Email { get; set; } = default!;
    }
}