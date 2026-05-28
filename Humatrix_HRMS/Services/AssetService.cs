// Infrastructure/Services/AssetService.cs
using Humanizer;
using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.DTOs.Asset;
//using Humatrix_HRMS.DTOs.Asset;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Infrastructure.Constants;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;


namespace Humatrix_HRMS.Infrastructure.Services
{
    public class AssetService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        private readonly ActivityLogService _activityLog;

        public AssetService(
            IDbContextFactory<ApplicationDbContext> dbFactory,
            ActivityLogService activityLog)
        {
            _dbFactory = dbFactory;
            _activityLog = activityLog;
        }

        // ─────────────────────────────────────────────────────────────────────
        // CREATE
        // ─────────────────────────────────────────────────────────────────────

        public async Task<AssetDto> CreateAssetAsync(
            Guid organizationId,
            CreateAssetDto dto,
            string userId,
            string actorRole,
            Guid actorEmployeeId,
            Guid? actorDepartmentId = null)
        {
            if (actorRole == SystemRoles.HR)
            {
                if (!actorDepartmentId.HasValue)
                    throw new UnauthorizedAccessException(
                        "HR configuration error: no department associated with this actor.");

                if (dto.DepartmentId != actorDepartmentId.Value)
                    throw new UnauthorizedAccessException(
                        "HR can only create assets within their own department.");
            }

            using var db = await _dbFactory.CreateDbContextAsync();

            if (!string.IsNullOrWhiteSpace(dto.SerialNumber))
            {
                var serialExists = await db.Assets.AnyAsync(a =>
                    a.OrganizationId == organizationId &&
                    !a.IsDeleted &&
                    a.SerialNumber == dto.SerialNumber.Trim());

                if (serialExists)
                    throw new InvalidOperationException(
                        "An asset with this serial number already exists.");
            }

            // Retry loop guards against the rare AssetCode unique-constraint race.
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                //using var tx = await db.Database.BeginTransactionAsync();
                try
                {
                    var assetCode = await GenerateAssetCodeAsync(db, organizationId);

                    var asset = new Asset
                    {
                        OrganizationId = organizationId,
                        DepartmentId = dto.DepartmentId,
                        AssetCode = assetCode,
                        AssetName = dto.AssetName.Trim(),
                        Category = dto.Category,
                        Brand = dto.Brand?.Trim(),
                        Model = dto.Model?.Trim(),
                        SerialNumber = dto.SerialNumber?.Trim(),
                        PurchaseDate = dto.PurchaseDate,
                        PurchaseCost = dto.PurchaseCost,
                        WarrantyExpiry = dto.WarrantyExpiry,
                        Status = AssetStatuses.Available,
                        Condition = string.IsNullOrWhiteSpace(dto.Condition)
                                            ? AssetConditions.Good
                                            : dto.Condition.Trim(),
                        Notes = dto.Notes?.Trim(),
                        CreatedByUserId = userId,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    db.Assets.Add(asset);
                    await db.SaveChangesAsync();
                    //await tx.CommitAsync();

                    await _activityLog.LogAsync(
                        organizationId, "Asset", "Created", "Asset", asset.AssetId,
                        userId, actorRole,
                        newValues: new
                        {
                            asset.AssetCode,
                            asset.AssetName,
                            asset.Category,
                            asset.DepartmentId
                        });

                    return await GetAssetDtoAsync(db, asset.AssetId);
                }
                catch (DbUpdateException ex)
                    when (IsUniqueConstraintViolation(ex) && attempt < 5)
                {
                    //await tx.RollbackAsync();
                    // duplicate AssetCode — regenerate on next iteration
                }
                catch
                {
                    //await tx.RollbackAsync();
                    throw;
                }
            }

            throw new InvalidOperationException(
                "Asset creation failed after multiple retry attempts. Please try again.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // UPDATE
        // ─────────────────────────────────────────────────────────────────────

        public async Task UpdateAssetAsync(
            Guid organizationId,
            UpdateAssetDto dto,
            string userId,
            string actorRole,
            Guid? actorDepartmentId = null)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var asset = await GetTrackedAssetAsync(
        db,
        dto.AssetId,
        organizationId);

            EnforceHrDepartmentScope(actorRole, actorDepartmentId, asset.DepartmentId);

            if (dto.RowVersion != null)
            {
                db.Entry(asset).Property(x => x.RowVersion).OriginalValue = dto.RowVersion;
            }

            if (string.IsNullOrWhiteSpace(dto.AssetName))
                throw new ArgumentException("Asset name is required.");

            if (dto.PurchaseCost < 0)
                throw new ArgumentException("Purchase cost cannot be negative.");

            // Department change guard — only OrgAdmin can reclassify; and not while assigned.
            if (actorRole == "OrgAdmin")
            {
                if (asset.Status == AssetStatuses.Assigned &&
                    asset.DepartmentId != dto.DepartmentId)
                {
                    throw new InvalidOperationException(
                        "Cannot change department of an assigned asset.");
                }

                asset.DepartmentId = dto.DepartmentId;
            }
            if (!string.IsNullOrWhiteSpace(dto.SerialNumber))
            {
                var serialExists = await db.Assets.AnyAsync(a =>
                    a.AssetId != dto.AssetId &&
                    a.OrganizationId == organizationId &&
                    !a.IsDeleted &&
                    a.SerialNumber == dto.SerialNumber.Trim());

                if (serialExists)
                {
                    throw new InvalidOperationException(
                        "Another asset already uses this serial number.");
                }
            }
            var old = new
            {
                asset.AssetName,
                asset.Category,
                asset.Brand,
                asset.Model,
                asset.SerialNumber,
                asset.PurchaseCost,
                asset.WarrantyExpiry,
                asset.Condition,
                asset.Notes,
                asset.DepartmentId
            };

            asset.AssetName = dto.AssetName.Trim();
            asset.Category = dto.Category;
            asset.Brand = dto.Brand?.Trim();
            asset.Model = dto.Model?.Trim();
            asset.SerialNumber = dto.SerialNumber?.Trim();
            asset.PurchaseDate = dto.PurchaseDate;
            asset.PurchaseCost = dto.PurchaseCost;
            asset.WarrantyExpiry = dto.WarrantyExpiry;
            asset.Condition = string.IsNullOrWhiteSpace(dto.Condition)
                                    ? asset.Condition
                                    : dto.Condition.Trim();
            asset.Notes = dto.Notes?.Trim();
            asset.UpdatedAt = DateTime.UtcNow;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException(
                    "This asset was modified by another user. Please refresh and try again.");
            }

            await _activityLog.LogAsync(
                organizationId, "Asset", "Updated", "Asset", asset.AssetId,
                userId, actorRole,
                oldValues: old,
                newValues: new
                {
                    asset.AssetName,
                    asset.Category,
                    asset.Brand,
                    asset.Model,
                    asset.SerialNumber,
                    asset.PurchaseCost,
                    asset.WarrantyExpiry,
                    asset.Condition,
                    asset.Notes,
                    asset.DepartmentId
                });
        }

