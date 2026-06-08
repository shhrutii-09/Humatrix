// Services/EmployeeExitService.cs
using Humatrix_HRMS.Data;
using Humatrix_HRMS.Infrastructure.Constants;
using Humatrix_HRMS.Models;
using Humatrix_HRMS.Services.Documents;
using Microsoft.AspNetCore.Identity;
using Microsoft.CodeAnalysis.Differencing;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class EmployeeExitService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly CurrentUserService _currentUser;
        private readonly NotificationService _notificationService;
        private readonly IOrgGeneratedDocumentService _orgDocumentService;

        public EmployeeExitService(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            CurrentUserService currentUser,
            NotificationService notificationService,
            IOrgGeneratedDocumentService orgDocumentService)
        {
            _db = db;
            _userManager = userManager;
            _currentUser = currentUser;
            _notificationService = notificationService;
            _orgDocumentService = orgDocumentService;
        }

        #region Employee Actions

        public async Task<EmployeeExit> SubmitResignationAsync(
            DateTime lastWorkingDay,
            string reason,
            string? remarks = null)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _db.Employees
                .Include(e => e.AssetAssignments.Where(a => a.ReturnedAt == null))
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee profile not found");

            if (employee.Status != "Active")
                throw new Exception("Only active employees can submit resignation");

            // Check if pending exit already exists
            //var existing = await _db.EmployeeExits
            //    .FirstOrDefaultAsync(x => x.EmployeeId == employee.EmployeeId &&
            //                             x.Status == ExitStatus.Pending);
            var existing = await _db.EmployeeExits
    .FirstOrDefaultAsync(x =>
        x.EmployeeId == employee.EmployeeId &&
        x.Status != ExitStatus.Completed &&
        x.Status != ExitStatus.Rejected);

            if (existing != null)
                throw new Exception("You already have a pending exit request");

            // Validate last working day (minimum notice period)
            var minNoticeDays = await GetMinimumNoticePeriodAsync(employee.OrganizationId);
            var noticeDays = (lastWorkingDay.Date - DateTime.UtcNow.Date).Days;

            if (noticeDays < minNoticeDays)
                throw new Exception($"Minimum notice period is {minNoticeDays} days. Your requested last working day is only {noticeDays} days away.");

            // Check if employee has any pending asset requests - FIXED: Use RequestedByEmployeeId instead of EmployeeId
            var pendingAssetRequests = await _db.AssetRequests
                .AnyAsync(a => a.RequestedByEmployeeId == employee.EmployeeId && a.Status == AssetRequestStatus.Pending);

            if (pendingAssetRequests)
                throw new Exception("Please resolve all pending asset requests before submitting resignation.");

            var exit = new EmployeeExit
            {
                EmployeeId = employee.EmployeeId,
                OrganizationId = employee.OrganizationId,
                ResignationDate = DateTime.UtcNow,
                LastWorkingDay = lastWorkingDay,
                Reason = reason,
                Remarks = remarks,
                Status = ExitStatus.Pending
            };

            _db.EmployeeExits.Add(exit);
            await _db.SaveChangesAsync();

            // Notify HR and OrgAdmin
            await NotifyExitSubmittedAsync(exit, employee);

            return exit;
        }

        public async Task<EmployeeExit?> GetMyExitRequestAsync()
        {
            var user = await _currentUser.GetUserAsync();
            var employee = await _db.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            if (employee == null) return null;

            return await _db.EmployeeExits
                .Include(x => x.ApprovedBy)
                .Include(x => x.CompletedBy)
                .Where(x => x.EmployeeId == employee.EmployeeId)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> CancelExitRequestAsync()
        {
            var user = await _currentUser.GetUserAsync();
            var employee = await _db.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            var exit = await _db.EmployeeExits
                .FirstOrDefaultAsync(x => x.EmployeeId == employee.EmployeeId &&
                                         x.Status == ExitStatus.Pending);

            if (exit == null)
                throw new Exception("No pending exit request found");

            _db.EmployeeExits.Remove(exit);
            await _db.SaveChangesAsync();

            return true;
        }

        #endregion

        #region HR/OrgAdmin Actions

        public async Task<List<EmployeeExit>> GetExitRequestsAsync(
            string? statusFilter = null,
            Guid? departmentId = null)
        {
            var user = await _currentUser.GetUserAsync();
            var userRoles = await _userManager.GetRolesAsync(user);
            var isHr = userRoles.Contains("HR");
            var isOrgAdmin = userRoles.Contains("OrgAdmin");

            var query = _db.EmployeeExits
                .Include(x => x.Employee)
                    .ThenInclude(e => e.Department)
                .Include(x => x.ApprovedBy)
                .Include(x => x.CompletedBy)
                .Where(x => x.OrganizationId == user.OrganizationId);

            // HR sees only their department
            if (isHr && !isOrgAdmin)
            {
                var currentEmployee = await _db.Employees
                    .FirstOrDefaultAsync(e => e.UserId == user.Id);

                if (currentEmployee != null)
                {
                    //query = query.Where(x => x.Employee.DepartmentId == currentEmployee.DepartmentId);
                    query = query.Where(x =>
           x.Employee.DepartmentId == currentEmployee.DepartmentId &&
           x.Employee.EmployeeId != currentEmployee.EmployeeId);
                }
            }

            // Optional department filter
            if (departmentId.HasValue)
            {
                query = query.Where(x => x.Employee.DepartmentId == departmentId.Value);
            }

            if (!string.IsNullOrEmpty(statusFilter))
            {
                query = query.Where(x => x.Status == statusFilter);
            }

            return await query
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();
        }

        public async Task<EmployeeExit> ApproveExitAsync(Guid exitId, string? approvalRemarks = null)
        {
            var currentUser = await _currentUser.GetUserAsync();
            var currentEmployee = await _db.Employees
                .FirstOrDefaultAsync(e => e.UserId == currentUser.Id);

            var exit = await _db.EmployeeExits
                .Include(x => x.Employee)
                .FirstOrDefaultAsync(x => x.ExitId == exitId);
            if (exit == null)
                throw new Exception("Exit request not found");

            //if (currentEmployee != null &&
            //    exit.EmployeeId == currentEmployee.EmployeeId)
            //{
            //    throw new Exception("You cannot approve your own resignation request.");
            //}
            if (exit == null)
                throw new Exception("Exit request not found");

            if (exit.Status != ExitStatus.Pending)
                throw new Exception($"Cannot approve. Current status: {exit.Status}");

            exit.Status = ExitStatus.Approved;
            //exit.Status = ExitStatus.ClearanceInProgress;
            exit.ApprovedAt = DateTime.UtcNow;
            exit.ApprovedByEmployeeId = currentEmployee?.EmployeeId;
            exit.ApprovalRemarks = approvalRemarks;

            await _db.SaveChangesAsync();

            // Notify employee
            await _notificationService.CreateNotificationAsync(
                exit.Employee.UserId,
                "Exit Request Approved",
                $"Your resignation has been approved. Your last working day is {exit.LastWorkingDay:dd MMM yyyy}. Please complete all clearance items.",
                "/employee/exit");

            return exit;
        }

        public async Task<EmployeeExit> RejectExitAsync(Guid exitId, string rejectionReason)
        {
            var currentUser = await _currentUser.GetUserAsync();
            var currentEmployee = await _db.Employees
                .FirstOrDefaultAsync(e => e.UserId == currentUser.Id);

            var exit = await _db.EmployeeExits
                .Include(x => x.Employee)
                .FirstOrDefaultAsync(x => x.ExitId == exitId);

            if (exit == null)
                throw new Exception("Exit request not found");

            if (exit.Status != ExitStatus.Pending)
                throw new Exception($"Cannot reject. Current status: {exit.Status}");

            exit.Status = ExitStatus.Rejected;
            exit.ApprovedAt = DateTime.UtcNow;
            exit.ApprovedByEmployeeId = currentEmployee?.EmployeeId;
            exit.ApprovalRemarks = rejectionReason;

            await _db.SaveChangesAsync();

            // Notify employee
            await _notificationService.CreateNotificationAsync(
                exit.Employee.UserId,
                "Exit Request Rejected",
                $"Your resignation request has been rejected. Reason: {rejectionReason}",
                "/employee/exit");

            return exit;
        }

        #endregion

        #region Clearance Management (Auto Asset Handling)

        public async Task<EmployeeExit> StartClearanceAsync(Guid exitId)
        {
            var exit = await _db.EmployeeExits
                .Include(x => x.Employee)
                .FirstOrDefaultAsync(x => x.ExitId == exitId);

            if (exit == null)
                throw new Exception("Exit request not found");

            if (exit.Status != ExitStatus.Approved)
                throw new Exception("Exit must be approved before starting clearance");

            exit.Status = ExitStatus.ClearanceInProgress;
            await _db.SaveChangesAsync();

            // Auto-check assets and create pending items
            await AutoCheckAssetsAsync(exit.EmployeeId, exit.ExitId);

            return exit;
        }

        private async Task AutoCheckAssetsAsync(Guid employeeId, Guid exitId)
        {
            var assignedAssets = await _db.AssetAssignments
                .Include(a => a.Asset)
                .Where(a => a.EmployeeId == employeeId && a.ReturnedAt == null)
                .ToListAsync();

            var exit = await _db.EmployeeExits.FindAsync(exitId);

            if (assignedAssets.Any())
            {
                // FIXED: Use .Name instead of .AssetName
                exit.AssetsReturned = false;
                exit.ClearanceRemarks = $"Pending return of {assignedAssets.Count} asset(s): {string.Join(", ", assignedAssets.Select(a => a.Asset != null ? a.Asset.Name : "Unknown Asset"))}";

                // Notify HR about pending assets
                await _notificationService.CreateOrgAdminNotificationsAsync(
                    exit.OrganizationId,
                    "Assets Pending Return",
                    $"Employee has {assignedAssets.Count} assets assigned. Please collect before completing exit.",
                    $"/hr/exit?exitId={exitId}");
            }
            else
            {
                exit.AssetsReturned = true;
                exit.AssetsReturnedDate = DateTime.UtcNow;
            }
            var hasPendingTasks =
    await HasPendingTasksAsync(employeeId);

            if (hasPendingTasks)
            {
                exit.TasksCompleted = false;

                exit.ClearanceRemarks =
                    (exit.ClearanceRemarks ?? "") +
                    "\nPending task assignments must be completed before exit.";
            }
            else
            {
                exit.TasksCompleted = true;
                exit.TasksCompletedDate = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();
        }

        public async Task<EmployeeExit> UpdateClearanceAsync(
     Guid exitId,
     bool assetsReturned,
     bool accessRevoked,
     bool knowledgeTransferred,
     bool noDuesCleared,
     bool tasksCompleted,
     decimal? fullFinalAmount = null,
     string? remarks = null)
        {
            var exit = await _db.EmployeeExits
                .Include(x => x.Employee)
                .FirstOrDefaultAsync(x => x.ExitId == exitId);

            if (exit == null)
                throw new Exception("Exit request not found");

            // Asset Clearance
            if (assetsReturned && !exit.AssetsReturned)
            {
                await AutoReturnAssetsAsync(exit.EmployeeId);
                exit.AssetsReturnedDate = DateTime.UtcNow;
            }

            // Task Clearance
            if (tasksCompleted)
            {
                bool hasPendingTasks =
                    await HasPendingTasksAsync(exit.EmployeeId);

                if (hasPendingTasks)
                {
                    throw new Exception(
                        "Employee still has pending or incomplete tasks.");
                }

                exit.TasksCompleted = true;
                exit.TasksCompletedDate = DateTime.UtcNow;
            }

            exit.AssetsReturned = assetsReturned;
            exit.AccessRevoked = accessRevoked;
            exit.KnowledgeTransferred = knowledgeTransferred;
            exit.NoDuesCleared = noDuesCleared;

            exit.NoDuesClearedDate =
                noDuesCleared
                    ? DateTime.UtcNow
                    : null;

            exit.FullFinalAmount = fullFinalAmount;
            exit.ClearanceRemarks = remarks;

            await _db.SaveChangesAsync();

            return exit;
        }

        private async Task AutoReturnAssetsAsync(Guid employeeId)
        {
            var assignedAssets = await _db.AssetAssignments
                .Where(a => a.EmployeeId == employeeId && a.ReturnedAt == null)
                .ToListAsync();

            foreach (var assignment in assignedAssets)
            {
                assignment.ReturnedAt = DateTime.UtcNow;

                // Update asset status
                var asset = await _db.Assets.FindAsync(assignment.AssetId);
                if (asset != null)
                {
                    asset.Status = "Available";
                }
            }

            await _db.SaveChangesAsync();
        }

        private async Task AutoRevokeAccessAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);

            if (user != null)
            {
                user.IsActive = false;
                await _userManager.UpdateAsync(user);
            }
        }

        public async Task<EmployeeExit> CompleteExitAsync(
            Guid exitId,
            bool issueExperienceLetter = false,
            bool issueRelievingLetter = false)
        {
            var exit = await _db.EmployeeExits
                .Include(x => x.Employee)
                    .ThenInclude(e => e.AssetAssignments)
                .FirstOrDefaultAsync(x => x.ExitId == exitId);

            if (exit == null)
                throw new Exception("Exit request not found");

            if (exit.Status != ExitStatus.ClearanceInProgress && exit.Status != ExitStatus.Approved)
                throw new Exception("Exit must be in clearance or approved state");

            // Verify all clearance items are done
            if (!exit.AssetsReturned ||
      !exit.AccessRevoked ||
      !exit.KnowledgeTransferred ||
      !exit.NoDuesCleared ||
      !exit.TasksCompleted)
            {
                throw new Exception("Complete all clearance items before completing exit");
            }
            // Check if Last Working Day has passed
            var today = DateTime.UtcNow.Date;
            if (exit.LastWorkingDay.Date > today)
            {
                throw new Exception($"Cannot complete exit before Last Working Day. Employee's last working day is {exit.LastWorkingDay:dd MMM yyyy}.");
            }

            var currentUser = await _currentUser.GetUserAsync();
            var currentEmployee = await _db.Employees
                .FirstOrDefaultAsync(e => e.UserId == currentUser.Id);

            // MARK EMPLOYEE AS INACTIVE
            var employee = exit.Employee;
            employee.Status = "Inactive";
            employee.LastWorkingDay = exit.LastWorkingDay;
            employee.ExitReason = exit.Reason;

            // Disable ApplicationUser
            //var user = await _userManager.FindByIdAsync(employee.UserId);
            //if (user != null)
            //{
            //    user.IsActive = false;
            //    await _userManager.UpdateAsync(user);
            //}
            await AutoRevokeAccessAsync(employee.UserId);

            exit.AccessRevokedDate = DateTime.UtcNow;

            // Ensure all assets are returned (final check)
            var stillAssignedAssets = await _db.AssetAssignments
                .AnyAsync(a => a.EmployeeId == employee.EmployeeId && a.ReturnedAt == null);

            //if (stillAssignedAssets)
            //{
            //    await AutoReturnAssetsAsync(employee.EmployeeId);
            //}
            if (stillAssignedAssets)
            {
                throw new Exception(
                    "Employee still has assigned assets. Return all assets before completing exit.");
            }
            exit.Status = ExitStatus.Completed;
            exit.CompletedAt = DateTime.UtcNow;
            exit.CompletedByEmployeeId = currentEmployee?.EmployeeId;

            exit.ExperienceLetterIssued = issueExperienceLetter;
            exit.RelievingLetterIssued = issueRelievingLetter;

            await _db.SaveChangesAsync();

            // Notify employee
            await _notificationService.CreateNotificationAsync(
                employee.UserId,
                "Exit Process Completed",
                "Your exit process is complete. Thank you for your contribution to the organization.",
                "/");

            // Notify HR that exit is complete
            await _notificationService.CreateOrgAdminNotificationsAsync(
                exit.OrganizationId,
                "Exit Process Completed",
                $"{employee.FirstName} {employee.LastName}'s exit process has been completed.",
                "/hr/exit");

            return exit;
        }


        private async Task<bool> HasPendingTasksAsync(Guid employeeId)
        {
            return await _db.Tasks.AnyAsync(t =>
                t.AssignedTo == employeeId &&
                t.Status != "Completed");
        }

        #endregion

        #region Auto-Process Expired Exits

        public async Task<int> AutoCompleteExpiredExitsAsync()
        {
            var today = DateTime.UtcNow.Date;

            var exitsToComplete = await _db.EmployeeExits
                .Include(x => x.Employee)
              .Where(x =>
    (x.Status == ExitStatus.Approved ||
     x.Status == ExitStatus.ClearanceInProgress)
    &&
    x.LastWorkingDay.Date <= today)
                .ToListAsync();

            int completedCount = 0;

            foreach (var exit in exitsToComplete)
            {
                try
                {
                    //await AutoReturnAssetsAsync(exit.EmployeeId);
                    //exit.AssetsReturned = true;
                    var hasUnreturnedAssets =
    await _db.AssetAssignments
        .AnyAsync(a =>
            a.EmployeeId == exit.EmployeeId &&
            a.ReturnedAt == null);

                    if (hasUnreturnedAssets)
                    {
                        continue;
                    }
                    exit.AssetsReturned = true;
                    exit.AssetsReturnedDate = DateTime.UtcNow;

                    //                await AutoRevokeAccessAsync(exit.Employee.UserId);
                    //                exit.AccessRevoked = true;
                    //                exit.AccessRevokedDate = DateTime.UtcNow;

                    //                //exit.KnowledgeTransferred = true;
                    //                //exit.NoDuesCleared = true;
                    //                if (!exit.KnowledgeTransferred ||
                    //!exit.NoDuesCleared)
                    //                {
                    //                    continue;
                    //                }
                    if (!exit.KnowledgeTransferred ||
                        !exit.NoDuesCleared ||
                        !exit.TasksCompleted)
                    {
                        continue;
                    }

                    await AutoRevokeAccessAsync(exit.Employee.UserId);
                    exit.AccessRevoked = true;
                    exit.AccessRevokedDate = DateTime.UtcNow;

                    exit.NoDuesClearedDate = DateTime.UtcNow;

                    var employee = exit.Employee;
                    employee.Status = "Inactive";
                    employee.LastWorkingDay = exit.LastWorkingDay;
                    employee.ExitReason = exit.Reason;

                    exit.Status = ExitStatus.Completed;
                    exit.CompletedAt = DateTime.UtcNow;

                    await _db.SaveChangesAsync();
                    completedCount++;

                    await _notificationService.CreateOrgAdminNotificationsAsync(
                        exit.OrganizationId,
                        "Exit Auto-Completed",
                        $"{employee.FirstName} {employee.LastName}'s exit was auto-completed as last working day has passed.",
                        "/hr/exit");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error auto-completing exit {exit.ExitId}: {ex.Message}");
                }
            }

            return completedCount;
        }

        #endregion

        #region Dashboard Stats

        public async Task<ExitDashboardStatsDto> GetExitDashboardStatsAsync(Guid? departmentId = null)
        {
            var user = await _currentUser.GetUserAsync();
            var userRoles = await _userManager.GetRolesAsync(user);
            var isHr = userRoles.Contains("HR");
            var isOrgAdmin = userRoles.Contains("OrgAdmin");

            var query = _db.EmployeeExits
                .Include(x => x.Employee)
                .Where(x => x.OrganizationId == user.OrganizationId);

            if (isHr && !isOrgAdmin && departmentId.HasValue)
            {
                query = query.Where(x => x.Employee.DepartmentId == departmentId.Value);
            }

            var today = DateTime.UtcNow.Date;

            var stats = new ExitDashboardStatsDto
            {
                PendingExits = await query.CountAsync(x => x.Status == ExitStatus.Pending),
                ApprovedExits = await query.CountAsync(x => x.Status == ExitStatus.Approved),
                ClearanceInProgress = await query.CountAsync(x => x.Status == ExitStatus.ClearanceInProgress),
                CompletedThisMonth = await query.CountAsync(x =>
                    x.Status == ExitStatus.Completed &&
                    x.CompletedAt.HasValue &&
                    x.CompletedAt.Value.Month == DateTime.UtcNow.Month),
                TotalExitsThisYear = await query.CountAsync(x =>
                    x.CreatedAt.Year == DateTime.UtcNow.Year),
                UpcomingExits = await query.CountAsync(x =>
      (x.Status == ExitStatus.Approved ||
       x.Status == ExitStatus.ClearanceInProgress) &&
      x.LastWorkingDay.Date > today &&
      x.LastWorkingDay.Date <= today.AddDays(7))
            };

            return stats;
        }

        #endregion

        #region Private Helpers

        private async Task<int> GetMinimumNoticePeriodAsync(Guid organizationId)
        {
            var org = await _db.Organizations.FindAsync(organizationId);
            return 15;
        }

        public async Task<List<string>> GetUserRolesAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return new List<string>();

            var roles = await _userManager.GetRolesAsync(user);
            return roles.ToList();
        }
        #region Exit Interview

