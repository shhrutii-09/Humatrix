using System.ComponentModel.DataAnnotations.Schema;

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

        public string? Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }

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

        public string? CreatedByUserId { get; set; } // ✅ FIXED

        //public Guid OrganizationId { get; set; }

        [ForeignKey("OrganizationId")]
        public Organization? Organization { get; set; }   // ← Add this navigation property

        [ForeignKey("DepartmentId")]
        public Department? Department { get; set; }


        // ADD THIS SECTION BELOW
        [ForeignKey("DesignationId")]
        public Designation? Designation { get; set; }
    }
}