        // ─────────────────────────────────────────────────────────────────────
        // SOFT DELETE
        // ─────────────────────────────────────────────────────────────────────

        public async Task DeleteAssetAsync(
            Guid organizationId,
            Guid assetId,
            string actorUserId,
            string actorRole)
        {
            if (actorRole != SystemRoles.OrgAdmin)
                throw new UnauthorizedAccessException("Only OrgAdmin can delete assets.");

            using var db = await _dbFactory.CreateDbContextAsync();

            var asset = await GetTrackedAssetAsync(
     db,
     assetId,
     organizationId);

            if (asset.Status == AssetStatuses.Assigned)
                throw new InvalidOperationException(
                    "Cannot delete an assigned asset. Return it first.");

            var hasOpenHistory = await db.AssetAssignmentHistories
                .AnyAsync(h => h.AssetId == assetId && h.ReturnedAt == null);

            if (hasOpenHistory)
                throw new InvalidOperationException(
                    "Cannot delete an asset with an open assignment record.");

            // Soft delete — preserves full audit trail.
            asset.IsDeleted = true;
            asset.DeletedAt = DateTime.UtcNow;
            asset.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();

            await _activityLog.LogAsync(
                organizationId, "Asset", "Deleted", "Asset", assetId,
                actorUserId, actorRole,
                oldValues: new { asset.AssetCode, asset.AssetName, asset.Status });
        }

