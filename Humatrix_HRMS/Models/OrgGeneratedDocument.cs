// ============================================================
// FILE: Models/Documents/OrgGeneratedDocument.cs
// ============================================================
// Represents a document issued BY the organisation TO an employee
// (e.g. Offer Letter, Appointment Letter, Salary Slip, etc.)
//
// Core invariants:
//   - Employee/HR can only VIEW and DOWNLOAD — never upload or modify.
//   - Only OrgAdmin (any dept) or HR (own dept only) can issue/revoke.
//   - Versioning: only one IsLatestVersion=true record per (EmployeeId, DocumentTypeId).
//   - IsRevoked = soft-delete; employee can no longer see the document.
// ============================================================

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Humatrix_HRMS.Data;

namespace Humatrix_HRMS.Models.Documents
{
    public class OrgGeneratedDocument
    {
        public Guid OrgDocumentId { get; set; } = Guid.NewGuid();

        [Required]
        public Guid OrganizationId { get; set; }

        [Required]
        public Guid EmployeeId { get; set; }

        /// <summary>
        /// Must link to a DocumentType where IsOrganizationGenerated = true.
        /// </summary>
        [Required]
        public Guid DocumentTypeId { get; set; }

        // ── File ──────────────────────────────────────────────
        [Required, MaxLength(500)]
        public string FileName { get; set; } = default!;           // GUID-based stored name

        [Required, MaxLength(500)]
        public string OriginalFileName { get; set; } = default!;   // original user-facing name

        [Required, MaxLength(1000)]
        public string FilePath { get; set; } = default!;           // relative web path

        public long FileSize { get; set; }

        [Required, MaxLength(200)]
        public string MimeType { get; set; } = default!;

        // ── Document metadata ─────────────────────────────────
        [MaxLength(200)]
        public string? DocumentNumber { get; set; }

        /// <summary>Date printed on the letter (e.g. offer date, issue date).</summary>
        public DateOnly? DocumentDate { get; set; }

        [MaxLength(500)]
        public string? Remarks { get; set; }

        // ── Issuer audit ──────────────────────────────────────
        [Required, MaxLength(450)]
        public string IssuedByUserId { get; set; } = default!;

        /// <summary>"OrgAdmin" | "HR"</summary>
        [Required, MaxLength(50)]
        public string IssuedByRole { get; set; } = default!;

        public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

        // ── Versioning ────────────────────────────────────────
        public int Version { get; set; } = 1;
        public Guid? PreviousOrgDocumentId { get; set; }
        public bool IsLatestVersion { get; set; } = true;

        // ── Revocation ────────────────────────────────────────
        public bool IsRevoked { get; set; }
        public DateTime? RevokedAt { get; set; }

        [MaxLength(450)]
        public string? RevokedByUserId { get; set; }

        [MaxLength(500)]
        public string? RevocationReason { get; set; }

        // ── Navigation ────────────────────────────────────────
        [ForeignKey("OrganizationId")]
        public Organization? Organization { get; set; }

        [ForeignKey("EmployeeId")]
        public Employee? Employee { get; set; }

        [ForeignKey("DocumentTypeId")]
        public DocumentType? DocumentType { get; set; }

        [ForeignKey("PreviousOrgDocumentId")]
        public OrgGeneratedDocument? PreviousDocument { get; set; }

        [ForeignKey("IssuedByUserId")]
        public ApplicationUser? IssuedByUser { get; set; }
    }
}