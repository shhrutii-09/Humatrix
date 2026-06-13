namespace Humatrix_HRMS.DTOs.Documents
{
    public class EmployeeWithRoleDto
    {
        public Guid EmployeeId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string EmployeeCode { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsHR { get; set; }
        public bool IsOrgAdmin { get; set; }
        public string DisplayName => $"{FirstName} {LastName} {(IsHR ? "(HR)" : IsOrgAdmin ? "(Admin)" : "")}";
        public string DepartmentName { get; set; } = string.Empty;
    }
}