        // ─────────────────────────────────────────────────────────────────────
        // RETIRE / DISPOSE
        // ─────────────────────────────────────────────────────────────────────

        public async Task RetireOrDisposeAssetAsync(
            Guid organizationId,
            RetireDisposeAssetDto dto,
            string actorUserId,
            string actorRole)
        {
            if (dto.NewStatus != AssetStatuses.Retired &&
                dto.NewStatus != AssetStatuses.Disposed)
            {
                throw new ArgumentException(
                    "NewStatus must be 'Retired' or 'Disposed'.");
            }

            using var db = await _dbFactory.CreateDbContextAsync();

            var asset = await GetTrackedAssetAsync(
      db,
      dto.AssetId,
      organizationId);

            if (asset.Status == AssetStatuses.Assigned)
                throw new InvalidOperationException(
                    "Return the asset before retiring or disposing it.");

            var hasOpenHistory = await db.AssetAssignmentHistories
                .AnyAsync(h => h.AssetId == asset.AssetId && h.ReturnedAt == null);

            if (hasOpenHistory)
                throw new InvalidOperationException(
                    "Cannot retire/dispose an asset with an open assignment record.");

            AssetStatusValidator.Enforce(asset.Status, dto.NewStatus);

            var oldStatus = asset.Status;

            asset.Status = dto.NewStatus;
            asset.UpdatedAt = DateTime.UtcNow;
            asset.Notes = AssetNotesHelper.Append(
      asset.Notes,
      dto.NewStatus,
      dto.Reason);

            await db.SaveChangesAsync();

            await _activityLog.LogAsync(
                organizationId, "Asset", dto.NewStatus, "Asset", asset.AssetId,
                actorUserId, actorRole,
                oldValues: new { Status = oldStatus },
                newValues: new { Status = dto.NewStatus, dto.Reason });
        }

        // ─────────────────────────────────────────────────────────────────────
        // REPAIR LIFECYCLE
        // ─────────────────────────────────────────────────────────────────────

        public async Task MarkInRepairAsync(
            Guid organizationId,
            MarkAssetRepairDto dto,
            string actorUserId,
            string actorRole,
            Guid? actorDepartmentId = null)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var asset = await GetTrackedAssetAsync(
        db,
        dto.AssetId,
        organizationId);

            EnforceHrDepartmentScope(actorRole, actorDepartmentId, asset.DepartmentId);

            //if (asset.Status == AssetStatuses.Assigned)
            //    throw new InvalidOperationException(
            //        "Assigned assets must be returned before being sent to repair.");

            //AssetStatusValidator.Enforce(asset.Status, AssetStatuses.InRepair);
            var hasOpenAssignment = await db.AssetAssignmentHistories
    .AnyAsync(h =>
        h.AssetId == asset.AssetId &&
        h.ReturnedAt == null);

            AssetTransitionEngine.Validate(
                asset.Status,
                AssetStatuses.InRepair,
                hasOpenAssignment);
            if (dto.RowVersion != null)
                db.Entry(asset).Property(a => a.RowVersion).OriginalValue = dto.RowVersion;

            var oldStatus = asset.Status;

