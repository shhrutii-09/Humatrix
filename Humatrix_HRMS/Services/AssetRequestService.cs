// Services/AssetRequestService.cs
using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.DTOs.Asset;
using Humatrix_HRMS.Infrastructure.Constants;
using Humatrix_HRMS.Infrastructure.Services;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class AssetRequestService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        private readonly NotificationService _notificationService;
        private readonly ActivityLogService _activityLog;

        public AssetRequestService(
            IDbContextFactory<ApplicationDbContext> dbFactory,
            NotificationService notificationService,
            ActivityLogService activityLog)
        {
            _dbFactory = dbFactory;
            _notificationService = notificationService;
            _activityLog = activityLog;
        }

        // ─────────────────────────────────────────────────────────────────────
        // SUBMIT
        // ─────────────────────────────────────────────────────────────────────

        public async Task<AssetRequestDto> SubmitRequestAsync(
            Guid organizationId,
            Guid employeeId,
            string actorUserId,
            string actorRole,
            Guid? hrDepartmentId,
            SubmitAssetRequestDto dto)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            using var tx = await db.Database.BeginTransactionAsync();

            try
            {
                var employee = await db.Employees
                    .AsNoTracking()
                    .Include(e => e.Department)
                    .FirstOrDefaultAsync(e =>
                        e.EmployeeId == employeeId &&
                        e.OrganizationId == organizationId)
                    ?? throw new KeyNotFoundException(
                        "Employee profile not found within your organisation.");

                // Validate the asset belongs to this org if one was specified.
                if (dto.AssetId.HasValue)
                {
                    var assetBelongsToOrg = await db.Assets.AnyAsync(a =>
                        a.AssetId == dto.AssetId.Value &&
                        a.OrganizationId == organizationId &&
                        !a.IsDeleted);

                    if (!assetBelongsToOrg)
                        throw new UnauthorizedAccessException(
                            "The specified asset does not belong to your organisation.");
                }

                // Role + category permission guard.
                ValidateRequestPermission(actorRole, dto.RequestCategory, dto.RequestType);

                // HR scope guard — HR can only raise requests for their own department.
                if (actorRole == SystemRoles.HR)
                {
                    if (!hrDepartmentId.HasValue)
                        throw new UnauthorizedAccessException(
                            "HR department configuration missing.");

                    if (employee.DepartmentId != hrDepartmentId.Value)
                        throw new UnauthorizedAccessException(
                            "HR can only create requests for their own department.");
                }

                // Prevent duplicate pending requests of the same type for the same employee.
                var duplicateExists = await db.AssetRequests.AnyAsync(r =>
                    r.OrganizationId == organizationId &&
                    r.EmployeeId == employeeId &&
                    r.RequestType == dto.RequestType &&
                    r.AssetId == dto.AssetId &&
                    r.Status == AssetRequestStatuses.Pending);

                if (duplicateExists)
                    throw new InvalidOperationException(
                        "A similar pending request already exists for this asset.");

                var now = DateTime.UtcNow;

                var request = new AssetRequest
                {
                    AssetRequestId = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    EmployeeId = employeeId,
                    AssetId = dto.AssetId,
                    RequestType = dto.RequestType,

                    // FIX: Both category fields were never set in the original code.
                    // RequestCategory = "Operational" or "Procurement"
                    // Category mirrors RequestCategory (legacy field — kept in sync)
                    RequestCategory = dto.RequestCategory,
                    Category = dto.RequestCategory,

                    RequestedCategory = dto.RequestedCategory?.Trim(),
                    RequestedSpecs = dto.RequestedSpecs?.Trim(),
                    Quantity = dto.Quantity < 1 ? 1 : dto.Quantity,
                    Reason = dto.Reason.Trim(),
                    Status = AssetRequestStatuses.Pending,
                    CreatedAt = now,
                    RequestedAt = now
                };

                db.AssetRequests.Add(request);
                await db.SaveChangesAsync();
                await tx.CommitAsync();

                await _activityLog.LogAsync(
                    organizationId, "AssetRequest", "Submitted",
                    "AssetRequest", request.AssetRequestId,
                    actorUserId, actorRole,
                    newValues: new
                    {
                        request.RequestType,
                        request.RequestCategory,
                        request.Quantity,
                        request.Status
                    });

                // Fire-and-forget — notification failure must never fail the request.
                _ = Task.Run(() =>
                    SendSubmissionNotificationsAsync(
                        organizationId, actorRole, employee, dto.RequestType));

                // Re-query to return the full DTO with all navigation data.
                return await GetRequestDtoOrThrowAsync(db, request.AssetRequestId, organizationId);
            }
            catch
            {
                try { await tx.RollbackAsync(); } catch { /* already rolled back */ }
                throw;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // APPROVE / REJECT  (unified)
        // ─────────────────────────────────────────────────────────────────────

        public async Task<AssetRequestDto> ProcessRequestAsync(
            Guid organizationId,
            ReviewAssetRequestDto dto,
            string actorUserId,
            string actorRole,
            Guid actorEmployeeId,
            Guid? actorDepartmentId)
        {
            if (dto.Decision != AssetRequestStatuses.Approved &&
                dto.Decision != AssetRequestStatuses.Rejected)
            {
                throw new ArgumentException(
                    "Decision must be 'Approved' or 'Rejected'.");
            }

            if (dto.Decision == AssetRequestStatuses.Rejected &&
                string.IsNullOrWhiteSpace(dto.RejectionReason))
            {
                throw new ArgumentException(
                    "A rejection reason is required when rejecting a request.");
            }

            using var db = await _dbFactory.CreateDbContextAsync();
            using var tx = await db.Database.BeginTransactionAsync();

            try
            {
                var request = await db.AssetRequests
                    .Include(x => x.Employee)
                    .FirstOrDefaultAsync(x =>
                        x.AssetRequestId == dto.AssetRequestId &&
                        x.OrganizationId == organizationId)
                    ?? throw new KeyNotFoundException("Asset request not found.");

                ValidateProcessorAccess(
                    actorRole, actorDepartmentId, request.Employee?.DepartmentId);

                if (request.Status != AssetRequestStatuses.Pending)
                    throw new InvalidOperationException(
                        $"Only pending requests can be processed. Current status: {request.Status}.");

                var now = DateTime.UtcNow;

                request.Status = dto.Decision;
                request.ReviewedAt = now;
                request.ProcessedAt = now;
                request.ProcessedByUserId = actorUserId;
                request.ReviewedByEmployeeId = actorEmployeeId;
                request.ReviewComments = dto.Comments?.Trim();
                request.RejectionReason = dto.Decision == AssetRequestStatuses.Rejected
                    ? dto.RejectionReason?.Trim()
                    : null;

                await db.SaveChangesAsync();
                await tx.CommitAsync();

                await _activityLog.LogAsync(
                    organizationId, "AssetRequest", dto.Decision,
                    "AssetRequest", request.AssetRequestId,
                    actorUserId, actorRole,
                    oldValues: new { Status = AssetRequestStatuses.Pending },
                    newValues: new
                    {
                        Status = dto.Decision,
                        request.ReviewComments,
                        request.RejectionReason
                    });

                _ = Task.Run(() =>
                    NotifyRequesterAsync(
                        request.EmployeeId,
                        dto.Decision == AssetRequestStatuses.Approved
                            ? "Asset Request Approved"
                            : "Asset Request Rejected",
                        BuildOutcomeMessage(request.RequestType, dto.Decision, dto.RejectionReason)));

                return await GetRequestDtoOrThrowAsync(db, request.AssetRequestId, organizationId);
            }
            catch
            {
                try { await tx.RollbackAsync(); } catch { /* already rolled back */ }
                throw;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // CANCEL  (by the employee who raised it, or HR for their dept requests)
        // ─────────────────────────────────────────────────────────────────────

        public async Task CancelRequestAsync(
            Guid organizationId,
            Guid assetRequestId,
            string actorUserId,
            string actorRole,
            Guid actorEmployeeId,
            Guid? actorDepartmentId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var request = await db.AssetRequests
                .Include(x => x.Employee)
                .FirstOrDefaultAsync(x =>
                    x.AssetRequestId == assetRequestId &&
                    x.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("Asset request not found.");

            // Employees can only cancel their own requests.
            if (actorRole == "Employee" && request.EmployeeId != actorEmployeeId)
                throw new UnauthorizedAccessException(
                    "You can only cancel your own requests.");

            // HR can cancel requests from their own department.
            if (actorRole == SystemRoles.HR)
            {
                if (!actorDepartmentId.HasValue)
                    throw new UnauthorizedAccessException("HR department context missing.");

                if (request.Employee?.DepartmentId != actorDepartmentId.Value)
                    throw new UnauthorizedAccessException(
                        "HR can only cancel requests from their own department.");
            }

            if (request.Status != AssetRequestStatuses.Pending)
                throw new InvalidOperationException(
                    $"Only pending requests can be cancelled. Current status: {request.Status}.");

            request.Status = AssetRequestStatuses.Cancelled;
            request.ProcessedAt = DateTime.UtcNow;
            request.ProcessedByUserId = actorUserId;

            await db.SaveChangesAsync();

            await _activityLog.LogAsync(
                organizationId, "AssetRequest", "Cancelled",
                "AssetRequest", request.AssetRequestId,
                actorUserId, actorRole,
                oldValues: new { Status = AssetRequestStatuses.Pending },
                newValues: new { Status = AssetRequestStatuses.Cancelled });
        }

        // ─────────────────────────────────────────────────────────────────────
        // QUERIES
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Admin / HR view: all requests visible to the actor (org-wide or dept-scoped).
        /// </summary>
        public async Task<PagedResult<AssetRequestDto>> GetRequestsAsync(
            Guid organizationId,
            string actorRole,
            Guid? actorDepartmentId,
            AssetRequestFilterDto filter)
        {
            if (filter.Page < 1) filter.Page = 1;
            if (filter.PageSize < 1 || filter.PageSize > 100) filter.PageSize = 25;

            using var db = await _dbFactory.CreateDbContextAsync();

            var q = db.AssetRequests
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId);

            if (actorRole == SystemRoles.HR)
            {
                if (!actorDepartmentId.HasValue)
                    throw new UnauthorizedAccessException("HR department context missing.");

                q = q.Where(x => x.Employee!.DepartmentId == actorDepartmentId.Value);
            }

            if (!string.IsNullOrWhiteSpace(filter.Status))
                q = q.Where(x => x.Status == filter.Status);

            if (!string.IsNullOrWhiteSpace(filter.RequestType))
                q = q.Where(x => x.RequestType == filter.RequestType);

            if (!string.IsNullOrWhiteSpace(filter.RequestCategory))
                q = q.Where(x => x.RequestCategory == filter.RequestCategory);

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(x => x.CreatedAt)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(RequestDtoSelector())
                .ToListAsync();

            return new PagedResult<AssetRequestDto>
            {
                Items = items,
                TotalCount = total,
                Page = filter.Page,
                PageSize = filter.PageSize
            };
        }

        /// <summary>
        /// Employee view: all requests raised by the given employee.
        /// </summary>
        public async Task<PagedResult<AssetRequestDto>> GetMyRequestsAsync(
            Guid employeeId,
            Guid organizationId,
            int page = 1,
            int pageSize = 25)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 25;

            using var db = await _dbFactory.CreateDbContextAsync();

            var q = db.AssetRequests
                .AsNoTracking()
                .Where(x =>
                    x.OrganizationId == organizationId &&
                    x.EmployeeId == employeeId);

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(RequestDtoSelector())
                .ToListAsync();

            return new PagedResult<AssetRequestDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<AssetRequestDto?> GetByIdAsync(
            Guid assetRequestId,
            Guid organizationId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            return await db.AssetRequests
                .AsNoTracking()
                .Where(x =>
                    x.AssetRequestId == assetRequestId &&
                    x.OrganizationId == organizationId)
                .Select(RequestDtoSelector())
                .FirstOrDefaultAsync();
        }

        // ─────────────────────────────────────────────────────────────────────
        // PRIVATE HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static void ValidateRequestPermission(
            string actorRole,
            string requestCategory,
            string requestType)
        {
            if (requestCategory == AssetRequestCategories.Procurement)
            {
                if (actorRole != "HR")
                    throw new UnauthorizedAccessException(
                        "Only HR can raise procurement requests.");

                if (!AssetRequestTypes.ProcurementTypes.Contains(requestType))
                    throw new InvalidOperationException(
                        $"'{requestType}' is not a valid procurement request type.");

                return;
            }

            if (requestCategory == AssetRequestCategories.Operational)
            {
                if (actorRole != "Employee" && actorRole != "HR")
                    throw new UnauthorizedAccessException(
                        "Only employees or HR can raise operational requests.");

                if (!AssetRequestTypes.OperationalTypes.Contains(requestType))
                    throw new InvalidOperationException(
                        $"'{requestType}' is not a valid operational request type.");

                return;
            }

            throw new InvalidOperationException(
                $"Unknown request category: '{requestCategory}'.");
        }

        private static void ValidateProcessorAccess(
            string actorRole,
            Guid? actorDepartmentId,
            Guid? employeeDepartmentId)
        {
            if (actorRole == "OrgAdmin") return;

            if (actorRole == SystemRoles.HR)
            {
                if (!actorDepartmentId.HasValue)
                    throw new UnauthorizedAccessException("HR department context missing.");

                if (employeeDepartmentId != actorDepartmentId.Value)
                    throw new UnauthorizedAccessException(
                        "HR can only process requests from their own department.");

                return;
            }

            throw new UnauthorizedAccessException(
                "Your role does not have permission to process asset requests.");
        }

        private static string BuildOutcomeMessage(
            string requestType,
            string decision,
            string? rejectionReason)
        {
            var msg = $"Your {requestType} request has been {decision.ToLower()}.";

            if (!string.IsNullOrWhiteSpace(rejectionReason))
                msg += $" Reason: {rejectionReason}";

            return msg;
        }

        /// <summary>
        /// Reusable EF Core projection from AssetRequest → AssetRequestDto.
        /// Avoids duplicating the same .Select(...) across query methods.
        /// </summary>
        private static System.Linq.Expressions.Expression<Func<AssetRequest, AssetRequestDto>>
            RequestDtoSelector() =>
            r => new AssetRequestDto
            {
                AssetRequestId = r.AssetRequestId,
                OrganizationId = r.OrganizationId,
                RequestType = r.RequestType,
                RequestCategory = r.RequestCategory,
                AssetId = r.AssetId,
                AssetCode = r.Asset != null ? r.Asset.AssetCode : null,
                AssetName = r.Asset != null ? r.Asset.AssetName : null,
                EmployeeId = r.EmployeeId,
                EmployeeName = r.Employee != null
                    ? $"{r.Employee.FirstName} {r.Employee.LastName}"
                    : string.Empty,
                EmployeeCode = r.Employee != null ? r.Employee.EmployeeCode : string.Empty,
                DepartmentName = r.Employee != null && r.Employee.Department != null
                    ? r.Employee.Department.Name
                    : null,
                Reason = r.Reason,
                Status = r.Status,
                Quantity = r.Quantity,
                RequestedAt = r.RequestedAt,
                ProcessedAt = r.ProcessedAt,
                RejectionReason = r.RejectionReason,
                ReviewedAt = r.ReviewedAt,
                ReviewedByName = r.ReviewedByEmployee != null
                    ? $"{r.ReviewedByEmployee.FirstName} {r.ReviewedByEmployee.LastName}"
                    : null,
                ReviewComments = r.ReviewComments,
                RequestedCategory = r.RequestedCategory,
                RequestedSpecs = r.RequestedSpecs,
                ApprovalRequestId = r.ApprovalRequestId,
                ApprovalStatus = r.ApprovalRequest != null
                    ? r.ApprovalRequest.Status
                    : null
            };

        private async Task<AssetRequestDto> GetRequestDtoOrThrowAsync(
            ApplicationDbContext db,
            Guid requestId,
            Guid organizationId)
        {
            return await db.AssetRequests
                .AsNoTracking()
                .Where(r =>
                    r.AssetRequestId == requestId &&
                    r.OrganizationId == organizationId)
                .Select(RequestDtoSelector())
                .FirstAsync();
        }

        // ─────────────────────────────────────────────────────────────────────
        // NOTIFICATION HELPERS  (fire-and-forget — failures are swallowed)
        // ─────────────────────────────────────────────────────────────────────

        private async Task SendSubmissionNotificationsAsync(
            Guid organizationId,
            string actorRole,
            Employee employee,
            string requestType)
        {
            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();
                var employeeName = $"{employee.FirstName} {employee.LastName}";

                if (actorRole == "Employee")
                {
                    // Notify HR in the same department.
                    var hrUsers = await (
                        from u in db.Users
                        join ur in db.UserRoles on u.Id equals ur.UserId
                        join r in db.Roles on ur.RoleId equals r.Id
                        join emp in db.Employees on u.Id equals emp.UserId
                        where u.OrganizationId == organizationId
                              && r.Name == "HR"
                              && emp.DepartmentId == employee.DepartmentId
                        select u.Id
                    ).ToListAsync();

                    foreach (var hrUserId in hrUsers)
                    {
                        await _notificationService.CreateNotificationAsync(
                            hrUserId,
                            "New Asset Request",
                            $"{employeeName} submitted a {requestType} request.",
                            "/hr/asset-requests");
                    }
                }
                else if (actorRole == SystemRoles.HR)
                {
                    // Notify OrgAdmin.
                    var adminIds = await (
                        from u in db.Users
                        join ur in db.UserRoles on u.Id equals ur.UserId
                        join r in db.Roles on ur.RoleId equals r.Id
                        where u.OrganizationId == organizationId && r.Name == "OrgAdmin"
                        select u.Id
                    ).ToListAsync();

                    foreach (var adminId in adminIds)
                    {
                        await _notificationService.CreateNotificationAsync(
                            adminId,
                            "HR Asset Request",
                            $"{employeeName} (HR) submitted a {requestType} request.",
                            "/orgadmin/asset-requests");
                    }
                }
            }
            catch
            {
                // Notification failure must never break the request submission.
            }
        }

        private async Task NotifyRequesterAsync(
            Guid employeeId,
            string title,
            string message)
        {
            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();

                var userId = await db.Employees
                    .Where(e => e.EmployeeId == employeeId)
                    .Select(e => e.UserId)
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrWhiteSpace(userId))
                {
                    await _notificationService.CreateNotificationAsync(
                        userId, title, message, "/my-assets/requests");
                }
            }
            catch
            {
                // Notification failure must never surface to callers.
            }
        }
    }
}