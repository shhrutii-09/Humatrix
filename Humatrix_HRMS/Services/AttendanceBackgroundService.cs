using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

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
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await AutoCheckoutAsync();   // 🔥 FIRST
                    await MarkAbsentsAsync();   // 🔥 THEN
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Auto-Absent job");
                }

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        private async Task MarkAbsentsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var targetDate = DateTime.UtcNow.Date.AddDays(-1);

            // 1. Skip weekends
            if (targetDate.DayOfWeek == DayOfWeek.Saturday ||
                targetDate.DayOfWeek == DayOfWeek.Sunday)
                return;

            // 2. Get holidays
            var holidays = await context.Holidays
                .Where(h => h.Date == targetDate)
                .Select(h => h.OrganizationId)
                .ToListAsync();

            // 3. Get employees
            var employees = await context.Employees
                .Where(e => e.Status == "Active")
                .Select(e => new
                {
                    e.EmployeeId,
                    e.UserId,
                    e.OrganizationId
                })
                .ToListAsync();

            if (!employees.Any()) return;

            // 4. Get employees on leave
            var employeesOnLeave = await context.LeaveRequests
                .Where(l =>
                    l.Status == "Approved" &&
                    targetDate >= l.FromDate &&
                    targetDate <= l.ToDate)
                .Select(l => l.EmployeeId)
                .ToListAsync();

            var leaveSet = employeesOnLeave.ToHashSet();

            // 5. Existing attendance
            var existingAttendance = (await context.Attendances
                .Where(a => a.Date == targetDate)
                .Select(a => a.EmployeeId)
                .ToListAsync())
                .ToHashSet();

            // 6. Filter employees who should be marked absent
            var toMarkAbsent = employees
                .Where(e =>
                    !existingAttendance.Contains(e.EmployeeId) &&
                    !leaveSet.Contains(e.EmployeeId) &&
                    !holidays.Contains(e.OrganizationId))
                .ToList();

            if (!toMarkAbsent.Any()) return;

            var records = toMarkAbsent.Select(e => new Attendance
            {
                AttendanceId = Guid.NewGuid(),
                UserId = e.UserId,
                EmployeeId = e.EmployeeId,
                OrganizationId = e.OrganizationId,
                Date = targetDate,
                IsPresent = false,
                Status = "Absent" // 🔥 ADD THIS
            });

            await context.Attendances.AddRangeAsync(records);
            await context.SaveChangesAsync();
        }

        private async Task AutoCheckoutAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var today = DateTime.UtcNow.Date;

            var records = await context.Attendances
                .Include(a => a.Employee)
                .ThenInclude(e => e.Shift)
                .Where(a =>
                    a.Date == today &&
                    a.CheckIn != null &&
                    a.CheckOut == null)
                .ToListAsync();

            foreach (var att in records)
            {
                var shift = att.Employee?.Shift;
                if (shift == null) continue;

                var checkoutTime = today.Add(shift.EndTime);

                if (shift.EndTime < shift.StartTime)
                    checkoutTime = checkoutTime.AddDays(1);

                checkoutTime = checkoutTime.AddMinutes(30);

                var now = DateTime.UtcNow;

                if (now < checkoutTime)
                    continue;

                att.CheckOut = checkoutTime;

                var totalHours = (att.CheckOut.Value - att.CheckIn.Value).TotalHours;
                att.TotalHours = totalHours;

                if (totalHours < shift.MinimumHoursForHalfDay)
                    att.Status = "Short Hours";
                else if (totalHours < shift.MinimumHoursForFullDay)
                    att.Status = "Half Day";
                else
                    att.Status = att.Status.Contains("Late") ? "Late" : "Present";

                att.IsManual = false; // ✅ system checkout
            }

            await context.SaveChangesAsync();
        }
    }
}