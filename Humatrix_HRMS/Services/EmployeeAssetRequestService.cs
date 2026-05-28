// Infrastructure/Services/EmployeeAssetRequestService.cs
using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.DTOs.Asset;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Infrastructure.Constants;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Infrastructure.Services
{
    /// <summary>
    /// Manages employee-raised operational asset requests:
    /// ReturnRequest, ReplacementRequest, RepairRequest, AccessoryRequest, NewAssetRequest.
    ///
    /// All asset status changes, assignment records, analytics, activity logs,
    /// and notifications are kept in sync within each operation.
    /// </summary>
    public class EmployeeAssetRequestService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        private readonly ActivityLogService _activityLog;
        private readonly NotificationEngine _notifications;

        public EmployeeAssetRequestService(
            IDbContextFactory<ApplicationDbContext> dbFactory,
            ActivityLogService activityLog,
            NotificationEngine notifications)
        {
            _dbFactory = dbFactory;
            _activityLog = activityLog;
            _notifications = notifications;
        }

        // ─────────────────────────────────────────────────────────────────────
        // SUBMIT
        // ─────────────────────────────────────────────────────────────────────

        public async Task<EmployeeAssetRequestDto> SubmitAsync(
            Guid organizationId,
            Guid employeeId,
            string actorUserId,
            string actorRole,
            Guid? actorDepartmentId,
            SubmitEmployeeAssetRequestDto dto)
        {
            if (!EmployeeAssetRequestTypes.All.Contains(dto.RequestType))
                throw new InvalidOperationException($"'{dto.RequestType}' is not a valid employee request type.");

            // Only Employee and HR can submit.
            if (actorRole != "Employee" && actorRole != "HR")
                throw new UnauthorizedAccessException("Only employees or HR can submit employee asset requests.");

            using var db = await _dbFactory.CreateDbContextAsync();

            var employee = await db.Employees
                .AsNoTracking()
                .Include(e => e.Department)
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId && e.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("Employee not found.");

            // HR scope guard.
            if (actorRole == SystemRoles.HR)
            {
                if (!actorDepartmentId.HasValue)
                    throw new UnauthorizedAccessException("HR department context missing.");
                if (employee.DepartmentId != actorDepartmentId.Value)
                    throw new UnauthorizedAccessException("HR can only raise requests for employees in their department.");
            }

            // Request types that need an existing asset.
            if (EmployeeAssetRequestTypes.RequiresExistingAsset.Contains(dto.RequestType))
            {
                if (!dto.AssetId.HasValue)
                    throw new ArgumentException($"AssetId is required for {dto.RequestType}.");

                var asset = await db.Assets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a =>
                        a.AssetId == dto.AssetId.Value &&
                        a.OrganizationId == organizationId &&
                        !a.IsDeleted)
                    ?? throw new KeyNotFoundException("Asset not found.");

                // The asset must belong to this employee.
                if (dto.RequestType == EmployeeAssetRequestTypes.ReturnRequest ||
                    dto.RequestType == EmployeeAssetRequestTypes.ReplacementRequest ||
                    dto.RequestType == EmployeeAssetRequestTypes.RepairRequest)
                {
                    if (asset.CurrentEmployeeId != employeeId)
                        throw new InvalidOperationException(
                            "You can only raise this request for an asset currently assigned to you.");
                }
            }

            // Duplicate guard: same type + asset, pending.
            var duplicate = await db.EmployeeAssetRequests.AnyAsync(r =>
                r.OrganizationId == organizationId &&
                r.EmployeeId == employeeId &&
                r.RequestType == dto.RequestType &&
                r.AssetId == dto.AssetId &&
                (r.Status == EmployeeAssetRequestStatuses.Pending ||
                 r.Status == EmployeeAssetRequestStatuses.UnderReview ||
                 r.Status == EmployeeAssetRequestStatuses.Approved));

            if (duplicate)
                throw new InvalidOperationException(
                    "A similar active request already exists for this asset.");

            var now = DateTime.UtcNow;

            var request = new EmployeeAssetRequest
            {
                OrganizationId = organizationId,
                EmployeeId = employeeId,
                RequestType = dto.RequestType,
                AssetId = dto.AssetId,
                Reason = dto.Reason.Trim(),
                AdditionalDetails = dto.AdditionalDetails?.Trim(),
                RequestedAssetCategory = dto.RequestedAssetCategory?.Trim(),
                RequestedSpecs = dto.RequestedSpecs?.Trim(),
                Status = EmployeeAssetRequestStatuses.Pending,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.EmployeeAssetRequests.Add(request);
            await db.SaveChangesAsync();

            await _activityLog.LogAsync(
                organizationId, "EmployeeAssetRequest", "Submitted",
                "EmployeeAssetRequest", request.EmployeeAssetRequestId,
                actorUserId, actorRole,
                newValues: new
                {
                    request.RequestType,
                    request.AssetId,
                    request.Status
                });

            _ = Task.Run(() => NotifyHrOfNewRequestAsync(
                organizationId, actorUserId, employee, dto.RequestType));

            return await GetDtoOrThrowAsync(db, request.EmployeeAssetRequestId, organizationId);
        }

        // ─────────────────────────────────────────────────────────────────────
        // REVIEW (HR approves / rejects / marks under review)
        // ─────────────────────────────────────────────────────────────────────

        public async Task<EmployeeAssetRequestDto> ReviewAsync(
            Guid organizationId,
            Guid actorEmployeeId,
            string actorUserId,
            string actorRole,
            Guid? actorDepartmentId,
            ReviewEmployeeAssetRequestDto dto)
        {
            var validDecisions = new[]
            {
                EmployeeAssetRequestStatuses.Approved,
                EmployeeAssetRequestStatuses.Rejected,
                EmployeeAssetRequestStatuses.UnderReview
            };

            if (!validDecisions.Contains(dto.Decision))
                throw new ArgumentException("Decision must be 'Approved', 'Rejected', or 'UnderReview'.");

            if (dto.Decision == EmployeeAssetRequestStatuses.Rejected &&
                string.IsNullOrWhiteSpace(dto.RejectionReason))
                throw new ArgumentException("Rejection reason is required.");

            using var db = await _dbFactory.CreateDbContextAsync();

            var request = await db.EmployeeAssetRequests
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r =>
                    r.EmployeeAssetRequestId == dto.EmployeeAssetRequestId &&
                    r.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("Employee asset request not found.");

            // HR dept scope.
            if (actorRole == SystemRoles.HR)
            {
                if (!actorDepartmentId.HasValue)
                    throw new UnauthorizedAccessException("HR department context missing.");
                if (request.Employee?.DepartmentId != actorDepartmentId.Value)
                    throw new UnauthorizedAccessException("HR can only review requests from their own department.");
            }

            if (request.Status != EmployeeAssetRequestStatuses.Pending &&
                request.Status != EmployeeAssetRequestStatuses.UnderReview)
                throw new InvalidOperationException(
                    $"Only Pending or UnderReview requests can be reviewed. Current: {request.Status}.");

            if (dto.RowVersion != null)
                db.Entry(request).Property(r => r.RowVersion).OriginalValue = dto.RowVersion;

            var oldStatus = request.Status;
            var now = DateTime.UtcNow;

            request.Status = dto.Decision;
            request.ReviewedByEmployeeId = actorEmployeeId;
            request.ReviewedAt = now;
            request.ReviewComments = dto.ReviewComments?.Trim();
            request.RejectionReason = dto.Decision == EmployeeAssetRequestStatuses.Rejected
                                           ? dto.RejectionReason?.Trim()
                                           : null;
            request.UpdatedAt = now;

            // When approved, advance to InProgress for requests that need action.
            if (dto.Decision == EmployeeAssetRequestStatuses.Approved)
            {
                if (request.RequestType == EmployeeAssetRequestTypes.RepairRequest ||
                    request.RequestType == EmployeeAssetRequestTypes.ReplacementRequest)
                {
                    request.Status = EmployeeAssetRequestStatuses.InProgress;
                }
            }

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException(
                    "Request was modified by another user. Please refresh and try again.");
            }

            await _activityLog.LogAsync(
                organizationId, "EmployeeAssetRequest", dto.Decision,
                "EmployeeAssetRequest", request.EmployeeAssetRequestId,
                actorUserId, actorRole,
                oldValues: new { Status = oldStatus },
                newValues: new
                {
                    Status = request.Status,
                    request.ReviewComments,
                    request.RejectionReason
                });

            _ = Task.Run(() => NotifyEmployeeOfDecisionAsync(
                organizationId, request.EmployeeId, request.RequestType, dto.Decision,
                dto.RejectionReason, actorUserId));

            return await GetDtoOrThrowAsync(db, request.EmployeeAssetRequestId, organizationId);
        }

        // ─────────────────────────────────────────────────────────────────────
        // COMPLETE — HR/OrgAdmin finalizes the request, syncing all asset state
        // ─────────────────────────────────────────────────────────────────────

        public async Task<EmployeeAssetRequestDto> CompleteAsync(
            Guid organizationId,
            Guid actorEmployeeId,
            string actorUserId,
            string actorRole,
            Guid? actorDepartmentId,
            CompleteEmployeeAssetRequestDto dto)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            using var tx = await db.Database.BeginTransactionAsync();

            try
            {
                var request = await db.EmployeeAssetRequests
                    .Include(r => r.Employee)
                    .Include(r => r.Asset)
                    .FirstOrDefaultAsync(r =>
                        r.EmployeeAssetRequestId == dto.EmployeeAssetRequestId &&
                        r.OrganizationId == organizationId)
                    ?? throw new KeyNotFoundException("Employee asset request not found.");

                // HR dept scope.
                if (actorRole == SystemRoles.HR)
                {
                    if (!actorDepartmentId.HasValue)
                        throw new UnauthorizedAccessException("HR department context missing.");
                    if (request.Employee?.DepartmentId != actorDepartmentId.Value)
                        throw new UnauthorizedAccessException(
                            "HR can only complete requests from their own department.");
                }

                var allowedStatuses = new[]
                {
                    EmployeeAssetRequestStatuses.Approved,
                    EmployeeAssetRequestStatuses.InProgress
                };

                if (!allowedStatuses.Contains(request.Status))
                    throw new InvalidOperationException(
                        $"Only Approved or InProgress requests can be completed. Current: {request.Status}.");

                if (dto.RowVersion != null)
                    db.Entry(request).Property(r => r.RowVersion).OriginalValue = dto.RowVersion;

                var now = DateTime.UtcNow;

                // ── Delegate to type-specific handler ──────────────────────
                switch (request.RequestType)
                {
                    case EmployeeAssetRequestTypes.ReturnRequest:
                        await ProcessReturnAsync(db, request, actorEmployeeId, actorUserId, actorRole, now);
                        break;

                    case EmployeeAssetRequestTypes.RepairRequest:
                        await ProcessRepairAsync(db, request, actorUserId, actorRole, organizationId, now);
                        break;

                    case EmployeeAssetRequestTypes.ReplacementRequest:
                        await ProcessReplacementAsync(db, request, dto, actorEmployeeId, actorUserId, actorRole, organizationId, now);
                        break;

                    case EmployeeAssetRequestTypes.AccessoryRequest:
                    case EmployeeAssetRequestTypes.NewAssetRequest:
                        // No automated asset state change; HR manually handles fulfillment.
                        break;

                    default:
                        throw new InvalidOperationException($"Unknown request type: {request.RequestType}");
                }

                request.Status = EmployeeAssetRequestStatuses.Completed;
                request.ProcessedByEmployeeId = actorEmployeeId;
                request.ProcessedAt = now;
                request.ResolutionNotes = dto.ResolutionNotes?.Trim();
                request.ReplacementAssetId = dto.ReplacementAssetId;
                request.UpdatedAt = now;

                try
                {
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new InvalidOperationException(
                        "A concurrent modification occurred. Please refresh and try again.");
                }

                await _activityLog.LogAsync(
                    organizationId, "EmployeeAssetRequest", "Completed",
                    "EmployeeAssetRequest", request.EmployeeAssetRequestId,
                    actorUserId, actorRole,
                    newValues: new
                    {
                        request.RequestType,
                        request.Status,
                        request.ReplacementAssetId,
                        request.ResolutionNotes
                    });

                _ = Task.Run(() => NotifyEmployeeOfCompletionAsync(
                    organizationId, request.EmployeeId, request.RequestType, actorUserId));

                return await GetDtoOrThrowAsync(db, request.EmployeeAssetRequestId, organizationId);
            }
            catch
            {
                try { await tx.RollbackAsync(); } catch { }
                throw;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // CANCEL (employee cancels own pending request, or HR cancels for dept)
        // ─────────────────────────────────────────────────────────────────────

        public async Task CancelAsync(
            Guid organizationId,
            Guid employeeAssetRequestId,
            string actorUserId,
            string actorRole,
            Guid actorEmployeeId,
            Guid? actorDepartmentId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var request = await db.EmployeeAssetRequests
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r =>
                    r.EmployeeAssetRequestId == employeeAssetRequestId &&
                    r.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("Employee asset request not found.");

            if (actorRole == "Employee" && request.EmployeeId != actorEmployeeId)
                throw new UnauthorizedAccessException("You can only cancel your own requests.");

            if (actorRole == SystemRoles.HR)
            {
                if (!actorDepartmentId.HasValue)
                    throw new UnauthorizedAccessException("HR department context missing.");
                if (request.Employee?.DepartmentId != actorDepartmentId.Value)
                    throw new UnauthorizedAccessException(
                        "HR can only cancel requests from their own department.");
            }

            var cancellableStatuses = new[]
            {
                EmployeeAssetRequestStatuses.Pending,
                EmployeeAssetRequestStatuses.UnderReview
            };

            if (!cancellableStatuses.Contains(request.Status))
                throw new InvalidOperationException(
                    $"Only Pending or UnderReview requests can be cancelled. Current: {request.Status}.");

            request.Status = EmployeeAssetRequestStatuses.Cancelled;
            request.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();

            await _activityLog.LogAsync(
                organizationId, "EmployeeAssetRequest", "Cancelled",
                "EmployeeAssetRequest", request.EmployeeAssetRequestId,
                actorUserId, actorRole,
                oldValues: new { request.Status },
                newValues: new { Status = EmployeeAssetRequestStatuses.Cancelled });
        }

        // ─────────────────────────────────────────────────────────────────────
        // QUERIES
        // ─────────────────────────────────────────────────────────────────────

        public async Task<PagedResult<EmployeeAssetRequestDto>> GetRequestsAsync(
            EmployeeAssetRequestFilterDto filter,
            string actorRole,
            Guid? actorDepartmentId)
        {
            if (filter.Page < 1) filter.Page = 1;
            if (filter.PageSize < 1 || filter.PageSize > 100) filter.PageSize = 25;

            using var db = await _dbFactory.CreateDbContextAsync();

            var q = db.EmployeeAssetRequests
                .AsNoTracking()
                .Where(r => r.OrganizationId == filter.OrganizationId);

            if (actorRole == SystemRoles.HR)
            {
                if (!actorDepartmentId.HasValue)
                    throw new UnauthorizedAccessException("HR department context missing.");
                q = q.Where(r => r.Employee!.DepartmentId == actorDepartmentId.Value);
            }
            else if (filter.DepartmentId.HasValue)
            {
                q = q.Where(r => r.Employee!.DepartmentId == filter.DepartmentId.Value);
            }

            if (!string.IsNullOrWhiteSpace(filter.Status))
                q = q.Where(r => r.Status == filter.Status);

            if (!string.IsNullOrWhiteSpace(filter.RequestType))
                q = q.Where(r => r.RequestType == filter.RequestType);

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(r => r.CreatedAt)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(RequestDtoSelector())
                .ToListAsync();

            return new PagedResult<EmployeeAssetRequestDto>
            {
                Items = items,
                TotalCount = total,
                Page = filter.Page,
                PageSize = filter.PageSize
            };
        }

        public async Task<PagedResult<EmployeeAssetRequestDto>> GetMyRequestsAsync(
            Guid employeeId,
            Guid organizationId,
            int page = 1,
            int pageSize = 25)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 25;

            using var db = await _dbFactory.CreateDbContextAsync();

            var q = db.EmployeeAssetRequests
                .AsNoTracking()
                .Where(r => r.EmployeeId == employeeId && r.OrganizationId == organizationId);

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(RequestDtoSelector())
                .ToListAsync();

            return new PagedResult<EmployeeAssetRequestDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<EmployeeAssetRequestDto?> GetByIdAsync(
            Guid requestId,
            Guid organizationId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            return await db.EmployeeAssetRequests
                .AsNoTracking()
                .Where(r => r.EmployeeAssetRequestId == requestId && r.OrganizationId == organizationId)
                .Select(RequestDtoSelector())
                .FirstOrDefaultAsync();
        }

        // ─────────────────────────────────────────────────────────────────────
        // TYPE-SPECIFIC ASSET-STATE HANDLERS
        // ─────────────────────────────────────────────────────────────────────

        private static async Task ProcessReturnAsync(
            ApplicationDbContext db,
            EmployeeAssetRequest request,
            Guid actorEmployeeId,
            string actorUserId,
            string actorRole,
            DateTime now)
        {
            if (!request.AssetId.HasValue) return;

            var asset = await db.Assets
                .FirstOrDefaultAsync(a => a.AssetId == request.AssetId.Value)
                ?? throw new KeyNotFoundException("Asset not found.");

            if (asset.Status != AssetStatuses.Assigned)
                throw new InvalidOperationException("Asset is not currently assigned.");

            AssetStatusValidator.Enforce(asset.Status, AssetStatuses.Available);

            // Close the active assignment.
            var assignment = await db.AssetAssignments
                .FirstOrDefaultAsync(a => a.AssetId == asset.AssetId && a.ReturnedAt == null);

            if (assignment != null)
            {
                assignment.ReturnedAt = now;
                assignment.ReturnCondition = "Returned via request";
                assignment.Status = AssetAssignmentStatuses.Returned;
            }

            var openHistories = await db.AssetAssignmentHistories
                .Where(h => h.AssetId == asset.AssetId && h.ReturnedAt == null)
                .ToListAsync();

            foreach (var h in openHistories)
            {
                h.ReturnedAt = now;
                h.ReturnCondition = "Returned via request";
            }

            asset.Status = AssetStatuses.Available;
            asset.CurrentEmployeeId = null;
            asset.AssignedAt = null;
            asset.UpdatedAt = now;
        }

        private static async Task ProcessRepairAsync(
            ApplicationDbContext db,
            EmployeeAssetRequest request,
            string actorUserId,
            string actorRole,
            Guid organizationId,
            DateTime now)
        {
            if (!request.AssetId.HasValue) return;

            var asset = await db.Assets
                .FirstOrDefaultAsync(a => a.AssetId == request.AssetId.Value)
                ?? throw new KeyNotFoundException("Asset not found.");

            // Must be returned first if assigned.
            if (asset.Status == AssetStatuses.Assigned)
                throw new InvalidOperationException(
                    "Asset must be returned before being sent to repair. Complete a ReturnRequest first.");

            AssetStatusValidator.Enforce(asset.Status, AssetStatuses.InRepair);

            asset.Status = AssetStatuses.InRepair;
            asset.UpdatedAt = now;
            asset.Notes = string.IsNullOrWhiteSpace(asset.Notes)
                ? $"[Repair via request] {request.Reason}"
                : $"{asset.Notes}\n[Repair via request] {request.Reason}";
        }

        private static async Task ProcessReplacementAsync(
            ApplicationDbContext db,
            EmployeeAssetRequest request,
            CompleteEmployeeAssetRequestDto dto,
            Guid actorEmployeeId,
            string actorUserId,
            string actorRole,
            Guid organizationId,
            DateTime now)
        {
            // First, return the old asset.
            if (request.AssetId.HasValue)
            {
                var oldAsset = await db.Assets
                    .FirstOrDefaultAsync(a => a.AssetId == request.AssetId.Value);

                if (oldAsset != null && oldAsset.Status == AssetStatuses.Assigned)
                {
                    var oldAssignment = await db.AssetAssignments
                        .FirstOrDefaultAsync(a => a.AssetId == oldAsset.AssetId && a.ReturnedAt == null);

                    if (oldAssignment != null)
                    {
                        oldAssignment.ReturnedAt = now;
                        oldAssignment.ReturnCondition = "Replaced";
                        oldAssignment.Status = AssetAssignmentStatuses.Returned;
                    }

                    var openHistories = await db.AssetAssignmentHistories
                        .Where(h => h.AssetId == oldAsset.AssetId && h.ReturnedAt == null)
                        .ToListAsync();

                    foreach (var h in openHistories)
                    {
                        h.ReturnedAt = now;
                        h.ReturnCondition = "Replaced";
                    }

                    oldAsset.Status = AssetStatuses.Available;
                    oldAsset.CurrentEmployeeId = null;
                    oldAsset.AssignedAt = null;
                    oldAsset.UpdatedAt = now;
                }
            }

            // Assign the replacement asset if provided.
            if (!dto.ReplacementAssetId.HasValue) return;

            var newAsset = await db.Assets
                .FirstOrDefaultAsync(a =>
                    a.AssetId == dto.ReplacementAssetId.Value &&
                    a.OrganizationId == organizationId &&
                    !a.IsDeleted)
                ?? throw new KeyNotFoundException("Replacement asset not found.");

            if (newAsset.Status != AssetStatuses.Available &&
                newAsset.Status != AssetStatuses.Reserved)
                throw new InvalidOperationException(
                    $"Replacement asset is not available. Current status: {newAsset.Status}.");

            if (request.Employee?.EmployeeId == null)
                throw new InvalidOperationException("Employee data not loaded.");

            var newAssignment = new AssetAssignment
            {
                AssignmentId = Guid.NewGuid(),
                AssetId = newAsset.AssetId,
                EmployeeId = request.EmployeeId,
                AssignedByEmployeeId = actorEmployeeId,
                AssignedAt = now,
                AssignmentNotes = $"Replacement for request {request.EmployeeAssetRequestId}",
                Status = AssetAssignmentStatuses.Active
            };

            db.AssetAssignments.Add(newAssignment);

            db.AssetAssignmentHistories.Add(new AssetAssignmentHistory
            {
                AssetId = newAsset.AssetId,
                EmployeeId = request.EmployeeId,
                AssignedByEmployeeId = actorEmployeeId,
                AssignedAt = now,
                AssignmentNotes = $"Replacement for request {request.EmployeeAssetRequestId}"
            });

            newAsset.Status = AssetStatuses.Assigned;
            newAsset.CurrentEmployeeId = request.EmployeeId;
            newAsset.AssignedAt = now;
            newAsset.UpdatedAt = now;

            request.ReplacementAssetId = dto.ReplacementAssetId;
        }

        // ─────────────────────────────────────────────────────────────────────
        // PRIVATE HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static System.Linq.Expressions.Expression<Func<EmployeeAssetRequest, EmployeeAssetRequestDto>>
            RequestDtoSelector() =>
            r => new EmployeeAssetRequestDto
            {
                EmployeeAssetRequestId = r.EmployeeAssetRequestId,
                OrganizationId = r.OrganizationId,
                EmployeeId = r.EmployeeId,
                EmployeeName = r.Employee != null
                    ? $"{r.Employee.FirstName} {r.Employee.LastName}"
                    : string.Empty,
                EmployeeCode = r.Employee != null ? r.Employee.EmployeeCode : string.Empty,
                DepartmentName = r.Employee != null && r.Employee.Department != null
                    ? r.Employee.Department.Name
                    : null,
                RequestType = r.RequestType,
                AssetId = r.AssetId,
                AssetCode = r.Asset != null ? r.Asset.AssetCode : null,
                AssetName = r.Asset != null ? r.Asset.AssetName : null,
                Reason = r.Reason,
                AdditionalDetails = r.AdditionalDetails,
                RequestedAssetCategory = r.RequestedAssetCategory,
                RequestedSpecs = r.RequestedSpecs,
                Status = r.Status,
                RejectionReason = r.RejectionReason,
                ReviewComments = r.ReviewComments,
                ReviewedByEmployeeName = r.ReviewedByEmployee != null
                    ? $"{r.ReviewedByEmployee.FirstName} {r.ReviewedByEmployee.LastName}"
                    : null,
                ReviewedAt = r.ReviewedAt,
                ResolutionNotes = r.ResolutionNotes,
                ReplacementAssetId = r.ReplacementAssetId,
                ReplacementAssetCode = r.ReplacementAsset != null ? r.ReplacementAsset.AssetCode : null,
                ReplacementAssetName = r.ReplacementAsset != null ? r.ReplacementAsset.AssetName : null,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                RowVersion = r.RowVersion,
                ApprovalRequestId = r.ApprovalRequestId
            };

        private async Task<EmployeeAssetRequestDto> GetDtoOrThrowAsync(
            ApplicationDbContext db,
            Guid requestId,
            Guid organizationId)
        {
            return await db.EmployeeAssetRequests
                .AsNoTracking()
                .Where(r => r.EmployeeAssetRequestId == requestId && r.OrganizationId == organizationId)
                .Select(RequestDtoSelector())
                .FirstAsync();
        }

        private async Task NotifyHrOfNewRequestAsync(
            Guid organizationId,
            string actorUserId,
            Employee employee,
            string requestType)
        {
            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();

                var hrIds = await (
                    from u in db.Users
                    join ur in db.UserRoles on u.Id equals ur.UserId
                    join r in db.Roles on ur.RoleId equals r.Id
                    join emp in db.Employees on u.Id equals emp.UserId
                    where u.OrganizationId == organizationId
                          && r.Name == "HR"
                          && emp.DepartmentId == employee.DepartmentId
                    select u.Id
                ).ToListAsync();

                var empName = $"{employee.FirstName} {employee.LastName}";

                foreach (var hrId in hrIds)
                {
                    await _notifications.SendToUserAsync(
                        hrId, organizationId, actorUserId,
                        AssetNotificationTypes.EmployeeRequestSubmitted,
                        "EmployeeAssetRequest", Guid.Empty,
                        "New Employee Asset Request",
                        $"{empName} submitted a {requestType} request.",
                        "/hr/employee-asset-requests");
                }
            }
            catch { }
        }

        private async Task NotifyEmployeeOfDecisionAsync(
            Guid organizationId,
            Guid employeeId,
            string requestType,
            string decision,
            string? rejectionReason,
            string actorUserId)
        {
            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();
                var userId = await db.Employees
                    .Where(e => e.EmployeeId == employeeId)
                    .Select(e => e.UserId)
                    .FirstOrDefaultAsync();

                if (string.IsNullOrWhiteSpace(userId)) return;

                var notifType = decision == EmployeeAssetRequestStatuses.Approved
                    ? AssetNotificationTypes.EmployeeRequestApproved
                    : AssetNotificationTypes.EmployeeRequestRejected;

                var msg = decision == EmployeeAssetRequestStatuses.Approved
                    ? $"Your {requestType} request has been approved."
                    : $"Your {requestType} request was rejected. Reason: {rejectionReason}";

                await _notifications.SendToUserAsync(
                    userId, organizationId, actorUserId, notifType,
                    "EmployeeAssetRequest", Guid.Empty,
                    $"Asset Request {decision}", msg,
                    "/my-assets/requests");
            }
            catch { }
        }

        private async Task NotifyEmployeeOfCompletionAsync(
            Guid organizationId,
            Guid employeeId,
            string requestType,
            string actorUserId)
        {
            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();
                var userId = await db.Employees
                    .Where(e => e.EmployeeId == employeeId)
                    .Select(e => e.UserId)
                    .FirstOrDefaultAsync();

                if (string.IsNullOrWhiteSpace(userId)) return;

                await _notifications.SendToUserAsync(
                    userId, organizationId, actorUserId,
                    AssetNotificationTypes.EmployeeRequestCompleted,
                    "EmployeeAssetRequest", Guid.Empty,
                    "Asset Request Completed",
                    $"Your {requestType} request has been completed.",
                    "/my-assets/requests");
            }
            catch { }
        }
    }
}
