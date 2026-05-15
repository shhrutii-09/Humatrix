using Microsoft.AspNetCore.SignalR;

namespace Humatrix_HRMS.Hubs
{
    public class NotificationHub : Hub
    {
        public async Task JoinUserGroup(string userId)
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                userId);
        }
    }
}