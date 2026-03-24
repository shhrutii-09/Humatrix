namespace Humatrix_HRMS.Models
{
    public class Designation
    {
        public Guid DesignationId { get; set; } = Guid.NewGuid();

        public Guid OrganizationId { get; set; }
        public Guid DepartmentId { get; set; }

        public string Name { get; set; }

        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
