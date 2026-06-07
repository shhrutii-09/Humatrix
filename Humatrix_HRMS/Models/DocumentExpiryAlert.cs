// ============================================================
// FILE: Models/Documents/DocumentExpiryAlert.cs
// ============================================================

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Humatrix_HRMS.Models.Documents
{
    public class DocumentExpiryAlert
    {
        public Guid AlertId { get; set; } = Guid.NewGuid();

        public Guid? DocumentId { get; set; }

        [Required]
        public Guid EmployeeId { get; set; }

        [Required]
        public Guid OrganizationId { get; set; }

        /// <summary>Which threshold triggered this alert: 30, 60, or 90.</summary>
        public int DaysBeforeExpiry { get; set; }

        public DateTime AlertSentAt { get; set; } = DateTime.UtcNow;

        [Required, MaxLength(50)]
        public string AlertType { get; set; } = "InApp"; // "InApp" | "Email"

        // Navigation
        [ForeignKey("DocumentId")]
        public EmployeeDocument? Document { get; set; }

        [ForeignKey("EmployeeId")]
        public Employee? Employee { get; set; }
    }
}