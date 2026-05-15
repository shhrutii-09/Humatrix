namespace Humatrix_HRMS.DTOs
{
    public class NotificationDto
    {
        public int NotificationId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public bool IsRead { get; set; }

        public string? RedirectUrl { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}