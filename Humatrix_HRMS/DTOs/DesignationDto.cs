namespace Humatrix_HRMS.DTOs
{
    public class DesignationDto
    {
        public Guid DesignationId { get; set; }
        public Guid DepartmentId { get; set; }
        public string? Name { get; set; }
        public string? Department { get; set; }
        public bool IsActive { get; set; } // ✅ ADD


    }
}
