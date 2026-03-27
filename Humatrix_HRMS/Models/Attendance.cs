using System.ComponentModel.DataAnnotations.Schema;
using Humatrix_HRMS.Data;

namespace Humatrix_HRMS.Models
{
    public class Attendance
    {
        public Guid AttendanceId { get; set; }

        public string UserId { get; set; }
        public ApplicationUser User { get; set; }

        // ✅ This properly links Attendance to the Employee profile
        public Guid? EmployeeId { get; set; } // Matches Employee's PK type

        [ForeignKey("EmployeeId")]
        public Employee? Employee { get; set; }

        public DateTime Date { get; set; }
        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }
        public bool IsPresent { get; set; }
        public Guid OrganizationId { get; set; }
    }
}