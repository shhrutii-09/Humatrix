namespace Humatrix_HRMS.Models
{
    public class Designation
    {
        public Guid DesignationId { get; set; } = Guid.NewGuid();

        public Guid OrganizationId { get; set; }
        public Guid DepartmentId { get; set; }

        public string Name { get; set; }

        public bool IsActive { get; set; } = true; // ✅ NEW

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
