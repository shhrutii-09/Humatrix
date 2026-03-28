using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class AttendanceBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AttendanceBackgroundService> _logger;

        public AttendanceBackgroundService(IServiceProvider serviceProvider, ILogger<AttendanceBackgroundService> logger)
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
                    _logger.LogError(ex, "Error occurred while executing Auto-Absent task.");
                }

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        private async Task MarkAbsentsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var _context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // We mark people absent for 'Yesterday' because today is still in progress.
            var targetDate = DateTime.Now.Date.AddDays(-1);

            // 1. Skip processing if it's a weekend (Optional: Adjust based on Org settings later)
            if (targetDate.DayOfWeek == DayOfWeek.Saturday || targetDate.DayOfWeek == DayOfWeek.Sunday)
                return;

            // 2. Get all employees who are active
            var activeEmployees = await _context.Employees
                .Where(e => e.Status == "Active")
                .ToListAsync();

            // 3. Get IDs of employees who ALREADY have a record for that date
            var employeesWithRecords = await _context.Attendances
                .Where(a => a.Date == targetDate)
                .Select(a => a.EmployeeId)
                .ToListAsync();

            // 4. Find those who are missing a record
            var missingEmployees = activeEmployees
                .Where(e => !employeesWithRecords.Contains(e.EmployeeId))
                .ToList();

            if (!missingEmployees.Any()) return;

            foreach (var emp in missingEmployees)
            {
                _context.Attendances.Add(new Attendance
                {
                    AttendanceId = Guid.NewGuid(),
                    UserId = emp.UserId,
                    EmployeeId = emp.EmployeeId,
                    OrganizationId = emp.OrganizationId,
                    Date = targetDate,
                    CheckIn = null,
                    CheckOut = null,
                    IsPresent = false // Explicitly marked as Absent
                });
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation($"Auto-Absent: Marked {missingEmployees.Count} employees as absent for {targetDate:yyyy-MM-dd}");
        }
    }
}