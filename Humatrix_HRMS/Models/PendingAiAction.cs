// Models/PendingAiAction.cs
namespace Humatrix_HRMS.Models
{
    public class PendingAiAction
    {
        public Guid PendingActionId { get; set; } = Guid.NewGuid();
        public string UserId { get; set; } = string.Empty;
        public string ActionJson { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(30);
    }
}