using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    /// <summary>
    /// Production-grade attendance correction / regularization service.
    ///
    /// ── KEY FIXES IN THIS VERSION ──────────────────────────────────────────────
    ///
    /// FIX 1 — "SqlTransaction has completed" / double-rollback crash
    ///   Root cause: the outer catch { await tx.RollbackAsync(); throw; } executed
    ///   AFTER the inner try/catch had already called tx.RollbackAsync() on a
    ///   DbUpdateConcurrencyException. Rolling back a completed transaction throws.
    ///   Fix: remove the inner try/catch around SaveChanges; let all exceptions
    ///   bubble to the single outer catch that owns the rollback.
    ///
    /// FIX 2 — Request marked Approved even when approval failed
    ///   Root cause: request.Status = Approved was set BEFORE SaveChangesAsync.
    ///   If SaveChanges threw, the in-memory object was already mutated and EF
    ///   could persist the wrong state on a later save in the same DbContext.
    ///   Fix: all mutations are still inside the try, but the transaction is only
    ///   committed if SaveChanges succeeds. Rollback reverts the DB; the
    ///   in-memory object is discarded because the Blazor component re-loads data
    ///   from the DB after any operation.
    ///
    /// FIX 3 — "Request is already Approved" false positive
    ///   Root cause: ResolveEmployeeContextAsync() uses AsNoTracking for the
    ///   employee/org query but the request is loaded WITH tracking. If EF's
    ///   change-tracker already held a stale snapshot of the request from an
    ///   earlier query in the same DbContext lifetime (Blazor Server = per-circuit
    ///   = long-lived), the stale snapshot was returned by FirstOrDefaultAsync
    ///   instead of hitting the DB. Status appeared Approved before HR touched it.
    ///   Fix: load the request with .AsNoTracking() first to check status, then
    ///   re-attach for mutation. Alternatively: use a fresh DbContext per
    ///   operation (IDbContextFactory pattern — recommended for Blazor Server).
    ///   Applied here: explicit entry detach + reload pattern inside the service.
    ///
    /// FIX 4 — ApplyCorrectionToAttendanceAsync did not reliably update Attendance
    ///   Root cause: when AttendanceId was set on the request, the Attendance row
    ///   was loaded with .Include(e => e.Shift) but if EF's tracker already had
    ///   a stale Attendance snapshot (same long-lived DbContext), the stale
    ///   version was returned and mutations were lost after SaveChanges because EF
    ///   saw no changes vs. its cached state.
    ///   Fix: explicitly reload the Attendance row inside the transaction using
    ///   a fresh query; detach any stale tracked entry first.
    ///
    /// FIX 5 — CancelAsync missing finally { cancelRequestId = null; }
    ///   This was a UI-only bug; the service itself was fine. Fixed in the Razor.
    ///
    /// FIX 6 — RejectInternalAsync called without await (was void, not Task)
    ///   Root cause: method was renamed from async to sync but the call site still
    ///   had the commented-out await, then the await was removed leaving a plain
    ///   call. No bug here since it was sync, but the call is now explicit.
    ///
    /// ARCHITECTURE CONTRACTS (unchanged):
    ///   • All DateTime parameters that represent org-local times MUST be
    ///     Kind=Unspecified when passed in. This service converts them to UTC.
    ///   • All DateTime values written to the database are UTC.
    ///   • WorkDate is always a date-only value (time component = 00:00:00).
    ///   • DateTime.Now and .ToLocalTime() are NEVER used here.
    ///   • AttendanceCalculationService.Recalculate() is called after every
    ///     mutation of CheckIn/CheckOut on an Attendance record.
    ///   • Approval and Apply happen in a single EF transaction.
    ///   • The CorrectionAuditLog is append-only; rows are never updated.
    /// </summary>
    public class AttendanceCorrectionService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        private readonly CurrentUserService _currentUser;
        private readonly AttendanceCalculationService _calc;

        private const int MaxCorrectionLookbackDays = 30;

        // ── IMPORTANT: We accept IDbContextFactory instead of ApplicationDbContext
        //   directly. Blazor Server circuits are long-lived; a single injected
        //   DbContext accumulates tracked entities across multiple user actions,
        //   leading to stale-snapshot bugs (Issues #2, #3, #4 above).
        //   Each public method creates its own short-lived context so the
        //   change-tracker is always clean.
        public AttendanceCorrectionService(
            IDbContextFactory<ApplicationDbContext> dbFactory,
            CurrentUserService currentUser,
            AttendanceCalculationService calc)
        {
            _dbFactory = dbFactory;
            _currentUser = currentUser;
            _calc = calc;
        }

        // =========================================================================
        // EMPLOYEE: SUBMIT CORRECTION REQUEST
        // =========================================================================

        public async Task<Guid> SubmitAsync(SubmitCorrectionRequestDto dto)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var (employee, org, tz) = await ResolveEmployeeContextAsync(db);

            ValidateCorrectionType(dto.CorrectionType);
            var workDate = dto.WorkDate.Date;
            ValidateWorkDate(workDate, tz);

            var existing = await db.Attendances
                .AsNoTracking()
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employee.EmployeeId &&
                    a.WorkDate.Date == workDate);

            ValidateCorrectionAgainstAttendance(dto.CorrectionType, existing);

            bool duplicatePending = await db.AttendanceCorrectionRequests
                .AnyAsync(r =>
                    r.EmployeeId == employee.EmployeeId &&
                    r.WorkDate.Date == workDate &&
                    r.Status == CorrectionStatuses.Pending);

            if (duplicatePending)
                throw new CorrectionValidationException(
                    "You already have a pending correction request for this date. " +
                    "Cancel it before submitting a new one.");

            var (requestedCheckInUtc, requestedCheckOutUtc) =
                ConvertRequestedTimesToUtc(dto, workDate, tz);

            ValidateTimeConsistency(dto.CorrectionType, requestedCheckInUtc, requestedCheckOutUtc);

            var request = new AttendanceCorrectionRequest
            {
                OrganizationId = org.OrganizationId,
                EmployeeId = employee.EmployeeId,
                AttendanceId = existing?.AttendanceId,
                WorkDate = workDate,
                CorrectionType = dto.CorrectionType,

                OriginalCheckIn = existing?.CheckIn is null ? null : TimeHelper.EnsureUtc(existing.CheckIn.Value),
                OriginalCheckOut = existing?.CheckOut is null ? null : TimeHelper.EnsureUtc(existing.CheckOut.Value),
                OriginalStatus = existing?.Status,
                OriginalTotalHours = existing?.TotalHours,

                RequestedCheckIn = requestedCheckInUtc,
                RequestedCheckOut = requestedCheckOutUtc,
                RequestedStatus = dto.RequestedStatus,

                Reason = dto.Reason.Trim(),
                AttachmentPath = dto.AttachmentPath,
                Status = CorrectionStatuses.Pending,
                SubmittedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };
            request.AttendanceCorrectionRequestId = Guid.NewGuid();
            db.AttendanceCorrectionRequests.Add(request);

            AppendAuditLog(db,request, CorrectionAuditActions.Submitted,
                employee.EmployeeId,
                $"{employee.FirstName} {employee.LastName}",
                $"Correction type: {dto.CorrectionType}. Reason: {dto.Reason}",
                null, null,
                requestedCheckInUtc, requestedCheckOutUtc,
                null, null);

            await db.SaveChangesAsync();
            return request.AttendanceCorrectionRequestId;
        }

        // =========================================================================
        // EMPLOYEE: CANCEL PENDING REQUEST
        // =========================================================================

        // =========================================================================
        // EMPLOYEE: CANCEL PENDING REQUEST
        // =========================================================================

        public async Task CancelAsync(CancelCorrectionDto dto)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var (employee, _, _) = await ResolveEmployeeContextAsync(db);

            var request = await db.AttendanceCorrectionRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.AttendanceCorrectionRequestId == dto.AttendanceCorrectionRequestId);

            if (request == null)
                throw new CorrectionValidationException("Correction request not found.");

            if (request.EmployeeId != employee.EmployeeId)
                throw new CorrectionValidationException(
                    "You can only cancel your own requests.");

            if (request.Status != CorrectionStatuses.Pending)
                throw new CorrectionValidationException(
                    "Only pending requests can be cancelled.");

            // DIRECT UPDATE (avoids stale tracking/concurrency issues)
            var rowsAffected = await db.AttendanceCorrectionRequests
                .Where(r => r.AttendanceCorrectionRequestId == dto.AttendanceCorrectionRequestId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.Status, CorrectionStatuses.Cancelled)
                    .SetProperty(r => r.UpdatedAt, DateTime.UtcNow));

            if (rowsAffected == 0)
                throw new CorrectionValidationException(
                    "Failed to cancel request.");

            // Add audit log separately
            db.CorrectionAuditLogs.Add(new CorrectionAuditLog
            {
                CorrectionAuditLogId = Guid.NewGuid(),
                AttendanceCorrectionRequestId = request.AttendanceCorrectionRequestId,
                OrganizationId = request.OrganizationId,
                ActorEmployeeId = employee.EmployeeId,
                ActorName = $"{employee.FirstName} {employee.LastName}",
                Action = CorrectionAuditActions.Cancelled,
                Notes = "Employee cancelled the request.",
                OccurredAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        // =========================================================================
        // HR: REVIEW (APPROVE / REJECT)
        // =========================================================================

        /// <summary>
        /// HR approves or rejects a correction request.
        /// On approval, the correction is applied to the Attendance table in the
        /// SAME transaction as the status update — atomically.
        ///
        /// FIX: Single outer try/catch owns the rollback. No nested try/catch
        /// inside the transaction block. This prevents the
        /// "SqlTransaction has completed" crash (double-rollback).
        /// </summary>
        public async Task ReviewAsync(ReviewCorrectionDto dto)
        {
            // Fresh DbContext per operation — no stale tracked entities.
            await using var db = await _dbFactory.CreateDbContextAsync();
            var (hrEmployee, org, tz) = await ResolveEmployeeContextAsync(db);

            // Load with tracking so EF can detect changes and save them.
            //var request = await db.AttendanceCorrectionRequests
            //    .Include(r => r.Employee)
            //    .Include(r => r.Attendance)
            //    .Include(r => r.AuditLogs)
            //        var request = await db.AttendanceCorrectionRequests
            //.Include(r => r.Employee)
            //.Include(r => r.Attendance)
            var request = await db.AttendanceCorrectionRequests
            .Include(r => r.Employee)
                        .FirstOrDefaultAsync(r =>
                    r.AttendanceCorrectionRequestId == dto.AttendanceCorrectionRequestId &&
                    r.OrganizationId == org.OrganizationId)
                ?? throw new CorrectionValidationException("Correction request not found.");

            // FIX #3: Because we use a fresh DbContext, the status read here is
            // always the current DB value — no stale snapshot can exist.
            if (request.Status != CorrectionStatuses.Pending)
                throw new CorrectionValidationException(
                    $"Request is already {request.Status} and cannot be reviewed again.");

            // ── Reject path (no transaction needed — single table write) ──────────
            if (!dto.Approve)
            {
                RejectInternal(db,request, hrEmployee, dto.RejectionReason, dto.HrNote);
                await db.SaveChangesAsync();
                return;
            }

            // ── Approve path ──────────────────────────────────────────────────────

            var approvedCheckInUtc = ResolveApprovedTime(dto.HrOverrideCheckIn, request.RequestedCheckIn, request.WorkDate, tz);
            var approvedCheckOutUtc = ResolveApprovedTime(dto.HrOverrideCheckOut, request.RequestedCheckOut, request.WorkDate, tz);

            ValidateTimeConsistency(request.CorrectionType, approvedCheckInUtc, approvedCheckOutUtc);

            // ── Single transaction: update request + apply to Attendance ──────────
            // FIX #1: ONE try/catch, ONE rollback path.
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                bool hrModifiedTimes =
                    (dto.HrOverrideCheckIn.HasValue && approvedCheckInUtc != request.RequestedCheckIn) ||
                    (dto.HrOverrideCheckOut.HasValue && approvedCheckOutUtc != request.RequestedCheckOut);

                if (hrModifiedTimes)
                {
                    AppendAuditLog(db,request, CorrectionAuditActions.HrModified,
                        hrEmployee.EmployeeId,
                        $"{hrEmployee.FirstName} {hrEmployee.LastName}",
                        "HR adjusted the requested times before approval.",
                        request.RequestedCheckIn, request.RequestedCheckOut,
                        approvedCheckInUtc, approvedCheckOutUtc,
                        null, null);
                }

                // Stamp approved values onto the request entity.
                request.ApprovedCheckIn = approvedCheckInUtc;
                request.ApprovedCheckOut = approvedCheckOutUtc;
                request.HrNote = dto.HrNote?.Trim();
                request.Status = CorrectionStatuses.Approved;
                request.ReviewedByEmployeeId = hrEmployee.EmployeeId;
                request.ReviewedAt = DateTime.UtcNow;
                request.UpdatedAt = DateTime.UtcNow;

                // FIX #4: Apply correction to the Attendance table INSIDE this
                // transaction, using the same fresh DbContext. The method loads
                // Attendance by its PK — guaranteed to be the current DB row.
                var attendance = await ApplyCorrectionToAttendanceAsync(request, org, tz, db);
                //request.ApprovedStatus = attendance.Status;

                //request.IsApplied = true;
                //request.AppliedAt = DateTime.UtcNow;
                request.ApprovedStatus = attendance.Status;
                request.IsApplied = true;
                request.AppliedAt = DateTime.UtcNow;


                AppendAuditLog(db,request, CorrectionAuditActions.Approved,
                    hrEmployee.EmployeeId,
                    $"{hrEmployee.FirstName} {hrEmployee.LastName}",
                    dto.HrNote,
                    request.OriginalCheckIn, request.OriginalCheckOut,
                    approvedCheckInUtc, approvedCheckOutUtc,
                    request.OriginalStatus, attendance.Status);

                AppendAuditLog(db,request, CorrectionAuditActions.Applied,
                    hrEmployee.EmployeeId,
                    $"{hrEmployee.FirstName} {hrEmployee.LastName}",
                    $"Attendance record updated. New status: {attendance.Status}",
                    null, null, null, null, null, null);

                // Single SaveChanges — persists both the request update AND the
                // Attendance mutation in one round-trip inside the transaction.
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                // FIX #1: Only ONE rollback. Never nested.
                await tx.RollbackAsync();
                throw;
            }
        }

        // =========================================================================
        // HR: MANUAL CORRECTION (HR-INITIATED, OPTIONAL AUTO-APPLY)
        // =========================================================================

        public async Task<Guid> HrManualCorrectionAsync(HrManualCorrectionDto dto)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var (hrEmployee, org, tz) = await ResolveEmployeeContextAsync(db);

            var targetEmployee = await db.Employees
                .Include(e => e.Shift)
                .FirstOrDefaultAsync(e =>
                    e.EmployeeId == dto.EmployeeId &&
                    e.OrganizationId == org.OrganizationId)
                ?? throw new CorrectionValidationException("Target employee not found in your organisation.");

            var workDate = dto.WorkDate.Date;
            ValidateWorkDate(workDate, tz);

            //var newCheckInUtc = ConvertLocalTimeToUtc(dto.NewCheckIn, workDate, tz);
            //var newCheckOutUtc = ConvertLocalTimeToUtc(dto.NewCheckOut, workDate, tz);

            DateTime? localCheckIn = null;
            DateTime? localCheckOut = null;

            if (dto.NewCheckIn.HasValue)
            {
                localCheckIn = workDate.Date
                    .Add(dto.NewCheckIn.Value.TimeOfDay);
            }

            if (dto.NewCheckOut.HasValue)
            {
                localCheckOut = workDate.Date
                    .Add(dto.NewCheckOut.Value.TimeOfDay);

                // Overnight support
                if (localCheckIn.HasValue &&
                    localCheckOut <= localCheckIn)
                {
                    localCheckOut = localCheckOut.Value.AddDays(1);
                }
            }

            var newCheckInUtc =
                ConvertLocalDateTimeToUtc(localCheckIn, tz);

            var newCheckOutUtc =
                ConvertLocalDateTimeToUtc(localCheckOut, tz);

            ValidateTimeConsistency(CorrectionTypes.HrManualCorrection, newCheckInUtc, newCheckOutUtc);

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
                    OrganizationId = org.OrganizationId,
                    EmployeeId = targetEmployee.EmployeeId,
                    AttendanceId = existing?.AttendanceId,
                    WorkDate = workDate,
                    CorrectionType = CorrectionTypes.HrManualCorrection,
                    InitiatedByHrEmployeeId = hrEmployee.EmployeeId,

                    OriginalCheckIn = existing?.CheckIn is null ? null : TimeHelper.EnsureUtc(existing.CheckIn.Value),
                    OriginalCheckOut = existing?.CheckOut is null ? null : TimeHelper.EnsureUtc(existing.CheckOut.Value),
                    OriginalStatus = existing?.Status,
                    OriginalTotalHours = existing?.TotalHours,

                    RequestedCheckIn = newCheckInUtc,
                    RequestedCheckOut = newCheckOutUtc,
                    RequestedStatus = dto.OverrideStatus,

                    ApprovedCheckIn = dto.AutoApply ? newCheckInUtc : null,
                    ApprovedCheckOut = dto.AutoApply ? newCheckOutUtc : null,

                    Reason = $"[HR Manual] {dto.HrNote.Trim()}",
                    HrNote = dto.HrNote.Trim(),
                    Status = dto.AutoApply ? CorrectionStatuses.Approved : CorrectionStatuses.Pending,
                    ReviewedByEmployeeId = dto.AutoApply ? hrEmployee.EmployeeId : null,
                    ReviewedAt = dto.AutoApply ? DateTime.UtcNow : null,
                    SubmittedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                };

                request.AttendanceCorrectionRequestId = Guid.NewGuid();
                db.AttendanceCorrectionRequests.Add(request);

                AppendAuditLog(db,request, CorrectionAuditActions.Submitted,
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

                    AppendAuditLog(db,request, CorrectionAuditActions.Approved,
                        hrEmployee.EmployeeId,
                        $"{hrEmployee.FirstName} {hrEmployee.LastName}",
                        "HR auto-approved and applied.",
                        request.OriginalCheckIn, request.OriginalCheckOut,
                        newCheckInUtc, newCheckOutUtc,
                        existing?.Status, attendance.Status);

                    AppendAuditLog(db,request, CorrectionAuditActions.Applied,
                        hrEmployee.EmployeeId,
                        $"{hrEmployee.FirstName} {hrEmployee.LastName}",
                        $"Attendance updated. New status: {attendance.Status}",
                        null, null, null, null, null, null);
                }

                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return request.AttendanceCorrectionRequestId;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // =========================================================================
        // QUERIES: EMPLOYEE
        // =========================================================================

        public async Task<List<CorrectionRequestListDto>> GetMyRequestsAsync(int pageSize = 30)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var (employee, _, tz) = await ResolveEmployeeContextAsync(db);

            var rows = await db.AttendanceCorrectionRequests
                .AsNoTracking()
                .Include(r => r.Employee).ThenInclude(e => e!.Department)
                .Include(r => r.ReviewedByEmployee)
                .Where(r => r.EmployeeId == employee.EmployeeId)
                .OrderByDescending(r => r.SubmittedAt)
                .Take(pageSize)
                .ToListAsync();

            return rows.Select(r => MapToListDto(r, tz)).ToList();
        }

        // =========================================================================
        // QUERIES: HR QUEUE
        // =========================================================================

        public async Task<PagedResult<CorrectionRequestListDto>> GetHrQueueAsync(
            CorrectionQueueFilterDto filter)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var (_, org, tz) = await ResolveEmployeeContextAsync(db);

            var query = db.AttendanceCorrectionRequests
                .AsNoTracking()
                .Include(r => r.Employee).ThenInclude(e => e!.Department)
                .Include(r => r.ReviewedByEmployee)
                .Where(r => r.OrganizationId == org.OrganizationId);

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
                .ToListAsync();

            return new PagedResult<CorrectionRequestListDto>
            {
                Items = items.Select(r => MapToListDto(r, tz)).ToList(),
                TotalCount = totalCount,
                Page = filter.Page,
                PageSize = filter.PageSize,
            };
        }

        public async Task<CorrectionRequestDetailDto?> GetDetailAsync(Guid requestId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var (_, org, tz) = await ResolveEmployeeContextAsync(db);

            var r = await db.AttendanceCorrectionRequests
                .AsNoTracking()
                .Include(x => x.Employee).ThenInclude(e => e!.Department)
                .Include(x => x.ReviewedByEmployee)
                .Include(x => x.AuditLogs)
                .FirstOrDefaultAsync(x =>
                    x.AttendanceCorrectionRequestId == requestId &&
                    x.OrganizationId == org.OrganizationId);

            if (r == null) return null;

            var listDto = MapToListDto(r, tz);
            return new CorrectionRequestDetailDto
            {
                AttendanceCorrectionRequestId = listDto.AttendanceCorrectionRequestId,
                EmployeeId = listDto.EmployeeId,
                EmployeeName = listDto.EmployeeName,
                Department = listDto.Department,
                EmployeeCode = listDto.EmployeeCode,
                WorkDate = listDto.WorkDate,
                CorrectionType = listDto.CorrectionType,
                Status = listDto.Status,
                RequestedCheckIn = listDto.RequestedCheckIn,
                RequestedCheckOut = listDto.RequestedCheckOut,
                OriginalCheckIn = listDto.OriginalCheckIn,
                OriginalCheckOut = listDto.OriginalCheckOut,
                OriginalStatus = listDto.OriginalStatus,
                OriginalTotalHours = listDto.OriginalTotalHours,
                Reason = listDto.Reason,
                HrNote = listDto.HrNote,
                RejectionReason = listDto.RejectionReason,
                SubmittedAt = listDto.SubmittedAt,
                ReviewedAt = listDto.ReviewedAt,
                ReviewedByName = listDto.ReviewedByName,
                IsApplied = listDto.IsApplied,
                IsHrInitiated = listDto.IsHrInitiated,
                HasAttachment = listDto.HasAttachment,
                AuditLogs = r.AuditLogs
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
            var (_, org, tz) = await ResolveEmployeeContextAsync(db);

            var now = DateTime.UtcNow;

            return new CorrectionQueueSummaryDto
            {
                TotalPending = await db.AttendanceCorrectionRequests
                    .CountAsync(r => r.OrganizationId == org.OrganizationId
                                     && r.Status == CorrectionStatuses.Pending),

                PendingOlderThan2Days = await db.AttendanceCorrectionRequests
                    .CountAsync(r => r.OrganizationId == org.OrganizationId
                                     && r.Status == CorrectionStatuses.Pending
                                     && r.SubmittedAt <= now.AddDays(-2)),

                ApprovedThisWeek = await db.AttendanceCorrectionRequests
                    .CountAsync(r => r.OrganizationId == org.OrganizationId
                                     && r.Status == CorrectionStatuses.Approved
                                     && r.ReviewedAt >= now.AddDays(-7)),

                RejectedThisWeek = await db.AttendanceCorrectionRequests
                    .CountAsync(r => r.OrganizationId == org.OrganizationId
                                     && r.Status == CorrectionStatuses.Rejected
                                     && r.ReviewedAt >= now.AddDays(-7)),
            };
        }

        // =========================================================================
        // CORE: APPLY CORRECTION TO ATTENDANCE (private)
        // =========================================================================

        /// <summary>
        /// Writes the approved times from a correction request to the Attendance table
        /// and calls AttendanceCalculationService.Recalculate().
        ///
        /// MUST be called inside an open EF transaction.
        /// FIX #4: accepts the DbContext explicitly so we always operate on the
        /// same context that owns the transaction — no cross-context save issues.
        /// Returns the mutated Attendance entity so the caller can read .Status.
        /// </summary>
        private async Task<Attendance> ApplyCorrectionToAttendanceAsync(
            AttendanceCorrectionRequest request,
            Organization org,
            TimeZoneInfo tz,
            ApplicationDbContext db)
        {
            Attendance? attendance = null;

            if (request.AttendanceId.HasValue)
            {
                // FIX #4: load by PK with tracking so EF will detect and save changes.
                // Include the shift for Recalculate().
                attendance = await db.Attendances
                    .Include(a => a.Employee)
                        .ThenInclude(e => e!.Shift)
                    .FirstOrDefaultAsync(a => a.AttendanceId == request.AttendanceId.Value);
            }

            if (attendance == null)
            {
                // Correction for a missing row (AbsentButWorked or HrManualCorrection with no prior row).
                var employee = await db.Employees
                    .Include(e => e.Shift)
                    .FirstOrDefaultAsync(e => e.EmployeeId == request.EmployeeId)
                    ?? throw new CorrectionValidationException("Employee not found.");

                var user = await db.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == employee.UserId)
                    ?? throw new CorrectionValidationException("Employee user account not found.");

                attendance = new Attendance
                {
                    AttendanceId = Guid.NewGuid(),
                    UserId = employee.UserId!,
                    EmployeeId = employee.EmployeeId,
                    OrganizationId = org.OrganizationId,
                    WorkDate = request.WorkDate,
                    IsPresent = true,
                    IsManual = true,
                    CreatedAt = DateTime.UtcNow,
                    Employee = employee,
                };
                db.Attendances.Add(attendance);

                // Link the new row back to the request so future lookups work.
                request.AttendanceId = attendance.AttendanceId;
            }

            var shift = attendance.Employee?.Shift;

            // ── Apply approved times based on correction type ──────────────────────
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
                    if (request.ApprovedCheckIn.HasValue)
                        attendance.CheckIn = request.ApprovedCheckIn;
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
                        $"Unknown correction type: {request.CorrectionType}");
            }

            // ── Common flags ──────────────────────────────────────────────────────
            attendance.IsPresent = true;
            attendance.IsManual = true;
            attendance.IsHrCorrected = true;
            attendance.UpdatedAt = DateTime.UtcNow;
            attendance.UpdatedBy = request.ReviewedByEmployeeId;
            attendance.LastModifiedByEmployeeId = request.ReviewedByEmployeeId;
            attendance.LastModifiedAt = DateTime.UtcNow;
            attendance.ModificationReason = $"Correction #{request.AttendanceCorrectionRequestId:N}";

            // Apply optional status override BEFORE recalculation.
            //if (!string.IsNullOrWhiteSpace(request.ApprovedStatus))
            //    attendance.Status = request.ApprovedStatus;

            //// ── Recalculate TotalHours, OvertimeHours, Status ──────────────────────
            //_calc.Recalculate(attendance, shift, tz);

            // Recalculate from corrected times first
            _calc.Recalculate(attendance, shift, tz);

            // Optional HR manual override wins AFTER recalculation
            if (!string.IsNullOrWhiteSpace(request.ApprovedStatus))
            {
                attendance.Status = request.ApprovedStatus;
            }

            return attendance;
        }

        // =========================================================================
        // PRIVATE: REJECT
        // =========================================================================

        private static void RejectInternal(
            ApplicationDbContext db, AttendanceCorrectionRequest request,
            Employee hrEmployee,
            string? rejectionReason,
            string? hrNote)
        {
            if (string.IsNullOrWhiteSpace(rejectionReason))
                throw new CorrectionValidationException("A rejection reason is required.");

            request.Status = CorrectionStatuses.Rejected;
            request.RejectionReason = rejectionReason.Trim();
            request.HrNote = hrNote?.Trim();
            request.ReviewedByEmployeeId = hrEmployee.EmployeeId;
            request.ReviewedAt = DateTime.UtcNow;
            request.UpdatedAt = DateTime.UtcNow;

            AppendAuditLog(db, request, CorrectionAuditActions.Rejected,
                hrEmployee.EmployeeId,
                $"{hrEmployee.FirstName} {hrEmployee.LastName}",
                $"Rejection reason: {rejectionReason}",
                null, null, null, null, null, null);
        }

        // =========================================================================
        // PRIVATE: VALIDATION
        // =========================================================================

        private static void ValidateCorrectionType(string correctionType)
        {
            if (!CorrectionTypes.All.Contains(correctionType))
                throw new CorrectionValidationException(
                    $"Invalid correction type: '{correctionType}'.");
        }

        private static void ValidateWorkDate(DateTime workDate, TimeZoneInfo tz)
        {
            var orgToday = TimeHelper.GetOrgDate(tz);

            if (workDate.Date > orgToday)
                throw new CorrectionValidationException(
                    "Corrections cannot be submitted for future dates.");

            if (workDate.Date < orgToday.AddDays(-MaxCorrectionLookbackDays))
                throw new CorrectionValidationException(
                    $"Corrections can only be submitted for the last {MaxCorrectionLookbackDays} days.");
        }

        private static void ValidateCorrectionAgainstAttendance(
            string correctionType,
            Attendance? existing)
        {
            switch (correctionType)
            {
                case CorrectionTypes.ForgotCheckIn:
                    if (existing?.CheckIn != null)
                        throw new CorrectionValidationException(
                            "A check-in time already exists for this date. Use 'Wrong Time' instead.");
                    break;

                case CorrectionTypes.ForgotCheckOut:
                    if (existing == null || existing.CheckIn == null)
                        throw new CorrectionValidationException(
                            "No check-in record found for this date.");
                    if (existing.CheckOut != null && !existing.IsAutoCheckedOut)
                        throw new CorrectionValidationException(
                            "A check-out time already exists. Use 'Wrong Time' instead.");
                    break;

                case CorrectionTypes.WrongTime:
                    if (existing == null)
                        throw new CorrectionValidationException(
                            "No attendance record found for this date. Use 'Absent but Worked' instead.");
                    break;

                case CorrectionTypes.AbsentButWorked:
                    if (existing?.CheckIn != null)
                        throw new CorrectionValidationException(
                            "An attendance record with check-in already exists for this date.");
                    break;

                case CorrectionTypes.OvertimeCorrection:
                    if (existing == null || existing.CheckIn == null)
                        throw new CorrectionValidationException(
                            "No check-in record found. Cannot request overtime correction.");
                    break;

                case CorrectionTypes.HrManualCorrection:
                    break; // HR can correct anything
            }
        }

        private static void ValidateTimeConsistency(
            string correctionType,
            DateTime? checkInUtc,
            DateTime? checkOutUtc)
        {
            if (CorrectionTypes.RequiresCheckIn.Contains(correctionType) && checkInUtc == null)
                throw new CorrectionValidationException(
                    $"Check-in time is required for correction type '{correctionType}'.");

            if (CorrectionTypes.RequiresCheckOut.Contains(correctionType) && checkOutUtc == null)
                throw new CorrectionValidationException(
                    $"Check-out time is required for correction type '{correctionType}'.");

            if (checkInUtc.HasValue && checkOutUtc.HasValue &&
                checkOutUtc.Value <= checkInUtc.Value)
                throw new CorrectionValidationException(
                    "Check-out time must be after check-in time.");

            if (checkInUtc.HasValue && checkOutUtc.HasValue)
            {
                var span = (checkOutUtc.Value - checkInUtc.Value).TotalHours;
                if (span > 24)
                    throw new CorrectionValidationException(
                        "The requested time span exceeds 24 hours. Please verify the times.");
            }
        }

        // =========================================================================
        // PRIVATE: TIME CONVERSION HELPERS
        // =========================================================================

        //private static (DateTime? checkInUtc, DateTime? checkOutUtc) ConvertRequestedTimesToUtc(
        //    SubmitCorrectionRequestDto dto,
        //    DateTime workDate,
        //    TimeZoneInfo tz)
        //{
        //    var checkInUtc = ConvertLocalTimeToUtc(dto.RequestedCheckIn, workDate, tz);
        //    var checkOutUtc = ConvertLocalTimeToUtc(dto.RequestedCheckOut, workDate, tz);
        //    return (checkInUtc, checkOutUtc);
        //}



        /// <summary>
        /// Converts an org-local DateTime (Kind=Unspecified) to UTC.
        /// The UI passes only a time-of-day; this method combines it with workDate.
        /// Overnight checkout (time-of-day < check-in time-of-day) is supported by
        /// the caller passing Kind=Unspecified with the correct date already set.
        /// </summary>
        private static (DateTime? checkInUtc, DateTime? checkOutUtc)
      ConvertRequestedTimesToUtc(
          SubmitCorrectionRequestDto dto,
          DateTime workDate,
          TimeZoneInfo tz)
        {
            DateTime? checkInLocal = null;
            DateTime? checkOutLocal = null;

            // Build LOCAL check-in datetime
            if (dto.RequestedCheckIn.HasValue)
            {
                checkInLocal = workDate.Date
                    .Add(dto.RequestedCheckIn.Value.TimeOfDay);
            }

            // Build LOCAL check-out datetime
            if (dto.RequestedCheckOut.HasValue)
            {
                checkOutLocal = workDate.Date
                    .Add(dto.RequestedCheckOut.Value.TimeOfDay);

                // FIX: Overnight shift support
                // Example:
                // IN  = 11:00 PM
                // OUT = 03:00 AM
                // => checkout belongs to NEXT DAY
                if (checkInLocal.HasValue &&
                    checkOutLocal.Value <= checkInLocal.Value)
                {
                    checkOutLocal = checkOutLocal.Value.AddDays(1);
                }
            }

            return (
                ConvertLocalDateTimeToUtc(checkInLocal, tz),
                ConvertLocalDateTimeToUtc(checkOutLocal, tz)
            );
        }

        private static DateTime? ConvertLocalDateTimeToUtc(
    DateTime? localDateTime,
    TimeZoneInfo tz)
        {
            if (!localDateTime.HasValue)
                return null;

            var unspecified = DateTime.SpecifyKind(
                localDateTime.Value,
                DateTimeKind.Unspecified);

            return DateTime.SpecifyKind(
                TimeZoneInfo.ConvertTimeToUtc(unspecified, tz),
                DateTimeKind.Utc);
        }

        /// <summary>
        /// Returns the HR override as UTC if provided;
        /// otherwise returns the already-stored RequestedValue (UTC).
        /// </summary>
        private static DateTime? ResolveApprovedTime(
    DateTime? hrOverrideLocal,
    DateTime? requestedUtc,
    DateTime workDate,
    TimeZoneInfo tz)
        {
            if (!hrOverrideLocal.HasValue)
                return requestedUtc;

            var local = workDate.Date
                .Add(hrOverrideLocal.Value.TimeOfDay);

            // Overnight support
            if (requestedUtc.HasValue)
            {
                var requestedLocal =
                    TimeZoneInfo.ConvertTimeFromUtc(requestedUtc.Value, tz);

                if (local.TimeOfDay < requestedLocal.TimeOfDay)
                {
                    local = local.AddDays(1);
                }
            }

            return ConvertLocalDateTimeToUtc(local, tz);
        }

        // =========================================================================
        // PRIVATE: AUDIT LOG HELPER
        // =========================================================================

        //private static void AppendAuditLog(
        //    AttendanceCorrectionRequest request,
        //    string action,
        //    Guid actorEmployeeId,
        //    string? actorName,
        //    string? notes,
        //    DateTime? previousCheckIn,
        //    DateTime? previousCheckOut,
        //    DateTime? newCheckIn,
        //    DateTime? newCheckOut,
        //    string? previousStatus,
        //    string? newStatus)
        //{
        //    request.AuditLogs.Add(new CorrectionAuditLog
        //    {
        //        AttendanceCorrectionRequestId = request.AttendanceCorrectionRequestId,
        //        OrganizationId = request.OrganizationId,
        //        Action = action,
        //        ActorEmployeeId = actorEmployeeId,
        //        ActorName = actorName,
        //        Notes = notes,
        //        OccurredAt = DateTime.UtcNow,
        //        PreviousCheckIn = previousCheckIn,
        //        PreviousCheckOut = previousCheckOut,
        //        NewCheckIn = newCheckIn,
        //        NewCheckOut = newCheckOut,
        //        PreviousStatus = previousStatus,
        //        NewStatus = newStatus,
        //    });
        //}

        private static void AppendAuditLog(
    ApplicationDbContext db,
    AttendanceCorrectionRequest request,
    string action,
    Guid actorEmployeeId,
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

        // =========================================================================
        // PRIVATE: CONTEXT RESOLVER
        // =========================================================================

        private async Task<(Employee employee, Organization org, TimeZoneInfo tz)>
            ResolveEmployeeContextAsync(ApplicationDbContext db)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new UnauthorizedAccessException("User not authenticated.");

            var employee = await db.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new CorrectionValidationException("Employee profile not found.");

            var org = await db.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrganizationId == employee.OrganizationId)
                ?? throw new CorrectionValidationException("Organisation not found.");

            var tz = TimeHelper.GetOrgTimeZone(org.TimeZoneId);
            return (employee, org, tz);
        }

        // =========================================================================
        // PRIVATE: DTO MAPPER
        // =========================================================================

        private static CorrectionRequestListDto MapToListDto(
            AttendanceCorrectionRequest r,
            TimeZoneInfo tz)
        {
            _ = tz;
            return new CorrectionRequestListDto
            {
                AttendanceCorrectionRequestId = r.AttendanceCorrectionRequestId,
                EmployeeId = r.EmployeeId,
                EmployeeName = r.Employee != null ? $"{r.Employee.FirstName} {r.Employee.LastName}" : string.Empty,
                Department = r.Employee?.Department?.Name,
                EmployeeCode = r.Employee?.EmployeeCode,
                WorkDate = r.WorkDate,
                CorrectionType = r.CorrectionType,
                Status = r.Status,
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
                    ? $"{r.ReviewedByEmployee.FirstName} {r.ReviewedByEmployee.LastName}"
                    : null,
                IsApplied = r.IsApplied,
                IsHrInitiated = r.IsHrInitiated,
                HasAttachment = !string.IsNullOrEmpty(r.AttachmentPath),
                // FIX: CanCancel is true only when Pending AND not yet applied.
                CanCancel = r.Status == CorrectionStatuses.Pending && !r.IsApplied,
            };
        }
    }

    // =========================================================================
    // CUSTOM EXCEPTION
    // =========================================================================

    public class CorrectionValidationException : Exception
    {
        public CorrectionValidationException(string message) : base(message) { }
    }
}