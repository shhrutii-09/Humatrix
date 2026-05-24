using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using CorrectionTypes = Humatrix_HRMS.Helpers.CorrectionTypes;

namespace Humatrix_HRMS.Services
{
    public class AttendanceCorrectionService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        private readonly CurrentUserService _currentUser;
        private readonly AttendanceCalculationService _calc;
        private readonly NotificationService _notificationService;

     
        public AttendanceCorrectionService(
            IDbContextFactory<ApplicationDbContext> dbFactory,
            CurrentUserService currentUser,
            AttendanceCalculationService calc,
            NotificationService notificationService) // 🚀 ADDED TO SIGNATURE)
        {
            _dbFactory = dbFactory;
            _currentUser = currentUser;
            _calc = calc;
            _notificationService = notificationService; // 🚀 ASSIGNED
        }
        public async Task<Guid> SubmitAsync(SubmitCorrectionRequestDto dto)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var (employee, org, tz) = await ResolveEmployeeContextAsync(db);
            var (isHr, isOrgAdmin) = await GetUserRolesAsync(db, employee.UserId!);

            // OrgAdmin does not submit correction requests through this flow.
            if (isOrgAdmin)
                throw new CorrectionValidationException(
                    "OrgAdmin accounts do not submit attendance corrections. " +
                    "Use the HR Manual Correction tool instead.");

            // Employees may only submit employee-submittable types.
            if (!isHr)
                CorrectionValidationEngine.AssertEmployeeSubmittableType(dto.CorrectionType);

            CorrectionValidationEngine.AssertValidCorrectionType(dto.CorrectionType);
            var workDate = dto.WorkDate.Date;
            CorrectionValidationEngine.AssertValidWorkDate(workDate, tz);

            // ── Business rule checks (all must pass) ──────────────────────────────
            await CorrectionValidationEngine.AssertNotHolidayAsync(db, org.OrganizationId, workDate);
            await CorrectionValidationEngine.AssertIsWorkingDayAsync(db, org.OrganizationId, workDate);
            await CorrectionValidationEngine.AssertNotOnApprovedLeaveAsync(db, employee.EmployeeId, workDate);
            await CorrectionValidationEngine.AssertNotOnApprovedWfhAsync(db, employee.EmployeeId, workDate);
            //await CorrectionValidationEngine.AssertNoDuplicatePendingAsync(db, employee.EmployeeId, workDate);
            await CorrectionValidationEngine.AssertNoDuplicatePendingAsync(
    db,
    employee.EmployeeId,
    workDate,
    dto.CorrectionType);
            await CorrectionValidationEngine.AssertNoDuplicateApprovedForTypeAsync(
                db, employee.EmployeeId, workDate, dto.CorrectionType);

