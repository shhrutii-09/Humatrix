// ============================================================
// FILE: Services/Documents/OrgGeneratedDocumentService.cs
// ============================================================

using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs.Documents;
using Humatrix_HRMS.Models.Documents;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services.Documents
{
    public class OrgGeneratedDocumentService : IOrgGeneratedDocumentService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        private const long MaxFileSizeBytes = 20L * 1024 * 1024; // 20 MB

        public OrgGeneratedDocumentService(
            IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        // ── Issue ─────────────────────────────────────────────────────────────

        public async Task<OrgGeneratedDocument> IssueAsync(
            OrgDocumentIssueDto dto,
            Guid organizationId,
            string issuedByUserId,
            string issuedByRole,
            Guid? callerDepartmentId)
        {
            using var db = await _contextFactory.CreateDbContextAsync();

            // 1. Validate document type exists, belongs to this org, is org-generated
            var docType = await db.DocumentTypes
                .FirstOrDefaultAsync(x =>
                    x.DocumentTypeId == dto.DocumentTypeId &&
                    x.OrganizationId == organizationId &&
                    x.IsOrganizationGenerated &&
                    x.IsActive)
                ?? throw new InvalidOperationException(
                    "Document type not found or is not an org-generated type.");

            // 2. Validate target employee belongs to this org
            var employee = await db.Employees
                .FirstOrDefaultAsync(x =>
                    x.EmployeeId == dto.EmployeeId &&
                    x.OrganizationId == organizationId)
                ?? throw new InvalidOperationException(
                    "Employee not found in this organisation.");

            // 3. HR department scope guard
            if (issuedByRole == "HR" && callerDepartmentId.HasValue)
            {
                if (employee.DepartmentId != callerDepartmentId.Value)
                    throw new UnauthorizedAccessException(
                        "HR can only issue documents for employees in their own department.");
            }

            // 4. File size check
            if (dto.File.Size > MaxFileSizeBytes)
                throw new InvalidOperationException(
                    "File exceeds the 20 MB size limit.");

            // 5. File type check against DocumentType's allowed extensions
            var ext = Path.GetExtension(dto.File.Name).ToLowerInvariant();
            var allowed = docType.AllowedFileTypes
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim().ToLowerInvariant())
                .ToHashSet();

            if (allowed.Any() && !allowed.Contains(ext))
                throw new InvalidOperationException(
                    $"File type '{ext}' is not allowed for this document type. Allowed: {docType.AllowedFileTypes}");

            // 6. Versioning — mark current latest as superseded
            var existingLatest = await db.OrgGeneratedDocuments
                .IgnoreQueryFilters()
                .Where(x =>
                    x.EmployeeId == dto.EmployeeId &&
                    x.DocumentTypeId == dto.DocumentTypeId &&
                    x.IsLatestVersion)
                .FirstOrDefaultAsync();

            int version = 1;
            Guid? previousId = null;

            if (existingLatest != null)
            {
                existingLatest.IsLatestVersion = false;
                version = existingLatest.Version + 1;
                previousId = existingLatest.OrgDocumentId;
            }

            // 7. Save file — organised by org to keep filesystem clean
            string folder = Path.Combine(
                "wwwroot", "uploads", "org-documents", organizationId.ToString());
            Directory.CreateDirectory(folder);

            string storedFileName = $"{Guid.NewGuid()}{ext}";
            string fullPath = Path.Combine(folder, storedFileName);

            await using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await dto.File
                    .OpenReadStream(MaxFileSizeBytes)
                    .CopyToAsync(stream);
            }

            // 8. Persist
            var doc = new OrgGeneratedDocument
            {
                OrganizationId = organizationId,
                EmployeeId = dto.EmployeeId,
                DocumentTypeId = dto.DocumentTypeId,
                FileName = storedFileName,
                OriginalFileName = dto.File.Name,
                FilePath = $"/uploads/org-documents/{organizationId}/{storedFileName}",
                FileSize = dto.File.Size,
                MimeType = dto.File.ContentType,
                DocumentNumber = dto.DocumentNumber?.Trim(),
                DocumentDate = dto.DocumentDate,
                Remarks = dto.Remarks?.Trim(),
                IssuedByUserId = issuedByUserId,
                IssuedByRole = issuedByRole,
                IssuedAt = DateTime.UtcNow,
                Version = version,
                PreviousOrgDocumentId = previousId,
                IsLatestVersion = true
            };

            db.OrgGeneratedDocuments.Add(doc);
            await db.SaveChangesAsync();

            return doc;
        }

        // ── Revoke ────────────────────────────────────────────────────────────

        public async Task RevokeAsync(
            Guid orgDocumentId,
            Guid organizationId,
            string revokedByUserId,
            string revokedByRole,
            string reason,
            Guid? callerDepartmentId)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("Revocation reason is required.");

            using var db = await _contextFactory.CreateDbContextAsync();

            var doc = await db.OrgGeneratedDocuments
                .Include(x => x.Employee)
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x =>
                    x.OrgDocumentId == orgDocumentId &&
                    x.OrganizationId == organizationId)
                ?? throw new InvalidOperationException("Document not found.");

            if (doc.IsRevoked)
                throw new InvalidOperationException("Document is already revoked.");

            // HR scope guard
            if (revokedByRole == "HR" && callerDepartmentId.HasValue)
            {
                if (doc.Employee?.DepartmentId != callerDepartmentId.Value)
                    throw new UnauthorizedAccessException(
                        "HR can only revoke documents for employees in their own department.");
            }

            doc.IsRevoked = true;
            doc.RevokedAt = DateTime.UtcNow;
            doc.RevokedByUserId = revokedByUserId;
            doc.RevocationReason = reason.Trim();

            await db.SaveChangesAsync();
        }

        // ── Queries: management ───────────────────────────────────────────────

        public async Task<List<OrgGeneratedDocumentListDto>> GetIssuedDocumentsAsync(
            Guid organizationId,
            Guid? departmentId)
        {
            using var db = await _contextFactory.CreateDbContextAsync();

            var query = db.OrgGeneratedDocuments
                .Include(x => x.Employee)
                    .ThenInclude(e => e!.Department)
                .Include(x => x.DocumentType)
                .Include(x => x.IssuedByUser)
                .Where(x =>
                    x.OrganizationId == organizationId &&
                    x.IsLatestVersion);

            if (departmentId.HasValue)
                query = query.Where(x =>
                    x.Employee != null &&
                    x.Employee.DepartmentId == departmentId.Value);

            var rows = await query
                .OrderByDescending(x => x.IssuedAt)
                .ToListAsync();

            return rows.Select(ToListDto).ToList();
        }

        public async Task<OrgDocumentStatsDto> GetStatsAsync(
            Guid organizationId,
            Guid? departmentId)
        {
            using var db = await _contextFactory.CreateDbContextAsync();

            // Use IgnoreQueryFilters so we can count revoked too for the revoked stat
            var allQuery = db.OrgGeneratedDocuments
                .Include(x => x.Employee)
                .IgnoreQueryFilters()
                .Where(x =>
                    x.OrganizationId == organizationId &&
                    x.IsLatestVersion);

            if (departmentId.HasValue)
                allQuery = allQuery.Where(x =>
                    x.Employee != null &&
                    x.Employee.DepartmentId == departmentId.Value);

            var all = await allQuery.ToListAsync();

            var firstOfMonth = new DateTime(
                DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            return new OrgDocumentStatsDto
            {
                TotalIssued = all.Count(x => !x.IsRevoked),
                IssuedThisMonth = all.Count(x => !x.IsRevoked && x.IssuedAt >= firstOfMonth),
                UniqueEmployeesCovered = all.Where(x => !x.IsRevoked).Select(x => x.EmployeeId).Distinct().Count(),
                TotalRevoked = all.Count(x => x.IsRevoked)
            };
        }

        // ── Queries: employee read-only portal ───────────────────────────────

        public async Task<List<MyOrgDocumentDto>> GetMyDocumentsAsync(
            Guid employeeId,
            Guid organizationId)
        {
            using var db = await _contextFactory.CreateDbContextAsync();

            var docs = await db.OrgGeneratedDocuments
                .Include(x => x.DocumentType)
                .Include(x => x.IssuedByUser)
                .Where(x =>
                    x.EmployeeId == employeeId &&
                    x.OrganizationId == organizationId &&
                    x.IsLatestVersion)
                .OrderByDescending(x => x.IssuedAt)
                .ToListAsync();

            return docs.Select(d => new MyOrgDocumentDto
            {
                OrgDocumentId = d.OrgDocumentId,
                DocumentTypeName = d.DocumentType?.Name ?? "Document",
                Category = d.DocumentType?.Category ?? "Organisation",
                OriginalFileName = d.OriginalFileName,
                FilePath = d.FilePath,
                FileSize = d.FileSize,
                DocumentNumber = d.DocumentNumber,
                DocumentDate = d.DocumentDate,
                Remarks = d.Remarks,
                IssuedByName = d.IssuedByUser != null
                    ? $"{d.IssuedByUser.FirstName} {d.IssuedByUser.LastName}".Trim()
                    : d.IssuedByRole,
                IssuedAt = d.IssuedAt,
                Version = d.Version
            }).ToList();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        public async Task<List<DocumentType>> GetOrgDocumentTypesAsync(Guid organizationId)
        {
            using var db = await _contextFactory.CreateDbContextAsync();

            return await db.DocumentTypes
                .Where(x =>
                    x.OrganizationId == organizationId &&
                    x.IsOrganizationGenerated &&
                    x.IsActive)
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.Name)
                .ToListAsync();
        }

        // ── Private mapper ────────────────────────────────────────────────────

        private static OrgGeneratedDocumentListDto ToListDto(OrgGeneratedDocument d) => new()
        {
            OrgDocumentId = d.OrgDocumentId,
            EmployeeId = d.EmployeeId,
            EmployeeName = d.Employee != null
                ? $"{d.Employee.FirstName} {d.Employee.LastName}".Trim()
                : "—",
            EmployeeCode = d.Employee?.EmployeeCode ?? "—",
            DepartmentName = d.Employee?.Department?.Name ?? "Unassigned",
            DocumentTypeName = d.DocumentType?.Name ?? "—",
            Category = d.DocumentType?.Category ?? "—",
            OriginalFileName = d.OriginalFileName,
            FilePath = d.FilePath,
            FileSize = d.FileSize,
            DocumentNumber = d.DocumentNumber,
            DocumentDate = d.DocumentDate,
            Remarks = d.Remarks,
            IssuedByName = d.IssuedByUser != null
                ? $"{d.IssuedByUser.FirstName} {d.IssuedByUser.LastName}".Trim()
                : d.IssuedByRole,
            IssuedByRole = d.IssuedByRole,
            IssuedAt = d.IssuedAt,
            Version = d.Version,
            IsRevoked = d.IsRevoked,
            RevocationReason = d.RevocationReason
        };
    }
}