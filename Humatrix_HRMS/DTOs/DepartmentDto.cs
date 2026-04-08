namespace Humatrix_HRMS.DTOs
{
    public class DepartmentDto
    {

        //DepartmentDto(for display)

        public Guid DepartmentId { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }

        public bool IsActive { get; set; } // ✅ REQUIRED

    }
}
