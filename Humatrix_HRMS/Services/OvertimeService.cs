using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    /// <summary>
    /// Overtime flow in a real HRMS:
    ///
    /// 1. Employee checks out AFTER shift end  →  attendance.NeedsOvertimeApproval = true,
    ///    attendance.OvertimeHours = raw hours, attendance.SystemCheckOut = scheduled shift end.
    ///
    /// 2. Employee raises an OT request for that attendance record.
    ///    They provide the actual time they left (ActualCheckOut) and a reason.
    ///    The service validates the time against SystemCheckOut and the org cap.
    ///
    /// 3. HR approves / rejects.
    ///    On approval:
    ///      • attendance.CheckOut        = ActualCheckOut  (final record)
    ///      • attendance.ActualCheckOut  = ActualCheckOut
    ///      • attendance.ApprovedOvertimeHours set
    ///      • attendance.NeedsOvertimeApproval = false
    ///      • attendance.Status re-evaluated (could still be Present or Late)
    ///
    ///    On rejection:
    ///      • attendance.CheckOut        stays as SystemCheckOut (shift end)
    ///      • attendance.OvertimeHours   = 0
    ///      • attendance.NeedsOvertimeApproval = false
    /// </summary>
    public class OvertimeService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;

        // Maximum OT hours that can be approved per day — must match AttendanceService constant
        private const double MaxOvertimeHoursPerDay = 4.0;

        public OvertimeService(ApplicationDbContext context, CurrentUserService currentUser)
        {
            _context = context;
            _currentUser = currentUser;
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
                ?? throw new Exception("Employee profile not found");

            // ── Load the attendance record they are claiming OT for ──────────────
            var attendance = await _context.Attendances
                .Include(a => a.Employee)
                    .ThenInclude(e => e.Shift)
                .FirstOrDefaultAsync(a =>
                    a.AttendanceId == dto.AttendanceId &&
                    a.EmployeeId == employee.EmployeeId)
                ?? throw new Exception("Attendance record not found");

            if (attendance.CheckIn == null)
                throw new Exception("Attendance has no check-in time");

            if (!attendance.NeedsOvertimeApproval)
                throw new Exception("This attendance record is not eligible for overtime");

            var shift = attendance.Employee?.Shift
                ?? throw new Exception("No shift assigned — cannot calculate overtime");

            // ── Must be for a past day (not today) ───────────────────────────────
            var org = await _context.Organizations.AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrganizationId == employee.OrganizationId);
            var today = TimeHelper.GetOrgDate(org?.TimeZoneId);

            if (attendance.WorkDate >= today)
                throw new Exception("Overtime can only be requested for completed past days");

            // ── Convert ActualCheckOut from org-local to UTC ──────────────────────
            // The Razor page sends a local (org-timezone) DateTime with Unspecified kind.
            // We must convert it here so comparisons against UTC-stored SystemCheckOut work.
            if (dto.ActualCheckOut.Kind != DateTimeKind.Utc)
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(org?.TimeZoneId ?? "UTC");
                dto.ActualCheckOut = TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(dto.ActualCheckOut, DateTimeKind.Unspecified), tz);
            }

            // ── SystemCheckOut is the reference baseline ─────────────────────────
            // SystemCheckOut = scheduled shift end UTC (set during employee checkout).
            // ActualCheckOut = when employee really left.
            var systemCheckout = attendance.SystemCheckOut
                ?? throw new Exception("System checkout time is missing — contact HR");

            if (dto.ActualCheckOut <= systemCheckout)
                throw new Exception("Actual checkout must be after the scheduled shift end");

            // ── Enforce org-level cap ────────────────────────────────────────────
            var maxAllowedCheckout = systemCheckout.AddHours(MaxOvertimeHoursPerDay);
            if (dto.ActualCheckOut > maxAllowedCheckout)
                throw new Exception($"Overtime cannot exceed {MaxOvertimeHoursPerDay} hours per day");

            // ── Calculate OT hours ───────────────────────────────────────────────
            // OT = time beyond shift end (not beyond check-in)
            var overtimeHours = (dto.ActualCheckOut - systemCheckout).TotalHours;
            overtimeHours = Math.Min(overtimeHours, MaxOvertimeHoursPerDay);

            if (overtimeHours < 0.25) // less than 15 min — not worth raising
                throw new Exception("Overtime must be at least 15 minutes");

            if (string.IsNullOrWhiteSpace(dto.Reason))
                throw new Exception("A reason is required for the overtime request");

            // ── Duplicate guard ──────────────────────────────────────────────────
            var alreadyExists = await _context.OvertimeRequests.AnyAsync(r =>
                r.AttendanceId == dto.AttendanceId &&
                r.Status == "Pending");

            if (alreadyExists)
                throw new Exception("An overtime request is already pending for this day");

            // ── Create request ───────────────────────────────────────────────────
            var request = new OvertimeRequest
            {
                EmployeeId = employee.EmployeeId,
                AttendanceId = attendance.AttendanceId,
                Date = attendance.WorkDate,
                RequestedHours = Math.Round(overtimeHours, 2),
                ActualCheckOut = dto.ActualCheckOut,
                Reason = dto.Reason,
                Status = "Pending",
                AppliedAt = DateTime.UtcNow
            };

            // Keep flag set so HR dashboard can see it
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
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee profile not found");

            return await _context.OvertimeRequests
                .Include(r => r.Attendance)
                .Where(r => r.EmployeeId == employee.EmployeeId)
                .OrderByDescending(r => r.AppliedAt)
                .ToListAsync();
        }

        // =========================================================================
        // HR — View pending requests
        // =========================================================================
        /// <param name="allDepartments">
        ///   When true an OrgAdmin sees all departments.
        ///   When false (HR role) the caller is responsible for passing
        ///   the correct departmentId filter if needed.
        /// </param>
        public async Task<List<OvertimeRequest>> GetPendingAsync(bool allDepartments = false)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = user.OrganizationId ?? Guid.Empty;

            var query = _context.OvertimeRequests
                .Include(r => r.Employee)
                    .ThenInclude(e => e.Department)
                .Include(r => r.Attendance)
                .Where(r =>
                    r.Employee.OrganizationId == orgId &&
                    r.Status == "Pending");

            // HR restricted to own department unless allDepartments flag is set
            if (!allDepartments)
            {
                var currentEmployee = await _context.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.UserId == user.Id);

                if (currentEmployee?.DepartmentId != null)
                    query = query.Where(r => r.Employee.DepartmentId == currentEmployee.DepartmentId);
            }

            return await query.OrderBy(r => r.Date).ToListAsync();
        }

        // =========================================================================
        // HR — Approve / Reject
        // =========================================================================
        public async Task ReviewAsync(ReviewOvertimeDto dto)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = user.OrganizationId ?? Guid.Empty;

            // ── Load request with all related data ───────────────────────────────
            var request = await _context.OvertimeRequests
                .Include(r => r.Employee)
                .Include(r => r.Attendance)
                    .ThenInclude(a => a!.Employee)
                        .ThenInclude(e => e!.Shift)
                .FirstOrDefaultAsync(r => r.OvertimeRequestId == dto.OvertimeRequestId)
                ?? throw new Exception("Overtime request not found");

            if (request.Status != "Pending")
                throw new Exception("This request has already been reviewed");

            if (request.Employee.OrganizationId != orgId)
                throw new Exception("Unauthorized — request belongs to a different organization");

            request.ReviewedBy = Guid.Parse(user.Id);
            request.ReviewedAt = DateTime.UtcNow;

            var attendance = request.Attendance
                ?? throw new Exception("Associated attendance record is missing");

            // ── REJECTION PATH ───────────────────────────────────────────────────
            if (!dto.Approve)
            {
                request.Status = "Rejected";

                // Roll attendance back to scheduled shift end
                if (attendance.SystemCheckOut.HasValue)
                {
                    attendance.CheckOut = attendance.SystemCheckOut;
                    attendance.ActualCheckOut = attendance.SystemCheckOut;
                }

                // Recalculate hours using shift end as checkout
                if (attendance.CheckIn.HasValue && attendance.CheckOut.HasValue)
                {
                    attendance.TotalHours = Math.Round(
                        (attendance.CheckOut.Value - attendance.CheckIn.Value).TotalHours, 2);
                }

                attendance.OvertimeHours = 0;
                attendance.ApprovedOvertimeHours = 0;
                attendance.NeedsOvertimeApproval = false;
                attendance.IsAutoCheckedOut = false;

                // Re-run status with corrected hours
                ReapplyAttendanceStatus(attendance);

                await _context.SaveChangesAsync();
                return;
            }

            // ── APPROVAL PATH ────────────────────────────────────────────────────
            request.Status = "Approved";

            // Update checkout to what was actually worked
            attendance.ActualCheckOut = request.ActualCheckOut;
            attendance.CheckOut = request.ActualCheckOut;
            attendance.IsAutoCheckedOut = false;
            attendance.IsManual = false; // employee-driven, not HR-forced

            if (!attendance.CheckIn.HasValue)
                throw new Exception("Attendance check-in is missing");

            var totalHours = (attendance.CheckOut!.Value - attendance.CheckIn.Value).TotalHours;
            attendance.TotalHours = Math.Round(totalHours, 2);

            var shift = attendance.Employee?.Shift;
            if (shift != null)
            {
                // OT = total hours beyond full-day minimum (i.e. beyond shift end threshold)
                var rawOt = Math.Max(0, totalHours - shift.MinimumHoursForFullDay);
                rawOt = Math.Min(rawOt, MaxOvertimeHoursPerDay);
                attendance.OvertimeHours = Math.Round(rawOt, 2);
                attendance.ApprovedOvertimeHours = Math.Round(rawOt, 2);
            }
            else
            {
                attendance.OvertimeHours = Math.Round(request.RequestedHours, 2);
                attendance.ApprovedOvertimeHours = Math.Round(request.RequestedHours, 2);
            }

            attendance.NeedsOvertimeApproval = false;

            // Re-evaluate status (remains Present / Late — OT doesn't change day status)
            ReapplyAttendanceStatus(attendance);

            await _context.SaveChangesAsync();
        }

        // =========================================================================
        // PRIVATE HELPERS
        // =========================================================================

        /// <summary>
        /// Recalculate attendance Status after checkout time changes.
        /// Preserves Late flag set at check-in.
        /// </summary>
        private static void ReapplyAttendanceStatus(Attendance att)
        {
            if (!att.CheckIn.HasValue || !att.CheckOut.HasValue)
                return;

            var shift = att.Employee?.Shift;
            var totalHours = att.TotalHours ?? (att.CheckOut.Value - att.CheckIn.Value).TotalHours;

            if (shift == null)
            {
                att.Status = AttendanceStatuses.Present;
                return;
            }

            if (totalHours < shift.MinimumHoursForHalfDay)
                att.Status = AttendanceStatuses.ShortHours;
            else if (totalHours < shift.MinimumHoursForFullDay)
                att.Status = AttendanceStatuses.HalfDay;
            else
                // Keep Late if it was set at check-in; otherwise mark Present
                att.Status = att.Status == AttendanceStatuses.Late
                    ? AttendanceStatuses.Late
                    : AttendanceStatuses.Present;
        }
    }
}