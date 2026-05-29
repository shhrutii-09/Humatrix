using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs.Assets;
using Humatrix_HRMS.Infrastructure.Constants;
using Humatrix_HRMS.Infrastructure.Services;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services.Assets
{
    /// <summary>
    /// Central service for the Asset Management module.
    ///
    /// Responsibilities:
    ///   - Asset CRUD (OrgAdmin only)
    ///   - Asset assignment / return
    ///   - Status synchronisation (Asset + Assignment table always in step)
    ///   - Asset request lifecycle (Repair / Replacement / Return)
    ///   - Procurement request lifecycle
    ///   - Hooks into ActivityLogService and NotificationEngine
    ///
    /// Concurrency: every mutating operation runs inside an explicit
    /// EF Core transaction so that Asset, AssetAssignment, and related
    /// tables are always consistent.
    ///
    /// Authorization: this service is role-agnostic. Role checks belong in
    /// the controller or a thin authorization layer. However, department-scope
    /// is enforced here via helper methods so callers cannot bypass it.
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
        // ASSET CRUD  (OrgAdmin)
        // =====================================================================

        /// <summary>Creates a single asset in inventory with status Available.</summary>
        public async Task<AssetDto> CreateAssetAsync(
            Guid organizationId,
            CreateAssetDto dto,
            string actorUserId,
            string actorRole)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            await ValidateDepartmentAsync(dto.DepartmentId, organizationId);

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

        /// <summary>Updates non-status fields of an asset.</summary>
        public async Task<AssetDto> UpdateAssetAsync(
            Guid assetId,
            Guid organizationId,
            UpdateAssetDto dto,
            string actorUserId,
            string actorRole)
        {
            var asset = await GetAssetOrThrowAsync(assetId, organizationId);
            await ValidateDepartmentAsync(dto.DepartmentId, organizationId);

            var old = new { asset.Name, asset.Brand, asset.Model, asset.SerialNumber, asset.Notes };
            if (asset.Status == AssetStatus.Assigned &&
    dto.DepartmentId.HasValue &&
    dto.DepartmentId != asset.DepartmentId)
            {
                throw new InvalidOperationException(
                    "Cannot change department of an assigned asset.");
            }
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
                newValues: new { asset.Name, asset.Brand, asset.Model, asset.SerialNumber, asset.Notes });

            var currentEmployeeName = asset.CurrentEmployeeId.HasValue
                ? await GetEmployeeNameAsync(asset.CurrentEmployeeId.Value)
                : null;

            return MapAssetDto(asset, currentEmployeeName);
        }

        /// <summary>
        /// Retires an asset. Asset must be Available (not currently assigned).
        /// </summary>
        public async Task RetireAssetAsync(
            Guid assetId,
            Guid organizationId,
            string actorUserId,
            string actorRole)
        {
            var asset = await GetAssetOrThrowAsync(assetId, organizationId);

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
        // ASSIGNMENT  (OrgAdmin assigns to anyone; HR assigns within dept)
        // =====================================================================

        /// <summary>
        /// Assigns an asset to an employee.
        /// Enforces: asset must be Available, employee must belong to the same org.
        /// Department restriction is enforced in the controller for HR callers.
        ///
        /// Atomically:
        ///   1. Creates AssetAssignment record
        ///   2. Sets Asset.Status = Assigned
        ///   3. Sets Asset.CurrentEmployeeId
        /// </summary>
        public async Task<AssignmentDto> AssignAssetAsync(
            Guid organizationId,
            AssignAssetDto dto,
            string actorUserId,
            string actorRole)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            var asset = await GetAssetOrThrowAsync(dto.AssetId, organizationId);

            if (asset.Status != AssetStatus.Available)
                throw new InvalidOperationException(
                    $"Asset '{asset.AssetCode}' is not available for assignment (current status: {asset.Status}).");

            var employee = await _db.Employees
                .FirstOrDefaultAsync(e => e.EmployeeId == dto.EmployeeId && e.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("Employee not found in this organisation.");

            // Asset can only be assigned within its department
            if (asset.DepartmentId.HasValue)
            {
                if (employee.DepartmentId != asset.DepartmentId.Value)
                {
                    throw new InvalidOperationException(
                        "This asset can only be assigned to employees of the same department.");
                }
            }

            AssetStatusMachine.EnsureCanTransition(asset.Status, AssetStatus.Assigned);

            // 1. Create assignment record
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
            // 2. Sync asset status
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

        /// <summary>
        /// Returns an asset from an employee.
        ///
        /// Atomically:
        ///   1. Closes active AssetAssignment (sets ReturnedAt)
        ///   2. Sets Asset.Status = Available
        ///   3. Clears Asset.CurrentEmployeeId
        /// </summary>
        public async Task ReturnAssetAsync(
            Guid organizationId,
            ReturnAssetDto dto,
            string actorUserId,
            string actorRole)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            var asset = await GetAssetOrThrowAsync(dto.AssetId, organizationId);

            if (asset.Status != AssetStatus.Assigned)
                throw new InvalidOperationException(
                    $"Asset '{asset.AssetCode}' is not currently assigned.");

            var activeAssignment = await _db.AssetAssignments
                .FirstOrDefaultAsync(a => a.AssetId == asset.AssetId && a.ReturnedAt == null)
                ?? throw new InvalidOperationException("No active assignment found for this asset.");

            AssetStatusMachine.EnsureCanTransition(asset.Status, AssetStatus.Available);

            // 1. Close assignment
            activeAssignment.ReturnedAt = DateTime.UtcNow;
            activeAssignment.ReturnedByUserId = actorUserId;
            activeAssignment.ReturnNotes = dto.Notes?.Trim();

            // 2. Sync asset status
            asset.Status = AssetStatus.Available;
            asset.CurrentEmployeeId = null;
            asset.UpdatedAt = DateTime.UtcNow;
            asset.UpdatedByUserId = actorUserId;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            _ = _activityLog.LogAsync(
                organizationId, AssetModuleName.Asset, "Returned",
                "Asset", asset.AssetId, actorUserId, actorRole,
                newValues: new { asset.AssetCode, ReturnedBy = actorUserId });
        }

        // =====================================================================
        // ASSET REQUESTS  (Repair / Replacement / Return)
        // =====================================================================

        /// <summary>
        /// Employee or HR raises a request against their assigned asset.
        /// Validates that the requestor actually holds the asset.
        /// </summary>
        public async Task<AssetRequestDto> CreateAssetRequestAsync(
            Guid organizationId,
            Guid requestedByEmployeeId,
            string requestorRole,
            CreateAssetRequestDto dto)
        {
            if (!AssetRequestType_IsValid(dto.RequestType))
                throw new ArgumentException($"Invalid request type '{dto.RequestType}'.");

            var asset = await GetAssetOrThrowAsync(dto.AssetId, organizationId);

            // Requestor must currently hold the asset
            if (asset.CurrentEmployeeId != requestedByEmployeeId)
                throw new InvalidOperationException("You can only raise a request for an asset assigned to you.");

            // No duplicate pending request of the same type for the same asset
            var duplicate = await _db.AssetRequests.AnyAsync(r =>
                r.AssetId == dto.AssetId &&
                r.RequestType == dto.RequestType &&
                r.Status == AssetRequestStatus.Pending);

            if (duplicate)
                throw new InvalidOperationException(
                    $"A pending '{dto.RequestType}' request already exists for this asset.");

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

            _ = _activityLog.LogAsync(
                organizationId, AssetModuleName.AssetRequest, "Raised",
                "AssetRequest", request.AssetRequestId,
                (await GetUserIdForEmployeeAsync(requestedByEmployeeId)),
                requestorRole,
                newValues: new { asset.AssetCode, dto.RequestType });

            _ = NotifyAssetRequestRaisedAsync(request, asset, organizationId, requestorRole);

            return await GetAssetRequestDtoAsync(request.AssetRequestId);
        }

        /// <summary>
        /// Reviews (Approves / Rejects) an asset request.
        ///
        /// On Approval:
        ///   - Return:      same as ReturnAssetAsync
        ///   - Repair:      asset → InRepair (assigned stays closed once repair done)
        ///   - Replacement: old asset returned; new asset assigned; request records new asset
        ///
        /// On Rejection: request closed, no asset changes.
        /// </summary>
        public async Task<AssetRequestDto> ReviewAssetRequestAsync(
            Guid organizationId,
            Guid reviewerEmployeeId,
            ReviewAssetRequestDto dto,
            string actorUserId,
            string actorRole)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            var request = await _db.AssetRequests
                .Include(r => r.Asset)
                .Include(r => r.RequestedByEmployee)
                .FirstOrDefaultAsync(r =>
                    r.AssetRequestId == dto.AssetRequestId &&
                    r.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("Asset request not found.");

            if (request.Status != AssetRequestStatus.Pending)
                throw new InvalidOperationException("Only pending requests can be reviewed.");

            request.ReviewedByEmployeeId = reviewerEmployeeId;
            request.ReviewedAt = DateTime.UtcNow;
            request.ReviewNotes = dto.Notes?.Trim();

            if (!dto.Approve)
            {
                request.Status = AssetRequestStatus.Rejected;
            }
            else
            {
                request.Status = AssetRequestStatus.Approved;

                switch (request.RequestType)
                {
                    case AssetRequestType.Return:
                        await _ApplyReturnAsync(request.Asset, actorUserId);
                        request.Status = AssetRequestStatus.Completed;
                        break;

                    case AssetRequestType.Repair:
                        AssetStatusMachine.EnsureCanTransition(request.Asset.Status, AssetStatus.InRepair);
                        request.Asset.Status = AssetStatus.InRepair;
                        request.Asset.UpdatedAt = DateTime.UtcNow;
                        request.Asset.UpdatedByUserId = actorUserId;
                        break;

                    case AssetRequestType.Replacement:
                        if (!dto.ReplacementAssetId.HasValue)
                            throw new ArgumentException("ReplacementAssetId is required for Replacement approval.");

                        var newAsset = await _db.Assets
                            .FirstOrDefaultAsync(a =>
                                a.AssetId == dto.ReplacementAssetId.Value &&
                                a.OrganizationId == organizationId &&
                                a.Status == AssetStatus.Available)
                            ?? throw new KeyNotFoundException(
                                "Replacement asset not found or not available.");

                        // Return old asset
                        await _ApplyReturnAsync(request.Asset, actorUserId);

                        // Assign new asset to same employee
                        var newAssignment = new AssetAssignment
                        {
                            AssetId = newAsset.AssetId,
                            EmployeeId = request.RequestedByEmployeeId,
                            OrganizationId = organizationId,
                            AssignedAt = DateTime.UtcNow,
                            AssignedByUserId = actorUserId,
                            AssignmentNotes = $"Replacement for {request.Asset.AssetCode}"
                        };
                        _db.AssetAssignments.Add(newAssignment);

                        if (newAsset.DepartmentId.HasValue &&
    request.RequestedByEmployee.DepartmentId != newAsset.DepartmentId)
                        {
                            throw new InvalidOperationException(
                                "Replacement asset department mismatch.");
                        }

                        newAsset.Status = AssetStatus.Assigned;
                        newAsset.CurrentEmployeeId = request.RequestedByEmployeeId;
                        newAsset.UpdatedAt = DateTime.UtcNow;
                        newAsset.UpdatedByUserId = actorUserId;

                        request.ReplacementAssetId = newAsset.AssetId;
                        request.Status = AssetRequestStatus.Completed;
                        break;
                }
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            _ = _activityLog.LogAsync(
                organizationId, AssetModuleName.AssetRequest,
                dto.Approve ? "Approved" : "Rejected",
                "AssetRequest", request.AssetRequestId,
                actorUserId, actorRole);

            _ = NotifyAssetRequestReviewedAsync(request, organizationId, actorUserId);

            return await GetAssetRequestDtoAsync(request.AssetRequestId);
        }

        // =====================================================================
        // PROCUREMENT  (HR raises → OrgAdmin approves → OrgAdmin fulfils)
        // =====================================================================

        /// <summary>Creates a procurement request for a department (HR action).</summary>
        public async Task<ProcurementRequestDto> CreateProcurementRequestAsync(
            Guid organizationId,
            Guid departmentId,
            Guid requestedByEmployeeId,
            CreateProcurementRequestDto dto,
            string actorUserId)
        {
            await ValidateDepartmentAsync(departmentId, organizationId);
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
                throw new InvalidOperationException("Only pending procurement requests can be reviewed.");

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
        /// OrgAdmin fulfils an approved procurement request.
        ///
        /// Atomically creates the requested assets and marks the procurement fulfilled.
        /// Each asset gets an auto-generated AssetCode.
        /// </summary>
        public async Task<List<AssetDto>> FulfilProcurementAsync(
            Guid organizationId,
            FulfilProcurementDto dto,
            string actorUserId)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            var procurement = await _db.ProcurementRequests
                .FirstOrDefaultAsync(p =>
                    p.ProcurementRequestId == dto.ProcurementRequestId &&
                    p.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("Procurement request not found.");

            if (procurement.Status != ProcurementStatus.Approved)
                throw new InvalidOperationException("Only approved procurements can be fulfilled.");

            if (dto.QuantityToCreate < 1 ||
                dto.QuantityToCreate > (procurement.QuantityRequested - procurement.QuantityFulfilled))
                throw new ArgumentException(
                    $"QuantityToCreate must be between 1 and " +
                    $"{procurement.QuantityRequested - procurement.QuantityFulfilled}.");

            var createdAssets = new List<Asset>();

            for (int i = 0; i < dto.QuantityToCreate; i++)
            {
                var code = await AssetCodeGenerator.NextCodeAsync(_db, organizationId, procurement.AssetCategory);

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
                    Notes = $"Created via procurement {procurement.ProcurementRequestId}"
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

            return createdAssets.Select(a => MapAssetDto(a, employeeName: null)).ToList();
        }

        // =====================================================================
        // QUERIES
        // =====================================================================

        public async Task<PagedResult<AssetListDto>> GetAssetsAsync(
            Guid organizationId,
            AssetFilterDto filter, Guid? departmentId = null,
bool restrictToDepartment = false)
        {
            var q = _db.Assets
      .Include(a => a.CurrentEmployee)
      .Include(a => a.Department)
      .Where(a => a.OrganizationId == organizationId);

            if (restrictToDepartment && departmentId.HasValue)
            {
                q = q.Where(a => a.DepartmentId == departmentId.Value);
            }

            if (!string.IsNullOrWhiteSpace(filter.Category))
                q = q.Where(a => a.Category == filter.Category);

            if (!string.IsNullOrWhiteSpace(filter.Status))
                q = q.Where(a => a.Status == filter.Status);

            if (filter.EmployeeId.HasValue)
                q = q.Where(a => a.CurrentEmployeeId == filter.EmployeeId);

            var total = await q.CountAsync();
            var items = await q
                .OrderByDescending(a => a.CreatedAt)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(a => new AssetListDto
                {
                    //AssetId = a.AssetId,
                    //Name = a.Name,
                    //Category = a.Category,
                    //AssetCode = a.AssetCode,
                    //Status = a.Status,
                    //CurrentEmployeeName = a.CurrentEmployee != null
                    //    ? a.CurrentEmployee.FirstName + " " + a.CurrentEmployee.LastName
                    //    : null,
                    //CreatedAt = a.CreatedAt
                    AssetId = a.AssetId,
                    Name = a.Name,
                    Category = a.Category,
                    AssetCode = a.AssetCode,
                    Status = a.Status,
                    CurrentEmployeeName = a.CurrentEmployee != null
        ? a.CurrentEmployee.FirstName + " " + a.CurrentEmployee.LastName
        : null,

                    CreatedAt = a.CreatedAt,

                    DepartmentId = a.DepartmentId
                //            DepartmentName = a.Department != null
                //? a.Department.Name
                //: null
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

        public async Task<AssetDto> GetAssetByIdAsync(Guid assetId, Guid organizationId)
        {
            var asset = await _db.Assets
.Include(a => a.CurrentEmployee)
.Include(a => a.Department).FirstOrDefaultAsync(a => a.AssetId == assetId && a.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("Asset not found.");

            var empName = asset.CurrentEmployee != null
                ? $"{asset.CurrentEmployee.FirstName} {asset.CurrentEmployee.LastName}".Trim()
                : null;

            return MapAssetDto(asset, empName);
        }

        public async Task<List<AssignmentDto>> GetAssetHistoryAsync(Guid assetId, Guid organizationId)
        {
            // Validate org access
            _ = await GetAssetOrThrowAsync(assetId, organizationId);

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

        public async Task<List<AssignmentDto>> GetEmployeeAssetsAsync(
            Guid employeeId, Guid organizationId)
        {
            return await _db.AssetAssignments
    .Include(a => a.Asset)
    .Include(a => a.Employee)
                .Where(a => a.EmployeeId == employeeId
                         && a.Asset.OrganizationId == organizationId
                         && a.ReturnedAt == null)
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

        public async Task<List<AssetRequestDto>> GetAssetRequestsAsync(
            Guid organizationId,
            string? status = null,
            Guid? employeeId = null)
        {
            var q = _db.AssetRequests
                .Include(r => r.Asset)
                .Include(r => r.RequestedByEmployee)
                .Include(r => r.ReviewedByEmployee)
                .Where(r => r.OrganizationId == organizationId);

            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(r => r.Status == status);

            if (employeeId.HasValue)
                q = q.Where(r => r.RequestedByEmployeeId == employeeId);

            return await q
                .OrderByDescending(r => r.RequestedAt)
                .Select(r => MapAssetRequestDto(r))
                .ToListAsync();
        }

        public async Task<List<ProcurementRequestDto>> GetProcurementRequestsAsync(
            Guid organizationId,
            string? status = null,
            Guid? departmentId = null)
        {
            var q = _db.ProcurementRequests
                .Include(p => p.Department)
                .Include(p => p.RequestedByEmployee)
                .Where(p => p.OrganizationId == organizationId);

            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(p => p.Status == status);

            if (departmentId.HasValue)
                q = q.Where(p => p.DepartmentId == departmentId);

            return await q
                .OrderByDescending(p => p.RequestedAt)
                .Select(p => MapProcurementDto(p))
                .ToListAsync();
        }

        //private async Task<Asset> GetAssetOrThrowAsync(Guid assetId, Guid organizationId)
        //{
        //    return await _db.Assets
        //        .Include(a => a.Department)
        //        .Include(a => a.CurrentEmployee)
        //        .FirstOrDefaultAsync(a =>
        //            a.AssetId == assetId &&
        //            a.OrganizationId == organizationId)
        //        ?? throw new KeyNotFoundException($"Asset '{assetId}' not found.");
        //}
        private async Task<Asset> GetAssetOrThrowAsync(Guid assetId, Guid organizationId)
        {
            return await _db.Assets
                .Include(a => a.Department)
                .Include(a => a.CurrentEmployee)
                .FirstOrDefaultAsync(a =>
                    a.AssetId == assetId &&
                    a.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException($"Asset '{assetId}' not found.");
        }
        private async Task _ApplyReturnAsync(Asset asset, string actorUserId)
        {
            AssetStatusMachine.EnsureCanTransition(asset.Status, AssetStatus.Available);

            var activeAssignment = await _db.AssetAssignments
                .FirstOrDefaultAsync(a => a.AssetId == asset.AssetId && a.ReturnedAt == null)
                ?? throw new InvalidOperationException("No active assignment found for asset.");

            activeAssignment.ReturnedAt = DateTime.UtcNow;
            activeAssignment.ReturnedByUserId = actorUserId;

            asset.Status = AssetStatus.Available;
            asset.CurrentEmployeeId = null;
            asset.UpdatedAt = DateTime.UtcNow;
            asset.UpdatedByUserId = actorUserId;
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

        private static bool AssetRequestType_IsValid(string requestType) =>
            requestType is AssetRequestType.Repair
                        or AssetRequestType.Replacement
                        or AssetRequestType.Return;

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

        // ── Notification fire-and-forget wrappers ─────────────────────────────

        private Task NotifyAssetRequestRaisedAsync(
            AssetRequest request, Asset asset,
            Guid organizationId, string requestorRole)
        {
            return _notifications.SendAssetRequestRaisedAsync(
                asset.Name, asset.AssetCode, request.RequestType,
                request.RequestedByEmployeeId,
                request.AssetRequestId, organizationId,
                requestorRole, request.RequestedByEmployee?.UserId ?? string.Empty);
        }

        private Task NotifyAssetRequestReviewedAsync(
            AssetRequest request, Guid organizationId, string actorUserId)
        {
            var approved = request.Status is AssetRequestStatus.Approved or AssetRequestStatus.Completed;
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


        private async Task ValidateDepartmentAsync(Guid? departmentId, Guid organizationId)
        {
            if (!departmentId.HasValue)
                return;

            var exists = await _db.Departments
                .AnyAsync(d =>
                    d.DepartmentId == departmentId.Value &&
                    d.OrganizationId == organizationId);

            if (!exists)
                throw new InvalidOperationException("Invalid department.");
        }

        public async Task CompleteRepairAsync(
    Guid assetId,
    Guid organizationId,
    string actorUserId,
    string actorRole)
        {
            var asset = await GetAssetOrThrowAsync(assetId, organizationId);

            if (asset.Status != AssetStatus.InRepair)
                throw new InvalidOperationException(
                    "Only assets in repair can be completed.");

            // If asset still belongs to employee,
            // return back to Assigned status
            if (asset.CurrentEmployeeId.HasValue)
            {
                AssetStatusMachine.EnsureCanTransition(
                    asset.Status,
                    AssetStatus.Assigned);

                asset.Status = AssetStatus.Assigned;
            }
            else
            {
                AssetStatusMachine.EnsureCanTransition(
                    asset.Status,
                    AssetStatus.Available);

                asset.Status = AssetStatus.Available;
            }

            asset.UpdatedAt = DateTime.UtcNow;
            asset.UpdatedByUserId = actorUserId;

            await _db.SaveChangesAsync();

            _ = _activityLog.LogAsync(
                organizationId,
                AssetModuleName.Asset,
                "RepairCompleted",
                "Asset",
                asset.AssetId,
                actorUserId,
                actorRole);
        }

        public async Task<List<EmployeeDropdownDto>> GetAssignableEmployeesAsync(
        Guid organizationId,
        Guid assetId)
        {
            // Load asset directly
            var asset = await _db.Assets
                .FirstOrDefaultAsync(a =>
                    a.AssetId == assetId &&
                    a.OrganizationId == organizationId);

            if (asset == null)
                throw new Exception("Asset not found.");

            // Get employees from same department
            var employees = await _db.Employees
                .Where(e =>
                    e.OrganizationId == organizationId &&
                    e.DepartmentId == asset.DepartmentId)
                .OrderBy(e => e.FirstName)
                .Select(e => new EmployeeDropdownDto
                {
                    EmployeeId = e.EmployeeId,
                    FullName = (e.FirstName ?? "") + " " + (e.LastName ?? ""),
                    DepartmentId = e.DepartmentId
                })
                .ToListAsync();

            return employees;
        }

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

            if (!string.IsNullOrWhiteSpace(filter.Category))
                q = q.Where(a => a.Category == filter.Category);

            if (!string.IsNullOrWhiteSpace(filter.Status))
                q = q.Where(a => a.Status == filter.Status);

            if (filter.EmployeeId.HasValue)
                q = q.Where(a => a.CurrentEmployeeId == filter.EmployeeId);

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

        public async Task<List<EmployeeDropdownDto>> GetAssignableEmployeesForHRAsync(
    Guid organizationId,
    Guid assetId,
    UserManager<ApplicationUser> userManager)
        {
            var asset = await _db.Assets
                .FirstOrDefaultAsync(a =>
                    a.AssetId == assetId &&
                    a.OrganizationId == organizationId)
                ?? throw new KeyNotFoundException("Asset not found.");

            // Asset must belong to the HR's department (or be org-wide)
            // The caller already verified this; we just scope the employee query.
            var targetDeptId = asset.DepartmentId;

            // Base employee query — same org
            var employeeQuery = _db.Employees
                .Where(e => e.OrganizationId == organizationId);

            // If the asset has a department, restrict to that department
            if (targetDeptId.HasValue)
                employeeQuery = employeeQuery.Where(e => e.DepartmentId == targetDeptId.Value);

            var employees = await employeeQuery
                .OrderBy(e => e.FirstName)
                .ToListAsync();

            // Filter out HR and OrgAdmin users so HR cannot assign to another HR
            var result = new List<EmployeeDropdownDto>();
            foreach (var emp in employees)
            {
                if (string.IsNullOrEmpty(emp.UserId)) continue;
                var user = await userManager.FindByIdAsync(emp.UserId);
                if (user == null) continue;
                var roles = await userManager.GetRolesAsync(user);
                if (roles.Any(r => r is "HR" or "OrgAdmin")) continue;

                result.Add(new EmployeeDropdownDto
                {
                    EmployeeId = emp.EmployeeId,
                    FullName = $"{emp.FirstName} {emp.LastName}".Trim(),
                    DepartmentId = emp.DepartmentId
                });
            }

            return result;
        }

        public async Task<AssetRequestDto> CreateAssetRequestByHRAsync(
    Guid organizationId,
    Guid hrDepartmentId,
    Guid hrEmployeeId,
    CreateAssetRequestDto dto)
        {
            if (!AssetRequestType_IsValid(dto.RequestType))
                throw new ArgumentException($"Invalid request type '{dto.RequestType}'.");

            var asset = await GetAssetOrThrowAsync(dto.AssetId, organizationId);

            // HR can only raise requests for assets in their department (or org-wide)
            if (asset.DepartmentId.HasValue && asset.DepartmentId != hrDepartmentId)
                throw new InvalidOperationException(
                    "You can only raise requests for assets belonging to your department.");

            // Asset must be in a state where a request makes sense
            if (asset.Status == AssetStatus.Retired || asset.Status == AssetStatus.Lost)
                throw new InvalidOperationException(
                    $"Cannot raise a request for a {asset.Status} asset.");

            // No duplicate pending request of same type
            var duplicate = await _db.AssetRequests.AnyAsync(r =>
                r.AssetId == dto.AssetId &&
                r.RequestType == dto.RequestType &&
                r.Status == AssetRequestStatus.Pending);

            if (duplicate)
                throw new InvalidOperationException(
                    $"A pending '{dto.RequestType}' request already exists for this asset.");

            var request = new AssetRequest
            {
                AssetId = asset.AssetId,
                OrganizationId = organizationId,
                RequestedByEmployeeId = hrEmployeeId,
                RequestorRole = AssetRequestorRole.HR,
                RequestType = dto.RequestType,
                Reason = dto.Reason.Trim(),
                Status = AssetRequestStatus.Pending,
                RequestedAt = DateTime.UtcNow
            };

            _db.AssetRequests.Add(request);
            await _db.SaveChangesAsync();

            _ = _activityLog.LogAsync(
                organizationId, AssetModuleName.AssetRequest, "Raised",
                "AssetRequest", request.AssetRequestId,
                await GetUserIdForEmployeeAsync(hrEmployeeId),
                AssetRequestorRole.HR,
                newValues: new { asset.AssetCode, dto.RequestType });

            _ = NotifyAssetRequestRaisedAsync(request, asset, organizationId, AssetRequestorRole.HR);

            return await GetAssetRequestDtoAsync(request.AssetRequestId);
        }

        public async Task<List<AssetRequestDto>> GetAssetRequestsByDepartmentAsync(
    Guid organizationId,
    Guid departmentId,
    string? status = null)
        {
            var q = _db.AssetRequests
                .Include(r => r.Asset)
                .Include(r => r.RequestedByEmployee)
                .Include(r => r.ReviewedByEmployee)
                .Where(r =>
                    r.OrganizationId == organizationId &&
                    (r.Asset.DepartmentId == departmentId || r.Asset.DepartmentId == null));

            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(r => r.Status == status);

            return await q
                .OrderByDescending(r => r.RequestedAt)
                .Select(r => MapAssetRequestDto(r))
                .ToListAsync();
        }

    }
}