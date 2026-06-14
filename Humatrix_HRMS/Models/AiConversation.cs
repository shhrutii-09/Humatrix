// Models/AiConversation.cs
namespace Humatrix_HRMS.Models
{
    public class AiConversation
    {
        public Guid ConversationId { get; set; } = Guid.NewGuid();
        public string UserId { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;
        public string Intent { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}