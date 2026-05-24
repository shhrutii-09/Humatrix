// Models/Notification.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models
{
    public class Notification
    {
        [Key]
        public int NotificationId { get; set; }

        // ── Recipient ──────────────────────────────────────────────────
        /// <summary>AspNetUsers.Id of the recipient.</summary>
        [Required]
        public string UserId { get; set; } = string.Empty;

        // ── Organisation scoping (multi-tenant safety) ─────────────────
        public Guid OrganizationId { get; set; }

        // ── Categorisation ─────────────────────────────────────────────
        /// <summary>See NotificationTypes constants.</summary>
        [MaxLength(100)]
        public string NotificationType { get; set; } = string.Empty;

        /// <summary>See ReferenceTypes constants.</summary>
        [MaxLength(100)]
        public string? ReferenceType { get; set; }

        /// <summary>PK of the related entity (LeaveRequestId, etc.).</summary>
        public Guid? ReferenceId { get; set; }

        // ── Content ────────────────────────────────────────────────────
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string Message { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? RedirectUrl { get; set; }

        // ── Priority ───────────────────────────────────────────────────
        /// <summary>See NotificationPriorities constants.</summary>
        [MaxLength(20)]
        public string Priority { get; set; } = "Normal";

        // ── State ──────────────────────────────────────────────────────
        public bool IsRead { get; set; } = false;
        public DateTime? ReadAt { get; set; }

        // ── Audit ──────────────────────────────────────────────────────
        /// <summary>UserId of the actor who triggered this notification.</summary>
        public string? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ── Navigation (optional, avoids join in bell queries) ─────────
        [NotMapped]
        public bool IsUrgent => Priority == "Urgent" || Priority == "High";
    }
}