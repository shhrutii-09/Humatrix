using System.ComponentModel.DataAnnotations.Schema;
using Humatrix_HRMS.Data;

namespace Humatrix_HRMS.Models
{
    public class Attendance
    {
        public Guid AttendanceId { get; set; }

        public string UserId { get; set; }
        public ApplicationUser User { get; set; }

        //public Guid? EmployeeId { get; set; }
        public Guid EmployeeId { get; set; }

        [ForeignKey("EmployeeId")]
        public Employee? Employee { get; set; }

        //public DateTime Date { get; set; }
        public DateTime WorkDate { get; set; }
        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }

        public bool IsPresent { get; set; }

        public Guid OrganizationId { get; set; }

        // ✅ Location tracking (you already added)
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public bool NeedsOvertimeApproval { get; set; }
        public string Status { get; set; } = "Not Started";

        // 🔥 ADD THESE ↓↓↓ (IMPORTANT)

        // Total worked hours
        public double? TotalHours { get; set; }

        // Extra time beyond full day
        public double? OvertimeHours { get; set; }
        public double ApprovedOvertimeHours { get; set; } = 0;

        // HR manual update tracking
        public bool IsManual { get; set; } = false;

        public Guid? UpdatedBy { get; set; }

        public DateTime? SystemCheckOut { get; set; }   // auto by system
        public DateTime? ActualCheckOut { get; set; }   // real (manual/approved)
        public bool IsAutoCheckedOut { get; set; } = false;

        public DateTime? UpdatedAt { get; set; }

        public DateTime CreatedAt { get; set; }

        public bool IsHrCorrected { get; set; }

        public Guid? LastModifiedByEmployeeId { get; set; }

        public DateTime? LastModifiedAt { get; set; }

        public string? ModificationReason { get; set; }

        public ICollection<AttendanceCorrectionRequest> CorrectionRequests { get; set; }
    = new List<AttendanceCorrectionRequest>();
    }
}