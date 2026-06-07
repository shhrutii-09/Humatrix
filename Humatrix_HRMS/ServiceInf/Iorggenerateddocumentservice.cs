// ============================================================
// FILE: Services/Documents/IOrgGeneratedDocumentService.cs
// ============================================================

using Humatrix_HRMS.DTOs.Documents;
using Humatrix_HRMS.Models.Documents;

namespace Humatrix_HRMS.Services.Documents
{
    public interface IOrgGeneratedDocumentService
    {
        // ── Issue ─────────────────────────────────────────────
        /// <summary>
        /// Issues an org-generated document to an employee.
        /// OrgAdmin: any employee in the org.
        /// HR: only employees in the caller's own department.
        /// </summary>
        Task<OrgGeneratedDocument> IssueAsync(
            OrgDocumentIssueDto dto,
            Guid organizationId,
            string issuedByUserId,
            string issuedByRole,
            Guid? callerDepartmentId);   // null = OrgAdmin (unrestricted)

        // ── Revoke ────────────────────────────────────────────
        /// <summary>
        /// Soft-revokes a document. Employee can no longer view it.
        /// HR can only revoke for their own department's employees.
        /// </summary>
        Task RevokeAsync(
            Guid orgDocumentId,
            Guid organizationId,
            string revokedByUserId,
            string revokedByRole,
            string reason,
            Guid? callerDepartmentId);

        // ── Queries: management (HR / OrgAdmin) ───────────────
        Task<List<OrgGeneratedDocumentListDto>> GetIssuedDocumentsAsync(
            Guid organizationId,
            Guid? departmentId);         // null = all depts (OrgAdmin only)

        Task<OrgDocumentStatsDto> GetStatsAsync(
            Guid organizationId,
            Guid? departmentId);

        // ── Queries: read-only portal (Employee + HR for self) ─
        /// <summary>Returns active org-generated documents for one employee.</summary>
        Task<List<MyOrgDocumentDto>> GetMyDocumentsAsync(
            Guid employeeId,
            Guid organizationId);

        // ── Helpers ───────────────────────────────────────────
        /// <summary>Returns DocumentTypes flagged as IsOrganizationGenerated.</summary>
        Task<List<DocumentType>> GetOrgDocumentTypesAsync(Guid organizationId);
    }
}