// Models/NotificationPreferences.cs
using System.ComponentModel.DataAnnotations;

namespace Humatrix_HRMS.Models
{
    public class NotificationPreferences
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public bool SoundEnabled { get; set; } = true;
        public bool BrowserNotificationsEnabled { get; set; } = true;
        public bool EmailEnabled { get; set; } = false;

        // Category toggles — expand as modules grow
        public bool LeaveNotificationsEnabled { get; set; } = true;
        public bool WfhNotificationsEnabled { get; set; } = true;
        public bool OvertimeNotificationsEnabled { get; set; } = true;
        public bool AttendanceNotificationsEnabled { get; set; } = true;
        public bool TaskNotificationsEnabled { get; set; } = true;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}