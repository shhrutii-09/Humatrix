// Infrastructure/Services/NotificationEngine.cs
using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Hubs;
using Humatrix_HRMS.Infrastructure.Constants;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Infrastructure.Services
{
    /// <summary>
    /// Central notification engine. All notification creation flows through here.
    /// Business services call the domain-specific helpers (SendLeaveAppliedAsync, etc.)
    /// and never interact with Notification entities directly.
    /// </summary>
    public class NotificationEngine
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        private readonly IHubContext<NotificationHub> _hub;
        private readonly NotificationRecipientResolver _resolver;

        public NotificationEngine(
            IDbContextFactory<ApplicationDbContext> dbFactory,
            IHubContext<NotificationHub> hub,
            NotificationRecipientResolver resolver)
        {
            _dbFactory = dbFactory;
            _hub = hub;
            _resolver = resolver;
        }

        // ══════════════════════════════════════════════════════════════════════
        // DOMAIN-SPECIFIC HELPERS
        // ══════════════════════════════════════════════════════════════════════

        public Task SendLeaveAppliedAsync(
            string employeeFullName,
            string leaveTypeName,
            Guid leaveRequestId,
            Guid organizationId,
            Guid departmentId,
            string applicantRole,
            string actorUserId)
            => SendToApproversAsync(
                organizationId, departmentId, applicantRole, actorUserId,
                NotificationTypes.LeaveApplied,
                ReferenceTypes.LeaveRequest, leaveRequestId,
                "New Leave Request",
                $"{employeeFullName} applied for {leaveTypeName}",
                "/hr/leaves");

        public Task SendLeaveApprovedAsync(
            string employeeUserId,
            string leaveTypeName,
            string dateRange,
            Guid leaveRequestId,
            Guid organizationId,
            string actorUserId)
            => SendToUserAsync(
                employeeUserId, organizationId, actorUserId,
                NotificationTypes.LeaveApproved,
                ReferenceTypes.LeaveRequest, leaveRequestId,
                "Leave Approved ✓",
                $"Your {leaveTypeName} ({dateRange}) has been approved.",
                "/employee/leaves");

        public Task SendLeaveRejectedAsync(
            string employeeUserId,
            string leaveTypeName,
            string dateRange,
            Guid leaveRequestId,
            Guid organizationId,
            string actorUserId)
            => SendToUserAsync(
                employeeUserId, organizationId, actorUserId,
                NotificationTypes.LeaveRejected,
                ReferenceTypes.LeaveRequest, leaveRequestId,
                "Leave Rejected",
                $"Your {leaveTypeName} ({dateRange}) was not approved.",
                "/employee/leaves");

        public Task SendWfhAppliedAsync(
            string employeeFullName,
            DateTime date,
            Guid requestId,
            Guid organizationId,
            Guid departmentId,
            string applicantRole,
            string actorUserId)
            => SendToApproversAsync(
                organizationId, departmentId, applicantRole, actorUserId,
                NotificationTypes.WfhApplied,
                ReferenceTypes.WorkFromHomeRequest, requestId,
                "New WFH Request",
                $"{employeeFullName} requested WFH on {date:dd MMM yyyy}",
                "/hr/wfh");

        public Task SendWfhApprovedAsync(
            string employeeUserId,
            DateTime date,
            Guid requestId,
            Guid organizationId,
            string actorUserId)
            => SendToUserAsync(
                employeeUserId, organizationId, actorUserId,
                NotificationTypes.WfhApproved,
                ReferenceTypes.WorkFromHomeRequest, requestId,
                "WFH Approved ✓",
                $"Your WFH request for {date:dd MMM yyyy} has been approved.",
                "/employee/wfh");

        public Task SendWfhRejectedAsync(
            string employeeUserId,
            DateTime date,
            Guid requestId,
            Guid organizationId,
            string actorUserId)
            => SendToUserAsync(
                employeeUserId, organizationId, actorUserId,
                NotificationTypes.WfhRejected,
                ReferenceTypes.WorkFromHomeRequest, requestId,
                "WFH Request Rejected",
                $"Your WFH request for {date:dd MMM yyyy} was not approved.",
                "/employee/wfh");

        public Task SendOvertimeAppliedAsync(
            string employeeFullName,
            double hours,
            Guid requestId,
            Guid organizationId,
            Guid departmentId,
            string applicantRole,
            string actorUserId)
            => SendToApproversAsync(
                organizationId, departmentId, applicantRole, actorUserId,
                NotificationTypes.OtApplied,
                ReferenceTypes.OvertimeRequest, requestId,
                "New Overtime Request",
                $"{employeeFullName} requested {hours}h overtime",
                "/hr/overtime");

        public Task SendOvertimeApprovedAsync(
            string employeeUserId,
            double hours,
            Guid requestId,
            Guid organizationId,
            string actorUserId)
            => SendToUserAsync(
                employeeUserId, organizationId, actorUserId,
                NotificationTypes.OtApproved,
                ReferenceTypes.OvertimeRequest, requestId,
                "Overtime Approved ✓",
                $"Your {hours}h overtime request has been approved.",
                "/employee/overtime");

        public Task SendOvertimeRejectedAsync(
            string employeeUserId,
            double hours,
            Guid requestId,
            Guid organizationId,
            string actorUserId)
            => SendToUserAsync(
                employeeUserId, organizationId, actorUserId,
                NotificationTypes.OtRejected,
                ReferenceTypes.OvertimeRequest, requestId,
                "Overtime Request Rejected",
                $"Your {hours}h overtime request was not approved.",
                "/employee/overtime");

        public Task SendAttendanceCorrectionAppliedAsync(
            string employeeFullName,
            DateTime workDate,
            Guid requestId,
            Guid organizationId,
            Guid departmentId,
            string actorUserId)
            => SendToApproversAsync(
                organizationId, departmentId, "Employee", actorUserId,
                NotificationTypes.AttendanceCorrectionApplied,
                ReferenceTypes.AttendanceCorrectionRequest, requestId,
                "Attendance Correction Request",
                $"{employeeFullName} submitted a correction for {workDate:dd MMM yyyy}",
                "/hr/attendance-corrections");

        public Task SendAttendanceCorrectionApprovedAsync(
            string employeeUserId,
            DateTime workDate,
            Guid requestId,
            Guid organizationId,
            string actorUserId)
            => SendToUserAsync(
                employeeUserId, organizationId, actorUserId,
                NotificationTypes.AttendanceCorrectionApproved,
                ReferenceTypes.AttendanceCorrectionRequest, requestId,
                "Attendance Correction Approved ✓",
                $"Your correction for {workDate:dd MMM yyyy} has been approved.",
                "/employee/attendance");

        public Task SendAttendanceCorrectionRejectedAsync(
            string employeeUserId,
            DateTime workDate,
            Guid requestId,
            Guid organizationId,
            string actorUserId)
            => SendToUserAsync(
                employeeUserId, organizationId, actorUserId,
                NotificationTypes.AttendanceCorrectionRejected,
                ReferenceTypes.AttendanceCorrectionRequest, requestId,
                "Attendance Correction Rejected",
                $"Your correction for {workDate:dd MMM yyyy} was not approved.",
                "/employee/attendance");

        // ══════════════════════════════════════════════════════════════════════
        // GENERIC SEND METHODS
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Sends a notification to a single user. Does not throw on push failure.
        /// </summary>
        public async Task SendToUserAsync(
            string recipientUserId,
            Guid organizationId,
            string? actorUserId,
            string notificationType,
            string? referenceType,
            Guid? referenceId,
            string title,
            string message,
            string? redirectUrl = null,
            string priority = "Normal")
        {
            if (string.IsNullOrWhiteSpace(recipientUserId))
                return;

            using var db = await _dbFactory.CreateDbContextAsync();

            var notification = BuildNotification(
                recipientUserId, organizationId, actorUserId,
                notificationType, referenceType, referenceId,
                title, message, redirectUrl, priority);

            db.Notifications.Add(notification);
            await db.SaveChangesAsync();

            await PushAsync(recipientUserId, notification, db);
        }

        /// <summary>
        /// Sends notifications to all resolved approvers (HR + OrgAdmin).
        /// Uses batch insert for efficiency.
        /// </summary>
        public async Task SendToApproversAsync(
            Guid organizationId,
            Guid departmentId,
            string applicantRole,
            string actorUserId,
            string notificationType,
            string? referenceType,
            Guid? referenceId,
            string title,
            string message,
            string? redirectUrl = null,
            string priority = "Normal")
        {
            var recipientIds = await _resolver.GetApproverUserIdsAsync(
                organizationId, departmentId, applicantRole);

            if (!recipientIds.Any())
                return;

            using var db = await _dbFactory.CreateDbContextAsync();

            var notifications = recipientIds
                .Select(uid => BuildNotification(
                    uid, organizationId, actorUserId,
                    notificationType, referenceType, referenceId,
                    title, message, redirectUrl, priority))
                .ToList();

            db.Notifications.AddRange(notifications);
            await db.SaveChangesAsync();

            // Push in parallel — independent, fire-and-forget acceptable
            var pushTasks = notifications.Select(n => PushAsync(n.UserId, n, db));
            await Task.WhenAll(pushTasks);
        }

        // ══════════════════════════════════════════════════════════════════════
        // QUERY METHODS
        // ══════════════════════════════════════════════════════════════════════

        public async Task<List<NotificationDto>> GetMyNotificationsAsync(
            string userId, int page = 1, int pageSize = 20)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Notifications
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => MapToDto(x))
                .ToListAsync();
        }

        public async Task<int> GetUnreadCountAsync(string userId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Notifications
                .CountAsync(x => x.UserId == userId && !x.IsRead);
        }

        public async Task MarkAsReadAsync(int notificationId, string userId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var n = await db.Notifications
                .FirstOrDefaultAsync(x => x.NotificationId == notificationId && x.UserId == userId);
            if (n == null) return;
            n.IsRead = true;
            n.ReadAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        public async Task MarkAllReadAsync(string userId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            await db.Notifications
                .Where(x => x.UserId == userId && !x.IsRead)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.IsRead, true)
                    .SetProperty(x => x.ReadAt, DateTime.UtcNow));
        }

        public async Task DeleteAsync(int notificationId, string userId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            await db.Notifications
                .Where(x => x.NotificationId == notificationId && x.UserId == userId)
                .ExecuteDeleteAsync();
        }

        public async Task ClearAllAsync(string userId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            await db.Notifications
                .Where(x => x.UserId == userId)
                .ExecuteDeleteAsync();
        }

        // ══════════════════════════════════════════════════════════════════════
        // PRIVATE HELPERS
        // ══════════════════════════════════════════════════════════════════════

        private static Notification BuildNotification(
            string recipientUserId,
            Guid organizationId,
            string? actorUserId,
            string notificationType,
            string? referenceType,
            Guid? referenceId,
            string title,
            string message,
            string? redirectUrl,
            string priority)
            => new()
            {
                UserId = recipientUserId,
                OrganizationId = organizationId,
                CreatedByUserId = actorUserId,
                NotificationType = notificationType,
                ReferenceType = referenceType,
                ReferenceId = referenceId,
                Title = title,
                Message = message,
                RedirectUrl = redirectUrl,
                Priority = priority,
                //IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

        private async Task PushAsync(
            string userId,
            Notification notification,
            ApplicationDbContext db)
        {
            try
            {
                // Pre-compute unread count so the frontend never needs an extra call
                var unreadCount = await db.Notifications
                    .CountAsync(x => x.UserId == userId && !x.IsRead);

                var pushDto = new NotificationPushDto
                {
                    NotificationId = notification.NotificationId,
                    Title = notification.Title,
                    Message = notification.Message,
                    RedirectUrl = notification.RedirectUrl,
                    NotificationType = notification.NotificationType,
                    Priority = notification.Priority,
                    CreatedAt = notification.CreatedAt,
                    NewUnreadCount = unreadCount
                };

                //await _hub.Clients
                //    .Group(userId)
                //    .SendAsync("ReceiveNotification", pushDto);
    //            var unreadCount = await db.Notifications
    //.CountAsync(x => x.UserId == userId && !x.IsRead);

                await _hub.Clients
                    .Group($"user-{userId}")
                    .SendAsync("ReceiveNotification",
                        new NotificationPushDto
                        {
                            NotificationId = notification.NotificationId,
                            Title = notification.Title,
                            Message = notification.Message,
                            RedirectUrl = notification.RedirectUrl,
                            NotificationType = notification.NotificationType,
                            Priority = notification.Priority,
                            CreatedAt = notification.CreatedAt,
                            NewUnreadCount = unreadCount
                        });
            }
            catch (Exception ex)
            {
                // Never let SignalR failure break a business transaction
                Console.WriteLine($"[NotificationEngine] Push failed for {userId}: {ex.Message}");
            }
        }

        private static NotificationDto MapToDto(Notification x) => new()
        {
            NotificationId = x.NotificationId,
            Title = x.Title,
            Message = x.Message,
            IsRead = x.IsRead,
            RedirectUrl = x.RedirectUrl,
            NotificationType = x.NotificationType,
            ReferenceType = x.ReferenceType,
            ReferenceId = x.ReferenceId,
            Priority = x.Priority,
            CreatedAt = x.CreatedAt,
            ReadAt = x.ReadAt
        };

        /// <summary>Notify the employee that an asset has been assigned to them.</summary>
        public Task SendAssetAssignedAsync(
            string employeeUserId,
            string assetName,
            string assetCode,
            Guid assetId,
            Guid organizationId,
            string actorUserId)
            => SendToUserAsync(
                employeeUserId, organizationId, actorUserId,
                AssetNotificationTypes.AssetAssigned,
                "Asset", assetId,
                "Asset Assigned",
                $"{assetName} ({assetCode}) has been assigned to you.",
                "/employee/assets");

        // ── Asset request raised ──────────────────────────────────────────────

        /// <summary>
        /// Notify approvers (HR or OrgAdmin depending on requestor role)
        /// that a new asset request has been raised.
        /// </summary>
        public Task SendAssetRequestRaisedAsync(
     string assetName,
     string assetCode,
     string requestType,
     Guid requestedByEmployeeId,
     Guid assetRequestId,
     Guid organizationId,
     Guid departmentId, // <-- ADDED THIS ARGUMENT HERE
     string requestorRole,
     string actorUserId)
     => SendToApproversAsync(
         organizationId,
         departmentId, // <-- CHANGED from Guid.Empty to use the employee's departmentId
         requestorRole,
         actorUserId,
         AssetNotificationTypes.AssetRequestRaised,
         "AssetRequest", assetRequestId,
         $"New {requestType} Request",
         $"{assetName} ({assetCode}): {requestType} requested.",
         "/hr/asset-requests");

        // ── Asset request reviewed ────────────────────────────────────────────

        /// <summary>Notify the requestor that their asset request has been reviewed.</summary>
        public Task SendAssetRequestReviewedAsync(
            string requestorUserId,
            string assetName,
            string requestType,
            bool approved,
            Guid assetRequestId,
            Guid organizationId,
            string actorUserId)
            => SendToUserAsync(
                requestorUserId, organizationId, actorUserId,
                approved
                    ? AssetNotificationTypes.AssetRequestApproved
                    : AssetNotificationTypes.AssetRequestRejected,
                "AssetRequest", assetRequestId,
                approved ? $"{requestType} Request Approved ✓" : $"{requestType} Request Rejected",
                approved
                    ? $"Your {requestType} request for {assetName} has been approved."
                    : $"Your {requestType} request for {assetName} was not approved.",
                "/employee/asset-requests");

        // ── Procurement raised ────────────────────────────────────────────────

        /// <summary>Notify OrgAdmin that a new procurement request has been raised by HR.</summary>
        public Task SendProcurementRaisedAsync(
            string assetCategory,
            int quantity,
            Guid departmentId,
            Guid procurementRequestId,
            Guid organizationId,
            string actorUserId)
            => SendToApproversAsync(
                organizationId,
                departmentId,
                "HR",   // requestor is HR → approvers = OrgAdmins
                actorUserId,
                AssetNotificationTypes.ProcurementRaised,
                "ProcurementRequest", procurementRequestId,
                "New Procurement Request",
                $"HR has requested {quantity}× {assetCategory}.",
                "/admin/procurement");

        // ── Procurement reviewed ──────────────────────────────────────────────

        /// <summary>Notify the HR who raised the procurement of the review outcome.</summary>
        public Task SendProcurementReviewedAsync(
            string hrUserId,
            string assetCategory,
            int quantity,
            bool approved,
            Guid procurementRequestId,
            Guid organizationId,
            string actorUserId)
            => SendToUserAsync(
                hrUserId, organizationId, actorUserId,
                approved
                    ? AssetNotificationTypes.ProcurementApproved
                    : AssetNotificationTypes.ProcurementRejected,
                "ProcurementRequest", procurementRequestId,
                approved ? "Procurement Approved ✓" : "Procurement Rejected",
                approved
                    ? $"Your request for {quantity}× {assetCategory} has been approved."
                    : $"Your request for {quantity}× {assetCategory} was not approved.",
                "/hr/procurement");

        public Task SendProcurementFulfilledAsync(
    string hrUserId,
    string assetCategory,
    int quantityCreated,
    Guid procurementRequestId,
    Guid organizationId,
    string actorUserId)
    => SendToUserAsync(
        hrUserId, organizationId, actorUserId,
        AssetNotificationTypes.ProcurementFulfilled,
        "ProcurementRequest", procurementRequestId,
        "Assets Now Available 🎉",
        $"{quantityCreated}× {assetCategory} have been added to your department inventory and are ready to assign.",
        "/hr/assets");
    }
}