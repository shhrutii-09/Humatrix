namespace Humatrix_HRMS.Models
{
    public class Department
    {
        public Guid DepartmentId { get; set; } = Guid.NewGuid();
        public Guid OrganizationId { get; set; }

        public string Name { get; set; }
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true; // ✅ ONLY THIS
    }
}