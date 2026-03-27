using Microsoft.AspNetCore.Identity;

namespace Humatrix_HRMS.Data
{
    public class ApplicationUser : IdentityUser
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }

        public Guid? OrganizationId { get; set; }
        public Guid? DepartmentId { get; set; }
        public Guid? DesignationId { get; set; }
        public bool IsActive { get; set; } = true;

        public string? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}