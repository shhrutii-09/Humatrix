// Infrastructure/Services/AssetAnalyticsService.cs
using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs.Asset;
using Humatrix_HRMS.Infrastructure.Constants;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Infrastructure.Services
{
    /// <summary>
    /// Extended analytics service that includes HrProcurementRequests,
    /// EmployeeAssetRequests, and Reserved status in all counts.
    /// </summary>
    public class AssetAnalyticsService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

        public AssetAnalyticsService(IDbContextFactory<ApplicationDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<AssetAnalyticsExtendedDto> GetExtendedAnalyticsAsync(
            Guid organizationId,
            Guid? departmentId = null,
            string? actorRole = null,
            Guid? actorDepartmentId = null)
        {
            // HR is always scoped to their own department.
            if (actorRole == "HR" && actorDepartmentId.HasValue)
                departmentId = actorDepartmentId;

            using var db = await _dbFactory.CreateDbContextAsync();

            // ── Assets ──────────────────────────────────────────────────────
            var assetQuery = db.Assets
                .AsNoTracking()
                .Where(a => a.OrganizationId == organizationId && !a.IsDeleted);

            if (departmentId.HasValue)
                assetQuery = assetQuery.Where(a => a.DepartmentId == departmentId.Value);

            var assets = await assetQuery
                .Select(a => new
                {
                    a.AssetId,
                    a.Status,
                    a.Category,
                    a.DepartmentId,
                    a.PurchaseCost,
                    a.WarrantyExpiry,
                    DeptName = a.Department != null ? a.Department.Name : null
                })
                .ToListAsync();

            var now = DateTime.UtcNow;

            // ── Legacy AssetRequests (pending) ───────────────────────────────
            var legacyPendingQuery = db.AssetRequests
                .Where(r =>
                    r.OrganizationId == organizationId &&
                    r.Status == AssetRequestStatuses.Pending);

            if (departmentId.HasValue)
                legacyPendingQuery = legacyPendingQuery
                    .Where(r => r.Employee!.DepartmentId == departmentId.Value);

            var legacyPending = await legacyPendingQuery.CountAsync();

            // ── Employee Asset Requests (pending) ────────────────────────────
            var empRequestQuery = db.EmployeeAssetRequests
                .Where(r =>
                    r.OrganizationId == organizationId &&
                    (r.Status == EmployeeAssetRequestStatuses.Pending ||
                     r.Status == EmployeeAssetRequestStatuses.UnderReview));

            if (departmentId.HasValue)
                empRequestQuery = empRequestQuery
                    .Where(r => r.Employee!.DepartmentId == departmentId.Value);

            var empRequestCounts = await empRequestQuery
                .GroupBy(r => r.RequestType)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync();

            int openRepair = empRequestCounts.FirstOrDefault(x => x.Key == EmployeeAssetRequestTypes.RepairRequest)?.Count ?? 0;
            int openReturn = empRequestCounts.FirstOrDefault(x => x.Key == EmployeeAssetRequestTypes.ReturnRequest)?.Count ?? 0;
            int openReplacement = empRequestCounts.FirstOrDefault(x => x.Key == EmployeeAssetRequestTypes.ReplacementRequest)?.Count ?? 0;
            int totalEmpRequests = empRequestCounts.Sum(x => x.Count);

            // ── HR Procurement Requests (pending) ────────────────────────────
            var procurementQuery = db.HrProcurementRequests
                .Where(r =>
                    r.OrganizationId == organizationId &&
                    (r.Status == HrProcurementStatuses.Pending ||
                     r.Status == HrProcurementStatuses.Approved ||
                     r.Status == HrProcurementStatuses.PartiallyFulfilled));

            if (departmentId.HasValue)
                procurementQuery = procurementQuery.Where(r => r.DepartmentId == departmentId.Value);

            var procurementPending = await procurementQuery.CountAsync();

            // ── Recent Procurement Summary ───────────────────────────────────
            var recentProcurements = await db.HrProcurementRequests
                .AsNoTracking()
                .Where(r => r.OrganizationId == organizationId)
                .OrderByDescending(r => r.CreatedAt)
                .Take(5)
                .Select(r => new HrProcurementSummaryDto
                {
                    ProcurementRequestId = r.ProcurementRequestId,
                    DepartmentName = r.Department != null ? r.Department.Name : string.Empty,
                    AssetCategory = r.AssetCategory,
                    QuantityRequested = r.QuantityRequested,
                    QuantityFulfilled = r.QuantityFulfilled,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt
                })
                .ToListAsync();

            // ── Recently Assigned Assets ─────────────────────────────────────
            var recentlyAssigned = await db.AssetAssignmentHistories
                .AsNoTracking()
                .Where(h =>
                    h.Asset!.OrganizationId == organizationId &&
                    !h.Asset.IsDeleted &&
                    h.ReturnedAt == null)
                .OrderByDescending(h => h.AssignedAt)
                .Take(10)
                .Select(h => new AssetDto
                {
                    AssetId = h.Asset!.AssetId,
                    AssetCode = h.Asset.AssetCode,
                    AssetName = h.Asset.AssetName,
                    Category = h.Asset.Category,
                    Status = h.Asset.Status,
                    CurrentEmployeeId = h.EmployeeId,
                    CurrentEmployeeName = h.Employee != null
                        ? $"{h.Employee.FirstName} {h.Employee.LastName}" : null,
                    CurrentEmployeeCode = h.Employee != null ? h.Employee.EmployeeCode : null,
                    DepartmentName = h.Asset.Department != null ? h.Asset.Department.Name : null,
                    CreatedAt = h.Asset.CreatedAt
                })
                .ToListAsync();

            return new AssetAnalyticsExtendedDto
            {
                // Base counts
                TotalAssets = assets.Count,
                Available = assets.Count(a => a.Status == AssetStatuses.Available),
                Assigned = assets.Count(a => a.Status == AssetStatuses.Assigned),
                Reserved = assets.Count(a => a.Status == AssetStatuses.Reserved),
                InRepair = assets.Count(a => a.Status == AssetStatuses.InRepair),
                Lost = assets.Count(a => a.Status == AssetStatuses.Lost),
                Retired = assets.Count(a => a.Status == AssetStatuses.Retired),
                Disposed = assets.Count(a => a.Status == AssetStatuses.Disposed),
                TotalPurchaseCost = assets.Sum(a => a.PurchaseCost ?? 0),

                WarrantyExpiringSoon = assets.Count(a =>
                    a.WarrantyExpiry.HasValue &&
                    a.WarrantyExpiry.Value > now &&
                    a.WarrantyExpiry.Value <= now.AddDays(30)),
                WarrantyExpired = assets.Count(a =>
                    a.WarrantyExpiry.HasValue && a.WarrantyExpiry.Value < now),

                // Request counts
                PendingRequests = legacyPending,
                PendingEmployeeRequests = totalEmpRequests,
                PendingProcurementRequests = procurementPending,
                OpenRepairRequests = openRepair,
                OpenReturnRequests = openReturn,
                OpenReplacementRequests = openReplacement,

                // Breakdowns
                ByCategory = assets
                    .GroupBy(a => a.Category)
                    .Select(g => new AssetCategoryBreakdownDto
                    {
                        Category = g.Key,
                        Count = g.Count(),
                        TotalValue = g.Sum(x => x.PurchaseCost ?? 0)
                    })
                    .OrderByDescending(g => g.Count)
                    .ToList(),

                ByDepartment = assets
                    .GroupBy(a => new { a.DepartmentId, a.DeptName })
                    .Select(g => new AssetDepartmentBreakdownDto
                    {
                        DepartmentId = g.Key.DepartmentId,
                        DepartmentName = g.Key.DeptName ?? "Unallocated",
                        Count = g.Count(),
                        Assigned = g.Count(x => x.Status == AssetStatuses.Assigned),
                        Available = g.Count(x => x.Status == AssetStatuses.Available)
                    })
                    .OrderByDescending(g => g.Count)
                    .ToList(),

                RecentlyAssigned = recentlyAssigned,
                RecentProcurements = recentProcurements
            };
        }
    }
}