            var existing = await db.Attendances
                .AsNoTracking()
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employee.EmployeeId &&
                    a.WorkDate.Date == workDate);

            CorrectionValidationEngine.AssertCorrectionTypeMatchesAttendanceState(dto.CorrectionType, existing);

            var (requestedCheckInUtc, requestedCheckOutUtc) =
                ConvertRequestedTimesToUtc(dto.RequestedCheckIn, dto.RequestedCheckOut, workDate, tz);

            CorrectionValidationEngine.AssertTimeConsistency(
                dto.CorrectionType, requestedCheckInUtc, requestedCheckOutUtc);

            // ── Route to correct reviewer ─────────────────────────────────────────
            var (assignedReviewerId, reviewLevel, submittedByRole) =
                await ResolveReviewerAsync(db, employee, org, isHr);

            // ── Build and persist request ─────────────────────────────────────────
            var request = new AttendanceCorrectionRequest
            {
                AttendanceCorrectionRequestId = Guid.NewGuid(),
                OrganizationId = org.OrganizationId,
                EmployeeId = employee.EmployeeId,
                AttendanceId = existing?.AttendanceId,
                WorkDate = workDate,
                CorrectionType = dto.CorrectionType,
                AssignedReviewerEmployeeId = assignedReviewerId,
                ReviewLevel = reviewLevel,
                SubmittedByRole = submittedByRole,

                // Freeze current attendance state
                OriginalCheckIn = existing?.CheckIn is null ? null : TimeHelper.EnsureUtc(existing.CheckIn.Value),
                OriginalCheckOut = existing?.CheckOut is null ? null : TimeHelper.EnsureUtc(existing.CheckOut.Value),
                OriginalStatus = existing?.Status,
                OriginalTotalHours = existing?.TotalHours,

                RequestedCheckIn = requestedCheckInUtc,
                RequestedCheckOut = requestedCheckOutUtc,
                RequestedStatus = dto.RequestedStatus,

                //Reason = dto.Reason.Trim(),
                Reason = SafeTrim(dto.Reason),
                AttachmentPath = dto.AttachmentPath,
                Status = CorrectionStatuses.Pending,
                SubmittedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };

            db.AttendanceCorrectionRequests.Add(request);

            // Audit log added to same DbContext — saved in one round-trip
            AddAuditLog(db, request, CorrectionAuditActions.Submitted,
                employee.EmployeeId,
                $"{employee.FirstName} {employee.LastName}",
                $"Type: {dto.CorrectionType}. Reason: {SafeTrim(dto.Reason)}",
                null, null, requestedCheckInUtc, requestedCheckOutUtc, null, null);

            await db.SaveChangesAsync();

            // ──────────────────────────────────────────────────────────────────
            // NOTIFICATIONS
            // ──────────────────────────────────────────────────────────────────

            // Employee submitted → HR + OrgAdmin
            if (submittedByRole == "Employee")
            {
                // Notify assigned HR reviewer
                if (assignedReviewerId.HasValue)
                {
                    var hrUserId = await db.Employees
                        .Where(e => e.EmployeeId == assignedReviewerId.Value)
                        .Select(e => e.UserId)
                        .FirstOrDefaultAsync();

                    if (!string.IsNullOrEmpty(hrUserId))
                    {
                        await _notificationService.CreateNotificationAsync(
                            hrUserId,
                            "New Attendance Correction Request",
                            $"{employee.FirstName} {employee.LastName} submitted a correction for {workDate:dd MMM yyyy}.",
                            "/hr/corrections");
                    }
                }

                // Notify OrgAdmin
                await _notificationService.CreateOrgAdminNotificationsAsync(
                    org.OrganizationId,
                    "New Attendance Correction Request",
                    $"{employee.FirstName} {employee.LastName} submitted a correction for {workDate:dd MMM yyyy}.",
                    "/hr/corrections");
            }

            // HR submitted → OrgAdmin only
            if (submittedByRole == "HR")
            {
                await _notificationService.CreateOrgAdminNotificationsAsync(
                    org.OrganizationId,
                    "New Attendance Correction Request",
                    $"HR Team member {employee.FirstName} {employee.LastName} submitted a correction for {workDate:dd MMM yyyy}.",
                    "/hr/corrections");
            }

            // Dashboard refresh
            await _notificationService.BroadcastOrgDashboardRefreshAsync(org.OrganizationId);
            await _notificationService.BroadcastHrDashboardRefreshAsync(org.OrganizationId, employee.DepartmentId);

            return request.AttendanceCorrectionRequestId;
        }

        public async Task CancelAsync(CancelCorrectionDto dto)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var (employee, _, _) = await ResolveEmployeeContextAsync(db);

            // Load with tracking for the status update
            var request = await db.AttendanceCorrectionRequests
                .FirstOrDefaultAsync(r =>
                    r.AttendanceCorrectionRequestId == dto.AttendanceCorrectionRequestId)
                ?? throw new CorrectionValidationException("Correction request not found.");

            // Security: employee can only cancel their own request
            if (request.EmployeeId != employee.EmployeeId)
                throw new UnauthorizedAccessException("You can only cancel your own correction requests.");

            if (request.Status != CorrectionStatuses.Pending)
                throw new CorrectionValidationException(
                    $"Only Pending requests can be cancelled. This request is '{request.Status}'.");

            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                request.Status = CorrectionStatuses.Cancelled;
                request.UpdatedAt = DateTime.UtcNow;

                AddAuditLog(db, request, CorrectionAuditActions.Cancelled,
                    employee.EmployeeId,
                    $"{employee.FirstName} {employee.LastName}",
                    string.IsNullOrWhiteSpace(dto.CancelReason)
                        ? "Employee cancelled the request."
                        : $"Cancelled: {dto.CancelReason}",
                    null, null, null, null, null, null);

                await db.SaveChangesAsync();
                await tx.CommitAsync();

                // ──────────────────────────────────────────────────────────────────
                // 🚀 ADDED: BROADCAST STATE UPDATES UPON CANCELLATION
                // ──────────────────────────────────────────────────────────────────
                await _notificationService.BroadcastOrgDashboardRefreshAsync(request.OrganizationId);
                await _notificationService.BroadcastHrDashboardRefreshAsync(request.OrganizationId, employee.DepartmentId);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task ReviewAsync(ReviewCorrectionDto dto)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var currentUser = await _currentUser.GetUserAsync()
                ?? throw new UnauthorizedAccessException("User not authenticated.");

            //var (reviewer, org, tz) = await ResolveEmployeeContextAsync(db);

            var currentEmployee = await db.Employees
    .AsNoTracking()
    .FirstOrDefaultAsync(e => e.UserId == currentUser.Id);

            var (reviewer, org, tz) = await ResolveEmployeeContextAsync(db);

            reviewer ??= currentEmployee;

            var (isHr, isOrgAdmin) = await GetUserRolesAsync(db, currentUser.Id);

            // OrgAdmin may not have Employee record
            Guid? reviewerEmployeeId = reviewer?.EmployeeId;

            string reviewerName =
                reviewer != null
                    ? $"{reviewer.FirstName} {reviewer.LastName}"
                    : currentUser.UserName ?? "OrgAdmin";

            // Load correction request
            var request = await db.AttendanceCorrectionRequests
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r =>
                    r.AttendanceCorrectionRequestId == dto.AttendanceCorrectionRequestId)
                ?? throw new CorrectionValidationException(
                    "Correction request not found.");

            // Organization security
            CorrectionValidationEngine.AssertSameOrganization(
                request,
                org.OrganizationId);

            // Role security
            CorrectionValidationEngine.AssertReviewerCanReviewRequest(
                request,
                reviewer,
                isHr,
                isOrgAdmin);

            // Prevent self-review ONLY if reviewer has employee profile
            if (reviewer != null &&
                request.EmployeeId == reviewer.EmployeeId)
            {
                throw new UnauthorizedAccessException(
                    "You cannot review your own correction request.");
            }

            // Must still be pending
            if (request.Status != CorrectionStatuses.Pending)
            {
                throw new CorrectionValidationException(
                    $"This request is already '{request.Status}' and cannot be reviewed again.");
            }

            // Resolve approved times
            DateTime? approvedCheckInUtc = null;
            DateTime? approvedCheckOutUtc = null;

            if (dto.Approve)
            {
                approvedCheckInUtc = ResolveApprovedTime(
                    dto.HrOverrideCheckIn,
                    request.RequestedCheckIn,
                    request.WorkDate,
                    tz);

                approvedCheckOutUtc = ResolveApprovedTime(
                    dto.HrOverrideCheckOut,
                    request.RequestedCheckOut,
                    request.WorkDate,
                    tz);

                CorrectionValidationEngine.AssertTimeConsistency(
                    request.CorrectionType,
                    approvedCheckInUtc,
                    approvedCheckOutUtc);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(dto.RejectionReason))
                {
                    throw new CorrectionValidationException(
                        "A rejection reason is required when rejecting a request.");
                }
            }

            await using var tx = await db.Database.BeginTransactionAsync();

            try
            {
                // Recheck inside transaction
                var currentStatus = await db.AttendanceCorrectionRequests
                    .AsNoTracking()
                    .Where(r => r.AttendanceCorrectionRequestId ==
                                dto.AttendanceCorrectionRequestId)
                    .Select(r => r.Status)
                    .FirstOrDefaultAsync();

                if (currentStatus != CorrectionStatuses.Pending)
                {
                    throw new CorrectionValidationException(
                        $"Request was already processed (status: '{currentStatus}'). Refresh the page.");
                }

                if (dto.Approve)
                {
                    bool hrModifiedTimes =
                        (dto.HrOverrideCheckIn.HasValue &&
                         approvedCheckInUtc != request.RequestedCheckIn)
                        ||
                        (dto.HrOverrideCheckOut.HasValue &&
                         approvedCheckOutUtc != request.RequestedCheckOut);

                    if (hrModifiedTimes)
                    {
                        AddAuditLog(
                            db,
                            request,
                            CorrectionAuditActions.HrModified,
                            reviewerEmployeeId ,
                            reviewerName,
                            "Reviewer adjusted times before approval.",
                            request.RequestedCheckIn,
                            request.RequestedCheckOut,
                            approvedCheckInUtc,
                            approvedCheckOutUtc,
                            null,
                            null);
                    }

                    request.ApprovedCheckIn = approvedCheckInUtc;
                    request.ApprovedCheckOut = approvedCheckOutUtc;
                    request.HrNote = SafeTrim(dto.HrNote);
                    request.Status = CorrectionStatuses.Approved;
                    request.ReviewedByEmployeeId = reviewerEmployeeId;

                    request.ReviewedAt = DateTime.UtcNow;
                    request.UpdatedAt = DateTime.UtcNow;
                    var attendance = await ApplyCorrectionToAttendanceAsync(
                        request,
                        org,
                        tz,
                        db);

                    request.ApprovedStatus = attendance.Status;
                    request.IsApplied = true;
                    request.AppliedAt = DateTime.UtcNow;
                    AddAuditLog(
                        db,     
                        request,
                        CorrectionAuditActions.Approved,
                        reviewerEmployeeId ,
                        reviewerName,
                        dto.HrNote?.Trim(),
                        request.OriginalCheckIn,
                        request.OriginalCheckOut,
                        approvedCheckInUtc,
                        approvedCheckOutUtc,
                        request.OriginalStatus,
                        attendance.Status);

                    // Audit: attendance updated
                    AddAuditLog(
                        db,
                        request,
                        CorrectionAuditActions.Applied,
                        reviewerEmployeeId ,
                        reviewerName,
                        $"Attendance updated. New status: {attendance.Status}",
                        null,
                        null,
                        null,
                        null,
                        null,
                        null);
                }
                else
                {
                    request.Status = CorrectionStatuses.Rejected;

                    request.RejectionReason =
                        dto.RejectionReason!.Trim();

                    request.HrNote = dto.HrNote?.Trim();

                    // OrgAdmin may not have employee profile
                    request.ReviewedByEmployeeId = reviewerEmployeeId;

                    request.ReviewedAt = DateTime.UtcNow;
                    request.UpdatedAt = DateTime.UtcNow;

                    AddAuditLog(
                        db,
                        request,
                        CorrectionAuditActions.Rejected,
                        reviewerEmployeeId,
                        reviewerName,
                        $"Rejected. Reason: {dto.RejectionReason}",
                        null,
                        null,
                        null,
                        null,
                        request.OriginalStatus,
                        null);
                }

                //await db.SaveChangesAsync();
                //await tx.CommitAsync();
                //await _notificationService.BroadcastOrgDashboardRefreshAsync(request.OrganizationId);
                await db.SaveChangesAsync();
                await tx.CommitAsync();


                // ─────────────────────────────────────────────────────────────
                // NOTIFICATIONS AFTER REVIEW
                // ─────────────────────────────────────────────────────────────

                // Notify employee who submitted request
                var requestEmployeeUserId = await db.Employees
                    .Where(e => e.EmployeeId == request.EmployeeId)
                    .Select(e => e.UserId)
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrEmpty(requestEmployeeUserId))
                {
                    await _notificationService.CreateNotificationAsync(
                        requestEmployeeUserId,
                        dto.Approve
                            ? "Attendance Correction Approved"
                            : "Attendance Correction Rejected",

                        dto.Approve
                            ? $"Your attendance correction request for {request.WorkDate:dd MMM yyyy} was approved."
                            : $"Your attendance correction request for {request.WorkDate:dd MMM yyyy} was rejected.",

                        "/employee/corrections");
                }


                // 🚀 NEW: Notify HR when OrgAdmin reviews HR request
                if (request.SubmittedByRole == "HR")
                {
                    var hrUserId = await db.Employees
                        .Where(e => e.EmployeeId == request.EmployeeId)
                        .Select(e => e.UserId)
                        .FirstOrDefaultAsync();

                    if (!string.IsNullOrEmpty(hrUserId))
                    {
                        await _notificationService.CreateNotificationAsync(
                            hrUserId,
                            dto.Approve
                                ? "HR Correction Approved by OrgAdmin"
                                : "HR Correction Rejected by OrgAdmin",

                            dto.Approve
                                ? $"Your correction request for {request.WorkDate:dd MMM yyyy} was approved by OrgAdmin."
                                : $"Your correction request for {request.WorkDate:dd MMM yyyy} was rejected by OrgAdmin.",

                            "/hr/corrections");
                    }
                }


                // Dashboard refresh
                await _notificationService.BroadcastOrgDashboardRefreshAsync(request.OrganizationId);

                Guid departmentId = request.Employee?.DepartmentId ?? Guid.Empty;

                await _notificationService.BroadcastHrDashboardRefreshAsync(
                    request.OrganizationId,
                    departmentId);
                // Safe-fallback department refresh execution
                //Guid departmentId = request.Employee?.DepartmentId ?? Guid.Empty;
                //await _notificationService.BroadcastHrDashboardRefreshAsync(request.OrganizationId, departmentId);
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync();

                throw new CorrectionValidationException(
                    "This request was modified by another user while you were reviewing it. " +
                    "Please refresh the page and try again.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                var fullError =
                    ex.Message +
                    "\nINNER:\n" +
                    ex.InnerException?.Message;

                throw new Exception(fullError);
            }
        }
        public async Task<Guid> HrManualCorrectionAsync(HrManualCorrectionDto dto)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var (hrEmployee, org, tz) = await ResolveEmployeeContextAsync(db);
            //var (isHr, isOrgAdmin) = await GetUserRolesAsync(db, hrEmployee.UserId!);

            var currentUser = await _currentUser.GetUserAsync()
    ?? throw new UnauthorizedAccessException("User not authenticated.");

            var (isHr, isOrgAdmin) =
                await GetUserRolesAsync(db, currentUser.Id);

            if (!isHr && !isOrgAdmin)
                throw new UnauthorizedAccessException(
                    "Only HR or OrgAdmin can submit manual corrections.");

            var targetEmployee = await db.Employees
                .Include(e => e.Shift)
                .FirstOrDefaultAsync(e =>
                    e.EmployeeId == dto.EmployeeId &&
                    e.OrganizationId == org.OrganizationId)
                ?? throw new CorrectionValidationException(
                    "Target employee not found in your organisation.");

            // HR can only correct employees in their department (unless OrgAdmin)
            if (isHr && !isOrgAdmin &&
                targetEmployee.DepartmentId != hrEmployee.DepartmentId)
                throw new UnauthorizedAccessException(
                    "HR can only submit manual corrections for employees in their own department.");

            // HR cannot create a correction for themselves via this path
            if (targetEmployee.EmployeeId == hrEmployee.EmployeeId && !isOrgAdmin)
                throw new CorrectionValidationException(
                    "HR cannot submit a manual correction for themselves. " +
                    "Submit a regular correction request instead.");

            var workDate = dto.WorkDate.Date;
            CorrectionValidationEngine.AssertValidWorkDate(workDate, tz);
            await CorrectionValidationEngine.AssertNotHolidayAsync(db, org.OrganizationId, workDate);
            await CorrectionValidationEngine.AssertIsWorkingDayAsync(db, org.OrganizationId, workDate);

            var (newCheckInUtc, newCheckOutUtc) =
                ConvertRequestedTimesToUtc(dto.NewCheckIn, dto.NewCheckOut, workDate, tz);

            CorrectionValidationEngine.AssertTimeConsistency(
                CorrectionTypes.HrManualCorrection, newCheckInUtc, newCheckOutUtc);

            var existing = await db.Attendances
                .AsNoTracking()
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == targetEmployee.EmployeeId &&
                    a.WorkDate.Date == workDate);

            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                var request = new AttendanceCorrectionRequest
                {
                    AttendanceCorrectionRequestId = Guid.NewGuid(),
                    OrganizationId = org.OrganizationId,
                    EmployeeId = targetEmployee.EmployeeId,
                    AttendanceId = existing?.AttendanceId,
                    WorkDate = workDate,
                    CorrectionType = CorrectionTypes.HrManualCorrection,
                    InitiatedByHrEmployeeId = hrEmployee.EmployeeId,
                    ReviewLevel = CorrectionReviewLevels.OrgAdmin,
                    SubmittedByRole = isOrgAdmin ? "OrgAdmin" : "HR",

                    OriginalCheckIn = existing?.CheckIn is null ? null : TimeHelper.EnsureUtc(existing.CheckIn.Value),
                    OriginalCheckOut = existing?.CheckOut is null ? null : TimeHelper.EnsureUtc(existing.CheckOut.Value),
                    OriginalStatus = existing?.Status,
                    OriginalTotalHours = existing?.TotalHours,

                    RequestedCheckIn = newCheckInUtc,
                    RequestedCheckOut = newCheckOutUtc,
                    RequestedStatus = dto.OverrideStatus,

                    Reason = $"[HR Manual] {dto.HrNote.Trim()}",
                    HrNote = dto.HrNote.Trim(),

                    // Status and approved times set below based on AutoApply flag
                    Status = dto.AutoApply ? CorrectionStatuses.Approved : CorrectionStatuses.Pending,
                    ApprovedCheckIn = dto.AutoApply ? newCheckInUtc : null,
                    ApprovedCheckOut = dto.AutoApply ? newCheckOutUtc : null,

                    ReviewedByEmployeeId = dto.AutoApply ? hrEmployee.EmployeeId : null,
                    ReviewedAt = dto.AutoApply ? DateTime.UtcNow : null,
                    SubmittedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                };

                db.AttendanceCorrectionRequests.Add(request);

                AddAuditLog(db, request, CorrectionAuditActions.Submitted,
                    hrEmployee.EmployeeId,
                    $"{hrEmployee.FirstName} {hrEmployee.LastName}",
                    $"HR manual correction. Note: {dto.HrNote}",
                    null, null, newCheckInUtc, newCheckOutUtc, null, null);

                if (dto.AutoApply)
                {
                    var attendance = await ApplyCorrectionToAttendanceAsync(request, org, tz, db);

                    request.ApprovedStatus = attendance.Status;
                    request.IsApplied = true;
                    request.AppliedAt = DateTime.UtcNow;
                    request.UpdatedAt = DateTime.UtcNow;

                    AddAuditLog(db, request, CorrectionAuditActions.AutoApplied,
                        hrEmployee.EmployeeId,
                        $"{hrEmployee.FirstName} {hrEmployee.LastName}",
                        $"HR auto-applied. New attendance status: {attendance.Status}",
                        request.OriginalCheckIn, request.OriginalCheckOut,
                        newCheckInUtc, newCheckOutUtc,
                        existing?.Status, attendance.Status);
                }

                await db.SaveChangesAsync();
                await tx.CommitAsync();
                await _notificationService.BroadcastOrgDashboardRefreshAsync(org.OrganizationId);
                await _notificationService.BroadcastHrDashboardRefreshAsync(org.OrganizationId, targetEmployee.DepartmentId);

                return request.AttendanceCorrectionRequestId;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<List<CorrectionRequestListDto>> GetMyRequestsAsync(int pageSize = 50)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var (employee, _, tz) = await ResolveEmployeeContextAsync(db);
            var rows = await db.AttendanceCorrectionRequests
                .AsNoTracking()
                .Where(r => r.EmployeeId == employee.EmployeeId)
                .OrderByDescending(r => r.SubmittedAt)
                .Take(pageSize)
                .Select(r => new CorrectionRequestListDto
                {
                    AttendanceCorrectionRequestId = r.AttendanceCorrectionRequestId,
                    EmployeeId = r.EmployeeId,
                    EmployeeName = r.Employee.FirstName + " " + r.Employee.LastName,
                    Department = r.Employee.Department != null ? r.Employee.Department.Name : null,
                    EmployeeCode = r.Employee.EmployeeCode,
                    WorkDate = r.WorkDate,
                    CorrectionType = r.CorrectionType,
                    Status = r.Status,
                    ReviewLevel = r.ReviewLevel,
                    RequestedCheckIn = r.RequestedCheckIn,
                    RequestedCheckOut = r.RequestedCheckOut,
                    OriginalCheckIn = r.OriginalCheckIn,
                    OriginalCheckOut = r.OriginalCheckOut,
                    OriginalStatus = r.OriginalStatus,
                    OriginalTotalHours = r.OriginalTotalHours,
                    Reason = r.Reason,
                    HrNote = r.HrNote,
                    RejectionReason = r.RejectionReason,
                    SubmittedAt = r.SubmittedAt,
                    ReviewedAt = r.ReviewedAt,
                    ReviewedByName = r.ReviewedByEmployee != null
                        ? r.ReviewedByEmployee.FirstName + " " + r.ReviewedByEmployee.LastName
                        : null,
                    IsApplied = r.IsApplied,
                    IsHrInitiated = r.InitiatedByHrEmployeeId != null,
                    HasAttachment = r.AttachmentPath != null && r.AttachmentPath != "",
                    CanCancel = r.Status == CorrectionStatuses.Pending && !r.IsApplied,
                    IsOverdue = r.Status == CorrectionStatuses.Pending &&
                                      r.SubmittedAt <= DateTime.UtcNow.AddDays(-2),
                })
                .ToListAsync();

            return rows;
        }

        public async Task<PagedResult<CorrectionRequestListDto>> GetHrQueueAsync(CorrectionQueueFilterDto filter)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var currentUser = await _currentUser.GetUserAsync()
                ?? throw new UnauthorizedAccessException("Not authenticated.");

            var (isHr, isOrgAdmin) = await GetUserRolesAsync(db, currentUser.Id);

            if (!isHr && !isOrgAdmin)
                throw new UnauthorizedAccessException(
                    "Access denied. Insufficient administrative privileges.");

            Employee? currentEmployee = null;
            Guid organizationId;
            if (isOrgAdmin)
            {
                if (!currentUser.OrganizationId.HasValue)
                    throw new CorrectionValidationException(
                        "OrgAdmin organization not found.");

                organizationId = currentUser.OrganizationId.Value;
            }

            // =========================================================
            // HR FLOW
            // =========================================================
            else
            {
                currentEmployee = await db.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.UserId == currentUser.Id)
                    ?? throw new CorrectionValidationException(
                        "HR employee profile not found.");

                organizationId = currentEmployee.OrganizationId;
            }
            var query = db.AttendanceCorrectionRequests
     .AsNoTracking()
     .AsSplitQuery()
     .Include(r => r.Employee)
         .ThenInclude(e => e.Department)
     .Include(r => r.ReviewedByEmployee)
     .Where(r => r.OrganizationId == organizationId);

            // =========================================================
            // HR VIEW
            // =========================================================
            if (isHr && !isOrgAdmin)
            {
                query = query.Where(r =>

                    // HR can review only employee-submitted requests
                    r.ReviewLevel == CorrectionReviewLevels.Hr

                    // Same department only
                    && r.Employee.DepartmentId == currentEmployee!.DepartmentId

                    // HR cannot review their own request
                    && r.EmployeeId != currentEmployee.EmployeeId
                );
            }

            // =========================================================
            // ORGADMIN VIEW
            // =========================================================
            if (isOrgAdmin)
            {
                query = query.Where(r =>

                    // OrgAdmin can review:
                    // 1. HR submitted requests
                    // 2. Escalated/manual corrections
                    // 3. Employee requests if needed

                    r.ReviewLevel == CorrectionReviewLevels.OrgAdmin
                    || r.SubmittedByRole == "HR"
                    || r.SubmittedByRole == "Employee"
                );
            }
            // =========================================================
            // FILTERS
            // =========================================================
            if (!string.IsNullOrWhiteSpace(filter.Status))
                query = query.Where(r => r.Status == filter.Status);

            if (!string.IsNullOrWhiteSpace(filter.CorrectionType))
                query = query.Where(r => r.CorrectionType == filter.CorrectionType);

            if (filter.DepartmentId.HasValue)
                query = query.Where(r => r.Employee.DepartmentId == filter.DepartmentId.Value);

            if (filter.FromDate.HasValue)
                query = query.Where(r => r.WorkDate.Date >= filter.FromDate.Value.Date);

            if (filter.ToDate.HasValue)
                query = query.Where(r => r.WorkDate.Date <= filter.ToDate.Value.Date);

            if (!string.IsNullOrWhiteSpace(filter.SearchName))
            {
                var search = filter.SearchName.Trim().ToLower();

                query = query.Where(r =>
                    r.Employee.FirstName.ToLower().Contains(search) ||
                    r.Employee.LastName.ToLower().Contains(search) ||
                    r.Employee.EmployeeCode.ToLower().Contains(search));
            }
            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(r => r.SubmittedAt)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(r => new CorrectionRequestListDto
                {
                    AttendanceCorrectionRequestId = r.AttendanceCorrectionRequestId,

                    EmployeeId = r.EmployeeId,

                    EmployeeName =
                        r.Employee != null
                            ? r.Employee.FirstName + " " + r.Employee.LastName
                            : "Unknown Employee",

                    Department =
                        r.Employee != null && r.Employee.Department != null
                            ? r.Employee.Department.Name
                            : null,

                    EmployeeCode =
                        r.Employee != null
                            ? r.Employee.EmployeeCode
                            : null,

                    WorkDate = r.WorkDate,
                    CorrectionType = r.CorrectionType,
                    Status = r.Status,
                    ReviewLevel = r.ReviewLevel,

                    RequestedCheckIn = r.RequestedCheckIn,
                    RequestedCheckOut = r.RequestedCheckOut,

                    OriginalCheckIn = r.OriginalCheckIn,
                    OriginalCheckOut = r.OriginalCheckOut,

                    OriginalStatus = r.OriginalStatus,
                    OriginalTotalHours = r.OriginalTotalHours,

                    Reason = r.Reason,
                    HrNote = r.HrNote,
                    RejectionReason = r.RejectionReason,

                    SubmittedAt = r.SubmittedAt,
                    ReviewedAt = r.ReviewedAt,

                    ReviewedByName =
                        r.ReviewedByEmployee != null
                            ? r.ReviewedByEmployee.FirstName + " " + r.ReviewedByEmployee.LastName
                            : null,

                    IsApplied = r.IsApplied,
                    IsHrInitiated = r.InitiatedByHrEmployeeId != null,
                    HasAttachment =
                        r.AttachmentPath != null &&
                        r.AttachmentPath != ""
                })
                .ToListAsync();

            return new PagedResult<CorrectionRequestListDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = filter.Page,
                PageSize = filter.PageSize
            };
        }


        private static async Task<(Guid? assignedReviewerId, string reviewLevel, string submittedByRole)>
    ResolveReviewerAsync(
        ApplicationDbContext db,
        Employee employee,
        Organization org,
        bool isHr)
        {
            if (isHr)
            {
                var orgAdminExists = await db.Users
                    .Join(db.UserRoles,
                        u => u.Id,
                        ur => ur.UserId,
                        (u, ur) => new { u, ur.RoleId })
                    .Join(db.Roles,
                        x => x.RoleId,
                        r => r.Id,
                        (x, r) => new { x.u, RoleName = r.Name })
                    .AnyAsync(x =>
                        x.u.OrganizationId == org.OrganizationId &&
                        x.u.IsActive &&
                        x.RoleName == "OrgAdmin");

                if (!orgAdminExists)
                {
                    throw new CorrectionValidationException(
                        "No active OrgAdmin found for your organization.");
                }

                return (
                    null,
                    CorrectionReviewLevels.OrgAdmin,
                    "HR"
                );
            }

            // =========================================================
            // EMPLOYEE REQUEST → HR REVIEW
            // =========================================================
            var departmentHr = await FindDepartmentHrAsync(
                db,
                org.OrganizationId,
                employee.DepartmentId);

            if (departmentHr == null)
            {
                throw new CorrectionValidationException(
                    "No active HR found for your department.");
            }

            return (
                departmentHr.EmployeeId,
                CorrectionReviewLevels.Hr,
                "Employee"
            );
        }
        public async Task<CorrectionRequestDetailDto?> GetDetailAsync(Guid requestId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var currentUser = await _currentUser.GetUserAsync()
                ?? throw new UnauthorizedAccessException("Not authenticated.");

            var (isHr, isOrgAdmin) =
                await GetUserRolesAsync(db, currentUser.Id);

            var currentEmployee = await db.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == currentUser.Id);

            Guid organizationId;

            if (currentEmployee != null)
            {
                organizationId = currentEmployee.OrganizationId;
            }
            else if (currentUser.OrganizationId.HasValue)
            {
                organizationId = currentUser.OrganizationId.Value;
            }
            else
            {
                throw new CorrectionValidationException(
                    "Organisation information not found.");
            }

            var request = await db.AttendanceCorrectionRequests
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Employee)
                    .ThenInclude(e => e!.Department)
                .Include(x => x.ReviewedByEmployee)
                .Include(x => x.AuditLogs.OrderBy(l => l.OccurredAt))
                .FirstOrDefaultAsync(x =>
                    x.AttendanceCorrectionRequestId == requestId &&
                    x.OrganizationId == organizationId);

            if (request == null)
                return null;

            if (!isHr && !isOrgAdmin)
            {
                if (currentEmployee == null ||
                    request.EmployeeId != currentEmployee.EmployeeId)
                {
                    throw new UnauthorizedAccessException(
                        "You are not authorized to access this request.");
                }
            }
            if (isHr && !isOrgAdmin)
            {
                if (currentEmployee == null)
                    throw new UnauthorizedAccessException(
                        "HR employee profile not found.");

                if (request.Employee?.DepartmentId != currentEmployee.DepartmentId)
                {
                    throw new UnauthorizedAccessException(
                        "Unauthorized department access.");
                }

                if (request.EmployeeId == currentEmployee.EmployeeId)
                {
                    throw new UnauthorizedAccessException(
                        "HR cannot review their own request.");
                }
            }

            return new CorrectionRequestDetailDto
            {
                AttendanceCorrectionRequestId = request.AttendanceCorrectionRequestId,
                EmployeeId = request.EmployeeId,

                EmployeeName =
                    request.Employee != null
                        ? $"{request.Employee.FirstName} {request.Employee.LastName}"
                        : "Unknown Employee",

                Department = request.Employee?.Department?.Name,
                EmployeeCode = request.Employee?.EmployeeCode,

                WorkDate = request.WorkDate,
                CorrectionType = request.CorrectionType,
                Status = request.Status,
                ReviewLevel = request.ReviewLevel,

                RequestedCheckIn = request.RequestedCheckIn,
                RequestedCheckOut = request.RequestedCheckOut,

                OriginalCheckIn = request.OriginalCheckIn,
                OriginalCheckOut = request.OriginalCheckOut,

                OriginalStatus = request.OriginalStatus,
                OriginalTotalHours = request.OriginalTotalHours,

                ApprovedCheckIn = request.ApprovedCheckIn,
                ApprovedCheckOut = request.ApprovedCheckOut,
                ApprovedStatus = request.ApprovedStatus,

                Reason = request.Reason,
                HrNote = request.HrNote,
                RejectionReason = request.RejectionReason,

                SubmittedAt = request.SubmittedAt,
                ReviewedAt = request.ReviewedAt,

                ReviewedByName =
                    request.ReviewedByEmployee != null
                        ? request.ReviewedByEmployee.FirstName + " " +
                          request.ReviewedByEmployee.LastName
                        : request.SubmittedByRole == "HR"
                            ? "OrgAdmin"
                            : null,

                AppliedAt = request.AppliedAt,
                IsApplied = request.IsApplied,
                IsHrInitiated = request.IsHrInitiated,

                HasAttachment =
                    !string.IsNullOrWhiteSpace(request.AttachmentPath),

                CanCancel =
                    currentEmployee != null &&
                    request.Status == CorrectionStatuses.Pending &&
                    !request.IsApplied &&
                    request.EmployeeId == currentEmployee.EmployeeId,

                IsOverdue =
                    request.Status == CorrectionStatuses.Pending &&
                    request.SubmittedAt <= DateTime.UtcNow.AddDays(-2),

                AuditLogs = request.AuditLogs
                    .OrderBy(l => l.OccurredAt)
                    .Select(l => new CorrectionAuditLogDto
                    {
                        Action = l.Action,
                        ActorName = l.ActorName,
                        Notes = l.Notes,
                        OccurredAt = l.OccurredAt,

                        PreviousCheckIn = l.PreviousCheckIn,
                        PreviousCheckOut = l.PreviousCheckOut,

                        NewCheckIn = l.NewCheckIn,
                        NewCheckOut = l.NewCheckOut,

                        PreviousStatus = l.PreviousStatus,
                        NewStatus = l.NewStatus,
                    })
                    .ToList(),
            };
        }
        public async Task<CorrectionQueueSummaryDto> GetQueueSummaryAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var currentUser = await _currentUser.GetUserAsync()
                ?? throw new UnauthorizedAccessException("Not authenticated.");

            var (isHr, isOrgAdmin) =
                await GetUserRolesAsync(db, currentUser.Id);

            if (!isHr && !isOrgAdmin)
            {
                throw new UnauthorizedAccessException(
                    "Administrative access required.");
            }

            Employee? currentEmployee = null;

            Guid organizationId;

            if (isOrgAdmin)
            {
                if (!currentUser.OrganizationId.HasValue)
                {
                    throw new CorrectionValidationException(
                        "Organisation information missing.");
                }

                organizationId = currentUser.OrganizationId.Value;
            }
            else
            {
                currentEmployee = await db.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.UserId == currentUser.Id)
                    ?? throw new CorrectionValidationException(
                        "HR employee profile not found.");

                organizationId = currentEmployee.OrganizationId;
            }

            var now = DateTime.UtcNow;

            IQueryable<AttendanceCorrectionRequest> query =
    db.AttendanceCorrectionRequests
        .AsNoTracking()
        .Where(r => r.OrganizationId == organizationId);

            // =========================================================
            // HR VIEW
            // =========================================================
            if (isHr && !isOrgAdmin)
            {
                query = query.Where(r =>
                    r.ReviewLevel == CorrectionReviewLevels.Hr &&
                    r.Employee.DepartmentId == currentEmployee!.DepartmentId &&
                    r.EmployeeId != currentEmployee.EmployeeId);
            }

            // =========================================================
            // ORGADMIN VIEW
            // =========================================================
            if (isOrgAdmin)
            {
                query = query.Where(r =>
                    r.ReviewLevel == CorrectionReviewLevels.OrgAdmin
                    || r.SubmittedByRole == "HR"
                    || r.SubmittedByRole == "Employee");
            }

            return new CorrectionQueueSummaryDto
            {
                TotalPending = await query.CountAsync(r =>
                    r.Status == CorrectionStatuses.Pending),

                PendingOlderThan2Days = await query.CountAsync(r =>
                    r.Status == CorrectionStatuses.Pending &&
                    r.SubmittedAt <= now.AddDays(-2)),

                ApprovedThisWeek = await query.CountAsync(r =>
                    r.Status == CorrectionStatuses.Approved &&
                    r.ReviewedAt >= now.AddDays(-7)),

                RejectedThisWeek = await query.CountAsync(r =>
                    r.Status == CorrectionStatuses.Rejected &&
                    r.ReviewedAt >= now.AddDays(-7)),

                HrRequestsPendingOrgAdminReview =
                    isOrgAdmin
                        ? await db.AttendanceCorrectionRequests
                            .AsNoTracking()
                            .CountAsync(r =>
                                r.OrganizationId == organizationId &&
                                r.ReviewLevel == CorrectionReviewLevels.OrgAdmin &&
                                r.Status == CorrectionStatuses.Pending)
                        : 0
            };
        }
        public async Task<CorrectionPreValidationResult> PreValidateAsync(
            DateTime workDate, string correctionType)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var (employee, org, tz) = await ResolveEmployeeContextAsync(db);

            return await CorrectionValidationEngine.PreValidateAsync(
                db, org.OrganizationId, employee.EmployeeId, workDate.Date, correctionType, tz);
        }

        private async Task<Attendance> ApplyCorrectionToAttendanceAsync(
            AttendanceCorrectionRequest request,
            Organization org,
            TimeZoneInfo tz,
            ApplicationDbContext db)
        {
            Attendance? attendance = null;
           if (request.AttendanceId.HasValue)
            {
                attendance = await db.Attendances
                    .Include(a => a.Employee)
                    .ThenInclude(e => e!.Shift)
                    .FirstOrDefaultAsync(a =>
                        a.AttendanceId == request.AttendanceId.Value);
            }

            if (attendance == null)
            {
                attendance = await db.Attendances
                    .Include(a => a.Employee)
                    .ThenInclude(e => e!.Shift)
                    .FirstOrDefaultAsync(a =>
                        a.EmployeeId == request.EmployeeId &&
                        a.WorkDate.Date == request.WorkDate.Date);

                if (attendance != null)
                {
                    request.AttendanceId = attendance.AttendanceId;
                }
            }

            if (attendance == null)
            {
                var emp = await db.Employees
                    .Include(e => e.Shift)
                    .FirstOrDefaultAsync(e => e.EmployeeId == request.EmployeeId)
                    ?? throw new CorrectionValidationException("Employee not found.");

                attendance = new Attendance
                {
                    AttendanceId = Guid.NewGuid(),
                    UserId = emp.UserId!,
                    EmployeeId = emp.EmployeeId,
                    OrganizationId = org.OrganizationId,
                    WorkDate = request.WorkDate,
                    IsPresent = true,
                    IsManual = true,
                    CreatedAt = DateTime.UtcNow,
                    Employee = emp,
                };
                db.Attendances.Add(attendance);
                request.AttendanceId = attendance.AttendanceId;
            }

            var shift = attendance.Employee?.Shift;
            switch (request.CorrectionType)
            {
                case CorrectionTypes.ForgotCheckIn:
                    attendance.CheckIn = request.ApprovedCheckIn;
                    break;

                case CorrectionTypes.ForgotCheckOut:
                    attendance.CheckOut = request.ApprovedCheckOut;
                    attendance.ActualCheckOut = request.ApprovedCheckOut;
                    break;

                case CorrectionTypes.WrongTime:
                case CorrectionTypes.AbsentButWorked:
                case CorrectionTypes.HrManualCorrection:
                    if (request.ApprovedCheckIn.HasValue) attendance.CheckIn = request.ApprovedCheckIn;
                    if (request.ApprovedCheckOut.HasValue)
                    {
                        attendance.CheckOut = request.ApprovedCheckOut;
                        attendance.ActualCheckOut = request.ApprovedCheckOut;
                    }
                    break;

                case CorrectionTypes.OvertimeCorrection:
                    attendance.CheckOut = request.ApprovedCheckOut;
                    attendance.ActualCheckOut = request.ApprovedCheckOut;
                    attendance.IsAutoCheckedOut = false;
                    break;

                default:
                    throw new CorrectionValidationException(
                        $"Unknown correction type: '{request.CorrectionType}'.");
            }
            attendance.IsPresent = true;
            attendance.IsManual = true;
            attendance.IsHrCorrected = true;
            attendance.UpdatedAt = DateTime.UtcNow;
            attendance.UpdatedBy = request.ReviewedByEmployeeId;
            attendance.LastModifiedByEmployeeId = request.ReviewedByEmployeeId;
            attendance.LastModifiedAt = DateTime.UtcNow;
            attendance.ModificationReason =
                $"Correction #{request.AttendanceCorrectionRequestId:N}";
            _calc.Recalculate(attendance, shift, tz);
            attendance.NeedsOvertimeApproval = false;
            if (!string.IsNullOrWhiteSpace(request.ApprovedStatus))
                attendance.Status = request.ApprovedStatus;

            return attendance;
        }


        private static async Task<ApplicationUser?> FindOrgAdminAsync(
     ApplicationDbContext db,
     Guid organizationId)
        {
            var orgAdmins = await (
                from user in db.Users
                join userRole in db.UserRoles
                    on user.Id equals userRole.UserId
                join role in db.Roles
                    on userRole.RoleId equals role.Id
                where user.OrganizationId == organizationId
                      && user.IsActive
                      && role.Name == "OrgAdmin"
                select user
            ).ToListAsync();

            return orgAdmins.FirstOrDefault();
        }

        private static async Task<Employee?> FindDepartmentHrAsync(
            ApplicationDbContext db, Guid organizationId, Guid departmentId)
        {
            return await db.Employees
                .Join(db.UserRoles, e => e.UserId, ur => ur.UserId,
                    (e, ur) => new { Employee = e, ur.RoleId })
                .Join(db.Roles, x => x.RoleId, r => r.Id,
                    (x, r) => new { x.Employee, RoleName = r.Name })
                .Where(x =>
                    x.Employee.OrganizationId == organizationId &&
                    x.Employee.DepartmentId == departmentId &&
                    x.Employee.Status == "Active" &&
                    x.RoleName == "HR")
                .Select(x => x.Employee)
                .FirstOrDefaultAsync();
        }

        private static (DateTime? checkInUtc, DateTime? checkOutUtc)
            ConvertRequestedTimesToUtc(
                DateTime? localCheckIn,
                DateTime? localCheckOut,
                DateTime workDate,
                TimeZoneInfo tz)
        {
            DateTime? checkInLocal = null;
            DateTime? checkOutLocal = null;

            if (localCheckIn.HasValue)
                checkInLocal = workDate.Date.Add(localCheckIn.Value.TimeOfDay);

            if (localCheckOut.HasValue)
            {
                checkOutLocal = workDate.Date.Add(localCheckOut.Value.TimeOfDay);
                if (checkInLocal.HasValue && checkOutLocal.Value <= checkInLocal.Value)
                    checkOutLocal = checkOutLocal.Value.AddDays(1);
            }

            return (
                ToUtcSafe(checkInLocal, tz),
                ToUtcSafe(checkOutLocal, tz)
            );
        }

        private static DateTime? ToUtcSafe(DateTime? localTime, TimeZoneInfo tz)
        {
            if (!localTime.HasValue) return null;
            var unspecified = DateTime.SpecifyKind(localTime.Value, DateTimeKind.Unspecified);
            var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        }

        private static string SafeTrim(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim();
        }

        private static DateTime? ResolveApprovedTime(
            DateTime? hrOverrideLocal,
            DateTime? requestedUtc,
            DateTime workDate,
            TimeZoneInfo tz)
        {
            if (!hrOverrideLocal.HasValue) return requestedUtc;

            var local = workDate.Date.Add(hrOverrideLocal.Value.TimeOfDay);

            if (requestedUtc.HasValue)
            {
                var requestedLocal = TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(requestedUtc.Value, DateTimeKind.Utc), tz);

                if (local.TimeOfDay < requestedLocal.TimeOfDay)
                    local = local.AddDays(1);
            }

            return ToUtcSafe(local, tz);
        }
        private static void AddAuditLog(
            ApplicationDbContext db,
            AttendanceCorrectionRequest request,
            string action,
            Guid? actorEmployeeId,
            string? actorName,
            string? notes,
            DateTime? previousCheckIn,
            DateTime? previousCheckOut,
            DateTime? newCheckIn,
            DateTime? newCheckOut,
            string? previousStatus,
            string? newStatus)
        {
            db.CorrectionAuditLogs.Add(new CorrectionAuditLog
            {
                CorrectionAuditLogId = Guid.NewGuid(),
                AttendanceCorrectionRequestId = request.AttendanceCorrectionRequestId,
                OrganizationId = request.OrganizationId,
                Action = action,
                ActorEmployeeId = actorEmployeeId,
                ActorName = actorName,
                Notes = notes,
                OccurredAt = DateTime.UtcNow,
                PreviousCheckIn = previousCheckIn,
                PreviousCheckOut = previousCheckOut,
                NewCheckIn = newCheckIn,
                NewCheckOut = newCheckOut,
                PreviousStatus = previousStatus,
                NewStatus = newStatus,
            });
        }
        private async Task<(Employee? employee, Organization org, TimeZoneInfo tz)>
      ResolveEmployeeContextAsync(ApplicationDbContext db)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new UnauthorizedAccessException("User not authenticated.");

            var employee = await db.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            Guid organizationId;

            if (employee != null)
            {
                organizationId = employee.OrganizationId;
            }

            else if (user.OrganizationId.HasValue)
            {
                organizationId = user.OrganizationId.Value;
            }

            else
            {
                throw new CorrectionValidationException(
                    "Organisation information not found.");
            }

            var org = await db.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrganizationId == organizationId)
                ?? throw new CorrectionValidationException(
                    "Organisation not found.");

            var tz = TimeHelper.GetOrgTimeZone(org.TimeZoneId);

            return (employee, org, tz);
        }

       

        private static async Task<(bool isHr, bool isOrgAdmin)> GetUserRolesAsync(
            ApplicationDbContext db, string userId)
        {
            var roles = await db.UserRoles
                .Where(x => x.UserId == userId)
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                .ToListAsync();

            return (roles.Contains("HR"), roles.Contains("OrgAdmin"));
        }
    }

    public class CorrectionValidationException : Exception
    {
        public CorrectionValidationException(string message) : base(message) { }
    }
}