            asset.Status = AssetStatuses.InRepair;
            asset.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(dto.RepairReason))
            {
                asset.Notes = AssetNotesHelper.Append(
     asset.Notes,
     AssetNoteTags.Repair,
     dto.RepairReason);
            }

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException(
                    "Asset was modified by another user. Please refresh and try again.");
            }

            await _activityLog.LogAsync(
                organizationId, "Asset", "MarkedInRepair", "Asset", asset.AssetId,
                actorUserId, actorRole,
                oldValues: new { Status = oldStatus },
                newValues: new { Status = asset.Status, dto.RepairReason });
        }

        public async Task MarkRepairCompletedAsync(
            Guid organizationId,
            CompleteAssetRepairDto dto,
            string actorUserId,
            string actorRole,
            Guid? actorDepartmentId = null)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var asset = await GetTrackedAssetAsync(
    db,
    dto.AssetId,
    organizationId);

            EnforceHrDepartmentScope(actorRole, actorDepartmentId, asset.DepartmentId);

            AssetStatusValidator.Enforce(asset.Status, AssetStatuses.Available);

            if (dto.RowVersion != null)
                db.Entry(asset).Property(a => a.RowVersion).OriginalValue = dto.RowVersion;

            var oldStatus = asset.Status;

            asset.Status = AssetStatuses.Available;
            asset.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(dto.FinalCondition))
                asset.Condition = dto.FinalCondition.Trim();

            if (!string.IsNullOrWhiteSpace(dto.RepairNotes))
            {
                asset.Notes = AssetNotesHelper.Append(
      asset.Notes,
      AssetNoteTags.RepairCompleted,
      dto.RepairNotes);
            }

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException(
                    "Asset was modified by another user. Please refresh and try again.");
            }

            await _activityLog.LogAsync(
                organizationId, "Asset", "RepairCompleted", "Asset", asset.AssetId,
                actorUserId, actorRole,
                oldValues: new { Status = oldStatus },
                newValues: new { Status = asset.Status, dto.FinalCondition });
        }

        // ─────────────────────────────────────────────────────────────────────
        // LOST / RECOVERED
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Marks an asset as Lost. If the asset is currently Assigned, the active
        /// assignment is automatically closed before the status transitions.
        /// </summary>
        public async Task ReportLostAsync(
            Guid organizationId,
            ReportAssetLostDto dto,
            string actorUserId,
            string actorRole,
            Guid actorEmployeeId,
            Guid? actorDepartmentId = null)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            using var tx = await db.Database.BeginTransactionAsync();

            try
            {
                var asset = await GetTrackedAssetAsync(
    db,
    dto.AssetId,
    organizationId);

                EnforceHrDepartmentScope(actorRole, actorDepartmentId, asset.DepartmentId);

                AssetStatusValidator.Enforce(asset.Status, AssetStatuses.Lost);

                if (dto.RowVersion != null)
                    db.Entry(asset).Property(a => a.RowVersion).OriginalValue = dto.RowVersion;

                var now = DateTime.UtcNow;

                // If the asset was assigned, close the assignment and history records.
                if (asset.Status == AssetStatuses.Assigned)
                {
                    var assignment = await db.AssetAssignments
                        .FirstOrDefaultAsync(a =>
                            a.AssetId == asset.AssetId &&
                            a.ReturnedAt == null);

                    if (assignment != null)
                    {
                        assignment.ReturnedAt = now;
                        assignment.ReturnCondition = "Lost";
                        assignment.Status = AssetAssignmentStatuses.Returned;
                    }

                    var openHistories = await db.AssetAssignmentHistories
                        .Where(h => h.AssetId == asset.AssetId && h.ReturnedAt == null)
                        .ToListAsync();

                    foreach (var h in openHistories)
                    {
                        h.ReturnedAt = now;
                        h.ReturnCondition = "Lost";
                    }

                    asset.CurrentEmployeeId = null;
                }

                asset.Status = AssetStatuses.Lost;
                asset.UpdatedAt = now;
                asset.Notes = AssetNotesHelper.Append(
      asset.Notes,
      AssetNoteTags.Lost,
      dto.LostDescription);

                try
                {
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new InvalidOperationException(
                        "Asset was modified by another user. Please refresh and try again.");
                }

                await _activityLog.LogAsync(
                    organizationId, "Asset", "ReportedLost", "Asset", asset.AssetId,
                    actorUserId, actorRole,
                    newValues: new { dto.LostDescription });
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        /// <summary>Marks a Lost asset as Available (recovered).</summary>
        public async Task RecoverAssetAsync(
            Guid organizationId,
            RecoverAssetDto dto,
            string actorUserId,
            string actorRole,
            Guid? actorDepartmentId = null)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var asset = await GetTrackedAssetAsync(
         db,
         dto.AssetId,
         organizationId);

            EnforceHrDepartmentScope(actorRole, actorDepartmentId, asset.DepartmentId);

            AssetStatusValidator.Enforce(asset.Status, AssetStatuses.Available);

            if (dto.RowVersion != null)
                db.Entry(asset).Property(a => a.RowVersion).OriginalValue = dto.RowVersion;

            asset.Status = AssetStatuses.Available;
            asset.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(dto.Condition))
                asset.Condition = dto.Condition.Trim();

            if (!string.IsNullOrWhiteSpace(dto.RecoveryNotes))
            {
                asset.Notes = AssetNotesHelper.Append(
        asset.Notes,
        AssetNoteTags.Recovered,
        dto.RecoveryNotes);
            }

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException(
                    "Asset was modified by another user. Please refresh and try again.");
            }

            await _activityLog.LogAsync(
                organizationId, "Asset", "Recovered", "Asset", asset.AssetId,
                actorUserId, actorRole,
                newValues: new { dto.RecoveryNotes });
        }

        // ─────────────────────────────────────────────────────────────────────
        // QUERIES
        // ─────────────────────────────────────────────────────────────────────

        public async Task<AssetDto?> GetByIdAsync(
            Guid assetId,
            Guid organizationId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            return await db.Assets
                .AsNoTracking()
                .Where(a =>
                    a.AssetId == assetId &&
                    a.OrganizationId == organizationId &&
                    !a.IsDeleted)
                .Select(AssetDtoSelector())
                .FirstOrDefaultAsync();
        }

        public async Task<PagedResult<AssetDto>> GetAssetsAsync(
            AssetFilterDto filter,
            string actorRole,
            Guid? actorDepartmentId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var q = db.Assets
                .AsNoTracking()
                .Where(a =>
                    a.OrganizationId == filter.OrganizationId &&
                    !a.IsDeleted);

            // Role-based scoping
            if (actorRole == SystemRoles.HR)
            {
                if (!actorDepartmentId.HasValue)
                    throw new UnauthorizedAccessException(
                        "HR account has no department assigned.");

                q = q.Where(a => a.DepartmentId == actorDepartmentId.Value);
            }
            else if (filter.DepartmentId.HasValue)
            {
                q = q.Where(a => a.DepartmentId == filter.DepartmentId.Value);
            }

            // Status filter
            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                q = q.Where(a => a.Status == filter.Status);
            }
            else if (!filter.IncludeInactive)
            {
                q = q.Where(a =>
                    a.Status != AssetStatuses.Retired &&
                    a.Status != AssetStatuses.Disposed);
            }

            // Category filter
            if (!string.IsNullOrWhiteSpace(filter.Category))
                q = q.Where(a => a.Category == filter.Category);

            // Free-text search
            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim();
                q = q.Where(a =>
                    EF.Functions.Like(a.AssetCode, $"%{term}%") ||
                    EF.Functions.Like(a.AssetName, $"%{term}%") ||
                    (a.Brand != null && EF.Functions.Like(a.Brand, $"%{term}%")) ||
                    (a.SerialNumber != null && EF.Functions.Like(a.SerialNumber, $"%{term}%")));
            }

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(a => a.CreatedAt)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(AssetDtoSelector())
                .ToListAsync();

            return new PagedResult<AssetDto>
            {
                Items = items,
                TotalCount = total,
                Page = filter.Page,
                PageSize = filter.PageSize
            };
        }

        /// <summary>
        /// Returns all assets currently assigned to the given employee.
        /// Used by the Employee dashboard "My Assets" view.
        /// </summary>
        public async Task<List<AssetDto>> GetMyAssetsAsync(
            Guid employeeId,
            Guid organizationId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            return await db.Assets
                .AsNoTracking()
                .Where(a =>
                    a.OrganizationId == organizationId &&
                    !a.IsDeleted &&
                    a.CurrentEmployeeId == employeeId &&
                    a.Status == AssetStatuses.Assigned)
                .Select(AssetDtoSelector())
                .ToListAsync();
        }

        /// <summary>
        /// Full assignment history for a single asset.
        /// Accessible by OrgAdmin and the asset's scoped HR.
        /// </summary>
        public async Task<List<AssetAssignmentHistoryDto>> GetAssignmentHistoryAsync(
            Guid assetId,
            Guid organizationId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var exists = await db.Assets.AnyAsync(a =>
                a.AssetId == assetId &&
                a.OrganizationId == organizationId);

            if (!exists)
                throw new KeyNotFoundException("Asset not found.");

            return await db.AssetAssignmentHistories
                .AsNoTracking()
                .Where(h => h.AssetId == assetId)
                .OrderByDescending(h => h.AssignedAt)
                .Select(h => new AssetAssignmentHistoryDto
                {
                    HistoryId = h.HistoryId,
                    AssetId = h.AssetId,
                    AssetCode = h.Asset!.AssetCode,
                    AssetName = h.Asset.AssetName,
                    EmployeeId = h.EmployeeId,
                    EmployeeName = $"{h.Employee!.FirstName} {h.Employee.LastName}",
                    EmployeeCode = h.Employee.EmployeeCode,
                    AssignedByName = h.AssignedByEmployee != null
                        ? $"{h.AssignedByEmployee.FirstName} {h.AssignedByEmployee.LastName}"
                        : "System",
                    AssignedAt = h.AssignedAt,
                    ReturnedAt = h.ReturnedAt,
                    ReturnCondition = h.ReturnCondition,
                    AssignmentNotes = h.AssignmentNotes
                })
                .ToListAsync();
        }

        public async Task<AssetAnalyticsDto> GetAnalyticsAsync(
            Guid organizationId,
            Guid? departmentId = null)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

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

            var pendingRequestsQuery = db.AssetRequests
                .Where(r =>
                    r.OrganizationId == organizationId &&
                    r.Status == AssetRequestStatuses.Pending);

            if (departmentId.HasValue)
            {
                pendingRequestsQuery = pendingRequestsQuery
                    .Where(r => r.Employee!.DepartmentId == departmentId.Value);
            }

            var pendingRequests = await pendingRequestsQuery.CountAsync();

            var dto = new AssetAnalyticsDto
            {
                TotalAssets = assets.Count,
                Available = assets.Count(a => a.Status == AssetStatuses.Available),
                Assigned = assets.Count(a => a.Status == AssetStatuses.Assigned),
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
                PendingRequests = pendingRequests,
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
                    .ToList()
            };

            dto.RecentlyAssigned = await db.AssetAssignmentHistories
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
                    CurrentEmployeeCode = h.Employee != null
                        ? h.Employee.EmployeeCode : null,
                    DepartmentName = h.Asset.Department != null
                        ? h.Asset.Department.Name : null,
                    CreatedAt = h.Asset.CreatedAt
                })
                .ToListAsync();

            return dto;
        }

        public async Task<List<AssetDto>> GetAvailableDepartmentAssetsAsync(
            Guid organizationId,
            Guid departmentId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            return await db.Assets
                .AsNoTracking()
                .Where(a =>
                    a.OrganizationId == organizationId &&
                    !a.IsDeleted &&
                    a.DepartmentId == departmentId &&
                    a.Status == AssetStatuses.Available)
                .OrderBy(a => a.AssetName)
                .Select(AssetDtoSelector())
                .ToListAsync();
        }

        // ─────────────────────────────────────────────────────────────────────
        // PRIVATE HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static async Task<string> GenerateAssetCodeAsync(
            ApplicationDbContext db,
            Guid organizationId)
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"AST-{year}-";

            var lastCode = await db.Assets
                .Where(a =>
                    a.OrganizationId == organizationId &&
                    a.AssetCode.StartsWith(prefix))
                .OrderByDescending(a => a.AssetCode)
                .Select(a => a.AssetCode)
                .FirstOrDefaultAsync();

            var nextNumber = 1;

            if (!string.IsNullOrWhiteSpace(lastCode))
            {
                var numberPart = lastCode[prefix.Length..];
                if (int.TryParse(numberPart, out var parsed))
                    nextNumber = parsed + 1;
            }

            return $"{prefix}{nextNumber:D6}";
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            return msg.Contains("IX_Assets_AssetCode", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reusable projection expression for mapping Asset → AssetDto.
        /// Define it once to avoid duplication across query methods.
        /// </summary>
        private static System.Linq.Expressions.Expression<Func<Asset, AssetDto>> AssetDtoSelector() =>
            a => new AssetDto
            {
                AssetId = a.AssetId,
                OrganizationId = a.OrganizationId,
                DepartmentId = a.DepartmentId,
                DepartmentName = a.Department != null ? a.Department.Name : null,
                AssetCode = a.AssetCode,
                AssetName = a.AssetName,
                Category = a.Category,
                Brand = a.Brand,
                Model = a.Model,
                SerialNumber = a.SerialNumber,
                PurchaseDate = a.PurchaseDate,
                PurchaseCost = a.PurchaseCost,
                WarrantyExpiry = a.WarrantyExpiry,
                Status = a.Status,
                Condition = a.Condition,
                Notes = a.Notes,
                CurrentEmployeeId = a.CurrentEmployeeId,
                CurrentEmployeeName = a.CurrentEmployee != null
                    ? $"{a.CurrentEmployee.FirstName} {a.CurrentEmployee.LastName}" : null,
                CurrentEmployeeCode = a.CurrentEmployee != null
                    ? a.CurrentEmployee.EmployeeCode : null,
                UpdatedAt = a.UpdatedAt,
                CreatedAt = a.CreatedAt,
                RowVersion = a.RowVersion
            };

        private static async Task<AssetDto> GetAssetDtoAsync(
            ApplicationDbContext db,
            Guid assetId)
        {
            return await db.Assets
                .AsNoTracking()
                .Where(a => a.AssetId == assetId)
                .Select(AssetDtoSelector())
                .FirstAsync();
        }

        private static void EnforceHrDepartmentScope(
            string actorRole,
            Guid? actorDepartmentId,
            Guid? assetDepartmentId)
        {
            if (actorRole != "HR") return;

            if (!actorDepartmentId.HasValue)
                throw new UnauthorizedAccessException(
                    "HR account has no department assigned.");

            if (!assetDepartmentId.HasValue)
                throw new UnauthorizedAccessException(
                    "HR cannot manage organisation-wide assets.");

            if (assetDepartmentId.Value != actorDepartmentId.Value)
                throw new UnauthorizedAccessException(
                    "HR can only manage assets assigned to their own department.");
        }

        private static async Task<Asset> GetTrackedAssetAsync(
    ApplicationDbContext db,
    Guid assetId,
    Guid organizationId)
        {
            return await db.Assets
                .FirstOrDefaultAsync(a =>
                    a.AssetId == assetId &&
                    a.OrganizationId == organizationId &&
                    !a.IsDeleted)
                ?? throw new KeyNotFoundException("Asset not found.");
        }
    }
}