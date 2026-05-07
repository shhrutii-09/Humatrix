using Humatrix_HRMS.Data;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    /// <summary>
    /// Runs on a 1-hour cycle.
    ///
    /// AutoCheckout:
    ///   If an employee never checked out, we force checkout at scheduled shift end
    ///   once the overtime window (shift end + 4 h + 30 min grace) has passed.
    ///   SystemCheckOut is ALWAYS set to the scheduled shift end so OvertimeService
    ///   has a stable reference even for auto-checked-out records.
    ///
    /// MarkAbsents:
    ///   For yesterday's date, any active employee with no attendance record,
    ///   no approved leave, and no approved WFH is marked Absent.
    /// </summary>
    public class AttendanceBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AttendanceBackgroundService> _logger;

        private const double MaxOvertimeHoursPerDay = 4.0;   // match OvertimeService
        private const double AutoCheckoutGraceHours = 0.5;   // 30 min after OT window

        public AttendanceBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<AttendanceBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Brief startup delay so the app fully boots first
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await AutoCheckoutAsync();
                    await MarkAbsentsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Attendance background job failed at {Time}", DateTime.UtcNow);
                }

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        // =========================================================================
        // AUTO CHECKOUT
        // =========================================================================
        private async Task AutoCheckoutAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var orgs = await context.Organizations
                .Where(o => o.IsActive)
                .ToListAsync();

            foreach (var org in orgs)
            {
                try
                {
                    var tz = GetOrgTimezone(org.TimeZoneId);
                    var orgNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);

                    // Fetch all open attendances (checked in, not yet checked out)
                    var records = await context.Attendances
                        .Include(a => a.Employee)
                            .ThenInclude(e => e!.Shift)
                        .Where(a =>
                            a.OrganizationId == org.OrganizationId &&
                            a.CheckIn != null &&
                            a.CheckOut == null)
                        .ToListAsync();

                    if (!records.Any()) continue;

                    foreach (var att in records)
                    {
                        try
                        {
                            // Skip records that HR already handled manually
                            if (att.IsManual) continue;

                            var shift = att.Employee?.Shift;
                            if (shift == null) continue;

                            var checkInLocal = TimeZoneInfo.ConvertTimeFromUtc(att.CheckIn!.Value, tz);
                            var shiftDate = checkInLocal.Date;

                            var shiftEnd = shiftDate.Add(shift.EndTime);

                            // Handle overnight shift
                            if (shift.EndTime < shift.StartTime)
                                shiftEnd = shiftEnd.AddDays(1);

                            // ─── OVERTIME WINDOW ─────────────────────────────────
                            // Employee has until (shift end + 4 h) to work overtime.
                            // We wait an extra 30 min grace before forcing auto-checkout.
                            var overtimeWindowEnd = shiftEnd.AddHours(MaxOvertimeHoursPerDay);
                            var autoCheckoutTime = overtimeWindowEnd.AddHours(AutoCheckoutGraceHours);

                            // Still within allowed working / overtime time → skip
                            if (orgNow <= autoCheckoutTime) continue;

                            // ─── SET SystemCheckOut ───────────────────────────────
                            // Always = scheduled shift end (UTC) — this is the baseline
                            // the OvertimeService uses.  Set it BEFORE changing CheckOut.
                            var shiftEndUtc = TimeZoneInfo.ConvertTimeToUtc(shiftEnd, tz);
                            att.SystemCheckOut = shiftEndUtc;

                            // ─── SET CheckOut ─────────────────────────────────────
                            // Auto-checkout is recorded at scheduled shift end,
                            // NOT at the time this job runs (which would be hours later).
                            att.CheckOut = shiftEndUtc;
                            att.ActualCheckOut = shiftEndUtc;
                            att.IsAutoCheckedOut = true;

                            // ─── TOTAL HOURS ─────────────────────────────────────
                            var totalHours = (att.CheckOut.Value - att.CheckIn.Value).TotalHours;
                            att.TotalHours = Math.Round(totalHours, 2);

                            // ─── NO OVERTIME ON AUTO-CHECKOUT ────────────────────
                            // The employee didn't initiate a checkout — we don't know
                            // if they actually stayed.  OT requires a conscious request.
                            att.OvertimeHours = 0;
                            att.ApprovedOvertimeHours = 0;
                            att.NeedsOvertimeApproval = false;

                            // ─── ATTENDANCE STATUS ────────────────────────────────
                            bool wasLate = att.Status == AttendanceStatuses.Late;

                            if (totalHours < shift.MinimumHoursForHalfDay)
                                att.Status = AttendanceStatuses.ShortHours;
                            else if (totalHours < shift.MinimumHoursForFullDay)
                                att.Status = AttendanceStatuses.HalfDay;
                            else
                                att.Status = wasLate
                                    ? AttendanceStatuses.Late
                                    : AttendanceStatuses.Present;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "AutoCheckout failed | Org:{OrgId} Emp:{EmpId} Att:{AttId}",
                                org.OrganizationId, att.EmployeeId, att.AttendanceId);
                        }
                    }

                    await context.SaveChangesAsync();

                    _logger.LogInformation("AutoCheckout completed | Org {OrgId}", org.OrganizationId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AutoCheckout failed for org {OrgId}", org.OrganizationId);
                }
            }
        }

        // =========================================================================
        // MARK ABSENTS
        // =========================================================================
        private async Task MarkAbsentsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var orgs = await context.Organizations
                .Where(o => o.IsActive)
                .ToListAsync();

            foreach (var org in orgs)
            {
                try
                {
                    var orgNow = GetOrgNow(org.TimeZoneId);
                    var targetDate = orgNow.Date.AddDays(-1); // yesterday

                    // ── Skip non-working days ────────────────────────────────────
                    var workWeek = await context.WorkWeeks
                        .FirstOrDefaultAsync(w => w.OrganizationId == org.OrganizationId);

                    if (workWeek != null && !DateHelper.IsWorkingDay(targetDate, workWeek))
                        continue;

                    // Default weekend check when workWeek not configured
                    if (workWeek == null &&
                        (targetDate.DayOfWeek == DayOfWeek.Saturday ||
                         targetDate.DayOfWeek == DayOfWeek.Sunday))
                        continue;

                    // ── Skip holidays ────────────────────────────────────────────
                    var isHoliday = await context.Holidays.AnyAsync(h =>
                        h.OrganizationId == org.OrganizationId &&
                        h.Date.Date == targetDate &&
                        !h.IsOptional);

                    if (isHoliday) continue;

                    // ── Active employees ─────────────────────────────────────────
                    var employees = await context.Employees
                        .Where(e => e.OrganizationId == org.OrganizationId && e.Status == "Active")
                        .Select(e => new { e.EmployeeId, e.UserId })
                        .ToListAsync();

                    var employeeIds = employees.Select(e => e.EmployeeId).ToList();

                    // ── Approved leave ───────────────────────────────────────────
                    var onLeave = (await context.LeaveRequests
                        .Where(l =>
                            l.Status == "Approved" &&
                            l.FromDate.Date <= targetDate &&
                            l.ToDate.Date >= targetDate &&
                            employeeIds.Contains(l.EmployeeId))
                        .Select(l => l.EmployeeId)
                        .ToListAsync())
                        .ToHashSet();

                    // ── Approved WFH ─────────────────────────────────────────────
                    var wfhEmployees = (await context.WorkFromHomeRequests
                        .Where(w =>
                            w.Status == "Approved" &&
                            w.Date.Date == targetDate &&
                            employeeIds.Contains(w.EmployeeId))
                        .Select(w => w.EmployeeId)
                        .ToListAsync())
                        .ToHashSet();

                    // ── Already has an attendance record ─────────────────────────
                    var haveRecord = (await context.Attendances
                        .Where(a =>
                            a.OrganizationId == org.OrganizationId &&
                            a.WorkDate.Date == targetDate)
                        .Select(a => a.EmployeeId)
                        .ToListAsync())
                        .ToHashSet();

                    var toMark = employees
                        .Where(e =>
                            !haveRecord.Contains(e.EmployeeId) &&
                            !onLeave.Contains(e.EmployeeId) &&
                            !wfhEmployees.Contains(e.EmployeeId))
                        .ToList();

                    if (!toMark.Any()) continue;

                    var records = toMark.Select(e => new Attendance
                    {
                        AttendanceId = Guid.NewGuid(),
                        UserId = e.UserId ?? throw new InvalidOperationException($"Employee {e.EmployeeId} has no UserId"),
                        EmployeeId = e.EmployeeId,
                        OrganizationId = org.OrganizationId,
                        WorkDate = targetDate,
                        IsPresent = false,
                        Status = AttendanceStatuses.Absent
                    }).ToList();

                    await context.Attendances.AddRangeAsync(records);
                    await context.SaveChangesAsync();

                    _logger.LogInformation(
                        "MarkAbsents: {Count} marked absent | Org {OrgId}",
                        records.Count, org.OrganizationId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MarkAbsents failed for org {OrgId}", org.OrganizationId);
                }
            }
        }

        // =========================================================================
        // HELPERS
        // =========================================================================
        private DateTime GetOrgNow(string timeZoneId)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            }
            catch
            {
                return DateTime.UtcNow;
            }
        }

        private TimeZoneInfo GetOrgTimezone(string timeZoneId)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
            catch { return TimeZoneInfo.Utc; }
        }
    }
}