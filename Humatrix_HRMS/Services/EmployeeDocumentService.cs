using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs.Documents;
using Humatrix_HRMS.Models.Documents;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services.Documents;

public class EmployeeDocumentService : IEmployeeDocumentService
{
    private readonly ApplicationDbContext _db;
    private readonly IDocumentHistoryService _historyService;

    public EmployeeDocumentService(
        ApplicationDbContext db,
        IDocumentHistoryService historyService)
    {
        _db = db;
        _historyService = historyService;
    }

    public async Task<EmployeeDocument> UploadDocumentAsync( DocumentUploadDto dto,
        Guid organizationId,string userId,string role)
    {
                var documentType = await _db.DocumentTypes
                    .FirstOrDefaultAsync(x =>
                        x.DocumentTypeId == dto.DocumentTypeId &&
                        x.OrganizationId == organizationId &&
                        x.IsActive);

                if (documentType == null)
                    throw new Exception("Document type not found.");

                var latestVersion = await _db.EmployeeDocuments
                    .Where(x =>
                        x.EmployeeId == dto.EmployeeId &&
                        x.DocumentTypeId == dto.DocumentTypeId &&
                        x.IsLatestVersion)
                    .FirstOrDefaultAsync();

                int version = 1;
                Guid? previousId = null;

                if (latestVersion != null)
                {
                    latestVersion.IsLatestVersion = false;

                    version = latestVersion.Version + 1;
                    previousId = latestVersion.DocumentId;
                }

                // File storage service later
                string storedFileName =
                    $"{Guid.NewGuid()}{Path.GetExtension(dto.File.Name)}";

                string folder =
                    Path.Combine(
                        "wwwroot",
                        "uploads",
                        "documents");

                Directory.CreateDirectory(folder);

                string fullPath =
                    Path.Combine(folder, storedFileName);

                await using var stream =
                    new FileStream(fullPath, FileMode.Create);

                await dto.File.OpenReadStream(
                    10 * 1024 * 1024)
                    .CopyToAsync(stream);

                var document = new EmployeeDocument
                {
                    OrganizationId = organizationId,
                    EmployeeId = dto.EmployeeId,
                    DocumentTypeId = dto.DocumentTypeId,

                    FileName = storedFileName,
                    OriginalFileName = dto.File.Name,
                    FilePath = $"/uploads/documents/{storedFileName}",
                    FileSize = dto.File.Size,
                    MimeType = dto.File.ContentType,

                    DocumentNumber = dto.DocumentNumber,
                    IssueDate = dto.IssueDate,
                    ExpiryDate = dto.ExpiryDate,
                    IssuingAuthority = dto.IssuingAuthority,

                    UploadedByUserId = userId,
                    UploadedByRole = role,

                    Status =
                        documentType.RequiresVerification
                        ? DocumentStatus.Pending
                        : DocumentStatus.Verified,

                    PreviousDocumentId = previousId,
                    Version = version,
                    IsLatestVersion = true
                };

                _db.EmployeeDocuments.Add(document);
                await _db.SaveChangesAsync();

                await _historyService.LogAsync(
            document,
            version == 1
                ? DocumentAction.Uploaded
                : DocumentAction.Reuploaded,
            userId,
            role,
            null,
            document.Status);

                return document;


    }

    public async Task<List<EmployeeDocument>>
        GetEmployeeDocumentsAsync(Guid employeeId)
    {
        return await _db.EmployeeDocuments
            .Include(x => x.DocumentType)
            .Where(x =>
                x.EmployeeId == employeeId &&
                x.IsLatestVersion)
            .OrderBy(x => x.DocumentType!.Name)
            .ToListAsync();
    }

    public async Task<EmployeeDocument?>
        GetDocumentAsync(Guid documentId)
    {
        return await _db.EmployeeDocuments
            .Include(x => x.DocumentType)
            .Include(x => x.Employee)
            .FirstOrDefaultAsync(x =>
                x.DocumentId == documentId);
    }

