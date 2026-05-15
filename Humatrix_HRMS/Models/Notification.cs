using System.ComponentModel.DataAnnotations;

namespace Humatrix_HRMS.Models
{
    public class Notification
    {
        [Key]
        public int NotificationId { get; set; }

        public string UserId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public bool IsRead { get; set; } = false;

        public string? RedirectUrl { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}