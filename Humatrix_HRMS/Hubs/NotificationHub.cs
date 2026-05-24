// Hubs/NotificationHub.cs
using Microsoft.AspNetCore.SignalR;

namespace Humatrix_HRMS.Hubs
{
    /// <summary>
    /// Typed SignalR hub. Clients join their own userId group on connect.
    /// The hub itself is stateless — all business logic is in NotificationEngine.
    /// </summary>
    public interface INotificationClient
    {
        /// <summary>
        /// Pushes a full NotificationPushDto payload.
        /// Frontend uses this to instantly update badge + toast + list.
        /// </summary>
        Task ReceiveNotification(object payload);

        /// <summary>
        /// Signals the frontend to refresh its dashboard widgets.
        /// Sent after approvals/rejections.
        /// </summary>
        Task DashboardRefresh(object payload);
    }

    public class NotificationHub : Hub<INotificationClient>
    {
        public async Task JoinUserGroup(string userId)
        {
            Console.WriteLine($"JOIN USER GROUP: {userId}");

            if (string.IsNullOrWhiteSpace(userId))
                return;

            //await Groups.AddToGroupAsync(Context.ConnectionId, userId);
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                $"user-{userId}");
        }

        public async Task LeaveUserGroup(string userId)
        {
            //await Groups.RemoveFromGroupAsync(Context.ConnectionId, userId);
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                $"user-{userId}");
        }

        // Add to NotificationHub.cs
        //public async Task JoinHrDashboardGroup(string organizationId, string departmentId)
        //{
        //    var group = $"hr-dashboard-{organizationId}-{departmentId}";
        //    await Groups.AddToGroupAsync(Context.ConnectionId, group);
        //}

        //public async Task JoinOrgDashboardGroup(string organizationId)
        //{
        //    var group = $"org-dashboard-{organizationId}";
        //    await Groups.AddToGroupAsync(Context.ConnectionId, group);
        //}

        public async Task JoinHrDashboardGroup(
    string organizationId,
    string departmentId)
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                $"hr-dashboard-{organizationId}-{departmentId}");
        }

        public async Task JoinOrgDashboardGroup(string organizationId)
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                $"org-dashboard-{organizationId}");
        }


        //public async Task JoinUserGroup(string userId)
        //{

        //    if (string.IsNullOrWhiteSpace(userId))
        //        return;

        //    await Groups.AddToGroupAsync(
        //        Context.ConnectionId,
        //        $"user-{userId}");
        //}
    }


}