    public async Task<bool> DeleteDocumentAsync(
        Guid documentId,
        string userId)
    {
        var document =
            await _db.EmployeeDocuments
            .FirstOrDefaultAsync(x =>
                x.DocumentId == documentId);

        if (document == null)
            return false;

        document.IsDeleted = true;
        document.DeletedAt = DateTime.UtcNow;
        document.DeletedByUserId = userId;

        await _db.SaveChangesAsync();
        await _historyService.LogAsync(
    document,
    DocumentAction.Deleted,
    userId,
    "System");
        return true;
    }

    public async Task<List<DocumentType>>
        GetMissingMandatoryDocumentsAsync(
            Guid employeeId,
            Guid organizationId)
    {
        var uploadedDocumentTypes =
            await _db.EmployeeDocuments
            .Where(x =>
                x.EmployeeId == employeeId &&
                x.IsLatestVersion)
            .Select(x => x.DocumentTypeId)
            .ToListAsync();

        return await _db.DocumentTypes
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.IsMandatory &&
                x.IsActive &&
                !uploadedDocumentTypes.Contains(
                    x.DocumentTypeId))
            .ToListAsync();
    }

    public async Task<int?>
     GetProfileCompletionPercentageAsync(
         Guid employeeId,
         Guid organizationId)
    {
        int totalRequired =
            await _db.DocumentTypes
            .CountAsync(x =>
                x.OrganizationId == organizationId &&
                x.IsMandatory &&
                x.IsActive);

        if (totalRequired == 0)
            return null;  // CHANGE THIS: Return null instead of 100

        int uploaded =
            await _db.EmployeeDocuments
            .CountAsync(x =>
                x.EmployeeId == employeeId &&
                x.IsLatestVersion &&
                x.DocumentType!.IsMandatory);

        return (int)Math.Round(
            (uploaded * 100.0) /
            totalRequired);
    }

    // Add these methods to EmployeeDocumentService class

    public async Task<EmployeeDocumentDashboardDto> GetEmployeeDocumentDashboardAsync(
        Guid employeeId,
        Guid organizationId)
    {
        // Get all active document types for the organization
        var allDocumentTypes = await _db.DocumentTypes
            .Where(x => x.OrganizationId == organizationId && x.IsActive)
            .ToListAsync();

        var totalRequired = allDocumentTypes.Count(x => x.IsMandatory);

        // Get uploaded documents (latest versions only)
        var uploadedDocs = await _db.EmployeeDocuments
            .Include(x => x.DocumentType)
            .Where(x => x.EmployeeId == employeeId
                        && x.IsLatestVersion
                        && !x.IsDeleted)
            .ToListAsync();

        var uploadedDocTypeIds = uploadedDocs.Select(x => x.DocumentTypeId).ToHashSet();

        // Calculate stats
        var uploadedCount = uploadedDocs.Count;
        var verifiedCount = uploadedDocs.Count(x => x.Status == DocumentStatus.Verified);
        var pendingCount = uploadedDocs.Count(x => x.Status == DocumentStatus.Pending);
        var rejectedCount = uploadedDocs.Count(x => x.Status == DocumentStatus.Rejected);

        // Find missing mandatory documents
        var missingMandatory = allDocumentTypes
            .Where(x => x.IsMandatory && !uploadedDocTypeIds.Contains(x.DocumentTypeId))
            .ToList();

        // Calculate profile completion
        // Calculate profile completion
        int? completionPercentage = null;  // Change to nullable
        if (totalRequired > 0)
        {
            completionPercentage = (int)Math.Round((verifiedCount * 100.0) / totalRequired);
        }
        // If totalRequired == 0, keep completionPercentage as null (not 100)

        // Build document view DTOs
        // Update the GetEmployeeDocumentDashboardAsync method - specifically the part where you create documentViews
        // Find this section in the method and replace it:

        // ==========================================
        // UPDATED SECTION STARTS HERE
        // ==========================================
        var documentViews = new List<EmployeeDocumentViewDto>();

        // 1. Process all document types configured in the system
        foreach (var docType in allDocumentTypes)
        {
            var uploadedDoc = uploadedDocs.FirstOrDefault(x => x.DocumentTypeId == docType.DocumentTypeId);

            if (uploadedDoc != null)
            {
                var verifiedByUser = uploadedDoc.VerifiedByUserId != null
                    ? await _db.Users.FindAsync(uploadedDoc.VerifiedByUserId)
                    : null;

                documentViews.Add(new EmployeeDocumentViewDto
                {
                    DocumentId = uploadedDoc.DocumentId,
                    DocumentTypeId = docType.DocumentTypeId,
                    DocumentTypeName = docType.Name,
                    Category = docType.Category,
                    IsMandatory = docType.IsMandatory,
                    RequiresVerification = docType.RequiresVerification,
                    TrackExpiry = docType.TrackExpiry,
                    AllowedFileTypes = docType.AllowedFileTypes,
                    MaxFileSizeMB = docType.MaxFileSizeMB,
                    OriginalFileName = uploadedDoc.OriginalFileName,
                    FilePath = uploadedDoc.FilePath,
                    Status = uploadedDoc.Status.ToString(), // Converted explicitly to string for UI comparison rules
                    RejectionRemarks = uploadedDoc.RejectionRemarks,
                    Version = uploadedDoc.Version,
                    ExpiryDate = uploadedDoc.ExpiryDate,
                    UploadedAt = uploadedDoc.UploadedAt,
                    VerifiedAt = uploadedDoc.VerifiedAt,
                    VerifiedByName = verifiedByUser != null ? $"{verifiedByUser.FirstName} {verifiedByUser.LastName}" : null
                });
            }
            else
            {
                documentViews.Add(new EmployeeDocumentViewDto
                {
                    DocumentTypeId = docType.DocumentTypeId,
                    DocumentTypeName = docType.Name,
                    Category = docType.Category,
                    IsMandatory = docType.IsMandatory,
                    RequiresVerification = docType.RequiresVerification,
                    TrackExpiry = docType.TrackExpiry,
                    AllowedFileTypes = docType.AllowedFileTypes,
                    MaxFileSizeMB = docType.MaxFileSizeMB,
                    Status = "NotUploaded"
                });
            }
        }

        // 2. CRITICAL FIX: Capture uploaded documents whose DocumentTypes might be inactive or missing from 'allDocumentTypes'
        var processedDocTypeIds = documentViews.Select(v => v.DocumentTypeId).ToHashSet();
        var orphanedDocs = uploadedDocs.Where(d => !processedDocTypeIds.Contains(d.DocumentTypeId)).ToList();

        foreach (var uploadedDoc in orphanedDocs)
        {
            var verifiedByUser = uploadedDoc.VerifiedByUserId != null
                ? await _db.Users.FindAsync(uploadedDoc.VerifiedByUserId)
                : null;

            documentViews.Add(new EmployeeDocumentViewDto
            {
                DocumentId = uploadedDoc.DocumentId,
                DocumentTypeId = uploadedDoc.DocumentTypeId,
                DocumentTypeName = uploadedDoc.DocumentType?.Name ?? "Unknown / Legacy Document",
                Category = uploadedDoc.DocumentType?.Category ?? "Other",
                IsMandatory = false,
                RequiresVerification = uploadedDoc.DocumentType?.RequiresVerification ?? false,
                TrackExpiry = uploadedDoc.DocumentType?.TrackExpiry ?? false,
                OriginalFileName = uploadedDoc.OriginalFileName,
                FilePath = uploadedDoc.FilePath,
                Status = uploadedDoc.Status.ToString(),
                Version = uploadedDoc.Version,
                UploadedAt = uploadedDoc.UploadedAt,
                VerifiedAt = uploadedDoc.VerifiedAt,
                VerifiedByName = verifiedByUser != null ? $"{verifiedByUser.FirstName} {verifiedByUser.LastName}" : null
            });
        }
        // ==========================================
        // UPDATED SECTION ENDS HERE
        // ==========================================

        return new EmployeeDocumentDashboardDto
        {
            ProfileCompletionPercentage = completionPercentage,
            TotalRequiredDocuments = totalRequired,
            UploadedDocuments = uploadedCount,
            VerifiedDocuments = verifiedCount,
            PendingDocuments = pendingCount,
            RejectedDocuments = rejectedCount,
            MissingMandatoryCount = missingMandatory.Count,
            MissingMandatoryDocuments = missingMandatory,
            Documents = documentViews.OrderBy(x => x.IsMandatory ? 0 : 1)
                               .ThenBy(x => x.DocumentTypeName)
                               .ToList()
        };
    }

    public async Task<List<EmployeeDocumentViewDto>> GetEmployeeDocumentsWithTypesAsync(
        Guid employeeId,
        Guid organizationId)
    {
        var dashboard = await GetEmployeeDocumentDashboardAsync(employeeId, organizationId);
        return dashboard.Documents;
    }

    public async Task<bool> CanEmployeeUploadForTypeAsync(
        Guid employeeId,
        Guid documentTypeId,
        Guid organizationId)
    {
        var documentType = await _db.DocumentTypes
            .FirstOrDefaultAsync(x => x.DocumentTypeId == documentTypeId
                                      && x.OrganizationId == organizationId
                                      && x.IsActive);

        if (documentType == null)
            return false;

        return documentType.IsEmployeeUploadAllowed;
    }

    // Add to EmployeeDocumentService.cs
