namespace Humatrix_HRMS.DTOs
{
    public class EmployeeDashboardDto
    {
        public string FullName { get; set; } = default!;
        public string Email { get; set; } = default!;
        public string EmployeeCode { get; set; } = default!; // Added to fix error
        public string DepartmentName { get; set; } = "N/A";
        public string DesignationName { get; set; } = "N/A";
        public string ShiftName { get; set; } = "No Shift";
        public string Status { get; set; } = "Active";
        public DateTime JoiningDate { get; set; }

        // Helper for the profile circle avatar
        public string Initial => !string.IsNullOrEmpty(FullName) ? FullName[0].ToString().ToUpper() : "U";
    }
}