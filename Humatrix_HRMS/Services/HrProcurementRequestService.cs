// Infrastructure/Services/HrProcurementRequestService.cs
using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.DTOs.Asset;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Infrastructure.Constants;
using Humatrix_HRMS.Infrastructure.Results;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Infrastructure.Services
{
    /// <summary>
    /// Manages HR-raised asset procurement requests (bulk or new-demand).
    /// OrgAdmin approves; fulfillment auto-creates assets in inventory.
    /// </summary>
    public class HrProcurementRequestService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        private readonly ActivityLogService _activityLog;
        private readonly NotificationEngine _notifications;
        private readonly AssetService _assetService;

        public HrProcurementRequestService(
            IDbContextFactory<ApplicationDbContext> dbFactory,
            ActivityLogService activityLog,
            NotificationEngine notifications,
            AssetService assetService)
        {
            _dbFactory = dbFactory;
            _activityLog = activityLog;
            _notifications = notifications;
            _assetService = assetService;
        }

        // ─────────────────────────────────────────────────────────────────────
        // SUBMIT (HR only)
        // ─────────────────────────────────────────────────────────────────────

        public async Task<HrProcurementRequestDto> SubmitAsync(
            Guid organizationId,
            Guid hrEmployeeId,
            string actorUserId,
            Guid? actorDepartmentId,
            CreateHrProcurementRequestDto dto)
        {
            if (!actorDepartmentId.HasValue)
                throw new UnauthorizedAccessException("HR department context missing.");

            if (dto.DepartmentId != actorDepartmentId.Value)
                throw new UnauthorizedAccessException("HR can only raise procurement requests for their own department.");

            if (!AssetRequestTypeCatalog_ProcurementTypes().Contains(dto.RequestType))
                throw new InvalidOperationException($"'{dto.RequestType}' is not a valid procurement request type.");

            using var db = await _dbFactory.CreateDbContextAsync();

            var employee = await db.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.EmployeeId == hrEmployeeId && e.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("HR employee not found.");

            // Duplicate guard: same type, category, department, pending.
            var duplicate = await db.HrProcurementRequests.AnyAsync(r =>
                r.OrganizationId == organizationId &&
                r.DepartmentId == dto.DepartmentId &&
                r.AssetCategory == dto.AssetCategory &&
                r.RequestType == dto.RequestType &&
                r.Status == HrProcurementStatuses.Pending);

            if (duplicate)
                throw new InvalidOperationException(
                    "A similar pending procurement request already exists for this department and category.");

            var now = DateTime.UtcNow;

            var request = new HrProcurementRequest
            {
                OrganizationId = organizationId,
                DepartmentId = dto.DepartmentId,
                RequestedByEmployeeId = hrEmployeeId,
                RequestType = dto.RequestType,
                AssetCategory = dto.AssetCategory,
                QuantityRequested = dto.QuantityRequested,
                QuantityFulfilled = 0,
                Reason = dto.Reason.Trim(),
                Specifications = dto.Specifications?.Trim(),
                PreferredBrand = dto.PreferredBrand?.Trim(),
                EstimatedBudget = dto.EstimatedBudget,
                Status = HrProcurementStatuses.Pending,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedByUserId = actorUserId
            };

            db.HrProcurementRequests.Add(request);
            await db.SaveChangesAsync();

            await _activityLog.LogAsync(
                organizationId, "HrProcurement", "Submitted",
                "HrProcurementRequest", request.ProcurementRequestId,
                actorUserId, "HR",
                newValues: new
                {
                    request.RequestType,
                    request.AssetCategory,
                    request.QuantityRequested,
                    request.DepartmentId,
                    request.Status
                });

            _ = Task.Run(() => NotifyAdminsOfNewProcurementAsync(
                organizationId, actorUserId, employee, dto.AssetCategory, dto.QuantityRequested));

            return await GetDtoOrThrowAsync(db, request.ProcurementRequestId, organizationId);
        }

        // ─────────────────────────────────────────────────────────────────────
        // APPROVE / REJECT (OrgAdmin only)
        // ─────────────────────────────────────────────────────────────────────

        public async Task<HrProcurementRequestDto> ReviewAsync(
            Guid organizationId,
            Guid actorEmployeeId,
            string actorUserId,
            ReviewHrProcurementRequestDto dto)
        {
            if (dto.Decision != HrProcurementStatuses.Approved &&
                dto.Decision != HrProcurementStatuses.Rejected)
                throw new ArgumentException("Decision must be 'Approved' or 'Rejected'.");

            if (dto.Decision == HrProcurementStatuses.Rejected &&
                string.IsNullOrWhiteSpace(dto.RejectionReason))
                throw new ArgumentException("Rejection reason is required.");

            using var db = await _dbFactory.CreateDbContextAsync();

            var request = await db.HrProcurementRequests
                .Include(r => r.RequestedByEmployee)
                .FirstOrDefaultAsync(r =>
                    r.ProcurementRequestId == dto.ProcurementRequestId &&
                    r.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("Procurement request not found.");

            if (request.Status != HrProcurementStatuses.Pending)
                throw new InvalidOperationException(
                    $"Only pending requests can be reviewed. Current status: {request.Status}.");

            if (dto.RowVersion != null)
                db.Entry(request).Property(r => r.RowVersion).OriginalValue = dto.RowVersion;

            var oldStatus = request.Status;
            var now = DateTime.UtcNow;

            request.Status = dto.Decision;
            request.ReviewedByEmployeeId = actorEmployeeId;
            request.ReviewedAt = now;
            request.AdminNotes = dto.AdminNotes?.Trim();
            request.RejectionReason = dto.Decision == HrProcurementStatuses.Rejected
                                         ? dto.RejectionReason?.Trim()
                                         : null;
            request.UpdatedAt = now;

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
                organizationId, "HrProcurement", dto.Decision,
                "HrProcurementRequest", request.ProcurementRequestId,
                actorUserId, "OrgAdmin",
                oldValues: new { Status = oldStatus },
                newValues: new { Status = dto.Decision, dto.AdminNotes, dto.RejectionReason });

            _ = Task.Run(() => NotifyHrOfProcurementDecisionAsync(
                organizationId, request, dto.Decision, dto.RejectionReason, actorUserId));

            return await GetDtoOrThrowAsync(db, request.ProcurementRequestId, organizationId);
        }

        // ─────────────────────────────────────────────────────────────────────
        // CANCEL (HR who raised it, or OrgAdmin)
        // ─────────────────────────────────────────────────────────────────────

        public async Task CancelAsync(
            Guid organizationId,
            Guid procurementRequestId,
            string actorUserId,
            string actorRole,
            Guid actorEmployeeId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var request = await db.HrProcurementRequests
                .FirstOrDefaultAsync(r =>
                    r.ProcurementRequestId == procurementRequestId &&
                    r.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("Procurement request not found.");

            if (actorRole == "HR" && request.RequestedByEmployeeId != actorEmployeeId)
                throw new UnauthorizedAccessException("HR can only cancel their own requests.");

            if (request.Status == HrProcurementStatuses.Fulfilled)
                throw new InvalidOperationException("Cannot cancel a fully fulfilled request.");

            if (request.Status == HrProcurementStatuses.Cancelled)
                throw new InvalidOperationException("Request is already cancelled.");

            request.Status = HrProcurementStatuses.Cancelled;
            request.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();

            await _activityLog.LogAsync(
                organizationId, "HrProcurement", "Cancelled",
                "HrProcurementRequest", request.ProcurementRequestId,
                actorUserId, actorRole,
                oldValues: new { request.Status },
                newValues: new { Status = HrProcurementStatuses.Cancelled });
        }

        // ─────────────────────────────────────────────────────────────────────
        // FULFILL (OrgAdmin only) — creates assets, updates counts and status
        // ─────────────────────────────────────────────────────────────────────

        public async Task<HrProcurementRequestDto> FulfillAsync(
            Guid organizationId,
            Guid actorEmployeeId,
            string actorUserId,
            FulfillHrProcurementRequestDto dto)
        {
            if (dto.Assets == null || dto.Assets.Count == 0)
                throw new ArgumentException("At least one asset must be provided for fulfillment.");

            using var db = await _dbFactory.CreateDbContextAsync();
            using var tx = await db.Database.BeginTransactionAsync();

            try
            {
                var request = await db.HrProcurementRequests
                    .FirstOrDefaultAsync(r =>
                        r.ProcurementRequestId == dto.ProcurementRequestId &&
                        r.OrganizationId == organizationId)
                    ?? throw new KeyNotFoundException("Procurement request not found.");

                if (request.Status != HrProcurementStatuses.Approved &&
                    request.Status != HrProcurementStatuses.PartiallyFulfilled)
                    throw new InvalidOperationException(
                        $"Request must be Approved or PartiallyFulfilled to be fulfilled. Current: {request.Status}.");

                if (dto.RowVersion != null)
                    db.Entry(request).Property(r => r.RowVersion).OriginalValue = dto.RowVersion;

                int remaining = request.QuantityRequested - request.QuantityFulfilled;
                if (dto.Assets.Count > remaining)
                    throw new InvalidOperationException(
                        $"Cannot fulfill {dto.Assets.Count} assets. Only {remaining} remaining.");

                var now = DateTime.UtcNow;

                var fulfillment = new HrProcurementFulfillment
                {
                    ProcurementRequestId = request.ProcurementRequestId,
                    FulfilledByEmployeeId = actorEmployeeId,
                    QuantityFulfilled = dto.Assets.Count,
                    Notes = dto.Notes?.Trim(),
                    FulfilledAt = now
                };

                db.HrProcurementFulfillments.Add(fulfillment);
                await db.SaveChangesAsync(); // get FulfillmentId

                // Create each asset, linking it to this fulfillment and the procurement department.
                var createdAssetIds = new List<Guid>();
                foreach (var createDto in dto.Assets)
                {
                    // Override DepartmentId to match procurement request.
                    createDto.DepartmentId = request.DepartmentId;
                    // Force category to match procurement.
                    createDto.Category = string.IsNullOrWhiteSpace(createDto.Category)
                        ? request.AssetCategory
                        : createDto.Category;

                    var assetDto = await _assetService.CreateAssetAsync(
                        organizationId, createDto, actorUserId, "OrgAdmin", actorEmployeeId);

                    // Link to this fulfillment batch.
                    var asset = await db.Assets
                        .FirstAsync(a => a.AssetId == assetDto.AssetId);
                    asset.FulfillmentId = fulfillment.FulfillmentId;
                    fulfillment.CreatedAssets.Add(asset);
                    createdAssetIds.Add(asset.AssetId);
                }

                request.QuantityFulfilled += dto.Assets.Count;
                request.UpdatedAt = now;

                if (request.QuantityFulfilled >= request.QuantityRequested)
                    request.Status = HrProcurementStatuses.Fulfilled;
                else
                    request.Status = HrProcurementStatuses.PartiallyFulfilled;

                try
                {
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new InvalidOperationException(
                        "Request was modified by another user. Please refresh and try again.");
                }

                await _activityLog.LogAsync(
                    organizationId, "HrProcurement", "Fulfilled",
                    "HrProcurementRequest", request.ProcurementRequestId,
                    actorUserId, "OrgAdmin",
                    newValues: new
                    {
                        QuantityThisBatch = dto.Assets.Count,
                        request.QuantityFulfilled,
                        request.QuantityRequested,
                        request.Status,
                        FulfillmentId = fulfillment.FulfillmentId,
                        CreatedAssetIds = createdAssetIds
                    });

                _ = Task.Run(() => NotifyHrOfFulfillmentAsync(
                    organizationId, request, dto.Assets.Count, actorUserId));

                return await GetDtoOrThrowAsync(db, request.ProcurementRequestId, organizationId);
            }
            catch
            {
                try { await tx.RollbackAsync(); } catch { }
                throw;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // QUERIES
        // ─────────────────────────────────────────────────────────────────────

        public async Task<PagedResult<HrProcurementRequestDto>> GetRequestsAsync(
            HrProcurementFilterDto filter,
            string actorRole,
            Guid? actorDepartmentId)
        {
            if (filter.Page < 1) filter.Page = 1;
            if (filter.PageSize < 1 || filter.PageSize > 100) filter.PageSize = 25;

            using var db = await _dbFactory.CreateDbContextAsync();

            var q = db.HrProcurementRequests
                .AsNoTracking()
                .Where(r => r.OrganizationId == filter.OrganizationId);

            // HR can only see their department's procurement requests.
            if (actorRole == SystemRoles.HR)
            {
                if (!actorDepartmentId.HasValue)
                    throw new UnauthorizedAccessException("HR department context missing.");
                q = q.Where(r => r.DepartmentId == actorDepartmentId.Value);
            }
            else if (filter.DepartmentId.HasValue)
            {
                q = q.Where(r => r.DepartmentId == filter.DepartmentId.Value);
            }

            if (!string.IsNullOrWhiteSpace(filter.Status))
                q = q.Where(r => r.Status == filter.Status);

            if (!string.IsNullOrWhiteSpace(filter.RequestType))
                q = q.Where(r => r.RequestType == filter.RequestType);

            if (!string.IsNullOrWhiteSpace(filter.AssetCategory))
                q = q.Where(r => r.AssetCategory == filter.AssetCategory);

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(r => r.CreatedAt)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(ProcurementDtoSelector())
                .ToListAsync();

            return new PagedResult<HrProcurementRequestDto>
            {
                Items = items,
                TotalCount = total,
                Page = filter.Page,
                PageSize = filter.PageSize
            };
        }

        public async Task<HrProcurementRequestDto?> GetByIdAsync(
            Guid procurementRequestId,
            Guid organizationId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            return await db.HrProcurementRequests
                .AsNoTracking()
                .Where(r =>
                    r.ProcurementRequestId == procurementRequestId &&
                    r.OrganizationId == organizationId)
                .Select(ProcurementDtoSelector())
                .FirstOrDefaultAsync();
        }

        // ─────────────────────────────────────────────────────────────────────
        // PRIVATE HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static HashSet<string> AssetRequestTypeCatalog_ProcurementTypes() => new()
        {
            AssetRequestTypeCatalog.BulkAssetRequest,
            AssetRequestTypeCatalog.NewAssetDemand
        };

        private static System.Linq.Expressions.Expression<Func<HrProcurementRequest, HrProcurementRequestDto>>
            ProcurementDtoSelector() =>
            r => new HrProcurementRequestDto
            {
                ProcurementRequestId = r.ProcurementRequestId,
                OrganizationId = r.OrganizationId,
                DepartmentId = r.DepartmentId,
                DepartmentName = r.Department != null ? r.Department.Name : string.Empty,
                RequestedByEmployeeId = r.RequestedByEmployeeId,
                RequestedByEmployeeName = r.RequestedByEmployee != null
                    ? $"{r.RequestedByEmployee.FirstName} {r.RequestedByEmployee.LastName}"
                    : string.Empty,
                RequestedByEmployeeCode = r.RequestedByEmployee != null
                    ? r.RequestedByEmployee.EmployeeCode
                    : string.Empty,
                RequestType = r.RequestType,
                AssetCategory = r.AssetCategory,
                QuantityRequested = r.QuantityRequested,
                QuantityFulfilled = r.QuantityFulfilled,
                Reason = r.Reason,
                Specifications = r.Specifications,
                PreferredBrand = r.PreferredBrand,
                EstimatedBudget = r.EstimatedBudget,
                Status = r.Status,
                RejectionReason = r.RejectionReason,
                AdminNotes = r.AdminNotes,
                ReviewedByEmployeeName = r.ReviewedByEmployee != null
                    ? $"{r.ReviewedByEmployee.FirstName} {r.ReviewedByEmployee.LastName}"
                    : null,
                ReviewedAt = r.ReviewedAt,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                RowVersion = r.RowVersion,
                ApprovalRequestId = r.ApprovalRequestId,
                Fulfillments = r.Fulfillments.Select(f => new HrProcurementFulfillmentDto
                {
                    FulfillmentId = f.FulfillmentId,
                    ProcurementRequestId = f.ProcurementRequestId,
                    FulfilledByEmployeeName = f.FulfilledByEmployee != null
                        ? $"{f.FulfilledByEmployee.FirstName} {f.FulfilledByEmployee.LastName}"
                        : string.Empty,
                    QuantityFulfilled = f.QuantityFulfilled,
                    Notes = f.Notes,
                    FulfilledAt = f.FulfilledAt,
                    CreatedAssets = f.CreatedAssets.Select(a => new AssetDto
                    {
                        AssetId = a.AssetId,
                        AssetCode = a.AssetCode,
                        AssetName = a.AssetName,
                        Category = a.Category,
                        Status = a.Status,
                        DepartmentId = a.DepartmentId,
                        CreatedAt = a.CreatedAt
                    }).ToList()
                }).ToList()
            };

        private async Task<HrProcurementRequestDto> GetDtoOrThrowAsync(
            ApplicationDbContext db,
            Guid requestId,
            Guid organizationId)
        {
            return await db.HrProcurementRequests
                .AsNoTracking()
                .Where(r => r.ProcurementRequestId == requestId && r.OrganizationId == organizationId)
                .Select(ProcurementDtoSelector())
                .FirstAsync();
        }

        private async Task NotifyAdminsOfNewProcurementAsync(
            Guid organizationId,
            string actorUserId,
            Employee hrEmployee,
            string assetCategory,
            int quantity)
        {
            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();
                var adminIds = await (
                    from u in db.Users
                    join ur in db.UserRoles on u.Id equals ur.UserId
                    join r in db.Roles on ur.RoleId equals r.Id
                    where u.OrganizationId == organizationId && r.Name == "OrgAdmin"
                    select u.Id
                ).ToListAsync();

                var hrName = $"{hrEmployee.FirstName} {hrEmployee.LastName}";
                foreach (var adminId in adminIds)
                {
                    await _notifications.SendToUserAsync(
                        adminId,
                        organizationId,
                        actorUserId,
                        AssetNotificationTypes.AssetProcurementRequested,
                        "HrProcurementRequest",
                        Guid.Empty,
                        "New Procurement Request",
                        $"{hrName} (HR) requested {quantity} {assetCategory}(s).",
                        "/orgadmin/asset-procurement");
                }
            }
            catch { /* notifications must never break the main flow */ }
        }

        private async Task NotifyHrOfProcurementDecisionAsync(
            Guid organizationId,
            HrProcurementRequest request,
            string decision,
            string? rejectionReason,
            string actorUserId)
        {
            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();
                var hrUserId = await db.Employees
                    .Where(e => e.EmployeeId == request.RequestedByEmployeeId)
                    .Select(e => e.UserId)
                    .FirstOrDefaultAsync();

                if (string.IsNullOrWhiteSpace(hrUserId)) return;

                var notifType = decision == HrProcurementStatuses.Approved
                    ? AssetNotificationTypes.AssetProcurementApproved
                    : AssetNotificationTypes.AssetProcurementRejected;

                var msg = decision == HrProcurementStatuses.Approved
                    ? $"Your procurement request for {request.QuantityRequested} {request.AssetCategory}(s) was approved."
                    : $"Your procurement request for {request.QuantityRequested} {request.AssetCategory}(s) was rejected. Reason: {rejectionReason}";

                await _notifications.SendToUserAsync(
                    hrUserId, organizationId, actorUserId, notifType,
                    "HrProcurementRequest", request.ProcurementRequestId,
                    $"Procurement Request {decision}", msg,
                    "/hr/asset-procurement");
            }
            catch { }
        }

        private async Task NotifyHrOfFulfillmentAsync(
            Guid organizationId,
            HrProcurementRequest request,
            int batchCount,
            string actorUserId)
        {
            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();
                var hrUserId = await db.Employees
                    .Where(e => e.EmployeeId == request.RequestedByEmployeeId)
                    .Select(e => e.UserId)
                    .FirstOrDefaultAsync();

                if (string.IsNullOrWhiteSpace(hrUserId)) return;

                var msg = request.Status == HrProcurementStatuses.Fulfilled
                    ? $"Your procurement request for {request.AssetCategory} has been fully fulfilled. {request.QuantityFulfilled} asset(s) added to inventory."
                    : $"{batchCount} {request.AssetCategory}(s) added. {request.QuantityFulfilled}/{request.QuantityRequested} fulfilled so far.";

                await _notifications.SendToUserAsync(
                    hrUserId, organizationId, actorUserId,
                    AssetNotificationTypes.AssetProcurementFulfilled,
                    "HrProcurementRequest", request.ProcurementRequestId,
                    "Procurement Request Fulfilled", msg,
                    "/hr/asset-procurement");
            }
            catch { }
        }
    }
}
