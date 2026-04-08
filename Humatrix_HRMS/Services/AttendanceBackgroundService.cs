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
                    await MarkAbsentsAsync();
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

            // ✅ USE UTC (consistent with rest of app)
            var targetDate = DateTime.UtcNow.Date.AddDays(-1);

            // ✅ Skip weekends
            if (targetDate.DayOfWeek == DayOfWeek.Saturday ||
                targetDate.DayOfWeek == DayOfWeek.Sunday)
                return;

            // ✅ Get active employees (only needed fields)
            var activeEmployees = await context.Employees
                .Where(e => e.Status == "Active")
                .Select(e => new
                {
                    e.EmployeeId,
                    e.UserId,
                    e.OrganizationId
                })
                .ToListAsync();

            if (!activeEmployees.Any()) return;

            // ✅ Use HashSet for FAST lookup
            //var existingEmployeeIds = await context.Attendances
            //    .Where(a => a.Date == targetDate)
            //    .Select(a => a.EmployeeId)
            //    .ToHashSetAsync();

            var existingEmployeeIds = (await context.Attendances
    .Where(a => a.Date == targetDate)
    .Select(a => a.EmployeeId)
    .ToListAsync())
    .ToHashSet();

            var missingEmployees = activeEmployees
                .Where(e => !existingEmployeeIds.Contains(e.EmployeeId))
                .ToList();

            if (!missingEmployees.Any()) return;

            var newAttendances = missingEmployees.Select(emp => new Attendance
            {
                AttendanceId = Guid.NewGuid(),
                UserId = emp.UserId,
                EmployeeId = emp.EmployeeId,
                OrganizationId = emp.OrganizationId,
                Date = targetDate,
                IsPresent = false
            });

            await context.Attendances.AddRangeAsync(newAttendances);
            await context.SaveChangesAsync();

            _logger.LogInformation(
                "Auto-Absent: {Count} employees marked absent for {Date}",
                missingEmployees.Count,
                targetDate);
        }
    }
}