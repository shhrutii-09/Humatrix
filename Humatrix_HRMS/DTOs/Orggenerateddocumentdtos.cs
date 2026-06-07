// ============================================================
// FILE: DTOs/Documents/OrgGeneratedDocumentDtos.cs
// ============================================================

using Microsoft.AspNetCore.Components.Forms;

namespace Humatrix_HRMS.DTOs.Documents
{
    // ── Issue form ────────────────────────────────────────────

    /// <summary>Form payload when HR/OrgAdmin issues a document to an employee.</summary>
    public class OrgDocumentIssueDto
    {
        public Guid EmployeeId { get; set; }
        public Guid DocumentTypeId { get; set; }
        public IBrowserFile File { get; set; } = default!;
        public string? DocumentNumber { get; set; }
        public DateOnly? DocumentDate { get; set; }
        public string? Remarks { get; set; }
    }

    // ── HR / OrgAdmin list row ────────────────────────────────

    /// <summary>One row in the management grid.</summary>
    public class OrgGeneratedDocumentListDto
    {
        public Guid OrgDocumentId { get; set; }
        public Guid EmployeeId { get; set; }
        public string EmployeeName { get; set; } = default!;
        public string EmployeeCode { get; set; } = default!;
        public string DepartmentName { get; set; } = default!;
        public string DocumentTypeName { get; set; } = default!;
        public string Category { get; set; } = default!;
        public string OriginalFileName { get; set; } = default!;
        public string FilePath { get; set; } = default!;
        public long FileSize { get; set; }
        public string? DocumentNumber { get; set; }
        public DateOnly? DocumentDate { get; set; }
        public string? Remarks { get; set; }
        public string IssuedByName { get; set; } = default!;
        public string IssuedByRole { get; set; } = default!;
        public DateTime IssuedAt { get; set; }
        public int Version { get; set; }
        public bool IsRevoked { get; set; }
        public string? RevocationReason { get; set; }

        // Helpers
        public string FileSizeDisplay =>
            FileSize >= 1_048_576
                ? $"{FileSize / 1_048_576.0:F1} MB"
                : $"{FileSize / 1024.0:F0} KB";
    }

    // ── Employee / HR read-only portal ────────────────────────

    /// <summary>
    /// What an Employee or HR sees in their "My Official Documents" portal.
    /// No upload, no modify, download-only.
    /// </summary>
    public class MyOrgDocumentDto
    {
        public Guid OrgDocumentId { get; set; }
        public string DocumentTypeName { get; set; } = default!;
        public string Category { get; set; } = default!;
        public string OriginalFileName { get; set; } = default!;
        public string FilePath { get; set; } = default!;
        public long FileSize { get; set; }
        public string? DocumentNumber { get; set; }
        public DateOnly? DocumentDate { get; set; }
        public string? Remarks { get; set; }
        public string IssuedByName { get; set; } = default!;
        public DateTime IssuedAt { get; set; }
        public int Version { get; set; }

        public string FileSizeDisplay =>
            FileSize >= 1_048_576
                ? $"{FileSize / 1_048_576.0:F1} MB"
                : $"{FileSize / 1024.0:F0} KB";

        public string FileExtension =>
            Path.GetExtension(OriginalFileName).ToLowerInvariant();
    }

    // ── KPI stats ─────────────────────────────────────────────

    public class OrgDocumentStatsDto
    {
        public int TotalIssued { get; set; }
        public int IssuedThisMonth { get; set; }
        public int UniqueEmployeesCovered { get; set; }
        public int TotalRevoked { get; set; }
    }
}