using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;

namespace Humatrix_HRMS.Data
{
    // Add profile data for application users by adding properties to the ApplicationUser class
    public class ApplicationUser : IdentityUser
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }

        public Guid? OrganizationId { get; set; }

        public Guid? DepartmentId { get; set; }
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Guid? DesignationId { get; set; }
        public Department? Department { get; set; }

    }

}
