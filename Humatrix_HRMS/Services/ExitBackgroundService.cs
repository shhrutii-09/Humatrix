// Services/ExitBackgroundService.cs
using Microsoft.Extensions.Hosting;

namespace Humatrix_HRMS.Services
{
    public class ExitBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<ExitBackgroundService> _logger;

        public ExitBackgroundService(IServiceProvider services, ILogger<ExitBackgroundService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Run at midnight every day
                    var now = DateTime.UtcNow;
                    var nextRun = now.Date.AddDays(1);
                    var delay = nextRun - now;

                    await Task.Delay(delay, stoppingToken);

                    using var scope = _services.CreateScope();
                    var exitService = scope.ServiceProvider.GetRequiredService<EmployeeExitService>();

                    var completed = await exitService.AutoCompleteExpiredExitsAsync();

                    if (completed > 0)
                    {
                        _logger.LogInformation($"Auto-completed {completed} exit(s)");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in exit background service");
                }
            }
        }
    }
}