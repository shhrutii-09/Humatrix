using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Humatrix_HRMS.Hubs;

namespace Humatrix_HRMS.Services
{
    public class NotificationService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        private readonly IHubContext<NotificationHub> _hub;

        public NotificationService(
             IDbContextFactory<ApplicationDbContext> dbFactory,
             IHubContext<NotificationHub> hub)
        {
            _dbFactory = dbFactory;
            _hub = hub;
        }

        // =========================
        // CREATE NOTIFICATION
        // =========================
        public async Task CreateNotificationAsync(
     string userId,
     string title,
     string message,
     string? redirectUrl = null)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var notification = new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                RedirectUrl = redirectUrl,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            db.Notifications.Add(notification);

            await db.SaveChangesAsync();

            var unreadCount = await db.Notifications
                .CountAsync(x =>
                    x.UserId == userId &&
                    !x.IsRead);

            await _hub.Clients
                .Group($"user-{userId}")
                .SendAsync("ReceiveNotification", new NotificationPushDto
                {
                    NotificationId = notification.NotificationId,
                    Title = notification.Title,
                    Message = notification.Message,
                    RedirectUrl = notification.RedirectUrl,
                    CreatedAt = notification.CreatedAt,
                    NewUnreadCount = unreadCount
                });
        }

        // =========================
        // GET MY NOTIFICATIONS
        // =========================
        public async Task<List<NotificationDto>> GetMyNotificationsAsync(string userId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            return await db.Notifications
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new NotificationDto
                {
                    NotificationId = x.NotificationId,
                    Title = x.Title,
                    Message = x.Message,
                    RedirectUrl = x.RedirectUrl,
                    IsRead = x.IsRead,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync();
        }

        // =========================
        // GET UNREAD COUNT
        // =========================
        public async Task<int> GetUnreadCountAsync(string userId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            return await db.Notifications
                .CountAsync(x =>
                    x.UserId == userId &&
                    !x.IsRead);
        }

        // =========================
        // MARK AS READ
        // =========================
        public async Task MarkAsReadAsync(int notificationId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var notification = await db.Notifications
                .FirstOrDefaultAsync(x =>
                    x.NotificationId == notificationId);

            if (notification == null)
                return;

            notification.IsRead = true;

            await db.SaveChangesAsync();
        }

        // =========================
        // DELETE SINGLE NOTIFICATION
        // =========================
        public async Task DeleteNotificationAsync(int notificationId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var notification = await db.Notifications
                .FirstOrDefaultAsync(x =>
                    x.NotificationId == notificationId);

            if (notification == null)
                return;

            db.Notifications.Remove(notification);

            await db.SaveChangesAsync();
        }
        // =========================
        // MARK ALL AS READ
        // =========================
        public async Task MarkAllAsReadAsync(string userId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var notifications = await db.Notifications
                .Where(x =>
                    x.UserId == userId &&
                    !x.IsRead)
                .ToListAsync();

            if (!notifications.Any())
                return;

            foreach (var notification in notifications)
            {
                notification.IsRead = true;
            }

            await db.SaveChangesAsync();
        }
        // =========================
        // CLEAR ALL NOTIFICATIONS
        // =========================
        public async Task ClearAllNotificationsAsync(string userId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var notifications = await db.Notifications
                .Where(x => x.UserId == userId)
                .ToListAsync();

            if (!notifications.Any())
                return;

            db.Notifications.RemoveRange(notifications);

            await db.SaveChangesAsync();
        }

        public async Task BroadcastOrgDashboardRefreshAsync(Guid organizationId)
        {
            await _hub.Clients
                .Group($"org-dashboard-{organizationId}")
                .SendAsync("DashboardRefresh");
        }
        public async Task BroadcastHrDashboardRefreshAsync(
    Guid organizationId,
    Guid departmentId)
        {
            await _hub.Clients
                .Group($"hr-dashboard-{organizationId}-{departmentId}")
                .SendAsync("DashboardRefresh");
        }

        public async Task CreateOrgAdminNotificationsAsync(
      Guid organizationId,
      string title,
      string message,
      string? redirectUrl = null)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            var orgAdmins = await (
                from u in db.Users
                join ur in db.UserRoles on u.Id equals ur.UserId
                join r in db.Roles on ur.RoleId equals r.Id
                where u.OrganizationId == organizationId
                      && r.Name == "OrgAdmin"
                select u
            ).ToListAsync();

            foreach (var admin in orgAdmins)
            {
                var notification = new Notification
                {
                    UserId = admin.Id,
                    OrganizationId = organizationId,
                    Title = title,
                    Message = message,
                    RedirectUrl = redirectUrl,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                };

                db.Notifications.Add(notification);

                await db.SaveChangesAsync();

                var unreadCount = await db.Notifications
                    .CountAsync(x =>
                        x.UserId == admin.Id &&
                        !x.IsRead);
                Console.WriteLine($"SENDING TO user-{admin.Id}");

                await _hub.Clients
                    .Group($"user-{admin.Id}")
                    .SendAsync("ReceiveNotification", new NotificationPushDto
                    {
                        NotificationId = notification.NotificationId,
                        Title = notification.Title,
                        Message = notification.Message,
                        RedirectUrl = notification.RedirectUrl,
                        CreatedAt = notification.CreatedAt,
                        NewUnreadCount = unreadCount
                    });
            }
        }
    }
}