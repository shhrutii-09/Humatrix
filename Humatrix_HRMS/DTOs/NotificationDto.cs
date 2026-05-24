// DTOs/NotificationDto.cs
namespace Humatrix_HRMS.DTOs
{
    public class NotificationDto
    {
        public int NotificationId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public string? RedirectUrl { get; set; }
        public string NotificationType { get; set; } = string.Empty;
        public string? ReferenceType { get; set; }
        public Guid? ReferenceId { get; set; }
        public string Priority { get; set; } = "Normal";
        public DateTime CreatedAt { get; set; }
        public DateTime? ReadAt { get; set; }
    }

    /// <summary>
    /// Lightweight payload pushed over SignalR.
    /// Avoids full page-reload on the frontend.
    /// </summary>
    public class NotificationPushDto
    {
        public int NotificationId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? RedirectUrl { get; set; }
        public string NotificationType { get; set; } = string.Empty;
        public string Priority { get; set; } = "Normal";
        public DateTime CreatedAt { get; set; }
        public int NewUnreadCount { get; set; }   // ← pre-computed, avoids extra DB call
    }
}