// Models/TicketReply.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    public class TicketReply
    {
        [Key]
        public Guid ReplyId { get; set; } = Guid.NewGuid();

        public Guid TicketId { get; set; }

        [ForeignKey(nameof(TicketId))]
        public SupportTicket? Ticket { get; set; }

        /// <summary>ApplicationUser.Id of the reply author.</summary>
        public string UserId { get; set; } = string.Empty;

        [Required, MaxLength(4000)]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// When true, this reply is an internal note visible only to HR/OrgAdmin.
        /// The employee portal filters these out completely.
        /// </summary>
        public bool IsInternalNote { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}