// Models/TicketReply.cs
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    public class TicketReply
    {
        public Guid ReplyId { get; set; } = Guid.NewGuid();
        public Guid TicketId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(TicketId))]
        public SupportTicket? Ticket { get; set; }
    }
}