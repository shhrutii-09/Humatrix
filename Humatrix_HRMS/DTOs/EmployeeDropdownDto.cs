namespace Humatrix_HRMS.DTOs.Assets
{
    public class EmployeeDropdownDto
    {
        public Guid EmployeeId { get; set; }

        public string FullName { get; set; } = string.Empty;

        public Guid? DepartmentId { get; set; }
    }
}