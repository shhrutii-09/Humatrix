// Infrastructure/Services/ApprovalWorkflowService.cs
using Humatrix_HRMS.Data;
using Humatrix_HRMS.Infrastructure.Constants;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Infrastructure.Services
{
    /// <summary>
    /// Reusable approval workflow service.
    /// Handles submission, approval, rejection, and cancellation for ALL modules.
    /// Business services call this; they do NOT manage ApprovalRequest directly.
    /// </summary>
    public class ApprovalWorkflowService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

        public ApprovalWorkflowService(IDbContextFactory<ApplicationDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // ── Submit ────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates an ApprovalRequest for a new submission.
        /// Call this inside the same transaction as saving the business entity.
        /// </summary>
        public async Task<ApprovalRequest> SubmitAsync(
            ApplicationDbContext db,
            string requestType,
            Guid requestId,
            Guid organizationId,
            Guid requestedByEmployeeId,
            string applicantRole,
            string approvalLevel = "HR",
            string priority = "Normal")
        {
            var approval = new ApprovalRequest
            {
                RequestType = requestType,
                RequestId = requestId,
                OrganizationId = organizationId,
                RequestedByEmployeeId = requestedByEmployeeId,
                ApplicantRole = applicantRole,
                ApprovalLevel = applicantRole == "HR" || applicantRole == "OrgAdmin"
                                               ? "OrgAdmin" : approvalLevel,
                Status = ApprovalStatuses.Pending,
                Priority = priority,
                SubmittedAt = DateTime.UtcNow
            };

            db.ApprovalRequests.Add(approval);

            db.ApprovalHistories.Add(new ApprovalHistory
            {
                ApprovalRequestId = approval.ApprovalRequestId,
                Action = ApprovalActions.Submitted,
                FromStatus = null,
                ToStatus = ApprovalStatuses.Pending,
                PerformedByEmployeeId = requestedByEmployeeId,
                PerformedByRole = applicantRole,
                OccurredAt = DateTime.UtcNow
            });

            return approval;
        }

        // ── Approve ───────────────────────────────────────────────────────────

        public async Task<ApprovalRequest> ApproveAsync(
            ApplicationDbContext db,
            Guid approvalRequestId,
            Guid approverEmployeeId,
            string approverRole,
            string? comments = null)
        {
            var approval = await GetApprovalOrThrowAsync(db, approvalRequestId);

            ValidateApproverRole(approval, approverRole);
            EnsurePending(approval);

            var fromStatus = approval.Status;
            approval.Status = ApprovalStatuses.Approved;
            approval.ActionedByEmployeeId = approverEmployeeId;
            approval.ApproverComments = comments;
            approval.CompletedAt = DateTime.UtcNow;

            db.ApprovalHistories.Add(new ApprovalHistory
            {
                ApprovalRequestId = approvalRequestId,
                Action = ApprovalActions.Approved,
                FromStatus = fromStatus,
                ToStatus = ApprovalStatuses.Approved,
                PerformedByEmployeeId = approverEmployeeId,
                PerformedByRole = approverRole,
                Comments = comments,
                OccurredAt = DateTime.UtcNow
            });

            return approval;
        }

        // ── Reject ────────────────────────────────────────────────────────────

        public async Task<ApprovalRequest> RejectAsync(
            ApplicationDbContext db,
            Guid approvalRequestId,
            Guid approverEmployeeId,
            string approverRole,
            string reason,
            string? comments = null)
        {
            var approval = await GetApprovalOrThrowAsync(db, approvalRequestId);

            ValidateApproverRole(approval, approverRole);
            EnsurePending(approval);

            var fromStatus = approval.Status;
            approval.Status = ApprovalStatuses.Rejected;
            approval.ActionedByEmployeeId = approverEmployeeId;
            approval.RejectionReason = reason;
            approval.ApproverComments = comments;
            approval.CompletedAt = DateTime.UtcNow;

            db.ApprovalHistories.Add(new ApprovalHistory
            {
                ApprovalRequestId = approvalRequestId,
                Action = ApprovalActions.Rejected,
                FromStatus = fromStatus,
                ToStatus = ApprovalStatuses.Rejected,
                PerformedByEmployeeId = approverEmployeeId,
                PerformedByRole = approverRole,
                Comments = reason,
                OccurredAt = DateTime.UtcNow
            });

            return approval;
        }

        // ── Cancel ────────────────────────────────────────────────────────────

        public async Task<ApprovalRequest> CancelAsync(
            ApplicationDbContext db,
            Guid approvalRequestId,
            Guid requestingEmployeeId,
            string requestingRole)
        {
            var approval = await GetApprovalOrThrowAsync(db, approvalRequestId);
            EnsurePending(approval);

            var fromStatus = approval.Status;
            approval.Status = ApprovalStatuses.Cancelled;
            approval.CompletedAt = DateTime.UtcNow;

            db.ApprovalHistories.Add(new ApprovalHistory
            {
                ApprovalRequestId = approvalRequestId,
                Action = ApprovalActions.Cancelled,
                FromStatus = fromStatus,
                ToStatus = ApprovalStatuses.Cancelled,
                PerformedByEmployeeId = requestingEmployeeId,
                PerformedByRole = requestingRole,
                OccurredAt = DateTime.UtcNow
            });

            return approval;
        }

        // ── Query helpers ─────────────────────────────────────────────────────

        public async Task<ApprovalRequest?> GetByRequestIdAsync(
            Guid requestId, string requestType)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            return await db.ApprovalRequests
                .Include(x => x.History)
                .FirstOrDefaultAsync(x =>
                    x.RequestId == requestId &&
                    x.RequestType == requestType);
        }

        public async Task<List<ApprovalRequest>> GetPendingByOrgAsync(
            Guid organizationId, string? requestType = null)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var q = db.ApprovalRequests
                .Where(x => x.OrganizationId == organizationId &&
                            x.Status == ApprovalStatuses.Pending);
            if (requestType != null)
                q = q.Where(x => x.RequestType == requestType);
            return await q.OrderBy(x => x.SubmittedAt).ToListAsync();
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static async Task<ApprovalRequest> GetApprovalOrThrowAsync(
            ApplicationDbContext db, Guid id)
        {
            return await db.ApprovalRequests.FindAsync(id)
                ?? throw new InvalidOperationException($"ApprovalRequest {id} not found.");
        }

        private static void EnsurePending(ApprovalRequest approval)
        {
            if (approval.Status != ApprovalStatuses.Pending)
                throw new InvalidOperationException(
                    $"Cannot action a request in '{approval.Status}' status.");
        }

        private static void ValidateApproverRole(ApprovalRequest approval, string approverRole)
        {
            // HR cannot approve if applicant is also HR (would need OrgAdmin)
            if (approval.ApprovalLevel == "OrgAdmin" && approverRole == "HR")
                throw new UnauthorizedAccessException(
                    "This request requires OrgAdmin approval.");

            // OrgAdmin can always approve
        }
    }
}