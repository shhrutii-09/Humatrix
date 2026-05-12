using Humatrix_HRMS.Data;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Humatrix_HRMS.Services
{
    /// <summary>
    /// Background job running on a 1-hour cycle.
    ///
    /// AutoCheckout job:
    ///   • Processes per-org with their own timezone.
    ///   • Forces checkout at scheduled shift end once the overtime window
    ///     (shift end + MaxOT + 30 min grace) has passed.
    ///   • SystemCheckOut is ALWAYS set to the scheduled shift end (stable OT baseline).
    ///   • Auto-checked-out records get NO overtime flag — employee must
    ///     raise an explicit OT request.
    ///   • Idempotent: skips records that are already checked out or IsAutoCheckedOut.
    ///
    /// MarkAbsents job:
    ///   • Marks yesterday's eligible employees Absent.
    ///   • Skips: non-working days, holidays, employees on leave, WFH, or any
    ///     attendance record already existing.
    ///   • Idempotent: AnyAsync guard prevents duplicate absent rows.
    ///
    /// Race-condition safety:
    ///   • Each org is processed in its own scope.
    ///   • SaveChangesAsync is called once per org batch (not per record).
    ///   • The DB unique constraint on (EmployeeId, WorkDate) prevents duplicates.
    /// </summary>
    public class AttendanceBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<AttendanceBackgroundService> _logger;

        private const double MAX_OT = AttendanceConstants.MaxOvertimeHoursPerDay;
        private const double AUTO_CHECKOUT_GRACE = AttendanceConstants.AutoCheckoutGraceHours;

        public AttendanceBackgroundService(
            IServiceProvider services,
            ILogger<AttendanceBackgroundService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Brief startup delay so the app fully initialises first
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunAutoCheckoutAsync(stoppingToken);
                    await RunMarkAbsentsAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Attendance background jobs failed at {Time:u}", DateTime.UtcNow);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        // =========================================================================
        // AUTO CHECKOUT
        // =========================================================================
        private async Task RunAutoCheckoutAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var calc = scope.ServiceProvider.GetRequiredService<AttendanceCalculationService>();

            List<Organization> orgs;
            try
            {
                orgs = await context.Organizations
                    .Where(o => o.IsActive)
                    .ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoCheckout: failed to load organisations");
                return;
            }

            foreach (var org in orgs)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    await ProcessAutoCheckoutForOrgAsync(context, calc, org, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AutoCheckout failed for org {OrgId}", org.OrganizationId);
                }
            }
        }

        private async Task ProcessAutoCheckoutForOrgAsync(
            ApplicationDbContext context,
            AttendanceCalculationService calc,
            Organization org,
            CancellationToken ct)
        {
            var tz = TimeHelper.GetOrgTimeZone(org.TimeZoneId);
            var orgNow = TimeHelper.GetOrgNow(tz);

            // Load all open sessions (checked in, not yet checked out)
            var records = await context.Attendances
                .Include(a => a.Employee)
                    .ThenInclude(e => e!.Shift)
                .Where(a =>
                    a.OrganizationId == org.OrganizationId &&
                    a.CheckIn != null &&
                    a.CheckOut == null)
                .ToListAsync(ct);

            if (!records.Any()) return;

            bool anyChanged = false;

            foreach (var att in records)
            {
                try
                {
                    // Skip HR-manually-handled records
                    if (att.IsManual) continue;

                    var shift = att.Employee?.Shift;
                    if (shift == null) continue;

                    var checkInUtc = TimeHelper.EnsureUtc(att.CheckIn!.Value);
                    var checkInLocal = TimeHelper.ToOrgLocal(checkInUtc, tz);
                    var shiftDate = checkInLocal.Date;

                    var shiftEndLocal = TimeHelper.GetShiftEndLocal(shiftDate, shift);

                    // Total window before we force checkout:
                    //   shift end + max OT hours + grace
                    var autoCheckoutTime = shiftEndLocal
                        .AddHours(MAX_OT)
                        .AddHours(AUTO_CHECKOUT_GRACE);

                    // Still within the allowed window — skip
                    if (orgNow <= autoCheckoutTime) continue;

                    // ── Force checkout at scheduled shift end ─────────────────────
                    var shiftEndUtc = TimeHelper.ToUtc(shiftEndLocal, tz);
                    shiftEndUtc = DateTime.SpecifyKind(shiftEndUtc, DateTimeKind.Utc);

                    att.CheckOut = shiftEndUtc;
                    att.ActualCheckOut = shiftEndUtc;
                    att.SystemCheckOut = shiftEndUtc; // always = shift end
                    att.IsAutoCheckedOut = true;

                    // No overtime on auto-checkout — employee must raise an explicit request
                    att.OvertimeHours = 0;
                    att.ApprovedOvertimeHours = 0;
                    att.NeedsOvertimeApproval = false;

                    // Recalculate TotalHours and Status
                    calc.Recalculate(att, shift, tz);

                    // After auto-checkout recalculate, force OT flags off again
                    // (Recalculate might set NeedsOvertimeApproval if checkout > shift end,
                    //  which it won't be since we set checkout = shiftEnd, but guard anyway)
                    att.NeedsOvertimeApproval = false;
                    att.OvertimeHours = 0;

                    anyChanged = true;

                    _logger.LogInformation(
                        "AutoCheckout: Emp {EmpId} on {WorkDate:yyyy-MM-dd} checked out at shift end {ShiftEnd:u}",
                        att.EmployeeId, att.WorkDate, shiftEndUtc);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "AutoCheckout: failed processing Att {AttId} Emp {EmpId} Org {OrgId}",
                        att.AttendanceId, att.EmployeeId, org.OrganizationId);
                }
            }

            if (anyChanged)
            {
                await context.SaveChangesAsync(ct);
                _logger.LogInformation("AutoCheckout: completed for org {OrgId}", org.OrganizationId);
            }
        }

        // =========================================================================
        // MARK ABSENTS
        // =========================================================================
        private async Task RunMarkAbsentsAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            List<Organization> orgs;
            try
            {
                orgs = await context.Organizations
                    .Where(o => o.IsActive)
                    .ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MarkAbsents: failed to load organisations");
                return;
            }

            foreach (var org in orgs)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    await ProcessMarkAbsentsForOrgAsync(context, org, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MarkAbsents failed for org {OrgId}", org.OrganizationId);
                }
            }
        }

        private async Task ProcessMarkAbsentsForOrgAsync(
            ApplicationDbContext context,
            Organization org,
            CancellationToken ct)
        {
            var tz = TimeHelper.GetOrgTimeZone(org.TimeZoneId);
            var orgNow = TimeHelper.GetOrgNow(tz);
            var targetDate = orgNow.Date.AddDays(-1); // yesterday

            // ── Skip non-working days ─────────────────────────────────────────────
            var workWeek = await context.WorkWeeks
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.OrganizationId == org.OrganizationId, ct);

            bool isWorkingDay = workWeek != null
                ? DateHelper.IsWorkingDay(targetDate, workWeek)
                : (targetDate.DayOfWeek != DayOfWeek.Saturday && targetDate.DayOfWeek != DayOfWeek.Sunday);

            if (!isWorkingDay) return;

            // ── Skip mandatory holidays ───────────────────────────────────────────
            bool isHoliday = await context.Holidays.AnyAsync(h =>
                h.OrganizationId == org.OrganizationId &&
                h.Date.Date == targetDate &&
                !h.IsOptional, ct);

            if (isHoliday) return;

            // ── Active employees ──────────────────────────────────────────────────
            var employees = await context.Employees
                .Where(e => e.OrganizationId == org.OrganizationId && e.Status == "Active")
                .Select(e => new { e.EmployeeId, e.UserId })
                .ToListAsync(ct);

            if (!employees.Any()) return;

            var employeeIds = employees.Select(e => e.EmployeeId).ToList();

            // ── Exclusion sets ────────────────────────────────────────────────────
            var onLeave = (await context.LeaveRequests
                .Where(l =>
                    l.Status == "Approved" &&
                    l.FromDate.Date <= targetDate &&
                    l.ToDate.Date >= targetDate &&
                    employeeIds.Contains(l.EmployeeId))
                .Select(l => l.EmployeeId)
                .ToListAsync(ct))
                .ToHashSet();

            var onWfh = (await context.WorkFromHomeRequests
                .Where(w =>
                    w.Status == "Approved" &&
                    w.Date.Date == targetDate &&
                    employeeIds.Contains(w.EmployeeId))
                .Select(w => w.EmployeeId)
                .ToListAsync(ct))
                .ToHashSet();

            var haveRecord = (await context.Attendances
                .Where(a =>
                    a.OrganizationId == org.OrganizationId &&
                    a.WorkDate.Date == targetDate)
                .Select(a => a.EmployeeId)
                .ToListAsync(ct))
                .ToHashSet();

            var toMark = employees
                .Where(e =>
                    !haveRecord.Contains(e.EmployeeId) &&
                    !onLeave.Contains(e.EmployeeId) &&
                    !onWfh.Contains(e.EmployeeId))
                .ToList();

            if (!toMark.Any()) return;

            var records = toMark.Select(e => new Attendance
            {   
                AttendanceId = Guid.NewGuid(),
                UserId = e.UserId
                    ?? throw new InvalidOperationException(
                        $"Employee {e.EmployeeId} has no linked user account."),
                EmployeeId = e.EmployeeId,
                OrganizationId = org.OrganizationId,
                WorkDate = targetDate,
                IsPresent = false,
                Status = AttendanceStatuses.Absent,
                IsManual = false,
                CreatedAt = DateTime.UtcNow
            }).ToList();

            await context.Attendances.AddRangeAsync(records, ct);
            await context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "MarkAbsents: {Count} employees marked absent for {Date:yyyy-MM-dd} in org {OrgId}",
                records.Count, targetDate, org.OrganizationId);
        }
    }
}