using Humatrix_HRMS.Data;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Humatrix_HRMS.Services
{
    public class AttendanceBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AttendanceBackgroundService> _logger;

        public AttendanceBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<AttendanceBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
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
                    _logger.LogError(ex, "Attendance job failed at {Time}", DateTime.UtcNow);
                }

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        // ─────────────────────────────────────
        // AUTO CHECKOUT
        // ─────────────────────────────────────
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
                    var orgNow = GetOrgNow(org.TimeZoneId);
                    var workDate = TimeHelper.GetOrgDate(org.TimeZoneId);
                    var tz = GetOrgTimezone(org.TimeZoneId);

                    var records = await context.Attendances
                        .Include(a => a.Employee)
                            .ThenInclude(e => e!.Shift)
                        .Where(a =>
                            a.OrganizationId == org.OrganizationId &&
                            a.CheckIn != null &&
                            a.CheckOut == null &&
                            a.WorkDate == workDate)
                        .ToListAsync();

                    if (!records.Any()) continue;

                    var employeeIds = records.Select(r => r.EmployeeId).ToList();

                    // ✅ Approved Leave
                    var onLeave = (await context.LeaveRequests
                        .Where(l =>
                            l.Status == "Approved" &&
                            l.FromDate.Date <= workDate &&
                            l.ToDate.Date >= workDate &&
                            employeeIds.Contains(l.EmployeeId))
                        .Select(l => l.EmployeeId)
                        .ToListAsync())
                        .ToHashSet();

                    // ✅ Approved WFH
                    var wfhEmployees = (await context.WorkFromHomeRequests
                        .Where(w =>
                            w.Status == "Approved" &&
                            w.Date.Date == workDate &&
                            employeeIds.Contains(w.EmployeeId))
                        .Select(w => w.EmployeeId)
                        .ToListAsync())
                        .ToHashSet();

                    int processed = 0;

                    foreach (var att in records)
                    {
                        try
                        {
                            // 🚫 Skip manual edits
                            if (att.IsManual)
                                continue;

                            // 🚫 Skip WFH (dynamic system)
                            if (att.EmployeeId.HasValue && wfhEmployees.Contains(att.EmployeeId.Value))
                                continue;

                            if (att.EmployeeId.HasValue && onLeave.Contains(att.EmployeeId.Value))
                                continue;

                            var shift = att.Employee?.Shift;
                            if (shift == null) continue;

                            var checkInLocal = TimeZoneInfo.ConvertTimeFromUtc(att.CheckIn!.Value, tz);
                            var shiftDate = checkInLocal.Date;

                            var shiftStart = shiftDate.Add(shift.StartTime);
                            var shiftEnd = shiftDate.Add(shift.EndTime);

                            // Overnight shift
                            if (shift.EndTime < shift.StartTime)
                                shiftEnd = shiftEnd.AddDays(1);

                            var autoCheckoutAt = shiftEnd.AddMinutes(30);

                            if (orgNow < autoCheckoutAt)
                                continue;

                            var shiftEndUtc = TimeZoneInfo.ConvertTimeToUtc(shiftEnd, tz);

                            att.CheckOut = shiftEndUtc;

                            var totalHours = (att.CheckOut.Value - att.CheckIn.Value).TotalHours;
                            att.TotalHours = totalHours;

                            // ✅ Overtime calculation (NOT auto-approved)
                            if (totalHours > shift.MinimumHoursForFullDay)
                            {
                                att.OvertimeHours = totalHours - shift.MinimumHoursForFullDay;
                                att.NeedsOvertimeApproval = true;
                            }
                            else
                            {
                                att.OvertimeHours = 0;
                                att.NeedsOvertimeApproval = false;
                            }

                            bool wasLate = att.Status == AttendanceStatuses.Late;

                            if (totalHours < shift.MinimumHoursForHalfDay)
                                att.Status = AttendanceStatuses.ShortHours;
                            else if (totalHours < shift.MinimumHoursForFullDay)
                                att.Status = AttendanceStatuses.HalfDay;
                            else
                                att.Status = wasLate
                                    ? AttendanceStatuses.Late
                                    : AttendanceStatuses.Present;

                            att.IsManual = false;
                            processed++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "AutoCheckout failed | Org:{OrgId} Emp:{EmpId} Att:{AttId}",
                                org.OrganizationId,
                                att.EmployeeId,
                                att.AttendanceId);
                        }
                    }

                    await context.SaveChangesAsync();

                    _logger.LogInformation(
                        "AutoCheckout: {Count} processed | Org {OrgId}",
                        processed,
                        org.OrganizationId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AutoCheckout failed for org {OrgId}", org.OrganizationId);
                }
            }
        }

        // ─────────────────────────────────────
        // MARK ABSENTS
        // ─────────────────────────────────────
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
                    var targetDate = orgNow.Date.AddDays(-1);

                    var workWeek = await context.WorkWeeks
                        .FirstOrDefaultAsync(w => w.OrganizationId == org.OrganizationId);

                    if (workWeek != null && !DateHelper.IsWorkingDay(targetDate, workWeek))
                        continue;

                    if (workWeek == null &&
                        (targetDate.DayOfWeek == DayOfWeek.Saturday ||
                         targetDate.DayOfWeek == DayOfWeek.Sunday))
                        continue;

                    var isHoliday = await context.Holidays.AnyAsync(h =>
                        h.OrganizationId == org.OrganizationId &&
                        h.Date.Date == targetDate &&
                        !h.IsOptional);

                    if (isHoliday) continue;

                    var employees = await context.Employees
                        .Where(e =>
                            e.OrganizationId == org.OrganizationId &&
                            e.Status == "Active")
                        .Select(e => new { e.EmployeeId, e.UserId })
                        .ToListAsync();

                    var employeeIds = employees.Select(e => e.EmployeeId).ToList();

                    // ✅ Approved Leave
                    var onLeave = (await context.LeaveRequests
                        .Where(l =>
                            l.Status == "Approved" &&
                            l.FromDate.Date <= targetDate &&
                            l.ToDate.Date >= targetDate &&
                            employeeIds.Contains(l.EmployeeId))
                        .Select(l => l.EmployeeId)
                        .ToListAsync())
                        .ToHashSet();

                    // ✅ Approved WFH
                    var wfhEmployees = (await context.WorkFromHomeRequests
                        .Where(w =>
                            w.Status == "Approved" &&
                            w.Date.Date == targetDate &&
                            employeeIds.Contains(w.EmployeeId))
                        .Select(w => w.EmployeeId)
                        .ToListAsync())
                        .ToHashSet();

                    // Already has attendance
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
                        UserId = e.UserId ?? throw new Exception("UserId is null"), // ✅ FIX
                        EmployeeId = e.EmployeeId,
                        OrganizationId = org.OrganizationId,
                        WorkDate = targetDate,
                        IsPresent = false,
                        Status = AttendanceStatuses.Absent
                    });

                    await context.Attendances.AddRangeAsync(records);
                    await context.SaveChangesAsync();

                    _logger.LogInformation(
                        "MarkAbsents: {Count} marked | Org {OrgId}",
                        records.Count(),
                        org.OrganizationId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MarkAbsents failed for org {OrgId}", org.OrganizationId);
                }
            }
        }

        // ─────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────
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