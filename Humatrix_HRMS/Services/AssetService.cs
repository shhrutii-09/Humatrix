using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs.Assets;
using Humatrix_HRMS.Infrastructure.Constants;
using Humatrix_HRMS.Infrastructure.Services;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services.Assets
{
    /// <summary>
    /// Central service for all asset operations.
    ///
    /// ROLE SCOPING RULES (enforced here, not only in UI):
    ///   OrgAdmin  — sees/manages everything within their organisation.
    ///   HR        — sees/manages only assets and employees of their own department.
    ///   Employee  — sees/raises requests only for their own assigned assets.
    ///
    /// WORKFLOW SUMMARY:
    ///   Assign      : OrgAdmin/HR → asset must be Available, employee in same dept.
    ///   Return      : Employee raises Return request → HR/OrgAdmin approves → asset Available.
    ///   Repair      : Employee raises Repair request → HR/OrgAdmin approves → asset InRepair
    ///                 → OrgAdmin/HR calls CompleteRepairAsync → asset back to Assigned/Available.
    ///   Replacement : Employee raises Replacement request → HR/OrgAdmin approves with a chosen
    ///                 Available asset → old asset returned, new asset assigned atomically.
    ///
    /// CONFLICT RULES:
    ///   - If a Repair request is Pending/Approved for an asset, a Return request is blocked
    ///     (employee must cancel Repair first, or HR/Admin must handle it explicitly).
    ///   - Only ONE pending request of the same type per asset at a time (DB unique index + code).
    ///   - An asset that is InRepair cannot have a new Repair request raised.
    ///   - Replacement requires the replacement asset to be Available and same-dept compatible.
    /// </summary>
    public class AssetService
    {
        private readonly ApplicationDbContext _db;
        private readonly ActivityLogService _activityLog;
        private readonly NotificationEngine _notifications;

        public AssetService(
            ApplicationDbContext db,
            ActivityLogService activityLog,
            NotificationEngine notifications)
        {
            _db = db;
            _activityLog = activityLog;
            _notifications = notifications;
        }

        // =====================================================================
        // ASSET CRUD
        // =====================================================================

        /// <summary>
        /// Creates a new asset. Only OrgAdmin may create assets.
        /// Assigns an auto-generated AssetCode within a transaction to prevent duplicates.
        /// </summary>
        public async Task<AssetDto> CreateAssetAsync(
            Guid organizationId,
            CreateAssetDto dto,
            string actorUserId,
            string actorRole)
        {
            await ValidateDepartmentAsync(dto.DepartmentId, organizationId);

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var code = await AssetCodeGenerator.NextCodeAsync(_db, organizationId, dto.Category);

                var asset = new Asset
                {
                    OrganizationId = organizationId,
                    DepartmentId = dto.DepartmentId,
                    Name = dto.Name.Trim(),
                    Category = dto.Category.Trim(),
                    AssetCode = code,
                    Brand = dto.Brand?.Trim(),
                    Model = dto.Model?.Trim(),
                    SerialNumber = dto.SerialNumber?.Trim(),
                    PurchasePrice = dto.PurchasePrice,
                    PurchaseDate = dto.PurchaseDate,
                    WarrantyExpiryDate = dto.WarrantyExpiryDate,
                    Notes = dto.Notes?.Trim(),
                    Status = AssetStatus.Available,
                    CreatedByUserId = actorUserId,
                    CreatedAt = DateTime.UtcNow
                };

                _db.Assets.Add(asset);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                _ = _activityLog.LogAsync(
                    organizationId, AssetModuleName.Asset, "Created",
                    "Asset", asset.AssetId, actorUserId, actorRole,
                    newValues: new { asset.AssetCode, asset.Name, asset.Category });

                return MapAssetDto(asset, employeeName: null);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Updates non-status, non-assignment fields of an asset.
        /// Cannot change department while the asset is Assigned.
        /// HR can only update assets belonging to their department.
        /// </summary>
        public async Task<AssetDto> UpdateAssetAsync(
            Guid assetId,
            Guid organizationId,
            UpdateAssetDto dto,
            string actorUserId,
            string actorRole,
            Guid? callerDepartmentId = null)   // required when actorRole == "HR"
        {
            var asset = await GetAssetOrThrowAsync(assetId, organizationId);

            // HR scope check
            if (actorRole == "HR")
                EnsureHRCanAccessAsset(asset, callerDepartmentId);

            if (dto.DepartmentId.HasValue && dto.DepartmentId != asset.DepartmentId)
            {
                await ValidateDepartmentAsync(dto.DepartmentId, organizationId);

                if (asset.Status == AssetStatus.Assigned)
                    throw new InvalidOperationException(
                        "Cannot change the department of an asset that is currently assigned.");
            }

            var old = new { asset.Name, asset.Brand, asset.Model, asset.SerialNumber, asset.Notes, asset.DepartmentId };

            asset.Name = dto.Name?.Trim() ?? asset.Name;
            asset.Brand = dto.Brand?.Trim() ?? asset.Brand;
            asset.Model = dto.Model?.Trim() ?? asset.Model;
            asset.SerialNumber = dto.SerialNumber?.Trim() ?? asset.SerialNumber;
            asset.DepartmentId = dto.DepartmentId ?? asset.DepartmentId;
            asset.PurchasePrice = dto.PurchasePrice ?? asset.PurchasePrice;
            asset.PurchaseDate = dto.PurchaseDate ?? asset.PurchaseDate;
            asset.WarrantyExpiryDate = dto.WarrantyExpiryDate ?? asset.WarrantyExpiryDate;
            asset.Notes = dto.Notes?.Trim() ?? asset.Notes;
            asset.UpdatedAt = DateTime.UtcNow;
            asset.UpdatedByUserId = actorUserId;

            await _db.SaveChangesAsync();

            _ = _activityLog.LogAsync(
                organizationId, AssetModuleName.Asset, "Updated",
                "Asset", assetId, actorUserId, actorRole,
                oldValues: old,
                newValues: new { asset.Name, asset.Brand, asset.Model, asset.SerialNumber, asset.Notes, asset.DepartmentId });

            var currentEmployeeName = asset.CurrentEmployeeId.HasValue
                ? await GetEmployeeNameAsync(asset.CurrentEmployeeId.Value)
                : null;

            return MapAssetDto(asset, currentEmployeeName);
        }

        /// <summary>
        /// Retires an asset permanently.
        /// Blocked if the asset is currently Assigned (must be returned first).
        /// Blocked if the asset has any Pending requests (must be resolved first).
        /// </summary>
        public async Task RetireAssetAsync(
            Guid assetId,
            Guid organizationId,
            string actorUserId,
            string actorRole)
        {
            var asset = await GetAssetOrThrowAsync(assetId, organizationId);

            if (asset.Status == AssetStatus.Assigned)
                throw new InvalidOperationException(
                    "Cannot retire an asset that is currently assigned to an employee. Process a return first.");

            var pendingRequests = await _db.AssetRequests.AnyAsync(r =>
                r.AssetId == assetId &&
                r.Status == AssetRequestStatus.Pending);

            if (pendingRequests)
                throw new InvalidOperationException(
                    "Cannot retire an asset with pending requests. Resolve all pending requests first.");

            AssetStatusMachine.EnsureCanTransition(asset.Status, AssetStatus.Retired);

            asset.Status = AssetStatus.Retired;
            asset.UpdatedAt = DateTime.UtcNow;
            asset.UpdatedByUserId = actorUserId;

            await _db.SaveChangesAsync();

            _ = _activityLog.LogAsync(
                organizationId, AssetModuleName.Asset, "Retired",
                "Asset", assetId, actorUserId, actorRole);
        }

        // =====================================================================
        // ASSIGNMENT  (OrgAdmin assigns to anyone; HR assigns within their dept)
        // =====================================================================

        /// <summary>
        /// Assigns an Available asset to an employee.
        ///
        /// Validations:
        ///   - Asset must be Available.
        ///   - Employee must belong to the same org.
        ///   - If asset has a DepartmentId, employee must be in that department.
        ///   - HR caller can only assign assets that belong to their department.
        ///   - HR caller can only assign to employees in their department.
        ///   - Employee must not already hold an asset of the same category (configurable).
        ///
        /// Atomically: creates AssetAssignment, sets Status=Assigned, sets CurrentEmployeeId.
        /// </summary>
        public async Task<AssignmentDto> AssignAssetAsync(
            Guid organizationId,
            AssignAssetDto dto,
            string actorUserId,
            string actorRole,
            Guid? callerDepartmentId = null)   // required when actorRole == "HR"
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var asset = await GetAssetOrThrowAsync(dto.AssetId, organizationId);

                // HR scope: can only assign assets from their department
                if (actorRole == "HR")
                    EnsureHRCanAccessAsset(asset, callerDepartmentId);

                if (asset.Status != AssetStatus.Available)
                    throw new InvalidOperationException(
                        $"Asset '{asset.AssetCode}' is not available for assignment (current status: {asset.Status}).");

                var employee = await _db.Employees
                    .FirstOrDefaultAsync(e =>
                        e.EmployeeId == dto.EmployeeId &&
                        e.OrganizationId == organizationId)
                    ?? throw new KeyNotFoundException("Employee not found in this organisation.");

                // HR scope: can only assign to employees in their department
                if (actorRole == "HR" && callerDepartmentId.HasValue &&
                    employee.DepartmentId != callerDepartmentId.Value)
                    throw new InvalidOperationException(
                        "HR can only assign assets to employees within their own department.");

                // Department compatibility check
                if (asset.DepartmentId.HasValue &&
                    employee.DepartmentId != asset.DepartmentId.Value)
                    throw new InvalidOperationException(
                        "This asset is restricted to a specific department. The selected employee is not in that department.");

                AssetStatusMachine.EnsureCanTransition(asset.Status, AssetStatus.Assigned);

                var assignment = new AssetAssignment
                {
                    AssetId = asset.AssetId,
                    EmployeeId = employee.EmployeeId,
                    OrganizationId = organizationId,
                    AssignedAt = DateTime.UtcNow,
                    AssignedByUserId = actorUserId,
                    AssignmentNotes = dto.Notes?.Trim()
                };

                _db.AssetAssignments.Add(assignment);

                asset.Status = AssetStatus.Assigned;
                asset.CurrentEmployeeId = employee.EmployeeId;
                asset.UpdatedAt = DateTime.UtcNow;
                asset.UpdatedByUserId = actorUserId;

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                var employeeName = $"{employee.FirstName} {employee.LastName}".Trim();

                _ = _activityLog.LogAsync(
                    organizationId, AssetModuleName.Asset, "Assigned",
                    "Asset", asset.AssetId, actorUserId, actorRole,
                    newValues: new { asset.AssetCode, AssignedTo = employeeName });

                _ = _notifications.SendAssetAssignedAsync(
                    employee.UserId, asset.Name, asset.AssetCode,
                    asset.AssetId, organizationId, actorUserId);

                return MapAssignmentDto(assignment, asset, employeeName);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Directly returns an asset (admin/HR action without a request).
        /// Use this for forced returns. Employee-initiated returns go through CreateAssetRequestAsync.
        ///
        /// Atomically: closes AssetAssignment, sets Status=Available, clears CurrentEmployeeId.
        /// Also auto-cancels any Pending requests on this asset.
        /// </summary>
        public async Task ReturnAssetAsync(
            Guid organizationId,
            ReturnAssetDto dto,
            string actorUserId,
            string actorRole,
            Guid? callerDepartmentId = null)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var asset = await GetAssetOrThrowAsync(dto.AssetId, organizationId);

                if (actorRole == "HR")
                    EnsureHRCanAccessAsset(asset, callerDepartmentId);

                if (asset.Status != AssetStatus.Assigned)
                    throw new InvalidOperationException(
                        $"Asset '{asset.AssetCode}' is not currently assigned.");

                var activeAssignment = await _db.AssetAssignments
                    .FirstOrDefaultAsync(a => a.AssetId == asset.AssetId && a.ReturnedAt == null)
                    ?? throw new InvalidOperationException("No active assignment found for this asset.");

                AssetStatusMachine.EnsureCanTransition(asset.Status, AssetStatus.Available);

                activeAssignment.ReturnedAt = DateTime.UtcNow;
                activeAssignment.ReturnedByUserId = actorUserId;
                activeAssignment.ReturnNotes = dto.Notes?.Trim();

                asset.Status = AssetStatus.Available;
                asset.CurrentEmployeeId = null;
                asset.UpdatedAt = DateTime.UtcNow;
                asset.UpdatedByUserId = actorUserId;

                // Auto-cancel any pending requests on this asset (e.g., a pending Return
                // that was superseded by an admin forced-return).
                var pendingRequests = await _db.AssetRequests
                    .Where(r => r.AssetId == asset.AssetId && r.Status == AssetRequestStatus.Pending)
                    .ToListAsync();

                foreach (var req in pendingRequests)
                {
                    req.Status = AssetRequestStatus.Cancelled;
                    req.ReviewNotes = $"Auto-cancelled: asset was directly returned by {actorRole} on {DateTime.UtcNow:yyyy-MM-dd}.";
                    req.ReviewedAt = DateTime.UtcNow;
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                _ = _activityLog.LogAsync(
                    organizationId, AssetModuleName.Asset, "Returned",
                    "Asset", asset.AssetId, actorUserId, actorRole,
                    newValues: new { asset.AssetCode, ReturnedBy = actorUserId });
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // =====================================================================
        // REPAIR COMPLETION  (OrgAdmin or HR after external repair is done)
        // =====================================================================

        /// <summary>
        /// Marks an InRepair asset as repaired.
        ///
        /// Logic:
        ///   - If the asset was assigned before going to repair (CurrentEmployeeId is set),
        ///     it goes back to Assigned and a new AssetAssignment record is created.
        ///   - Otherwise it becomes Available.
        ///
        /// Also marks the related Approved Repair request as Completed.
        /// </summary>
        /// <summary>
        /// Marks an InRepair asset as repaired.
        ///
        /// Logic:
        ///   - If the asset was assigned before going to repair (CurrentEmployeeId is set),
        ///     it goes back to Assigned and a new AssetAssignment record is created.
        ///   - Otherwise it becomes Available.
        ///
        /// Also marks the related Approved Repair request as Completed.
        /// </summary>
        public async Task CompleteRepairAsync(
            Guid assetId,
            Guid organizationId,
            string actorUserId,
            string actorRole,
            string? completionNotes = null,
            Guid? callerDepartmentId = null)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var asset = await GetAssetOrThrowAsync(assetId, organizationId);

                if (actorRole == "HR")
                    EnsureHRCanAccessAsset(asset, callerDepartmentId);

                if (asset.Status != AssetStatus.InRepair)
                    throw new InvalidOperationException(
                        "Only assets currently in repair can be marked as repaired.");

                // Find the Approved Repair request for audit/notification
                var repairRequest = await _db.AssetRequests
                    .Include(r => r.RequestedByEmployee)
                    .FirstOrDefaultAsync(r =>
                        r.AssetId == assetId &&
                        r.RequestType == AssetRequestType.Repair &&
                        r.Status == AssetRequestStatus.Approved);

                if (asset.CurrentEmployeeId.HasValue)
                {
                    // Asset goes back to the employee — re-assign
                    // Use the state machine to validate the transition
                    AssetStatusMachine.EnsureCanTransition(asset.Status, AssetStatus.Assigned);

                    asset.Status = AssetStatus.Assigned;

                    // Check if there's already an active assignment for this asset
                    var existingActiveAssignment = await _db.AssetAssignments
                        .FirstOrDefaultAsync(a => a.AssetId == asset.AssetId && a.ReturnedAt == null);

                    if (existingActiveAssignment != null)
                    {
                        // Close the existing assignment first (the one from before repair)
                        existingActiveAssignment.ReturnedAt = DateTime.UtcNow;
                        existingActiveAssignment.ReturnedByUserId = actorUserId;
                        existingActiveAssignment.ReturnNotes = "Auto-closed: Asset returned from repair.";
                    }

                    // Create new assignment
                    var newAssignment = new AssetAssignment
                    {
                        AssetId = asset.AssetId,
                        EmployeeId = asset.CurrentEmployeeId.Value,
                        OrganizationId = organizationId,
                        AssignedAt = DateTime.UtcNow,
                        AssignedByUserId = actorUserId,
                        AssignmentNotes = completionNotes?.Trim() ?? "Re-assigned after repair completion."
                    };
                    _db.AssetAssignments.Add(newAssignment);
                }
                else
                {
                    // No employee was assigned - asset becomes available
                    AssetStatusMachine.EnsureCanTransition(asset.Status, AssetStatus.Available);
                    asset.Status = AssetStatus.Available;
                }

                asset.UpdatedAt = DateTime.UtcNow;
                asset.UpdatedByUserId = actorUserId;

                // Close the repair request
                if (repairRequest != null)
                {
                    repairRequest.Status = AssetRequestStatus.Completed;
                    repairRequest.ReviewNotes = (repairRequest.ReviewNotes ?? "") +
                        $" | Repair completed by {actorRole} on {DateTime.UtcNow:yyyy-MM-dd}. Notes: {completionNotes}";
                    repairRequest.ReviewedAt = DateTime.UtcNow;
                    repairRequest.ReviewedByEmployeeId = !string.IsNullOrEmpty(actorUserId)
                        ? await GetEmployeeIdByUserIdAsync(actorUserId, organizationId)
                        : null;
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                _ = _activityLog.LogAsync(
                    organizationId, AssetModuleName.Asset, "RepairCompleted",
                    "Asset", asset.AssetId, actorUserId, actorRole,
                    newValues: new { asset.AssetCode, asset.Status });

                // Notify the employee if asset was returned to them
                if (asset.CurrentEmployeeId.HasValue && repairRequest?.RequestedByEmployee != null)
                {
                    _ = _notifications.SendAssetAssignedAsync(
                        repairRequest.RequestedByEmployee.UserId,
                        asset.Name, asset.AssetCode,
                        asset.AssetId, organizationId, actorUserId);
                }
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync();
                var innerMessage = ex.InnerException?.Message ?? ex.Message;
                throw new InvalidOperationException($"Database error during repair completion: {innerMessage}", ex);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // Add this helper method to get EmployeeId from UserId
        private async Task<Guid?> GetEmployeeIdByUserIdAsync(string userId, Guid organizationId)
        {
            var employee = await _db.Employees
                .FirstOrDefaultAsync(e => e.UserId == userId && e.OrganizationId == organizationId);
            return employee?.EmployeeId;
        }
        // =====================================================================
        // ASSET REQUESTS  (Repair / Replacement / Return)
        // =====================================================================

        /// <summary>
        /// Employee or HR raises an asset request.
        ///
        /// Validation rules:
        ///   1. RequestType must be valid.
        ///   2. Asset must be assigned to the requestor.
        ///   3. No duplicate pending request of the same type on the same asset.
        ///   4. Return blocked if there is an active (Pending/Approved) Repair request.
        ///      (Cannot return an asset that is being repaired or pending repair approval.)
        ///   5. Repair blocked if asset is already InRepair.
        ///   6. Replacement blocked if asset is InRepair.
        ///   7. HR requestor may only raise requests for employees in their own department.
        /// </summary>
        public async Task<AssetRequestDto> CreateAssetRequestAsync(
            Guid organizationId,
            Guid requestedByEmployeeId,
            string requestorRole,
            CreateAssetRequestDto dto,
            Guid? callerDepartmentId = null)
        {
            if (!IsValidRequestType(dto.RequestType))
                throw new ArgumentException($"Invalid request type '{dto.RequestType}'. Valid: Repair, Replacement, Return.");

            var asset = await GetAssetOrThrowAsync(dto.AssetId, organizationId);

            // ── Rule 1: Requestor must hold this asset ──────────────────────────
            if (asset.CurrentEmployeeId != requestedByEmployeeId)
                throw new InvalidOperationException(
                    "You can only raise a request for an asset that is currently assigned to you.");

            // ── Rule 2: HR scope check ──────────────────────────────────────────
            if (requestorRole == "HR")
                EnsureHRCanAccessAsset(asset, callerDepartmentId);

            // ── Rule 3: Repair blocked if already InRepair ──────────────────────
            if (dto.RequestType == AssetRequestType.Repair && asset.Status == AssetStatus.InRepair)
                throw new InvalidOperationException(
                    "This asset is already in repair. No new repair request is needed.");

            // ── Rule 4: Replacement blocked if InRepair ─────────────────────────
            if (dto.RequestType == AssetRequestType.Replacement && asset.Status == AssetStatus.InRepair)
                throw new InvalidOperationException(
                    "Cannot raise a replacement request for an asset that is currently in repair.");

            // ── Rule 5: Return blocked if active Repair request exists ──────────
            if (dto.RequestType == AssetRequestType.Return)
            {
                var activeRepair = await _db.AssetRequests.AnyAsync(r =>
                    r.AssetId == dto.AssetId &&
                    r.RequestType == AssetRequestType.Repair &&
                    (r.Status == AssetRequestStatus.Pending || r.Status == AssetRequestStatus.Approved));

                if (activeRepair)
                    throw new InvalidOperationException(
                        "Cannot raise a Return request while a Repair request is pending or approved for this asset. " +
                        "Please cancel or wait for the Repair request to be resolved first.");
            }

            // ── Rule 6: No duplicate pending request of the same type ───────────
            var duplicate = await _db.AssetRequests.AnyAsync(r =>
                r.AssetId == dto.AssetId &&
                r.RequestType == dto.RequestType &&
                r.Status == AssetRequestStatus.Pending);

            if (duplicate)
                throw new InvalidOperationException(
                    $"A pending '{dto.RequestType}' request already exists for this asset. " +
                    "Please wait for it to be reviewed before submitting another.");

            var request = new AssetRequest
            {
                AssetId = asset.AssetId,
                OrganizationId = organizationId,
                RequestedByEmployeeId = requestedByEmployeeId,
                RequestorRole = requestorRole,
                RequestType = dto.RequestType,
                Reason = dto.Reason.Trim(),
                Status = AssetRequestStatus.Pending,
                RequestedAt = DateTime.UtcNow
            };

            _db.AssetRequests.Add(request);
            await _db.SaveChangesAsync();

            var actorUserId = await GetUserIdForEmployeeAsync(requestedByEmployeeId);

            _ = _activityLog.LogAsync(
                organizationId, AssetModuleName.AssetRequest, "Raised",
                "AssetRequest", request.AssetRequestId,
                actorUserId, requestorRole,
                newValues: new { asset.AssetCode, dto.RequestType, dto.Reason });

            try
            {
                var deptId = asset.DepartmentId ?? Guid.Empty;
                await _notifications.SendAssetRequestRaisedAsync(
                    asset.Name, asset.AssetCode, dto.RequestType,
                    requestedByEmployeeId, request.AssetRequestId,
                    organizationId, deptId, requestorRole, actorUserId ?? "");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Notification Warning] {ex.Message}");
            }

            return await GetAssetRequestDtoAsync(request.AssetRequestId);
        }

        /// <summary>
        /// HR or OrgAdmin reviews (approves or rejects) an asset request.
        ///
        /// APPROVAL OUTCOMES BY TYPE:
        ///
        ///   Return:
        ///     - Active assignment closed (ReturnedAt set).
        ///     - Asset.Status → Available, Asset.CurrentEmployeeId → null.
        ///     - Request.Status → Completed.
        ///
        ///   Repair:
        ///     - Asset.Status → InRepair (assignment record STAYS open — employee still holds it).
        ///     - Request.Status → Approved (stays Approved until CompleteRepairAsync is called).
        ///     - On CompleteRepairAsync: asset goes back to Assigned/Available, request → Completed.
        ///
        ///   Replacement:
        ///     - Replacement asset must be provided (Available, same dept compatible).
        ///     - Old asset returned (assignment closed, Status → Available).
        ///     - New AssetAssignment created for the same employee.
        ///     - New asset Status → Assigned, CurrentEmployeeId set.
        ///     - Request.ReplacementAssetId set.
        ///     - Request.Status → Completed.
        ///
        /// REJECTION:
        ///   - Request.Status → Rejected. No asset changes.
        ///
        /// SCOPE RULES:
        ///   - HR can only review requests for assets/employees in their department.
        ///   - OrgAdmin can review all requests in the org.
        /// </summary>
        public async Task<AssetRequestDto> ReviewAssetRequestAsync(
            Guid organizationId,
            Guid reviewerEmployeeId,
            ReviewAssetRequestDto dto,
            string actorUserId,
            string actorRole,
            Guid? callerDepartmentId = null)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var request = await _db.AssetRequests
                    .Include(r => r.Asset)
                    .Include(r => r.RequestedByEmployee)
                    .FirstOrDefaultAsync(r =>
                        r.AssetRequestId == dto.AssetRequestId &&
                        r.OrganizationId == organizationId)
                    ?? throw new KeyNotFoundException("Asset request not found.");

                if (request.Status != AssetRequestStatus.Pending)
                    throw new InvalidOperationException(
                        $"This request is already '{request.Status}' and cannot be reviewed again.");

                // HR scope check: request must belong to HR's department
                if (actorRole == "HR")
                {
                    EnsureHRCanAccessAsset(request.Asset, callerDepartmentId);

                    // Also check that the requester is in HR's department
                    if (request.RequestedByEmployee.DepartmentId != callerDepartmentId)
                        throw new InvalidOperationException(
                            "HR can only review requests from employees within their own department.");
                }

                request.ReviewedByEmployeeId = reviewerEmployeeId;
                request.ReviewedAt = DateTime.UtcNow;
                request.ReviewNotes = dto.Notes?.Trim();

                if (!dto.Approve)
                {
                    request.Status = AssetRequestStatus.Rejected;
                }
                else
                {
                    switch (request.RequestType)
                    {
                        // ── RETURN ──────────────────────────────────────────────────────────
                        case AssetRequestType.Return:
                            await ApplyReturnInternalAsync(request.Asset, actorUserId,
                                returnNotes: $"Approved via return request by {actorRole}.");
                            request.Status = AssetRequestStatus.Completed;
                            break;

                        // ── REPAIR ──────────────────────────────────────────────────────────
                        case AssetRequestType.Repair:
                            // Re-validate: ensure the asset is still Assigned (not returned in the meantime)
                            if (request.Asset.Status != AssetStatus.Assigned)
                                throw new InvalidOperationException(
                                    $"Cannot approve Repair: asset is currently '{request.Asset.Status}', not Assigned.");

                            AssetStatusMachine.EnsureCanTransition(request.Asset.Status, AssetStatus.InRepair);

                            request.Asset.Status = AssetStatus.InRepair;
                            request.Asset.UpdatedAt = DateTime.UtcNow;
                            request.Asset.UpdatedByUserId = actorUserId;
                            // NOTE: CurrentEmployeeId is intentionally KEPT set so CompleteRepairAsync
                            // knows who to re-assign the asset to after repair.
                            // The assignment record stays open (ReturnedAt = null) as well.
                            request.Status = AssetRequestStatus.Approved;
                            // Status will move to Completed when CompleteRepairAsync() is called.
                            break;

                        // ── REPLACEMENT ─────────────────────────────────────────────────────
                        case AssetRequestType.Replacement:
                            if (!dto.ReplacementAssetId.HasValue)
                                throw new ArgumentException(
                                    "ReplacementAssetId is required when approving a Replacement request.");

                            var replacementAsset = await _db.Assets
                                .FirstOrDefaultAsync(a =>
                                    a.AssetId == dto.ReplacementAssetId.Value &&
                                    a.OrganizationId == organizationId)
                                ?? throw new KeyNotFoundException(
                                    "Replacement asset not found in this organisation.");

                            if (replacementAsset.Status != AssetStatus.Available)
                                throw new InvalidOperationException(
                                    $"Replacement asset '{replacementAsset.AssetCode}' is not Available " +
                                    $"(current status: {replacementAsset.Status}). Select a different asset.");

                            // Dept compatibility: replacement must be compatible with employee's dept
                            if (replacementAsset.DepartmentId.HasValue &&
                                request.RequestedByEmployee.DepartmentId != replacementAsset.DepartmentId)
                                throw new InvalidOperationException(
                                    "Replacement asset belongs to a different department than the requesting employee.");

                            // HR scope: replacement asset must also be in HR's dept
                            if (actorRole == "HR")
                                EnsureHRCanAccessAsset(replacementAsset, callerDepartmentId);

                            // 1. Return old asset
                            await ApplyReturnInternalAsync(request.Asset, actorUserId,
                                returnNotes: $"Returned as part of replacement approval. Request: {request.AssetRequestId}");

                            // 2. Assign replacement asset to the same employee
                            var newAssignment = new AssetAssignment
                            {
                                AssetId = replacementAsset.AssetId,
                                EmployeeId = request.RequestedByEmployeeId,
                                OrganizationId = organizationId,
                                AssignedAt = DateTime.UtcNow,
                                AssignedByUserId = actorUserId,
                                AssignmentNotes = $"Replacement for {request.Asset.AssetCode}. Request: {request.AssetRequestId}"
                            };
                            _db.AssetAssignments.Add(newAssignment);

                            replacementAsset.Status = AssetStatus.Assigned;
                            replacementAsset.CurrentEmployeeId = request.RequestedByEmployeeId;
                            replacementAsset.UpdatedAt = DateTime.UtcNow;
                            replacementAsset.UpdatedByUserId = actorUserId;

                            request.ReplacementAssetId = replacementAsset.AssetId;
                            request.Status = AssetRequestStatus.Completed;

                            // Notify the employee about their new asset
                            var empUserId = request.RequestedByEmployee.UserId;
                            if (!string.IsNullOrEmpty(empUserId))
                            {
                                _ = _notifications.SendAssetAssignedAsync(
                                    empUserId, replacementAsset.Name, replacementAsset.AssetCode,
                                    replacementAsset.AssetId, organizationId, actorUserId);
                            }
                            break;

                        default:
                            throw new InvalidOperationException(
                                $"Unknown request type '{request.RequestType}'.");
                    }
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                _ = _activityLog.LogAsync(
                    organizationId, AssetModuleName.AssetRequest,
                    dto.Approve ? "Approved" : "Rejected",
                    "AssetRequest", request.AssetRequestId,
                    actorUserId, actorRole,
                    newValues: new
                    {
                        request.Asset.AssetCode,
                        request.RequestType,
                        request.Status,
                        ReplacementAsset = dto.ReplacementAssetId,
                        Note = dto.Notes
                    });

                _ = NotifyAssetRequestReviewedAsync(request, organizationId, actorUserId);

                return await GetAssetRequestDtoAsync(request.AssetRequestId);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Cancels a pending asset request.
        /// Employee can cancel their own. HR/OrgAdmin can cancel requests in their scope.
        /// Cannot cancel Approved or Completed requests.
        /// </summary>
        public async Task CancelAssetRequestAsync(
            Guid organizationId,
            Guid assetRequestId,
            Guid callerEmployeeId,
            string callerRole,
            string actorUserId,
            Guid? callerDepartmentId = null)
        {
            var request = await _db.AssetRequests
                .Include(r => r.Asset)
                .Include(r => r.RequestedByEmployee)
                .FirstOrDefaultAsync(r =>
                    r.AssetRequestId == assetRequestId &&
                    r.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("Asset request not found.");

            if (request.Status != AssetRequestStatus.Pending)
                throw new InvalidOperationException(
                    $"Only Pending requests can be cancelled. Current status: {request.Status}.");

            // Employee can only cancel their own requests
            if (callerRole == "Employee" && request.RequestedByEmployeeId != callerEmployeeId)
                throw new InvalidOperationException("You can only cancel your own requests.");

            // HR scope check
            if (callerRole == "HR")
                EnsureHRCanAccessAsset(request.Asset, callerDepartmentId);

            request.Status = AssetRequestStatus.Cancelled;
            request.ReviewedAt = DateTime.UtcNow;
            request.ReviewNotes = $"Cancelled by {callerRole} on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC.";

            await _db.SaveChangesAsync();

            _ = _activityLog.LogAsync(
                organizationId, AssetModuleName.AssetRequest, "Cancelled",
                "AssetRequest", assetRequestId, actorUserId, callerRole);
        }

        // =====================================================================
        // PROCUREMENT  (HR raises → OrgAdmin approves → OrgAdmin fulfils)
        // =====================================================================

        /// <summary>
        /// HR raises a procurement request for their department.
        /// Duplicate pending requests for the same category in the same department are blocked.
        /// </summary>
        public async Task<ProcurementRequestDto> CreateProcurementRequestAsync(
            Guid organizationId,
            Guid departmentId,
            Guid requestedByEmployeeId,
            CreateProcurementRequestDto dto,
            string actorUserId,
            Guid? callerDepartmentId = null)
        {
            await ValidateDepartmentAsync(departmentId, organizationId);

            // HR can only raise procurement for their own department
            if (callerDepartmentId.HasValue && callerDepartmentId != departmentId)
                throw new InvalidOperationException(
                    "HR can only raise procurement requests for their own department.");

            // Block duplicate pending procurement for same category in same dept
            var duplicate = await _db.ProcurementRequests.AnyAsync(p =>
                p.OrganizationId == organizationId &&
                p.DepartmentId == departmentId &&
                p.AssetCategory == dto.AssetCategory.Trim() &&
                p.Status == ProcurementStatus.Pending);

            if (duplicate)
                throw new InvalidOperationException(
                    $"A pending procurement request for '{dto.AssetCategory}' already exists for this department.");

            var procurement = new ProcurementRequest
            {
                OrganizationId = organizationId,
                DepartmentId = departmentId,
                RequestedByEmployeeId = requestedByEmployeeId,
                AssetCategory = dto.AssetCategory.Trim(),
                AssetName = dto.AssetName?.Trim(),
                QuantityRequested = dto.QuantityRequested,
                Justification = dto.Justification.Trim(),
                Status = ProcurementStatus.Pending,
                RequestedAt = DateTime.UtcNow
            };

            _db.ProcurementRequests.Add(procurement);
            await _db.SaveChangesAsync();

            _ = _activityLog.LogAsync(
                organizationId, AssetModuleName.Procurement, "Raised",
                "ProcurementRequest", procurement.ProcurementRequestId,
                actorUserId, "HR",
                newValues: new { dto.AssetCategory, dto.QuantityRequested });

            _ = _notifications.SendProcurementRaisedAsync(
                dto.AssetCategory, dto.QuantityRequested, departmentId,
                procurement.ProcurementRequestId, organizationId, actorUserId);

            return await GetProcurementDtoAsync(procurement.ProcurementRequestId);
        }

        /// <summary>OrgAdmin approves or rejects a procurement request.</summary>
        public async Task<ProcurementRequestDto> ReviewProcurementAsync(
            Guid organizationId,
            ReviewProcurementDto dto,
            string actorUserId)
        {
            var procurement = await _db.ProcurementRequests
                .Include(p => p.RequestedByEmployee)
                .FirstOrDefaultAsync(p =>
                    p.ProcurementRequestId == dto.ProcurementRequestId &&
                    p.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("Procurement request not found.");

            if (procurement.Status != ProcurementStatus.Pending)
                throw new InvalidOperationException(
                    "Only Pending procurement requests can be reviewed.");

            procurement.Status = dto.Approve ? ProcurementStatus.Approved : ProcurementStatus.Rejected;
            procurement.ReviewedByUserId = actorUserId;
            procurement.ReviewedAt = DateTime.UtcNow;
            procurement.ReviewNotes = dto.Notes?.Trim();

            await _db.SaveChangesAsync();

            _ = _activityLog.LogAsync(
                organizationId, AssetModuleName.Procurement,
                dto.Approve ? "Approved" : "Rejected",
                "ProcurementRequest", procurement.ProcurementRequestId,
                actorUserId, "OrgAdmin");

            var hrEmployee = procurement.RequestedByEmployee;
            if (hrEmployee != null)
            {
                _ = _notifications.SendProcurementReviewedAsync(
                    hrEmployee.UserId, procurement.AssetCategory,
                    procurement.QuantityRequested, dto.Approve,
                    procurement.ProcurementRequestId, organizationId, actorUserId);
            }

            return await GetProcurementDtoAsync(procurement.ProcurementRequestId);
        }

        /// <summary>
        /// OrgAdmin fulfils an approved procurement request by auto-creating assets.
        /// Can be called in batches (partial fulfilment allowed, QuantityToCreate ≤ remaining).
        /// Once QuantityFulfilled >= QuantityRequested the procurement moves to Fulfilled.
        /// </summary>
        public async Task<List<AssetDto>> FulfilProcurementAsync(
            Guid organizationId,
            FulfilProcurementDto dto,
            string actorUserId)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var procurement = await _db.ProcurementRequests
                    .FirstOrDefaultAsync(p =>
                        p.ProcurementRequestId == dto.ProcurementRequestId &&
                        p.OrganizationId == organizationId)
                    ?? throw new KeyNotFoundException("Procurement request not found.");

                if (procurement.Status != ProcurementStatus.Approved)
                    throw new InvalidOperationException(
                        "Only Approved procurement requests can be fulfilled.");

                var remaining = procurement.QuantityRequested - procurement.QuantityFulfilled;

                if (dto.QuantityToCreate < 1 || dto.QuantityToCreate > remaining)
                    throw new ArgumentException(
                        $"QuantityToCreate must be between 1 and {remaining} (remaining unfulfilled).");

                var createdAssets = new List<Asset>();

                for (int i = 0; i < dto.QuantityToCreate; i++)
                {
                    var code = await AssetCodeGenerator.NextCodeAsync(
                        _db, organizationId, procurement.AssetCategory);

                    var asset = new Asset
                    {
                        OrganizationId = organizationId,
                        DepartmentId = procurement.DepartmentId,
                        Name = dto.AssetName.Trim(),
                        Category = procurement.AssetCategory,
                        AssetCode = code,
                        Brand = dto.Brand?.Trim(),
                        Model = dto.Model?.Trim(),
                        PurchasePrice = dto.PurchasePrice,
                        PurchaseDate = dto.PurchaseDate,
                        WarrantyExpiryDate = dto.WarrantyExpiryDate,
                        Status = AssetStatus.Available,
                        CreatedByUserId = actorUserId,
                        CreatedAt = DateTime.UtcNow,
                        Notes = $"Created via procurement #{procurement.ProcurementRequestId}"
                    };

                    _db.Assets.Add(asset);
                    createdAssets.Add(asset);
                }

                procurement.QuantityFulfilled += dto.QuantityToCreate;

                if (procurement.QuantityFulfilled >= procurement.QuantityRequested)
                {
                    procurement.Status = ProcurementStatus.Fulfilled;
                    procurement.FulfilledAt = DateTime.UtcNow;
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                _ = _activityLog.LogAsync(
                    organizationId, AssetModuleName.Procurement, "Fulfilled",
                    "ProcurementRequest", procurement.ProcurementRequestId,
                    actorUserId, "OrgAdmin",
                    newValues: new { dto.QuantityToCreate, procurement.QuantityFulfilled });

                return createdAssets.Select(a => MapAssetDto(a, null)).ToList();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // =====================================================================
        // QUERIES
        // =====================================================================

        /// <summary>
        /// Paged asset list for OrgAdmin (all assets in org).
        /// </summary>
        public async Task<PagedResult<AssetListDto>> GetAssetsAsync(
            Guid organizationId,
            AssetFilterDto filter)
        {
            var q = _db.Assets
                .Include(a => a.CurrentEmployee)
                .Include(a => a.Department)
                .Where(a => a.OrganizationId == organizationId);

            q = ApplyAssetFilters(q, filter);

            return await ExecuteAssetPagedQueryAsync(q, filter);
        }

        /// <summary>
        /// Paged asset list for HR — only their department's assets
        /// plus org-wide (DepartmentId == null) assets.
        /// </summary>
        public async Task<PagedResult<AssetListDto>> GetAssetsForHRAsync(
            Guid organizationId,
            Guid hrDepartmentId,
            AssetFilterDto filter)
        {
            var q = _db.Assets
                .Include(a => a.CurrentEmployee)
                .Include(a => a.Department)
                .Where(a =>
                    a.OrganizationId == organizationId &&
                    (a.DepartmentId == hrDepartmentId || a.DepartmentId == null));

            q = ApplyAssetFilters(q, filter);

            return await ExecuteAssetPagedQueryAsync(q, filter);
        }

        public async Task<AssetDto> GetAssetByIdAsync(
            Guid assetId,
            Guid organizationId,
            string? callerRole = null,
            Guid? callerDepartmentId = null)
        {
            var asset = await _db.Assets
                .Include(a => a.CurrentEmployee)
                .Include(a => a.Department)
                .FirstOrDefaultAsync(a =>
                    a.AssetId == assetId && a.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("Asset not found.");

            if (callerRole == "HR")
                EnsureHRCanAccessAsset(asset, callerDepartmentId);

            var empName = asset.CurrentEmployee != null
                ? $"{asset.CurrentEmployee.FirstName} {asset.CurrentEmployee.LastName}".Trim()
                : null;

            return MapAssetDto(asset, empName);
        }

        public async Task<List<AssignmentDto>> GetAssetHistoryAsync(
            Guid assetId,
            Guid organizationId,
            string? callerRole = null,
            Guid? callerDepartmentId = null)
        {
            var asset = await GetAssetOrThrowAsync(assetId, organizationId);

            if (callerRole == "HR")
                EnsureHRCanAccessAsset(asset, callerDepartmentId);

            return await _db.AssetAssignments
                .Include(a => a.Asset)
                .Include(a => a.Employee)
                .Where(a => a.AssetId == assetId)
                .OrderByDescending(a => a.AssignedAt)
                .Select(a => new AssignmentDto
                {
                    AssetAssignmentId = a.AssetAssignmentId,
                    AssetId = a.AssetId,
                    AssetName = a.Asset.Name,
                    AssetCode = a.Asset.AssetCode,
                    EmployeeId = a.EmployeeId,
                    EmployeeName = a.Employee.FirstName + " " + a.Employee.LastName,
                    AssignedAt = a.AssignedAt,
                    ReturnedAt = a.ReturnedAt,
                    AssignmentNotes = a.AssignmentNotes,
                    ReturnNotes = a.ReturnNotes
                })
                .ToListAsync();
        }

        /// <summary>Active (not returned) assignments for an employee.</summary>
        public async Task<List<AssignmentDto>> GetEmployeeAssetsAsync(
            Guid employeeId,
            Guid organizationId)
        {
            return await _db.AssetAssignments
                .Include(a => a.Asset)
                .Include(a => a.Employee)
                .Where(a =>
                    a.EmployeeId == employeeId &&
                    a.Asset.OrganizationId == organizationId &&
                    a.ReturnedAt == null)
                .Select(a => new AssignmentDto
                {
                    AssetAssignmentId = a.AssetAssignmentId,
                    AssetId = a.AssetId,
                    AssetName = a.Asset.Name,
                    AssetCode = a.Asset.AssetCode,
                    EmployeeId = a.EmployeeId,
                    EmployeeName = a.Employee.FirstName + " " + a.Employee.LastName,
                    AssignedAt = a.AssignedAt,
                    ReturnedAt = a.ReturnedAt,
                    AssignmentNotes = a.AssignmentNotes
                })
                .ToListAsync();
        }

        /// <summary>
        /// Asset requests visible to OrgAdmin (all) or HR (their department only).
        /// </summary>
        public async Task<List<AssetRequestDto>> GetAssetRequestsAsync(
            Guid organizationId,
            string? status = null,
            Guid? employeeId = null,
            string? callerRole = null,
            Guid? callerDepartmentId = null)
        {
            var q = _db.AssetRequests
                .Include(r => r.Asset)
                .Include(r => r.RequestedByEmployee)
                .Include(r => r.ReviewedByEmployee)
                .Where(r => r.OrganizationId == organizationId);

            // HR sees only requests from their department's employees
            if (callerRole == "HR" && callerDepartmentId.HasValue)
                q = q.Where(r => r.RequestedByEmployee.DepartmentId == callerDepartmentId.Value);

            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(r => r.Status == status);

            if (employeeId.HasValue)
                q = q.Where(r => r.RequestedByEmployeeId == employeeId);

            return await q
                .OrderByDescending(r => r.RequestedAt)
                .Select(r => MapAssetRequestDto(r))
                .ToListAsync();
        }

        /// <summary>Asset requests raised by a specific employee (for Employee dashboard).</summary>
        public async Task<List<AssetRequestDto>> GetAssetRequestsByEmployeeIdAsync(
            Guid employeeId,
            Guid organizationId)
        {
            return await _db.AssetRequests
                .Include(r => r.Asset)
                .Include(r => r.RequestedByEmployee)
                .Include(r => r.ReviewedByEmployee)
                .Where(r =>
                    r.RequestedByEmployeeId == employeeId &&
                    r.OrganizationId == organizationId)
                .OrderByDescending(r => r.RequestedAt)
                .Select(r => MapAssetRequestDto(r))
                .ToListAsync();
        }

        /// <summary>
        /// Procurement requests: OrgAdmin sees all, HR sees their dept only.
        /// </summary>
        public async Task<List<ProcurementRequestDto>> GetProcurementRequestsAsync(
            Guid organizationId,
            string? status = null,
            Guid? departmentId = null,
            string? callerRole = null,
            Guid? callerDepartmentId = null)
        {
            var q = _db.ProcurementRequests
                .Include(p => p.Department)
                .Include(p => p.RequestedByEmployee)
                .Where(p => p.OrganizationId == organizationId);

            // HR sees only their department
            if (callerRole == "HR" && callerDepartmentId.HasValue)
                q = q.Where(p => p.DepartmentId == callerDepartmentId.Value);
            else if (departmentId.HasValue)
                q = q.Where(p => p.DepartmentId == departmentId.Value);

            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(p => p.Status == status);

            return await q
                .OrderByDescending(p => p.RequestedAt)
                .Select(p => MapProcurementDto(p))
                .ToListAsync();
        }

        public async Task<List<AssetAssignmentHistoryDto>> GetAssetAssignmentHistoryAsync(
            Guid assetId,
            Guid organizationId)
        {
            return await _db.AssetAssignments
                .Where(a => a.AssetId == assetId && a.OrganizationId == organizationId)
                .OrderByDescending(a => a.AssignedAt)
                .Select(a => new AssetAssignmentHistoryDto
                {
                    AssignmentId = a.AssetAssignmentId,
                    EmployeeName = _db.Employees
                        .Where(e => e.EmployeeId == a.EmployeeId)
                        .Select(e => e.FirstName + " " + e.LastName)
                        .FirstOrDefault() ?? "Unknown",
                    AssignedAt = a.AssignedAt,
                    ReturnedAt = a.ReturnedAt,
                    AssignmentNotes = a.AssignmentNotes,
                    ReturnNotes = a.ReturnNotes
                })
                .ToListAsync();
        }

        /// <summary>
        /// Returns employees eligible to receive a given asset (respects dept restriction).
        /// HR callers get employees from their dept only. OrgAdmin gets all eligible employees.
        /// </summary>
        public async Task<List<EmployeeDropdownDto>> GetAssignableEmployeesAsync(
            Guid organizationId,
            Guid assetId,
            string? callerRole = null,
            Guid? callerDepartmentId = null)
        {
            var asset = await _db.Assets
                .FirstOrDefaultAsync(a =>
                    a.AssetId == assetId &&
                    a.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("Asset not found.");

            IQueryable<Employee> q = _db.Employees
                .Where(e => e.OrganizationId == organizationId);

            // Asset dept restriction
            if (asset.DepartmentId.HasValue)
                q = q.Where(e => e.DepartmentId == asset.DepartmentId.Value);

            // HR caller scope: further restrict to HR's own department
            if (callerRole == "HR" && callerDepartmentId.HasValue)
                q = q.Where(e => e.DepartmentId == callerDepartmentId.Value);

            return await q
                .OrderBy(e => e.FirstName)
                .Select(e => new EmployeeDropdownDto
                {
                    EmployeeId = e.EmployeeId,
                    FullName = (e.FirstName ?? "") + " " + (e.LastName ?? ""),
                    DepartmentId = e.DepartmentId
                })
                .ToListAsync();
        }

        /// <summary>
        /// Returns Available assets suitable as a replacement for a given request.
        /// Filters by same category as the original asset and respects dept compatibility.
        /// </summary>
        public async Task<List<AssetListDto>> GetAvailableReplacementAssetsAsync(
            Guid organizationId,
            Guid assetRequestId,
            string? callerRole = null,
            Guid? callerDepartmentId = null)
        {
            var request = await _db.AssetRequests
                .Include(r => r.Asset)
                .Include(r => r.RequestedByEmployee)
                .FirstOrDefaultAsync(r =>
                    r.AssetRequestId == assetRequestId &&
                    r.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("Asset request not found.");

            if (request.RequestType != AssetRequestType.Replacement)
                throw new InvalidOperationException("This method is only valid for Replacement requests.");

            var employeeDeptId = request.RequestedByEmployee.DepartmentId;

            var q = _db.Assets
                .Include(a => a.CurrentEmployee)
                .Where(a =>
                    a.OrganizationId == organizationId &&
                    a.Status == AssetStatus.Available &&
                    a.AssetId != request.AssetId &&   // not the same asset
                    a.Category == request.Asset.Category);  // same category

            // Department compatibility: include assets with no dept OR same dept as employee
            q = q.Where(a => a.DepartmentId == null || a.DepartmentId == employeeDeptId);

            // HR scope
            if (callerRole == "HR" && callerDepartmentId.HasValue)
                q = q.Where(a => a.DepartmentId == null || a.DepartmentId == callerDepartmentId.Value);

            return await q
                .OrderBy(a => a.Name)
                .Select(a => new AssetListDto   
                {
                    AssetId = a.AssetId,
                    Name = a.Name,
                    Category = a.Category,
                    AssetCode = a.AssetCode,
                    Status = a.Status,
                    DepartmentId = a.DepartmentId,
                    CurrentEmployeeName = null,
                    CreatedAt = a.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<List<Organization>> GetOrganizationsAsync()
        {
            return await _db.Organizations.ToListAsync();
        }

        // =====================================================================
        // PRIVATE HELPERS
        // =====================================================================

        /// <summary>
        /// Core return logic used by both ReturnAssetAsync and ReviewAssetRequestAsync.
        /// Closes the active assignment and sets asset to Available.
        /// Does NOT save — caller must SaveChangesAsync.
        /// </summary>
        private async Task ApplyReturnInternalAsync(Asset asset, string actorUserId, string? returnNotes = null)
        {
            AssetStatusMachine.EnsureCanTransition(asset.Status, AssetStatus.Available);

            var activeAssignment = await _db.AssetAssignments
                .FirstOrDefaultAsync(a => a.AssetId == asset.AssetId && a.ReturnedAt == null)
                ?? throw new InvalidOperationException(
                    $"No active assignment found for asset '{asset.AssetCode}'.");

            activeAssignment.ReturnedAt = DateTime.UtcNow;
            activeAssignment.ReturnedByUserId = actorUserId;
            activeAssignment.ReturnNotes = returnNotes;

            asset.Status = AssetStatus.Available;
            asset.CurrentEmployeeId = null;
            asset.UpdatedAt = DateTime.UtcNow;
            asset.UpdatedByUserId = actorUserId;
        }

        /// <summary>
        /// Validates that an HR caller has access to the given asset.
        /// HR can only access assets that belong to their department or have no department (org-wide).
        /// </summary>
        private static void EnsureHRCanAccessAsset(Asset asset, Guid? hrDepartmentId)
        {
            if (!hrDepartmentId.HasValue)
                throw new InvalidOperationException("HR department context is missing.");

            if (asset.DepartmentId.HasValue && asset.DepartmentId != hrDepartmentId.Value)
                throw new InvalidOperationException(
                    "Access denied. This asset belongs to a different department.");
        }

        private IQueryable<Asset> ApplyAssetFilters(IQueryable<Asset> q, AssetFilterDto filter)
        {
            if (!string.IsNullOrWhiteSpace(filter.Category))
                q = q.Where(a => a.Category == filter.Category);

            if (!string.IsNullOrWhiteSpace(filter.Status))
                q = q.Where(a => a.Status == filter.Status);

            if (filter.EmployeeId.HasValue)
                q = q.Where(a => a.CurrentEmployeeId == filter.EmployeeId);

            if (filter.DepartmentId.HasValue)
                q = q.Where(a => a.DepartmentId == filter.DepartmentId);

            return q;
        }

        private static async Task<PagedResult<AssetListDto>> ExecuteAssetPagedQueryAsync(
            IQueryable<Asset> q, AssetFilterDto filter)
        {
            var total = await q.CountAsync();
            var items = await q
                .OrderByDescending(a => a.CreatedAt)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(a => new AssetListDto
                {
                    AssetId = a.AssetId,
                    Name = a.Name,
                    Category = a.Category,
                    AssetCode = a.AssetCode,
                    Status = a.Status,
                    DepartmentId = a.DepartmentId,
                    CurrentEmployeeName = a.CurrentEmployee != null
                        ? a.CurrentEmployee.FirstName + " " + a.CurrentEmployee.LastName
                        : null,
                    CreatedAt = a.CreatedAt
                })
                .ToListAsync();

            return new PagedResult<AssetListDto>
            {
                Items = items,
                TotalCount = total,
                Page = filter.Page,
                PageSize = filter.PageSize
            };
        }

        private async Task<Asset> GetAssetOrThrowAsync(Guid assetId, Guid organizationId)
        {
            return await _db.Assets
                .Include(a => a.Department)
                .Include(a => a.CurrentEmployee)
                .FirstOrDefaultAsync(a =>
                    a.AssetId == assetId &&
                    a.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException($"Asset not found (id: {assetId}).");
        }

        private async Task ValidateDepartmentAsync(Guid? departmentId, Guid organizationId)
        {
            if (!departmentId.HasValue) return;

            var exists = await _db.Departments.AnyAsync(d =>
                d.DepartmentId == departmentId.Value &&
                d.OrganizationId == organizationId);

            if (!exists)
                throw new InvalidOperationException(
                    "The specified department does not exist in this organisation.");
        }

        private async Task<string> GetEmployeeNameAsync(Guid employeeId)
        {
            var emp = await _db.Employees.FindAsync(employeeId);
            return emp == null ? "Unknown" : $"{emp.FirstName} {emp.LastName}".Trim();
        }

        private async Task<string> GetUserIdForEmployeeAsync(Guid employeeId)
        {
            var emp = await _db.Employees.FindAsync(employeeId);
            return emp?.UserId ?? string.Empty;
        }

        private static bool IsValidRequestType(string t) =>
            t is AssetRequestType.Repair or AssetRequestType.Replacement or AssetRequestType.Return;

        private async Task<AssetRequestDto> GetAssetRequestDtoAsync(Guid id)
        {
            var r = await _db.AssetRequests
                .Include(x => x.Asset)
                .Include(x => x.RequestedByEmployee)
                .Include(x => x.ReviewedByEmployee)
                .FirstAsync(x => x.AssetRequestId == id);
            return MapAssetRequestDto(r);
        }

        private async Task<ProcurementRequestDto> GetProcurementDtoAsync(Guid id)
        {
            var p = await _db.ProcurementRequests
                .Include(x => x.Department)
                .Include(x => x.RequestedByEmployee)
                .FirstAsync(x => x.ProcurementRequestId == id);
            return MapProcurementDto(p);
        }

        private Task NotifyAssetRequestReviewedAsync(
            AssetRequest request, Guid organizationId, string actorUserId)
        {
            var approved = request.Status
                is AssetRequestStatus.Approved
                or AssetRequestStatus.Completed;

            return _notifications.SendAssetRequestReviewedAsync(
                request.RequestedByEmployee?.UserId ?? string.Empty,
                request.Asset.Name, request.RequestType, approved,
                request.AssetRequestId, organizationId, actorUserId);
        }

        // ── Static mappers ────────────────────────────────────────────────────

        private static AssetDto MapAssetDto(Asset a, string? employeeName) => new()
        {
            AssetId = a.AssetId,
            OrganizationId = a.OrganizationId,
            Name = a.Name,
            Category = a.Category,
            AssetCode = a.AssetCode,
            Brand = a.Brand,
            Model = a.Model,
            SerialNumber = a.SerialNumber,
            Status = a.Status,
            CurrentEmployeeId = a.CurrentEmployeeId,
            CurrentEmployeeName = employeeName,
            PurchasePrice = a.PurchasePrice,
            PurchaseDate = a.PurchaseDate,
            WarrantyExpiryDate = a.WarrantyExpiryDate,
            Notes = a.Notes,
            CreatedAt = a.CreatedAt,
            DepartmentId = a.DepartmentId
        };

        private static AssignmentDto MapAssignmentDto(
            AssetAssignment a, Asset asset, string empName) => new()
            {
                AssetAssignmentId = a.AssetAssignmentId,
                AssetId = a.AssetId,
                AssetName = asset.Name,
                AssetCode = asset.AssetCode,
                EmployeeId = a.EmployeeId,
                EmployeeName = empName,
                AssignedAt = a.AssignedAt,
                ReturnedAt = a.ReturnedAt,
                AssignmentNotes = a.AssignmentNotes,
                ReturnNotes = a.ReturnNotes
            };

        private static AssetRequestDto MapAssetRequestDto(AssetRequest r) => new()
        {
            AssetRequestId = r.AssetRequestId,
            AssetId = r.AssetId,
            AssetName = r.Asset?.Name ?? string.Empty,
            AssetCode = r.Asset?.AssetCode ?? string.Empty,
            RequestedByEmployeeId = r.RequestedByEmployeeId,
            RequestedByEmployeeName = r.RequestedByEmployee != null
                ? $"{r.RequestedByEmployee.FirstName} {r.RequestedByEmployee.LastName}".Trim()
                : string.Empty,
            RequestorRole = r.RequestorRole,
            RequestType = r.RequestType,
            Status = r.Status,
            Reason = r.Reason,
            RequestedAt = r.RequestedAt,
            ReviewedByEmployeeName = r.ReviewedByEmployee != null
                ? $"{r.ReviewedByEmployee.FirstName} {r.ReviewedByEmployee.LastName}".Trim()
                : null,
            ReviewedAt = r.ReviewedAt,
            ReviewNotes = r.ReviewNotes
        };

        private static ProcurementRequestDto MapProcurementDto(ProcurementRequest p) => new()
        {
            ProcurementRequestId = p.ProcurementRequestId,
            DepartmentId = p.DepartmentId,
            DepartmentName = p.Department?.Name ?? string.Empty,
            RequestedByEmployeeName = p.RequestedByEmployee != null
                ? $"{p.RequestedByEmployee.FirstName} {p.RequestedByEmployee.LastName}".Trim()
                : string.Empty,
            AssetCategory = p.AssetCategory,
            AssetName = p.AssetName,
            QuantityRequested = p.QuantityRequested,
            QuantityFulfilled = p.QuantityFulfilled,
            Justification = p.Justification,
            Status = p.Status,
            RequestedAt = p.RequestedAt,
            ReviewedAt = p.ReviewedAt,
            ReviewNotes = p.ReviewNotes,
            FulfilledAt = p.FulfilledAt
        };
    }
}