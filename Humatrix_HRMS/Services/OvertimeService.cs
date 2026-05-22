using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    /// <summary>
    /// Overtime flow:
    ///
    /// 1. Employee checks out after shift end:
    ///      attendance.NeedsOvertimeApproval = true
    ///      attendance.OvertimeHours         = raw hours (capped)
    ///      attendance.SystemCheckOut        = scheduled shift end (UTC)
    ///
    /// 2. Employee raises an OT request (RaiseRequestAsync).
    ///    Provides ActualCheckOut (org-local, Unspecified kind) and Reason.
    ///    Service converts to UTC, validates against SystemCheckOut and the cap.
    ///
    /// 3. HR approves / rejects (ReviewAsync).
    ///    Approval:
    ///      • attendance.CheckOut             = ActualCheckOut (UTC)
    ///      • attendance.ActualCheckOut       = ActualCheckOut (UTC)
    ///      • attendance.ApprovedOvertimeHours set
    ///      • attendance.NeedsOvertimeApproval = false
    ///      • TotalHours, Status recalculated
    ///    Rejection:
    ///      • attendance.CheckOut   = SystemCheckOut (rollback to shift end)
    ///      • attendance.OvertimeHours = 0
    ///      • TotalHours, Status recalculated
    /// </summary>
    public class OvertimeService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;
        private readonly AttendanceCalculationService _calc;

        private const double MAX_OT = AttendanceConstants.MaxOvertimeHoursPerDay;
        private const double MIN_OT = AttendanceConstants.MinOvertimeHours;

        public OvertimeService(
            ApplicationDbContext context,
            CurrentUserService currentUser,
            AttendanceCalculationService calc)
        {
            _context = context;
            _currentUser = currentUser;
            _calc = calc;
        }

        // =========================================================================
        // EMPLOYEE — Raise OT request
        // =========================================================================
        public async Task RaiseRequestAsync(CreateOvertimeRequestDto dto)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee profile not found.");


            // ── USER ROLES ─────────────────────────────────────────────
            var userRoles = await _context.UserRoles
                .Where(x => x.UserId == user.Id)
                .Join(
                    _context.Roles,
                    ur => ur.RoleId,
                    r => r.Id,
                    (ur, r) => r.Name)
                .ToListAsync();

            var isHr = userRoles.Contains("HR");
            var isOrgAdmin = userRoles.Contains("OrgAdmin");

            var requesterRole =
                isOrgAdmin ? "OrgAdmin" :
                isHr ? "HR" :
                "Employee";

            // ── Load attendance record ────────────────────────────────────────────
            var attendance = await _context.Attendances
                .Include(a => a.Employee)
                    .ThenInclude(e => e!.Shift)
                .FirstOrDefaultAsync(a =>
                    a.AttendanceId == dto.AttendanceId &&
                    a.EmployeeId == employee.EmployeeId)
                ?? throw new Exception("Attendance record not found.");

            if (attendance.CheckIn == null)
                throw new Exception("Attendance has no check-in time.");

            if (!attendance.NeedsOvertimeApproval)
                throw new Exception("This attendance record is not eligible for overtime.");

            // ── Must be for a completed past day ──────────────────────────────────
            var org = await _context.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrganizationId == employee.OrganizationId)
                ?? throw new Exception("Organisation not found.");

            var today = TimeHelper.GetOrgDate(org.TimeZoneId);

            if (attendance.WorkDate >= today)
                throw new Exception("Overtime can only be requested for completed past days.");

            // ── SystemCheckOut must be present ────────────────────────────────────
            var systemCheckoutUtc = attendance.SystemCheckOut
                ?? throw new Exception("System checkout time is missing — contact HR to review the record.");

            systemCheckoutUtc = TimeHelper.EnsureUtc(systemCheckoutUtc);

            // ── Convert ActualCheckOut from org-local (Unspecified) to UTC ────────
            // The Razor page always sends org-local time with Kind=Unspecified.
            var tz = TimeHelper.GetOrgTimeZone(org.TimeZoneId);
            DateTime actualCheckOutUtc;

            if (dto.ActualCheckOut.Kind == DateTimeKind.Utc)
            {
                actualCheckOutUtc = dto.ActualCheckOut;
            }
            else
            {
                actualCheckOutUtc = TimeHelper.ToUtc(dto.ActualCheckOut, tz);
            }

            // ── Validate ActualCheckOut must be AFTER scheduled shift end ─────────
            if (actualCheckOutUtc <= systemCheckoutUtc)
                throw new Exception(
                    "Actual checkout time must be after your scheduled shift end.");

            // ── Enforce org-level cap ─────────────────────────────────────────────
            var maxAllowedUtc = systemCheckoutUtc.AddHours(MAX_OT);
            if (actualCheckOutUtc > maxAllowedUtc)
                throw new Exception(
                    $"Overtime cannot exceed {MAX_OT:0} hours per day. " +
                    $"Maximum allowed checkout: {TimeHelper.FormatOrgTime(maxAllowedUtc, tz)}.");

            // ── Calculate OT hours (time beyond shift end) ────────────────────────
            var overtimeHours = (actualCheckOutUtc - systemCheckoutUtc).TotalHours;
            overtimeHours = Math.Min(overtimeHours, MAX_OT);

            if (overtimeHours < MIN_OT)
                throw new Exception(
                    $"Overtime must be at least {(int)(MIN_OT * 60)} minutes.");

            if (string.IsNullOrWhiteSpace(dto.Reason))
                throw new Exception("A reason is required for the overtime request.");

            // ── Duplicate guard ───────────────────────────────────────────────────
            var alreadyPending = await _context.OvertimeRequests.AnyAsync(r =>
                r.AttendanceId == dto.AttendanceId &&
                r.Status == "Pending");

            if (alreadyPending)
                throw new Exception("An overtime request is already pending for this day.");

            // Previously rejected request is allowed; employee can re-raise.

            // ── Create request ────────────────────────────────────────────────────
            var request = new OvertimeRequest
            {
                EmployeeId = employee.EmployeeId,
                AttendanceId = attendance.AttendanceId,
                Date = attendance.WorkDate,
                RequestedHours = Math.Round(overtimeHours, 2),
                ActualCheckOut = actualCheckOutUtc,
                Reason = dto.Reason.Trim(),
                Status = "Pending",
                AppliedAt = DateTime.UtcNow,

                RequestedByRole = requesterRole,

                ApprovalLevel =
         requesterRole == "HR"
             ? "OrgAdmin"
             : "HR"
            };

            // Keep flag set so the HR dashboard shows it
            attendance.NeedsOvertimeApproval = true;

            _context.OvertimeRequests.Add(request);
            await _context.SaveChangesAsync();
        }

        // =========================================================================
        // EMPLOYEE — View own requests
        // =========================================================================
        public async Task<List<OvertimeRequest>> GetMyRequestsAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee profile not found.");

            return await _context.OvertimeRequests
                .Include(r => r.Attendance)
                .Where(r => r.EmployeeId == employee.EmployeeId)
                .OrderByDescending(r => r.AppliedAt)
                .ToListAsync();
        }

        // =========================================================================
        // HR — View pending requests
        // allDepartments = true for OrgAdmin; false for HR (own dept only)
        // =========================================================================
        public async Task<List<OvertimeRequest>> GetAllAsync(bool allDepartments = false)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = user.OrganizationId ?? Guid.Empty;

            var query = _context.OvertimeRequests
                .Include(r => r.Employee)
                    .ThenInclude(e => e!.Department)
                .Include(r => r.Attendance)
                .Where(r => r.Employee.OrganizationId == orgId);

            // HR → only own department
            if (!allDepartments)
            {
                var currentEmployee = await _context.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.UserId == user.Id);

                // SAFETY FOR ORGADMIN WITHOUT EMPLOYEE PROFILE
                if (currentEmployee == null)
                    return new List<OvertimeRequest>();

                query = query.Where(r =>
                    r.Employee.DepartmentId == currentEmployee.DepartmentId

                    // HR should only see employee requests
                    && (
                        r.RequestedByRole == "Employee" ||
                        r.RequestedByRole == null
                    )

                    // HR should not see own requests
                    && r.Employee.UserId != user.Id
                );
            }

            return await query
                .OrderByDescending(r => r.AppliedAt)
                .ToListAsync();
        }

        // =========================================================================
        // HR — Approve / Reject
        // =========================================================================
        public async Task ReviewAsync(ReviewOvertimeDto dto)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = user.OrganizationId ?? Guid.Empty;

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var request = await _context.OvertimeRequests
                    .Include(r => r.Employee)
                    .Include(r => r.Attendance)
                        .ThenInclude(a => a!.Employee)
                            .ThenInclude(e => e!.Shift)
                    .FirstOrDefaultAsync(r => r.OvertimeRequestId == dto.OvertimeRequestId)
                    ?? throw new Exception("Overtime request not found.");

                if (request.Status != "Pending")
                    throw new Exception("This request has already been reviewed.");

                if (request.Employee.OrganizationId != orgId)
                    throw new Exception("Unauthorized — request belongs to a different organisation.");

                // ── CURRENT USER ROLES ─────────────────────────────────
                var userRoles = await _context.UserRoles
                    .Where(x => x.UserId == user.Id)
                    .Join(
                        _context.Roles,
                        ur => ur.RoleId,
                        r => r.Id,
                        (ur, r) => r.Name)
                    .ToListAsync();

                var isOrgAdmin = userRoles.Contains("OrgAdmin");
                var isHR = userRoles.Contains("HR");

                // ======================================================
                // HR RESTRICTIONS
                // ======================================================
                if (isHR && !isOrgAdmin)
                {
                    var currentEmployee = await _context.Employees
                        .FirstOrDefaultAsync(e => e.UserId == user.Id);

                    if (currentEmployee == null)
                        throw new Exception("HR employee profile not found.");

                    // HR CANNOT REVIEW HR REQUESTS
                    if (request.RequestedByRole != "Employee")
                        throw new Exception("HR cannot process HR overtime requests.");

                    // SAME DEPARTMENT ONLY
                    if (request.Employee.DepartmentId != currentEmployee.DepartmentId)
                        throw new Exception("Unauthorized department access.");
                }

                var attendance = request.Attendance
                    ?? throw new Exception("Associated attendance record is missing — cannot process.");

                // ── Load org timezone ─────────────────────────────────────────────
                var org = await _context.Organizations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.OrganizationId == orgId)
                    ?? throw new Exception("Organisation not found.");

                var tz = TimeHelper.GetOrgTimeZone(org.TimeZoneId);

                request.ReviewedBy = Guid.Parse(user.Id);
                request.ReviewedAt = DateTime.UtcNow;

                // ── REJECTION PATH ────────────────────────────────────────────────
                if (!dto.Approve)
                {
                    request.Status = "Rejected";
                    request.RejectionReason = dto.RejectionReason;

                    // Roll checkout back to scheduled shift end
                    if (attendance.SystemCheckOut.HasValue)
                    {
                        var sysUtc = TimeHelper.EnsureUtc(attendance.SystemCheckOut.Value);
                        attendance.CheckOut = sysUtc;
                        attendance.ActualCheckOut = sysUtc;
                    }

                    attendance.OvertimeHours = 0;
                    attendance.ApprovedOvertimeHours = 0;
                    attendance.NeedsOvertimeApproval = false;
                    attendance.IsAutoCheckedOut = false;

                    // Recalculate TotalHours and Status with rolled-back checkout
                    _calc.ReapplyStatusAfterOtReview(attendance, attendance.Employee?.Shift);

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return;
                }

                // ── APPROVAL PATH ─────────────────────────────────────────────────
                request.Status = "Approved";

                if (!attendance.CheckIn.HasValue)
                    throw new Exception("Attendance check-in is missing.");

                if (!request.ActualCheckOut.HasValue)
                    throw new Exception("Actual checkout time is missing.");

                var actualCheckOutUtc = TimeHelper.EnsureUtc(request.ActualCheckOut.Value);

                attendance.CheckOut = actualCheckOutUtc;
                attendance.ActualCheckOut = actualCheckOutUtc;
                attendance.IsAutoCheckedOut = false;
                attendance.IsManual = false; // employee-driven, not HR-forced

                // OT = time worked beyond scheduled shift end (SystemCheckOut baseline)
                var sysCheckoutUtc = TimeHelper.EnsureUtc(
                    attendance.SystemCheckOut
                    ?? throw new Exception("SystemCheckOut missing on attendance record."));

                var rawOtHours = Math.Max(
                    0,
                    (actualCheckOutUtc - sysCheckoutUtc).TotalHours
                );

                rawOtHours = Math.Min(rawOtHours, MAX_OT);

                attendance.OvertimeHours = Math.Round(rawOtHours, 2);
                attendance.ApprovedOvertimeHours = Math.Round(rawOtHours, 2);
                attendance.NeedsOvertimeApproval = false;

                // Recalculate TotalHours and Status with the new checkout time
                _calc.ReapplyStatusAfterOtReview(attendance, attendance.Employee?.Shift);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}