public async Task<EmployeeExit> RecordExitInterviewAsync(Guid exitId, string feedback)
{
    var exit = await _db.EmployeeExits
        .FirstOrDefaultAsync(x => x.ExitId == exitId);

    if (exit == null)
        throw new Exception("Exit request not found");

    exit.ExitInterviewCompleted = true;
    exit.ExitInterviewDate = DateTime.UtcNow;
    exit.ExitInterviewFeedback = feedback;

    await _db.SaveChangesAsync();

    return exit;
}

#endregion
        private async Task NotifyExitSubmittedAsync(EmployeeExit exit, Employee employee)
        {
            var pendingAssets = await _db.AssetAssignments
                .CountAsync(a => a.EmployeeId == employee.EmployeeId && a.ReturnedAt == null);

            var assetMessage = pendingAssets > 0
                ? $"\n\nNote: Employee has {pendingAssets} asset(s) assigned that need to be returned."
                : "";

            var hrEmployees = await (
                from e in _db.Employees
                join ur in _db.UserRoles on e.UserId equals ur.UserId
                join r in _db.Roles on ur.RoleId equals r.Id
                where e.OrganizationId == employee.OrganizationId
                      && e.DepartmentId == employee.DepartmentId
                      && r.Name == "HR"
                select e
            ).ToListAsync();

            foreach (var hr in hrEmployees)
            {
                await _notificationService.CreateNotificationAsync(
                    hr.UserId,
                    "New Exit Request",
                    $"{employee.FirstName} {employee.LastName} has submitted resignation. Last working day: {exit.LastWorkingDay:dd MMM yyyy}.{assetMessage}",
                    "/hr/exit");
            }

            await _notificationService.CreateOrgAdminNotificationsAsync(
                employee.OrganizationId,
                "New Exit Request",
                $"{employee.FirstName} {employee.LastName} has submitted resignation. Last working day: {exit.LastWorkingDay:dd MMM yyyy}.{assetMessage}",
                "/hr/exit");
        }

        #endregion
    }

    public class ExitDashboardStatsDto
    {
        public int PendingExits { get; set; }
        public int ApprovedExits { get; set; }
        public int ClearanceInProgress { get; set; }
        public int CompletedThisMonth { get; set; }
        public int TotalExitsThisYear { get; set; }
        public int UpcomingExits { get; set; }
    }
}