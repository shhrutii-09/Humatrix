using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class YearlyBalanceService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<YearlyBalanceService> _logger;

        public YearlyBalanceService(IServiceProvider serviceProvider,
            ILogger<YearlyBalanceService> logger)
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
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var leaveService = scope.ServiceProvider.GetRequiredService<LeaveService>();

                    int year = DateTime.UtcNow.Year;

                    var organizations = await db.Organizations
                        .Select(o => o.OrganizationId)
                        .ToListAsync(stoppingToken);

                    foreach (var orgId in organizations)
                    {
                        bool alreadyRun = await db.YearlyJobLogs.AnyAsync(j =>
                            j.OrganizationId == orgId &&
                            j.Year == year &&
                            j.JobName == "LeaveBalanceInit",
                            stoppingToken);

                        if (alreadyRun)
                            continue;

                        _logger.LogInformation($"Initializing leave balances for Org {orgId}");

                        await leaveService.InitialiseBalancesForYearAsync(orgId, year);

                        db.YearlyJobLogs.Add(new YearlyJobLog
                        {
                            OrganizationId = orgId,
                            Year = year,
                            JobName = "LeaveBalanceInit",
                            ExecutedAt = DateTime.UtcNow
                        });

                        await db.SaveChangesAsync(stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in yearly balance job");
                }

                // 🔁 Run once every 24 hours
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }
    }
}