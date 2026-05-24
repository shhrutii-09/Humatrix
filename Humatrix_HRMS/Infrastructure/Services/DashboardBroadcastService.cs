// Infrastructure/Services/DashboardBroadcastService.cs
using Humatrix_HRMS.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Humatrix_HRMS.Infrastructure.Services
{
    /// <summary>
    /// Broadcasts dashboard refresh signals over SignalR.
    /// Dashboards subscribe to their 
    /// role groups.
    /// Business services call this after approvals/rejections.
    /// </summary>
    public class DashboardBroadcastService
    {
        private readonly IHubContext<NotificationHub> _hub;

        public DashboardBroadcastService(IHubContext<NotificationHub> hub)
        {
            _hub = hub;
        }

        /// <summary>
        /// Sends a dashboard refresh event to all HR users in the given org+dept.
        /// Frontend components subscribe to "DashboardRefresh" and reload their widgets.
        /// </summary>
        public async Task BroadcastHrDashboardAsync(Guid organizationId, Guid departmentId)
        {
            var group = $"hr-dashboard-{organizationId}-{departmentId}";
            await _hub.Clients.Group(group)
                .SendAsync("DashboardRefresh", new
                {
                    scope = "HR",
                    orgId = organizationId,
                    deptId = departmentId,
                    timestamp = DateTime.UtcNow
                });
        }

        /// <summary>
        /// Sends a dashboard refresh event to all OrgAdmin users in the org.
        /// </summary>
        public async Task BroadcastOrgDashboardAsync(Guid organizationId)
        {
            var group = $"org-dashboard-{organizationId}";
            await _hub.Clients.Group(group)
                .SendAsync("DashboardRefresh", new
                {
                    scope = "OrgAdmin",
                    orgId = organizationId,
                    timestamp = DateTime.UtcNow
                });
        }
    }
}