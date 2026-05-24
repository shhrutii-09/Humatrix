// Infrastructure/Services/ActivityLogService.cs
using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Humatrix_HRMS.Infrastructure.Services
{
    public class ActivityLogService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

        // JSON options: small + safe for audit logs
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public ActivityLogService(IDbContextFactory<ApplicationDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // ── Core Log Method ───────────────────────────────────────────────────

        public async Task LogAsync(
            Guid organizationId,
            string module,
            string action,
            string entityType,
            Guid entityId,
            string performedByUserId,
            string performedByRole,
            object? oldValues = null,
            object? newValues = null,
            string? ipAddress = null,
            string? additionalInfo = null)
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            db.ActivityLogs.Add(new ActivityLog
            {
                OrganizationId = organizationId,
                Module = module,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                PerformedByUserId = performedByUserId,
                PerformedByRole = performedByRole,
                OldValues = oldValues != null ? JsonSerializer.Serialize(oldValues, _jsonOpts) : null,
                NewValues = newValues != null ? JsonSerializer.Serialize(newValues, _jsonOpts) : null,
                IpAddress = ipAddress,
                AdditionalInfo = additionalInfo,
                OccurredAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        // ── Domain-Specific Helpers ───────────────────────────────────────────

        public Task LogLeaveAppliedAsync(
            Guid organizationId, Guid leaveRequestId,
            string userId, string role)
            => LogAsync(organizationId, "Leave", "Applied",
                "LeaveRequest", leaveRequestId, userId, role);

        public Task LogLeaveApprovedAsync(
            Guid organizationId, Guid leaveRequestId,
            string userId, string role)
            => LogAsync(organizationId, "Leave", "Approved",
                "LeaveRequest", leaveRequestId, userId, role);

        public Task LogLeaveRejectedAsync(
            Guid organizationId, Guid leaveRequestId,
            string userId, string role, string reason)
            => LogAsync(organizationId, "Leave", "Rejected",
                "LeaveRequest", leaveRequestId, userId, role,
                additionalInfo: reason);

        public Task LogWfhAppliedAsync(
            Guid organizationId, Guid requestId,
            string userId, string role)
            => LogAsync(organizationId, "WFH", "Applied",
                "WorkFromHomeRequest", requestId, userId, role);

        public Task LogOvertimeAppliedAsync(
            Guid organizationId, Guid requestId,
            string userId, string role)
            => LogAsync(organizationId, "Overtime", "Applied",
                "OvertimeRequest", requestId, userId, role);

        public Task LogAttendanceCorrectionAsync(
            Guid organizationId, Guid requestId,
            string action, string userId, string role)
            => LogAsync(organizationId, "Attendance", action,
                "AttendanceCorrectionRequest", requestId, userId, role);

        public Task LogEmployeeUpdatedAsync(
            Guid organizationId, Guid employeeId,
            string userId, string role,
            object? oldValues, object? newValues)
            => LogAsync(organizationId, "Employee", "Updated",
                "Employee", employeeId, userId, role, oldValues, newValues);

        // ── Query Helpers ─────────────────────────────────────────────────────

        public async Task<List<ActivityLog>> GetByEntityAsync(
            string entityType, Guid entityId, Guid organizationId)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            return await db.ActivityLogs
                .Where(x =>
                    x.EntityType == entityType &&
                    x.EntityId == entityId &&
                    x.OrganizationId == organizationId)
                .OrderByDescending(x => x.OccurredAt)
                .ToListAsync();
        }

        public async Task<List<ActivityLog>> GetByUserAsync(
            string userId, Guid organizationId, int pageSize = 50)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            return await db.ActivityLogs
                .Where(x =>
                    x.PerformedByUserId == userId &&
                    x.OrganizationId == organizationId)
                .OrderByDescending(x => x.OccurredAt)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<List<ActivityLog>> GetOrgAuditAsync(
            Guid organizationId,
            string? module = null,
            DateTime? fromDate = null,
            int pageSize = 100)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var q = db.ActivityLogs
                .Where(x => x.OrganizationId == organizationId);

            if (module != null) q = q.Where(x => x.Module == module);
            if (fromDate != null) q = q.Where(x => x.OccurredAt >= fromDate);

            return await q
                .OrderByDescending(x => x.OccurredAt)
                .Take(pageSize)
                .ToListAsync();
        }
    }
}