public async Task<List<EmployeeDocument>> GetAllEmployeeDocumentsWithHistoryAsync(Guid employeeId)
{
    return await _db.EmployeeDocuments
        .Include(x => x.DocumentType)
        .Include(x => x.PreviousDocument)
        .Where(x => x.EmployeeId == employeeId && !x.IsDeleted)
        .OrderByDescending(x => x.UploadedAt)
        .ToListAsync();
}
    public async Task<CompanyComplianceDto> GetCompanyComplianceDashboardAsync(Guid organizationId)
    {
        var dto = new CompanyComplianceDto();

        // 1. Fetch system baselines
        var activeDocumentTypes = await _db.DocumentTypes
            .Where(x => x.OrganizationId == organizationId && x.IsActive)
            .ToListAsync();

        var mandatoryTypes = activeDocumentTypes.Where(t => t.IsMandatory).ToList();

        dto.IsConfigured = mandatoryTypes.Any();
        // Fetch active employees belonging to the company context
        // FIXED: Added .Include(x => x.Department) to safely access department details
        var employees = await _db.Employees
            .Include(x => x.Department)
            .Where(x => x.OrganizationId == organizationId)
            .ToListAsync();

        if (!employees.Any())
        {
            dto.OverallCompliancePercentage = 100;
            dto.IsConfigured = false; // Cannot be configured with 0 employees
            return dto;
        }

        dto.TotalActiveEmployees = employees.Count;

        // 2. Fetch tracking information
        var allLatestDocuments = await _db.EmployeeDocuments
            .Include(d => d.DocumentType)
            .Where(x => x.OrganizationId == organizationId && x.IsLatestVersion && !x.IsDeleted)
            .ToListAsync();

        // Extract records pending validation across teams
        dto.PendingVerifications = allLatestDocuments
            .Where(d => d.Status == DocumentStatus.Pending)
            .Select(d => {
                var emp = employees.FirstOrDefault(e => e.EmployeeId == d.EmployeeId);
                // FIXED: Concatenated FirstName and LastName to replace non-existent FullName property
                var empName = emp != null ? $"{emp.FirstName} {emp.LastName}".Trim() : "Unknown";

                return new PendingVerificationSummaryDto
                {
                    DocumentId = d.DocumentId,
                    EmployeeId = d.EmployeeId,
                    EmployeeName = empName,
                    DocumentTypeName = d.DocumentType?.Name ?? "Custom Document",
                    UploadedAt = d.UploadedAt
                };
            })
            .OrderByDescending(x => x.UploadedAt)
            .ToList();

        //dto.TotalPendingVerificationsCount = dto.PendingVerifications.Count;
        dto.TotalExpiredDocumentsCount = allLatestDocuments.Count(d => d.ExpiryDate.HasValue && d.ExpiryDate.Value < DateOnly.FromDateTime(DateTime.UtcNow));

        // Working with DateOnly timeframes directly to resolve compile mismatches
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var trackingHorizon = today.AddDays(30);

        dto.ExpiringDocuments = allLatestDocuments
            .Where(d => d.ExpiryDate.HasValue && d.ExpiryDate.Value <= trackingHorizon && d.ExpiryDate.Value >= today)
            .Select(d => {
                var emp = employees.FirstOrDefault(e => e.EmployeeId == d.EmployeeId);
                // FIXED: Concatenated FirstName and LastName to replace non-existent FullName property
                var empName = emp != null ? $"{emp.FirstName} {emp.LastName}".Trim() : "Unknown";

                return new ExpiringDocumentSummaryDto
                {
                    DocumentId = d.DocumentId,
                    EmployeeId = d.EmployeeId,
                    EmployeeName = empName,
                    DocumentTypeName = d.DocumentType?.Name ?? "Custom Document",
                    ExpiryDate = d.ExpiryDate.Value.ToDateTime(TimeOnly.MinValue),
                    DaysRemaining = d.ExpiryDate.Value.DayNumber - today.DayNumber
                };
            })
            .OrderBy(x => x.DaysRemaining)
            .ToList();

        dto.TotalExpiredDocumentsCount = allLatestDocuments.Count(d => d.ExpiryDate.HasValue && d.ExpiryDate.Value < today);

        // 3. Evaluate Individual Employee Vaults for Strict Mandatory Gaps
        foreach (var employee in employees)
        {
            var empDocs = allLatestDocuments.Where(d => d.EmployeeId == employee.EmployeeId).ToList();

            // Count verified files mapped against system rules
            var verifiedTypeIds = empDocs
                .Where(d => d.Status == DocumentStatus.Verified)
                .Select(d => d.DocumentTypeId)
                .ToHashSet();

            var missingMandatoryTypes = mandatoryTypes
                .Where(t => !verifiedTypeIds.Contains(t.DocumentTypeId))
                .ToList();

            if (missingMandatoryTypes.Any())
            {
                // FIXED: Explicitly extracted combined names and nested Department relation string values
                var fullName = $"{employee.FirstName} {employee.LastName}".Trim();
                var departmentName = employee.Department?.Name ?? "Unassigned";

                dto.NonCompliantEmployees.Add(new EmployeeComplianceSummaryDto
                {
                    EmployeeId = employee.EmployeeId,
                    EmployeeCode = employee.EmployeeCode ?? string.Empty,
                    FullName = fullName,
                    DepartmentName = departmentName,
                    MissingMandatoryCount = missingMandatoryTypes.Count,
                    MissingDocumentTypeNames = missingMandatoryTypes.Select(t => t.Name).ToList()
                });
            }
        }

        dto.NonCompliantEmployeesCount = dto.NonCompliantEmployees.Count;
        dto.FullyCompliantEmployeesCount = dto.TotalActiveEmployees - dto.NonCompliantEmployeesCount;

        dto.OverallCompliancePercentage = (int)Math.Round(
            (dto.FullyCompliantEmployeesCount * 100.0) / dto.TotalActiveEmployees);

        // 4. Group Aggregation Breakdown By Department
        // FIXED: Safely grouped across nested reference properties to prevent errors
        var deptGroups = employees.GroupBy(e => e.Department?.Name ?? "General / Unassigned");
        foreach (var grp in deptGroups)
        {
            var totalInDept = grp.Count();
            var nonCompliantInDept = dto.NonCompliantEmployees.Count(nce => grp.Any(e => e.EmployeeId == nce.EmployeeId));
            var compliantInDept = totalInDept - nonCompliantInDept;

            dto.DepartmentStats.Add(new DepartmentComplianceDto
            {
                DepartmentName = grp.Key,
                TotalEmployees = totalInDept,
                CompliantEmployees = compliantInDept,
                CompliancePercentage = totalInDept > 0 ? (int)Math.Round((compliantInDept * 100.0) / totalInDept) : 100
            });
        }

        return dto;
    }



}