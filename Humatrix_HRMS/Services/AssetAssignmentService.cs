// Infrastructure/Services/AssetAssignmentService.cs
using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs.Asset;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Infrastructure.Constants;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Infrastructure.Services
{
    public class AssetAssignmentService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        private readonly NotificationEngine _notifications;
        private readonly ActivityLogService _activityLog;

        public AssetAssignmentService(
            IDbContextFactory<ApplicationDbContext> dbFactory,
            NotificationEngine notifications,
            ActivityLogService activityLog)
        {
            _dbFactory = dbFactory;
            _notifications = notifications;
            _activityLog = activityLog;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ASSIGN
        // ─────────────────────────────────────────────────────────────────────

        public async Task AssignAssetAsync(
            Guid organizationId,
            AssignAssetDto dto,
            string actorUserId,
            string actorRole,
            Guid actorEmployeeId,
            Guid? actorDepartmentId = null)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            using var tx = await db.Database.BeginTransactionAsync();

            try
            {
                // HR cannot assign assets to themselves.
                if (actorRole == "HR" && dto.EmployeeId == actorEmployeeId)
                    throw new UnauthorizedAccessException(
                        "HR cannot assign assets to themselves.");

                var asset = await RequireAssetAsync(db, organizationId, dto.AssetId);

                EnforceHrDepartmentScope(actorRole, actorDepartmentId, asset.DepartmentId);

                AssetStatusValidator.Enforce(asset.Status, AssetStatuses.Assigned);

                var activeAssignment = await GetActiveAssignmentAsync(db, asset.AssetId);
                if (activeAssignment != null)
                    throw new InvalidOperationException(
                        "Asset already has an active assignment.");

                var employee = await db.Employees
                    .FirstOrDefaultAsync(e =>
                        e.EmployeeId == dto.EmployeeId &&
                        e.OrganizationId == organizationId)
                    ?? throw new KeyNotFoundException("Employee not found.");

                if (asset.DepartmentId.HasValue &&
                    employee.DepartmentId != asset.DepartmentId.Value)
                {
                    throw new InvalidOperationException(
                        "Asset can only be assigned within its department.");
                }

                var now = DateTime.UtcNow;

                var assignment = new AssetAssignment
                {
                    AssignmentId = Guid.NewGuid(),
                    AssetId = asset.AssetId,
                    EmployeeId = employee.EmployeeId,
                    AssignedByEmployeeId = actorEmployeeId,
                    AssignedAt = now,
                    AssignmentNotes = dto.Notes?.Trim(),
                    Status = AssetAssignmentStatuses.Active
                };

                db.AssetAssignments.Add(assignment);

                db.AssetAssignmentHistories.Add(new AssetAssignmentHistory
                {
                    AssetId = asset.AssetId,
                    EmployeeId = employee.EmployeeId,
                    AssignedByEmployeeId = actorEmployeeId,
                    AssignedAt = now,
                    AssignmentNotes = dto.Notes?.Trim()
                });

                asset.Status = AssetStatuses.Assigned;
                asset.CurrentEmployeeId = employee.EmployeeId;
                asset.AssignedAt = now;
                asset.UpdatedAt = now;

                try
                {
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new InvalidOperationException(
                        "This asset was updated by another user. Please refresh and try again.");
                }

                // Post-commit: activity log + notification — failures must not surface to the caller.
                await _activityLog.LogAsync(
                    organizationId, "Asset", "Assigned", "Asset", asset.AssetId,
                    actorUserId, actorRole,
                    newValues: new
                    {
                        AssetCode = asset.AssetCode,
                        EmployeeCode = employee.EmployeeCode,
                        EmployeeId = employee.EmployeeId
                    });

                await SendAssignmentNotificationAsync(
                    employee.UserId, organizationId, actorUserId, asset);
            }
            catch
            {
                // Safe to call even after a concurrency throw since the tx is not committed.
                // EF Core's `using` disposes and rolls back, but explicit rollback is defensive.
                try { await tx.RollbackAsync(); } catch { /* already rolled back */ }
                throw;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // RETURN
        // ─────────────────────────────────────────────────────────────────────

        public async Task ReturnAssetAsync(
            Guid organizationId,
            ReturnAssetDto dto,
            string actorUserId,
            string actorRole,
            Guid actorEmployeeId,
            Guid? actorDepartmentId = null)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            using var tx = await db.Database.BeginTransactionAsync();

            try
            {
                var asset = await RequireAssetAsync(db, organizationId, dto.AssetId);

                EnforceHrDepartmentScope(actorRole, actorDepartmentId, asset.DepartmentId);

                var assignment = await GetActiveAssignmentAsync(db, asset.AssetId);
                if (assignment == null)
                    throw new InvalidOperationException("No active assignment found.");

                AssetStatusValidator.Enforce(asset.Status, AssetStatuses.Available);

                var now = DateTime.UtcNow;

                assignment.ReturnedAt = now;
                assignment.ReturnCondition = dto.ReturnCondition.Trim();
                assignment.Status = AssetAssignmentStatuses.Returned;

                var openHistories = await db.AssetAssignmentHistories
                    .Where(h => h.AssetId == asset.AssetId && h.ReturnedAt == null)
                    .ToListAsync();

                foreach (var history in openHistories)
                {
                    history.ReturnedAt = now;
                    history.ReturnCondition = dto.ReturnCondition.Trim();
                }

                asset.Status = AssetStatuses.Available;
                asset.CurrentEmployeeId = null;
                asset.AssignedAt = null;
                asset.UpdatedAt = now;

                if (!string.IsNullOrWhiteSpace(dto.ReturnCondition))
                    asset.Condition = dto.ReturnCondition.Trim();

                try
                {
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new InvalidOperationException(
                        "This asset was modified by another user. Please refresh and try again.");
                }

                await _activityLog.LogAsync(
                    organizationId, "Asset", "Returned", "Asset", asset.AssetId,
                    actorUserId, actorRole,
                    newValues: new
                    {
                        AssetCode = asset.AssetCode,
                        ReturnCondition = dto.ReturnCondition
                    });
            }
            catch
            {
                try { await tx.RollbackAsync(); } catch { /* already rolled back */ }
                throw;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // QUERIES
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the active assignment for an asset, or null if the asset is not
        /// currently assigned. Used by the UI to show "Return" actions.
        /// </summary>
        public async Task<AssetAssignmentHistoryDto?> GetActiveAssignmentDtoAsync(
            Guid assetId,
            Guid organizationId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            return await db.AssetAssignments
                .AsNoTracking()
                .Where(a =>
                    a.AssetId == assetId &&
                    a.Asset.OrganizationId == organizationId &&
                    a.ReturnedAt == null)
                .Select(a => new AssetAssignmentHistoryDto
                {
                    AssetId = a.AssetId,
                    AssetCode = a.Asset.AssetCode,
                    AssetName = a.Asset.AssetName,
                    EmployeeId = a.EmployeeId,
                    EmployeeName = $"{a.Employee.FirstName} {a.Employee.LastName}",
                    EmployeeCode = a.Employee.EmployeeCode,
                    AssignedByName = a.AssignedByEmployee != null
                        ? $"{a.AssignedByEmployee.FirstName} {a.AssignedByEmployee.LastName}"
                        : "System",
                    AssignedAt = a.AssignedAt,
                    ReturnedAt = a.ReturnedAt,
                    AssignmentNotes = a.AssignmentNotes
                })
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Returns every active assignment across the organisation (or department for HR).
        /// Useful for the admin "Currently Assigned" dashboard table.
        /// </summary>
        public async Task<List<AssetAssignmentHistoryDto>> GetAllActiveAssignmentsAsync(
            Guid organizationId,
            string actorRole,
            Guid? actorDepartmentId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var q = db.AssetAssignments
                .AsNoTracking()
                .Where(a =>
                    a.Asset.OrganizationId == organizationId &&
                    !a.Asset.IsDeleted &&
                    a.ReturnedAt == null);

            if (actorRole == SystemRoles.HR)
            {
                if (!actorDepartmentId.HasValue)
                    throw new UnauthorizedAccessException(
                        "HR department context missing.");

                q = q.Where(a => a.Asset.DepartmentId == actorDepartmentId.Value);
            }

            return await q
                .OrderByDescending(a => a.AssignedAt)
                .Select(a => new AssetAssignmentHistoryDto
                {
                    AssetId = a.AssetId,
                    AssetCode = a.Asset.AssetCode,
                    AssetName = a.Asset.AssetName,
                    EmployeeId = a.EmployeeId,
                    EmployeeName = $"{a.Employee.FirstName} {a.Employee.LastName}",
                    EmployeeCode = a.Employee.EmployeeCode,
                    AssignedByName = a.AssignedByEmployee != null
                        ? $"{a.AssignedByEmployee.FirstName} {a.AssignedByEmployee.LastName}"
                        : "System",
                    AssignedAt = a.AssignedAt,
                    ReturnedAt = a.ReturnedAt,
                    ReturnCondition = a.ReturnCondition,
                    AssignmentNotes = a.AssignmentNotes
                })
                .ToListAsync();
        }

        /// <summary>
        /// Returns all assignments (past and present) for a specific employee.
        /// Used on the Employee profile / "My Assignment History" page.
        /// </summary>
        public async Task<List<AssetAssignmentHistoryDto>> GetEmployeeAssignmentHistoryAsync(
            Guid employeeId,
            Guid organizationId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            return await db.AssetAssignmentHistories
                .AsNoTracking()
                .Where(h =>
                    h.EmployeeId == employeeId &&
                    h.Asset!.OrganizationId == organizationId &&
                    !h.Asset.IsDeleted)
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

        /// <summary>
        /// Returns the list of employees eligible to receive a specific asset.
        /// Enforces department scoping for HR.
        /// </summary>
        public async Task<List<EmployeeDropdownDto>> GetEligibleEmployeesForAssignmentAsync(
            Guid organizationId,
            Guid assetId,
            string actorRole,
            Guid actorEmployeeId,
            Guid? actorDepartmentId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var asset = await db.Assets
                .AsNoTracking()
                .FirstOrDefaultAsync(a =>
                    a.AssetId == assetId &&
                    a.OrganizationId == organizationId &&
                    !a.IsDeleted)
                ?? throw new KeyNotFoundException("Asset not found.");

            var query = db.Employees
                .AsNoTracking()
                .Where(e =>
                    e.OrganizationId == organizationId &&
                    e.Status == "Active");

            // Asset is department-scoped — restrict employees to that department.
            if (asset.DepartmentId.HasValue)
                query = query.Where(e => e.DepartmentId == asset.DepartmentId.Value);

            // HR can only assign within their own department (and not to themselves).
            if (actorRole == SystemRoles.HR)
            {
                if (!actorDepartmentId.HasValue)
                    throw new UnauthorizedAccessException("HR department context missing.");

                query = query
                    .Where(e => e.DepartmentId == actorDepartmentId.Value)
                    .Where(e => e.EmployeeId != actorEmployeeId);
            }

            return await query
                .OrderBy(e => e.FirstName)
                .ThenBy(e => e.LastName)
                .Select(e => new EmployeeDropdownDto
                {
                    EmployeeId = e.EmployeeId,
                    FullName = $"{e.FirstName} {e.LastName}",
                    EmployeeCode = e.EmployeeCode,
                    DepartmentName = e.Department != null ? e.Department.Name : null
                })
                .ToListAsync();
        }

        // ─────────────────────────────────────────────────────────────────────
        // PRIVATE HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static async Task<Asset> RequireAssetAsync(
            ApplicationDbContext db,
            Guid organizationId,
            Guid assetId)
        {
            return await db.Assets
                .FirstOrDefaultAsync(a =>
                    a.AssetId == assetId &&
                    a.OrganizationId == organizationId &&
                    !a.IsDeleted)
                ?? throw new KeyNotFoundException("Asset not found.");
        }

        private static async Task<AssetAssignment?> GetActiveAssignmentAsync(
            ApplicationDbContext db,
            Guid assetId)
        {
            return await db.AssetAssignments
                .FirstOrDefaultAsync(a =>
                    a.AssetId == assetId &&
                    a.ReturnedAt == null);
        }

        private async Task SendAssignmentNotificationAsync(
            string? employeeUserId,
            Guid organizationId,
            string actorUserId,
            Asset asset)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(employeeUserId))
                    return;

                await _notifications.SendToUserAsync(
                    employeeUserId,
                    organizationId,
                    actorUserId,
                    AssetNotificationTypes.AssetAssigned,
                    "Asset",
                    asset.AssetId,
                    "Asset Assigned",
                    $"You have been assigned: {asset.AssetName} ({asset.AssetCode})",
                    "/employee/my-assets");
            }
            catch
            {
                // Notification failures must never break the assignment.
            }
        }

        private static void EnforceHrDepartmentScope(
            string actorRole,
            Guid? actorDepartmentId,
            Guid? assetDepartmentId)
        {
            if (actorRole != "HR") return;

            if (!actorDepartmentId.HasValue)
                throw new UnauthorizedAccessException(
                    "HR has no department assigned.");

            if (!assetDepartmentId.HasValue)
                throw new UnauthorizedAccessException(
                    "HR cannot manage organisation-wide assets.");

            if (assetDepartmentId.Value != actorDepartmentId.Value)
                throw new UnauthorizedAccessException(
                    "HR can only manage their own department's assets.");
        }
    }
}