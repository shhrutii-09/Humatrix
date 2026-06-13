using Humatrix_HRMS.Data;
using System.ComponentModel.DataAnnotations.Schema;
using Humatrix_HRMS.Models.Documents;

namespace Humatrix_HRMS.Models
{
    public class Employee
    {
        public Guid EmployeeId { get; set; } = Guid.NewGuid();
        public Guid OrganizationId { get; set; }
        public string UserId { get; set; } = default!;
        public string EmployeeCode { get; set; } = default!;
        public string FirstName { get; set; } = default!;
        public string LastName { get; set; } = default!;

        // Added IsActive for Task Service filtering
        public bool IsActive { get; set; } = true;

        public DateTime? LastRehireDate { get; set; }
        public int? RehireCount { get; set; } = 0;
        public bool IsRehireable { get; set; } = true;
        public string? Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public bool OnboardingDocumentsProcessed { get; set; } = false;
        public DateTime? LastBirthdayNotificationSent { get; set; }
        public DateTime? LastWorkingDay { get; set; }
        public string? ExitReason { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }

        public Guid DepartmentId { get; set; }
        public Guid DesignationId { get; set; }
        public int? EmploymentTypeId { get; set; }
        public DateTime JoiningDate { get; set; } = DateTime.UtcNow;
        public Guid? ReportingManagerId { get; set; }
        public Guid? ShiftId { get; set; }

        [ForeignKey("ShiftId")]
        public Shift? Shift { get; set; }

        public string Status { get; set; } = "Active";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByUserId { get; set; }

        [ForeignKey("CreatedByUserId")]
        public ApplicationUser? CreatedByUser { get; set; }

        public string? CreatedByRole { get; set; }

        [ForeignKey("OrganizationId")]
        public Organization? Organization { get; set; }

        [ForeignKey("DepartmentId")]
        public Department? Department { get; set; }

        [ForeignKey("DesignationId")]
        public Designation? Designation { get; set; }

        public ICollection<EmployeeDocument> Documents { get; set; } = new List<EmployeeDocument>();

        public virtual ICollection<AssetAssignment> AssetAssignments { get; set; } = new List<AssetAssignment>();
    }
}