using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Humatrix_HRMS.Data;

namespace Humatrix_HRMS.Models.Documents
{
    public class DocumentType
    {
        public Guid DocumentTypeId { get; set; } = Guid.NewGuid();

        [Required]
        public Guid OrganizationId { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = default!;

        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>"Personal" | "Organization" | "Compliance"</summary>
        [Required, MaxLength(50)]
        public string Category { get; set; } = "Personal";

        public bool IsMandatory { get; set; }
        public bool RequiresVerification { get; set; } = true;
        public bool TrackExpiry { get; set; }

        /// <summary>Comma-separated e.g. ".pdf,.jpg,.png"</summary>
        [MaxLength(200)]
        public string AllowedFileTypes { get; set; } = ".pdf,.jpg,.png";

        public int MaxFileSizeMB { get; set; } = 5;

        public bool IsEmployeeUploadAllowed { get; set; } = true;
        public bool IsHRUploadAllowed { get; set; } = true;
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByUserId { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedByUserId { get; set; }

        // Navigation
        [ForeignKey("OrganizationId")]
        public Organization? Organization { get; set; }

        public ICollection<EmployeeDocument> Documents { get; set; } = new List<EmployeeDocument>();
    }
}