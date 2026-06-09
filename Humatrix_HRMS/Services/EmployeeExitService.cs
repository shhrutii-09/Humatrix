// Services/EmployeeExitService.cs
using Humatrix_HRMS.Data;
using Humatrix_HRMS.Infrastructure.Constants;
using Humatrix_HRMS.Models;
using Humatrix_HRMS.Services.Documents;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Humatrix_HRMS.Services
{
    public class EmployeeExitService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly CurrentUserService _currentUser;
        private readonly NotificationService _notificationService;
        private readonly IOrgGeneratedDocumentService _orgDocumentService;
        private readonly IConfiguration _config;
        private readonly EmailService _emailService;

        public EmployeeExitService(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            CurrentUserService currentUser,
            NotificationService notificationService,
            IOrgGeneratedDocumentService orgDocumentService,
            IConfiguration config,        // Add this
            EmailService emailService)     // Add this
        {
            _db = db;
            _userManager = userManager;
            _currentUser = currentUser;
            _notificationService = notificationService;
            _orgDocumentService = orgDocumentService;
            _config = config;              // Add this
            _emailService = emailService;  // Add this
        }

        // ─────────────────────────────────────────────
        // EMPLOYEE / HR SELF-SERVICE
        // ─────────────────────────────────────────────

        /// <summary>
        /// Submits a resignation for the currently authenticated user.
        /// Works for both Employee and HR roles.
        /// </summary>
        public async Task<EmployeeExit> SubmitResignationAsync(
            DateTime lastWorkingDay,
            string reason,
            string? remarks = null)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new InvalidOperationException("Unauthorized");

            var employee = await _db.Employees
                .Include(e => e.AssetAssignments.Where(a => a.ReturnedAt == null))
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new InvalidOperationException("Employee profile not found.");

            if (employee.Status != "Active")
                throw new InvalidOperationException("Only active employees can submit a resignation.");

            // Block if an active exit request already exists
            // Block if an active exit request already exists (exclude Cancelled and Rejected)
            var existing = await _db.EmployeeExits.FirstOrDefaultAsync(x =>
                x.EmployeeId == employee.EmployeeId &&
                x.Status != ExitStatus.Completed &&
                x.Status != ExitStatus.Rejected &&
                x.Status != ExitStatus.Cancelled); // Add this line

            if (existing != null)
                throw new InvalidOperationException("You already have an active exit request.");

            // Minimum notice period
            var minNoticeDays = await GetMinimumNoticePeriodAsync(employee.OrganizationId);
            var noticeDays = (lastWorkingDay.Date - DateTime.UtcNow.Date).Days;
            if (noticeDays < minNoticeDays)
                throw new InvalidOperationException(
                    $"Minimum notice period is {minNoticeDays} days. " +
                    $"Your requested last working day is only {noticeDays} day(s) away.");

            // Block if employee has pending asset requests
            var pendingAssetRequests = await _db.AssetRequests.AnyAsync(a =>
                a.RequestedByEmployeeId == employee.EmployeeId &&
                a.Status == AssetRequestStatus.Pending);

            if (pendingAssetRequests)
                throw new InvalidOperationException(
                    "Please resolve all pending asset requests before submitting your resignation.");

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

            await NotifyExitSubmittedAsync(exit, employee);

            return exit;
        }

        /// <summary>
        /// Returns the latest exit request for the currently authenticated user.
        /// </summary>
        public async Task<EmployeeExit?> GetMyExitRequestAsync()
        {
            var user = await _currentUser.GetUserAsync();
            if (user == null) return null;

            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id);
            if (employee == null) return null;

            return await _db.EmployeeExits
                .Include(x => x.ApprovedBy)
                .Include(x => x.CompletedBy)
                .Where(x => x.EmployeeId == employee.EmployeeId)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Cancels the current user's own pending resignation request.
        /// </summary>
        public async Task<bool> CancelExitRequestAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new InvalidOperationException("Unauthorized");

            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new InvalidOperationException("Employee profile not found.");

            var exit = await _db.EmployeeExits.FirstOrDefaultAsync(x =>
                x.EmployeeId == employee.EmployeeId &&
                x.Status == ExitStatus.Pending)
                ?? throw new InvalidOperationException("No pending exit request found.");

            _db.EmployeeExits.Remove(exit);
            await _db.SaveChangesAsync();

            return true;
        }

        // ─────────────────────────────────────────────
        // QUERY — HR / OrgAdmin
        // ─────────────────────────────────────────────

        /// <summary>
        /// Returns exit requests visible to the current user.
        /// OrgAdmin: all employees + all HR across the whole organisation.
        /// HR: only employees (not HR) in their own department.
        /// </summary>
        public async Task<List<EmployeeExit>> GetExitRequestsAsync(
           string? statusFilter = null,
           Guid? departmentFilter = null)
        {
            var user = await _currentUser.GetUserAsync();
            if (user?.OrganizationId == null) return [];

            var roles = await _userManager.GetRolesAsync(user);
            var isOrgAdmin = roles.Contains("OrgAdmin");
            var isHr = roles.Contains("HR");

            var currentEmployee = await _db.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            var query = _db.EmployeeExits
                .Include(x => x.Employee)
                    .ThenInclude(e => e.Department)
                .Include(x => x.Employee)
                    .ThenInclude(e => e.Designation)
                .Include(x => x.ApprovedBy)
                .Include(x => x.CompletedBy)
                .Where(x => x.OrganizationId == user.OrganizationId);

            if (isHr && !isOrgAdmin && currentEmployee != null)
            {
                // Get HR user IDs in a separate query to avoid concurrency
                var hrUserIds = await GetHrUserIdsInOrgAsync(user.OrganizationId!.Value);

                query = query.Where(x =>
                    x.Employee.DepartmentId == currentEmployee.DepartmentId &&
                    !hrUserIds.Contains(x.Employee.UserId));
            }

            // OrgAdmin department filter (optional)
            if (isOrgAdmin && departmentFilter.HasValue)
                query = query.Where(x => x.Employee.DepartmentId == departmentFilter.Value);

            if (!string.IsNullOrEmpty(statusFilter))
                query = query.Where(x => x.Status == statusFilter);

            return await query.OrderByDescending(x => x.CreatedAt).ToListAsync();
        }



        /// <summary>
        /// Returns a single exit request by ID, with auth scope enforcement.
        /// </summary>
        public async Task<EmployeeExit?> GetExitRequestByIdAsync(Guid exitId)
        {
            return await _db.EmployeeExits
                .Include(x => x.Employee)
                    .ThenInclude(e => e.Department)
                .Include(x => x.ApprovedBy)
                .Include(x => x.CompletedBy)
                .FirstOrDefaultAsync(x => x.ExitId == exitId);
        }


        public async Task<List<Department>> GetVisibleDepartmentsAsync()
        {
            var user = await _currentUser.GetUserAsync();
            if (user?.OrganizationId == null) return [];

            var roles = await _userManager.GetRolesAsync(user);
            var isOrgAdmin = roles.Contains("OrgAdmin");

            var query = _db.Departments.Where(d =>
                d.OrganizationId == user.OrganizationId && d.IsActive);

            if (!isOrgAdmin)
            {
                var currentEmployee = await _db.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.UserId == user.Id);

                if (currentEmployee != null)
                    query = query.Where(d => d.DepartmentId == currentEmployee.DepartmentId);
            }

            return await query.OrderBy(d => d.Name).ToListAsync();
        }
        public async Task<ExitDashboardStatsDto> GetExitDashboardStatsAsync()
        {
            var user = await _currentUser.GetUserAsync();
            if (user?.OrganizationId == null) return new ExitDashboardStatsDto();

            var roles = await _userManager.GetRolesAsync(user);
            var isHr = roles.Contains("HR");
            var isOrgAdmin = roles.Contains("OrgAdmin");

            var query = _db.EmployeeExits
                .Include(x => x.Employee)
                .Where(x => x.OrganizationId == user.OrganizationId);

            if (isHr && !isOrgAdmin)
            {
                var currentEmployee = await _db.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.UserId == user.Id);

                var hrUserIds = await GetHrUserIdsInOrgAsync(user.OrganizationId!.Value);

                if (currentEmployee != null)
                {
                    query = query.Where(x =>
                        x.Employee.DepartmentId == currentEmployee.DepartmentId &&
                        !hrUserIds.Contains(x.Employee.UserId));
                }
            }

            var today = DateTime.UtcNow.Date;

            // Run sequentially instead of in parallel
            var pendingCount = await query.CountAsync(x => x.Status == ExitStatus.Pending);
            var approvedCount = await query.CountAsync(x => x.Status == ExitStatus.Approved);
            var clearanceCount = await query.CountAsync(x => x.Status == ExitStatus.ClearanceInProgress);
            var completedThisMonthCount = await query.CountAsync(x =>
                x.Status == ExitStatus.Completed &&
                x.CompletedAt.HasValue &&
                x.CompletedAt.Value.Month == DateTime.UtcNow.Month &&
                x.CompletedAt.Value.Year == DateTime.UtcNow.Year);
            var totalExitsThisYearCount = await query.CountAsync(x =>
                x.CreatedAt.Year == DateTime.UtcNow.Year);
            var upcomingExitsCount = await query.CountAsync(x =>
                (x.Status == ExitStatus.Approved || x.Status == ExitStatus.ClearanceInProgress) &&
                x.LastWorkingDay.Date > today &&
                x.LastWorkingDay.Date <= today.AddDays(7));

            return new ExitDashboardStatsDto
            {
                PendingExits = pendingCount,
                ApprovedExits = approvedCount,
                ClearanceInProgress = clearanceCount,
                CompletedThisMonth = completedThisMonthCount,
                TotalExitsThisYear = totalExitsThisYearCount,
                UpcomingExits = upcomingExitsCount
            };
        }
        // ─────────────────────────────────────────────
        // APPROVAL / REJECTION
        // ─────────────────────────────────────────────

        /// <summary>
        /// Approves an exit request.
        /// HR can approve employee exits in their department.
        /// OrgAdmin approves everything (including HR exits).
        /// </summary>
        public async Task<EmployeeExit> ApproveExitAsync(Guid exitId, string? approvalRemarks = null)
        {
            var (exit, actor) = await LoadAndAuthorizeExitActionAsync(exitId);

            if (exit.Status != ExitStatus.Pending)
                throw new InvalidOperationException($"Cannot approve. Current status: {exit.Status}");

            exit.Status = ExitStatus.Approved;
            exit.ApprovedAt = DateTime.UtcNow;
            exit.ApprovedByEmployeeId = actor?.EmployeeId;
            exit.ApprovalRemarks = approvalRemarks;

            await _db.SaveChangesAsync();

            await _notificationService.CreateNotificationAsync(
                exit.Employee.UserId,
                "Resignation Approved",
                $"Your resignation has been approved. Last working day: {exit.LastWorkingDay:dd MMM yyyy}. Please complete all clearance items.",
                "/employee/exit");

            return exit;
        }


        /// <summary>
        /// HR/OrgAdmin terminates an employee (not a resignation)
        /// </summary>
        /// <summary>
        /// HR/OrgAdmin terminates an employee (involuntary separation)
        /// </summary>
        public async Task<EmployeeExit> TerminateEmployeeAsync(
            Guid employeeId,
            string terminationReason,
            DateTime lastWorkingDay,
            string? remarks = null)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new InvalidOperationException("Unauthorized");

            var roles = await _userManager.GetRolesAsync(user);
            var isOrgAdmin = roles.Contains("OrgAdmin");
            var isHr = roles.Contains("HR");

            if (!isOrgAdmin && !isHr)
                throw new UnauthorizedAccessException("Only HR or OrgAdmin can terminate employees.");

            var employee = await _db.Employees
                .Include(e => e.AssetAssignments.Where(a => a.ReturnedAt == null))
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId)
                ?? throw new InvalidOperationException("Employee not found.");

            if (employee.Status != "Active")
                throw new InvalidOperationException("Only active employees can be terminated.");

            // HR can only terminate employees in their department (non-HR)
            if (isHr && !isOrgAdmin)
            {
                var currentEmployeeHr = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id);
                if (currentEmployeeHr == null || employee.DepartmentId != currentEmployeeHr.DepartmentId)
                    throw new UnauthorizedAccessException("You can only terminate employees in your department.");

                var employeeRoles = await _userManager.GetRolesAsync(
                    await _userManager.FindByIdAsync(employee.UserId)
                    ?? throw new InvalidOperationException("User not found."));

                if (employeeRoles.Contains("HR"))
                    throw new UnauthorizedAccessException("HR cannot terminate other HR members. Only OrgAdmin can.");
            }

            // Check for existing active exit
            var existing = await _db.EmployeeExits.FirstOrDefaultAsync(x =>
                x.EmployeeId == employeeId &&
                x.Status != ExitStatus.Completed &&
                x.Status != ExitStatus.Rejected &&
                x.Status != ExitStatus.Cancelled);

            if (existing != null)
                throw new InvalidOperationException("Employee already has an active exit request.");

            var currentEmployeeActor = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id);

            // ============================================================
            // IMMEDIATE TERMINATION EFFECTS
            // ============================================================

            // 1. Deactivate the employee immediately
            employee.Status = "Inactive";
            employee.LastWorkingDay = lastWorkingDay;
            employee.ExitReason = terminationReason;

            // 2. Deactivate the user account immediately (can't login)
            var appUser = await _userManager.FindByIdAsync(employee.UserId);
            if (appUser != null)
            {
                appUser.IsActive = false;
                await _userManager.UpdateAsync(appUser);
            }

            // 3. Mark access as revoked immediately (or on last working day based on policy)
            // Option A: Immediate access revocation (recommended for termination)
            bool revokeAccessImmediately = true; // Could be configurable

            var exit = new EmployeeExit
            {
                EmployeeId = employeeId,
                OrganizationId = employee.OrganizationId,
                ExitType = "Termination",
                ResignationDate = DateTime.UtcNow,
                LastWorkingDay = lastWorkingDay,
                Reason = terminationReason,
                Remarks = remarks,
                Status = ExitStatus.Approved, // Auto-approved
                ApprovedAt = DateTime.UtcNow,
                ApprovedByEmployeeId = currentEmployeeActor?.EmployeeId,
                ApprovalRemarks = $"Terminated by {(isOrgAdmin ? "OrgAdmin" : "HR")} on {DateTime.UtcNow:dd MMM yyyy}",
                AccessRevoked = revokeAccessImmediately,
                AccessRevokedDate = revokeAccessImmediately ? DateTime.UtcNow : null
            };

            _db.EmployeeExits.Add(exit);
            await _db.SaveChangesAsync();

            // Send termination notifications
            await _notificationService.CreateNotificationAsync(
                employee.UserId,
                "⚠️ Employment Terminated",
                $"Your employment has been terminated effective {lastWorkingDay:dd MMM yyyy}.\n\n" +
                $"Reason: {terminationReason}\n\n" +
                $"Your system access has been {(revokeAccessImmediately ? "immediately revoked" : "will be revoked on your last working day")}.\n\n" +
                $"Please contact HR for further information regarding asset return and final settlement.",
                "/");

            await _notificationService.CreateOrgAdminNotificationsAsync(
                employee.OrganizationId,
                "Employee Terminated",
                $"⚠️ EMPLOYEE TERMINATED\n\n" +
                $"Employee: {employee.FirstName} {employee.LastName} (ID: {employee.EmployeeCode})\n" +
                $"Department: {employee.Department?.Name ?? "N/A"}\n" +
                $"Terminated by: {(isOrgAdmin ? "OrgAdmin" : "HR")}\n" +
                $"Effective date: {lastWorkingDay:dd MMM yyyy}\n" +
                $"Reason: {terminationReason}\n" +
                $"Access revoked: {(revokeAccessImmediately ? "Immediately" : "On last working day")}",
                "/admin/exit");

            return exit;
        }



        /// <summary>
        /// Rehires a previously terminated or resigned employee
        /// Only OrgAdmin can perform this action
        /// </summary>
        /// <summary>
        /// Rehires a previously terminated or resigned employee
        /// Only OrgAdmin can perform this action
        /// No invitation email - employee uses existing credentials
        /// </summary>
        public async Task<(bool Success, string Message)> RehireEmployeeAsync(
            Guid employeeId,
            DateTime rehireDate,
            string? remarks = null)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new InvalidOperationException("Unauthorized");

            var roles = await _userManager.GetRolesAsync(user);
            var isOrgAdmin = roles.Contains("OrgAdmin");

            if (!isOrgAdmin)
                throw new UnauthorizedAccessException("Only OrgAdmin can rehire employees.");

            // Find the employee
            var employee = await _db.Employees
                .Include(e => e.Department)
                .Include(e => e.Designation)
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId)
                ?? throw new InvalidOperationException("Employee not found.");

            // Check if employee is actually inactive/terminated
            if (employee.Status == "Active")
                return (false, "Employee is already active. No need to rehire.");

            // Check if employee is eligible for rehire
            if (employee.IsRehireable == false)
                return (false, "This employee is marked as not eligible for rehire.");

            // Find the last exit record
            var lastExit = await _db.EmployeeExits
                .Where(x => x.EmployeeId == employeeId)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            if (lastExit == null)
                return (false, "No exit record found for this employee.");

            // Use transaction for all changes
            using var transaction = await _db.Database.BeginTransactionAsync();

            try
            {
                // ============================================================
                // 1. REACTIVATE EMPLOYEE
                // ============================================================
                var oldStatus = employee.Status;
                employee.Status = "Active";
                employee.LastWorkingDay = null;
                employee.ExitReason = null;
                employee.LastRehireDate = rehireDate;
                employee.RehireCount = (employee.RehireCount ?? 0) + 1;

                // Reset leave balances? (Based on company policy)
                // Option 1: Keep previous balance
                // Option 2: Reset to default balance for new joinees
                // Option 3: Pro-rate based on rehire date

                // ============================================================
                // 2. REACTIVATE USER ACCOUNT (No password reset needed)
                // ============================================================
                var appUser = await _userManager.FindByIdAsync(employee.UserId);
                if (appUser != null)
                {
                    appUser.IsActive = true;
                    appUser.LockoutEnd = null; // Remove any lockout
                                               // Do NOT change password
                                               // Do NOT require email confirmation again
                    await _userManager.UpdateAsync(appUser);
                }

                // ============================================================
                // 3. REACTIVATE ASSETS (If they were returned)
                // ============================================================
                // Option: Reassign previously used assets if still available
                var previousAssignments = await _db.AssetAssignments
                    .Where(a => a.EmployeeId == employeeId && a.ReturnedAt != null)
                    .OrderByDescending(a => a.ReturnedAt)
                    .FirstOrDefaultAsync();

                if (previousAssignments != null && previousAssignments.ReturnedAt.HasValue)
                {
                    // Check if the asset is still available
                    var asset = await _db.Assets.FindAsync(previousAssignments.AssetId);
                    if (asset != null && asset.Status == AssetStatus.Available)
                    {
                        // Create a new assignment for the same asset
                        var newAssignment = new AssetAssignment
                        {
                            AssetId = asset.AssetId,
                            EmployeeId = employeeId,
                            OrganizationId = employee.OrganizationId,
                            AssignedAt = rehireDate,
                            AssignedByUserId = user.Id,
                            AssignmentNotes = "Reassigned upon rehire"
                        };
                        _db.AssetAssignments.Add(newAssignment);

                        asset.Status = AssetStatus.Assigned;
                        asset.CurrentEmployeeId = employeeId;
                        asset.UpdatedAt = DateTime.UtcNow;
                    }
                }

                // ============================================================
                // 4. CREATE REHIRE RECORD
                // ============================================================
                var rehireRecord = new EmployeeRehire
                {
                    EmployeeId = employeeId,
                    PreviousExitId = lastExit.ExitId,
                    RehireDate = rehireDate,
                    RehiredByUserId = user.Id,
                    Remarks = remarks,
                    PreviousStatus = lastExit.Status,
                    PreviousExitType = lastExit.ExitType ?? "Resignation"
                };
                _db.EmployeeRehires.Add(rehireRecord);

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                // ============================================================
                // 5. SEND NOTIFICATION (NOT INVITATION)
                // ============================================================
                var notificationMessage = $"✅ WELCOME BACK!\n\n" +
                                          $"You have been rehired effective {rehireDate:dd MMM yyyy}.\n\n" +
                                          $"📋 Rehire Details:\n" +
                                          $"• Previous Employment: {employee.JoiningDate:dd MMM yyyy} to {lastExit.LastWorkingDay:dd MMM yyyy}\n" +
                                          $"• New Start Date: {rehireDate:dd MMM yyyy}\n" +
                                          $"• Department: {employee.Department?.Name ?? "N/A"}\n" +
                                          $"• Designation: {employee.Designation?.Name ?? "N/A"}\n\n" +
                                          $"🔐 Account Access:\n" +
                                          $"• Your existing login credentials are active\n" +
                                          $"• Login here: {_config["AppBaseUrl"]}/login\n" +
                                          $"• If you forgot your password, use 'Forgot Password'\n\n" +
                                          $"📝 Next Steps:\n" +
                                          $"1. Login to your account\n" +
                                          $"2. Review/Update your profile information\n" +
                                          $"3. Check your assigned assets\n" +
                                          $"4. Review your leave balance\n\n" +
                                          $"Remarks: {remarks ?? "N/A"}\n\n" +
                                          $"Welcome back to the team!";

                await _notificationService.CreateNotificationAsync(
                    employee.UserId,
                    "✅ Welcome Back! You've Been Rehired",
                    notificationMessage,
                    "/employee/dashboard");

                // Send email notification (NOT invitation email)
                await _emailService.SendRehireNotificationAsync(
                    appUser?.Email ?? "",
                    $"{employee.FirstName} {employee.LastName}",
                    rehireDate,
                    employee.Department?.Name ?? "N/A",
                    employee.Designation?.Name ?? "N/A",
                    remarks);

                await _notificationService.CreateOrgAdminNotificationsAsync(
                    employee.OrganizationId,
                    "✅ Employee Rehired",
                    $"Employee: {employee.FirstName} {employee.LastName} (ID: {employee.EmployeeCode})\n" +
                    $"Has been rehired effective {rehireDate:dd MMM yyyy}\n" +
                    $"Previous exit type: {lastExit.ExitType ?? "Resignation"}\n" +
                    $"This was their {(employee.RehireCount ?? 0)} rehire\n" +
                    $"Rehired by: {user.FirstName} {user.LastName}\n" +
                    $"Remarks: {remarks ?? "N/A"}",
                    "/hr/employees");

                return (true, $"Employee {employee.FirstName} {employee.LastName} has been successfully rehired. They can login with their existing credentials.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException($"Failed to rehire employee: {ex.Message}", ex);
            }
        }
        public async Task<(bool Success, string Message, List<string> RevertedItems)> CancelExitRequestAsync(Guid? exitId = null)
        {
            var revertedItems = new List<string>();

            var user = await _currentUser.GetUserAsync()
                ?? throw new UnauthorizedAccessException("User not authenticated");

            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new InvalidOperationException("Employee profile not found.");

            EmployeeExit? exit;

            // Determine which exit to cancel
            if (exitId.HasValue)
            {
                // FIRST: Try to find the exit as the employee's own exit
                exit = await _db.EmployeeExits
                    .Include(x => x.Employee)
                    .ThenInclude(e => e.AssetAssignments)
                    .FirstOrDefaultAsync(x => x.ExitId == exitId.Value && x.EmployeeId == employee.EmployeeId);

                // If it's NOT the employee's own exit, then check admin/HR permissions
                if (exit == null)
                {
                    var roles = await _userManager.GetRolesAsync(user);
                    var isOrgAdmin = roles.Contains("OrgAdmin");
                    var isHr = roles.Contains("HR");

                    if (!isOrgAdmin && !isHr)
                        throw new UnauthorizedAccessException("You don't have permission to cancel this request.");

                    exit = await _db.EmployeeExits
                        .Include(x => x.Employee)
                        .ThenInclude(e => e.AssetAssignments)
                        .FirstOrDefaultAsync(x => x.ExitId == exitId.Value);

                    if (exit == null)
                        throw new InvalidOperationException("Exit request not found.");

                    if (isHr && !isOrgAdmin)
                    {
                        var currentEmployee = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id);
                        if (currentEmployee == null || exit.Employee.DepartmentId != currentEmployee.DepartmentId)
                            throw new UnauthorizedAccessException("You can only cancel exit requests in your department.");

                        var exiteeRoles = await _userManager.GetRolesAsync(
                            await _userManager.FindByIdAsync(exit.Employee.UserId)
                            ?? throw new InvalidOperationException("User not found."));

                        if (exiteeRoles.Contains("HR"))
                            throw new UnauthorizedAccessException("HR exits can only be cancelled by OrgAdmin.");
                    }
                }
            }
            else
            {
                exit = await _db.EmployeeExits
                    .Include(x => x.Employee)
                    .ThenInclude(e => e.AssetAssignments)
                    .Where(x => x.EmployeeId == employee.EmployeeId)
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefaultAsync();
            }

            if (exit == null)
                return (false, "No active exit request found.", revertedItems);

            if (!exitId.HasValue || exit.EmployeeId == employee.EmployeeId)
            {
                if (exit.Status == ExitStatus.Completed)
                    return (false, "Cannot cancel a completed exit. Please contact HR for assistance.", revertedItems);

                if (exit.LastWorkingDay.Date < DateTime.UtcNow.Date)
                    return (false, $"Cannot cancel exit request because your last working day ({exit.LastWorkingDay:dd MMM yyyy}) has passed. Please contact HR for assistance.", revertedItems);
            }
            else
            {
                if (exit.Status == ExitStatus.Completed)
                    return (false, "Cannot cancel a completed exit.", revertedItems);
            }

            using var transaction = await _db.Database.BeginTransactionAsync();

            try
            {
                var exitEmployee = exit.Employee;
                var originalStatus = exit.Status;
                var isSelfCancel = (!exitId.HasValue || exit.EmployeeId == employee.EmployeeId);
                var cancelledBy = isSelfCancel ? "Employee Self-Service" : "HR/Admin";

                // ============================================================
                // 1. REVERT ASSETS
                // ============================================================
                // ============================================================
                // 1. REVERT ASSETS - Create NEW assignments instead of reactivating old ones
                // ============================================================
                if (exit.AssetsReturned || exit.Status == ExitStatus.ClearanceInProgress || exit.Status == ExitStatus.Approved)
                {
                    // Find assets that were returned during exit process
                    var returnedAssignments = await _db.AssetAssignments
                        .Include(a => a.Asset)
                        .Where(a => a.EmployeeId == exit.EmployeeId && a.ReturnedAt != null)
                        .ToListAsync();

                    if (returnedAssignments.Any())
                    {
                        int reassignedCount = 0;

                        foreach (var oldAssignment in returnedAssignments)
                        {
                            var asset = await _db.Assets.FindAsync(oldAssignment.AssetId);
                            if (asset == null)
                            {
                                revertedItems.Add($"Asset with ID {oldAssignment.AssetId} not found. Skipping.");
                                continue;
                            }

                            // Check if asset is available for reassignment
                            if (asset.Status != AssetStatus.Available)
                            {
                                revertedItems.Add($"Asset '{asset.Name}' (Code: {asset.AssetCode}) is not available (Status: {asset.Status}). Cannot reassign.");
                                continue;
                            }

                            // Check if there's already an active assignment for this asset
                            var existingActive = await _db.AssetAssignments
                                .AnyAsync(a => a.AssetId == asset.AssetId && a.ReturnedAt == null);

                            if (existingActive)
                            {
                                revertedItems.Add($"Asset '{asset.Name}' is already assigned to someone else. Cannot reassign.");
                                continue;
                            }

                            // Create a BRAND NEW assignment (don't reactivate the old one)
                            var newAssignment = new AssetAssignment
                            {
                                AssetAssignmentId = Guid.NewGuid(),
                                AssetId = asset.AssetId,
                                EmployeeId = exit.EmployeeId,
                                OrganizationId = exit.OrganizationId,
                                AssignedAt = DateTime.UtcNow,
                                AssignedByUserId = user.Id,
                                AssignmentNotes = $"Reassigned upon resignation cancellation (Previous assignment ended on {oldAssignment.ReturnedAt:dd MMM yyyy})"
                            };

                            _db.AssetAssignments.Add(newAssignment);

                            // Update asset status
                            asset.Status = AssetStatus.Assigned;
                            asset.CurrentEmployeeId = exit.EmployeeId;
                            asset.UpdatedAt = DateTime.UtcNow;

                            reassignedCount++;
                        }

                        await _db.SaveChangesAsync();

                        if (reassignedCount > 0)
                        {
                            revertedItems.Add($"Reassigned {reassignedCount} asset(s) back to {exitEmployee.FirstName} {exitEmployee.LastName}");
                        }
                    }
                    else
                    {
                        revertedItems.Add("No assets were returned during the exit process.");
                    }
                }
                // ============================================================
                // 2. REVERT ACCESS
                // ============================================================
                if (exit.AccessRevoked)
                {
                    var appUser = await _userManager.FindByIdAsync(exitEmployee.UserId);
                    if (appUser != null && !appUser.IsActive)
                    {
                        appUser.IsActive = true;
                        appUser.LockoutEnd = null;
                        var updateResult = await _userManager.UpdateAsync(appUser);

                        if (updateResult.Succeeded)
                        {
                            revertedItems.Add($"Restored system access for {exitEmployee.FirstName} {exitEmployee.LastName}");
                        }
                        else
                        {
                            throw new InvalidOperationException($"Failed to restore user access: {string.Join(", ", updateResult.Errors)}");
                        }
                    }
                }

                // ============================================================
                // 3. REVERT EMPLOYEE STATUS
                // ============================================================
                if (exitEmployee.Status == "Inactive")
                {
                    exitEmployee.Status = "Active";
                    exitEmployee.LastWorkingDay = null;
                    exitEmployee.ExitReason = null;
                    await _db.SaveChangesAsync();
                    revertedItems.Add($"Reactivated employee status for {exitEmployee.FirstName} {exitEmployee.LastName}");
                }

                // ============================================================
                // 4. UPDATE EXIT REQUEST STATUS (FIXED)
                // ============================================================
                if (exit.Status == ExitStatus.Pending)
                {
                    _db.EmployeeExits.Remove(exit);
                    revertedItems.Add("Removed pending exit request.");
                }
                else
                {
                    // Set status to Cancelled
                    exit.Status = ExitStatus.Cancelled;

                    // Set cancellation audit fields
                    exit.CancelledAt = DateTime.UtcNow;
                    exit.CancelledByEmployeeId = employee.EmployeeId;
                    exit.CancellationReason = $"Cancelled by {cancelledBy} on {DateTime.UtcNow:dd MMM yyyy}";

                    await _db.SaveChangesAsync();
                    revertedItems.Add($"Marked exit request as cancelled (was {originalStatus}).");
                }

                await transaction.CommitAsync();

                // ============================================================
                // 5. SEND NOTIFICATIONS
                // ============================================================
                var notificationMessage = $"Your resignation has been CANCELLED successfully.\n\n" +
                                          $"📋 Summary of changes:\n" +
                                          string.Join("\n", revertedItems.Select((item, i) => $"   {i + 1}. {item}")) +
                                          $"\n\nYou will continue as an active employee. Please contact HR if you have any questions.";

                await _notificationService.CreateNotificationAsync(
                    exitEmployee.UserId,
                    "✅ Resignation Cancelled",
                    notificationMessage,
                    "/employee/dashboard");

                var adminMessage = $"⚠️ RESIGNATION CANCELLED\n\n" +
                                  $"Employee: {exitEmployee.FirstName} {exitEmployee.LastName} (ID: {exitEmployee.EmployeeCode})\n" +
                                  $"Original Last Working Day: {exit.LastWorkingDay:dd MMM yyyy}\n" +
                                  $"Status at cancellation: {originalStatus}\n" +
                                  $"Cancelled by: {cancelledBy}\n\n" +
                                  $"Changes reverted:\n{string.Join("\n", revertedItems.Select((item, i) => $"   {i + 1}. {item}"))}";

                await _notificationService.CreateOrgAdminNotificationsAsync(
                    exit.OrganizationId,
                    "⚠️ Resignation Cancelled",
                    adminMessage,
                    "/admin/exit");

                if (exit.ApprovedByEmployeeId.HasValue)
                {
                    var approverEmployee = await _db.Employees
                        .FirstOrDefaultAsync(e => e.EmployeeId == exit.ApprovedByEmployeeId.Value);

                    if (approverEmployee != null)
                    {
                        await _notificationService.CreateNotificationAsync(
                            approverEmployee.UserId,
                            "⚠️ Resignation Cancelled",
                            $"{exitEmployee.FirstName} {exitEmployee.LastName} has cancelled their resignation.\n\n" +
                            $"They were previously at status: {originalStatus}\n" +
                            $"All assets and access have been restored.",
                            "/admin/exit");
                    }
                }

                var successMessage = $"Successfully cancelled resignation. Reverted {revertedItems.Count} item(s).";
                return (true, successMessage, revertedItems);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                // Get detailed error
                var detailedError = ex.InnerException?.Message ?? ex.Message;
                Console.WriteLine($"[ERROR] Failed to cancel exit {exit?.ExitId}: {detailedError}");
                return (false, $"Failed to cancel: {detailedError}", revertedItems);
            }
        }

        /// <summary>
        /// Returns ALL exit requests for the current user (including cancelled/rejected)
        /// </summary>
        public async Task<List<EmployeeExit>> GetAllMyExitsAsync()
        {
            var user = await _currentUser.GetUserAsync();
            if (user == null) return new List<EmployeeExit>();

            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id);
            if (employee == null) return new List<EmployeeExit>();

            return await _db.EmployeeExits
                .Include(x => x.ApprovedBy)
                .Include(x => x.CompletedBy)
                .Where(x => x.EmployeeId == employee.EmployeeId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();
        }

        /// <summary>
        /// Returns the latest ACTIVE exit request (not cancelled/rejected)
        /// </summary>
        public async Task<EmployeeExit?> GetMyActiveExitRequestAsync()
        {
            var user = await _currentUser.GetUserAsync();
            if (user == null) return null;

            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id);
            if (employee == null) return null;

            return await _db.EmployeeExits
                .Include(x => x.ApprovedBy)
                .Include(x => x.CompletedBy)
                .Where(x => x.EmployeeId == employee.EmployeeId)
                .Where(x => x.Status != ExitStatus.Cancelled && x.Status != ExitStatus.Rejected)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();
        }


        /// <summary>
        /// Rejects an exit request with a reason.
        /// </summary>
        public async Task<EmployeeExit> RejectExitAsync(Guid exitId, string rejectionReason)
        {
            if (string.IsNullOrWhiteSpace(rejectionReason))
                throw new ArgumentException("Rejection reason is required.");

            var (exit, actor) = await LoadAndAuthorizeExitActionAsync(exitId);

            if (exit.Status != ExitStatus.Pending)
                throw new InvalidOperationException($"Cannot reject. Current status: {exit.Status}");

            exit.Status = ExitStatus.Rejected;
            exit.ApprovedAt = DateTime.UtcNow;
            exit.ApprovedByEmployeeId = actor?.EmployeeId;
            exit.ApprovalRemarks = rejectionReason;

            await _db.SaveChangesAsync();

            await _notificationService.CreateNotificationAsync(
                exit.Employee.UserId,
                "Resignation Rejected",
                $"Your resignation request has been rejected. Reason: {rejectionReason}",
                "/employee/exit");

            return exit;
        }

        // ─────────────────────────────────────────────
        // CLEARANCE MANAGEMENT
        // ─────────────────────────────────────────────

        public async Task<EmployeeExit> StartClearanceAsync(Guid exitId)
        {
            var exit = await _db.EmployeeExits
                .Include(x => x.Employee)
                .FirstOrDefaultAsync(x => x.ExitId == exitId)
                ?? throw new InvalidOperationException("Exit request not found.");

            if (exit.Status != ExitStatus.Approved)
                throw new InvalidOperationException("Exit must be approved before starting clearance.");

            exit.Status = ExitStatus.ClearanceInProgress;
            await _db.SaveChangesAsync();

            await AutoCheckAssetsAsync(exit.EmployeeId, exit.ExitId);

            return exit;
        }

        public async Task<EmployeeExit> UpdateClearanceAsync(
            Guid exitId,
            bool assetsReturned,
            bool accessRevoked,
            bool knowledgeTransferred,
            bool noDuesCleared,
            decimal? fullFinalAmount = null,
            string? remarks = null)
        {
            var exit = await _db.EmployeeExits
                .Include(x => x.Employee)
                .FirstOrDefaultAsync(x => x.ExitId == exitId)
                ?? throw new InvalidOperationException("Exit request not found.");

            if (exit.Status != ExitStatus.ClearanceInProgress && exit.Status != ExitStatus.Approved)
                throw new InvalidOperationException("Exit must be in clearance state to update.");

            if (assetsReturned && !exit.AssetsReturned)
            {
                await AutoReturnAssetsAsync(exit.EmployeeId);
                exit.AssetsReturnedDate = DateTime.UtcNow;
            }

            // Re-check tasks automatically
            bool hasPendingTasks = await HasPendingTasksAsync(exit.EmployeeId);
            exit.TasksCompleted = !hasPendingTasks;
            exit.TasksCompletedDate = !hasPendingTasks ? DateTime.UtcNow : null;

            exit.AssetsReturned = assetsReturned;
            exit.AccessRevoked = accessRevoked;
            exit.KnowledgeTransferred = knowledgeTransferred;
            exit.NoDuesCleared = noDuesCleared;
            exit.NoDuesClearedDate = noDuesCleared ? DateTime.UtcNow : null;
            exit.FullFinalAmount = fullFinalAmount;
            exit.ClearanceRemarks = remarks;

            await _db.SaveChangesAsync();
            return exit;
        }

        public async Task<EmployeeExit> RecordExitInterviewAsync(Guid exitId, string feedback)
        {
            var exit = await _db.EmployeeExits.FirstOrDefaultAsync(x => x.ExitId == exitId)
                ?? throw new InvalidOperationException("Exit request not found.");

            if (string.IsNullOrWhiteSpace(feedback))
                throw new ArgumentException("Exit interview feedback cannot be empty.");

            exit.ExitInterviewCompleted = true;
            exit.ExitInterviewDate = DateTime.UtcNow;
            exit.ExitInterviewFeedback = feedback;

            await _db.SaveChangesAsync();
            return exit;
        }

        // ─────────────────────────────────────────────
        // COMPLETE EXIT
        // ─────────────────────────────────────────────

        public async Task<EmployeeExit> CompleteExitAsync(
            Guid exitId,
            bool issueExperienceLetter = false,
            bool issueRelievingLetter = false)
        {
            var exit = await _db.EmployeeExits
                .Include(x => x.Employee)
                    .ThenInclude(e => e.AssetAssignments)
                .FirstOrDefaultAsync(x => x.ExitId == exitId)
                ?? throw new InvalidOperationException("Exit request not found.");

            if (exit.Status != ExitStatus.ClearanceInProgress && exit.Status != ExitStatus.Approved)
                throw new InvalidOperationException("Exit must be in clearance or approved state.");

            // Check all clearance items
            var missing = new List<string>();
            if (!exit.AssetsReturned) missing.Add("Assets Returned");
            if (!exit.AccessRevoked) missing.Add("Access Revoked");
            if (!exit.KnowledgeTransferred) missing.Add("Knowledge Transfer");
            if (!exit.NoDuesCleared) missing.Add("No Dues Clearance");
            if (!exit.TasksCompleted) missing.Add("Tasks Completed");

            if (missing.Any())
                throw new InvalidOperationException(
                    $"Complete all clearance items before finalising exit: {string.Join(", ", missing)}.");

            var today = DateTime.UtcNow.Date;
            if (exit.LastWorkingDay.Date > today)
                throw new InvalidOperationException(
                    $"Cannot complete exit before Last Working Day ({exit.LastWorkingDay:dd MMM yyyy}).");

            // Verify no assets still assigned
            var stillAssigned = await _db.AssetAssignments.AnyAsync(a =>
                a.EmployeeId == exit.EmployeeId && a.ReturnedAt == null);
            if (stillAssigned)
                throw new InvalidOperationException(
                    "Employee still has assigned assets. Return all assets before completing exit.");

            var currentUser = await _currentUser.GetUserAsync();
            var currentEmployee = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == currentUser!.Id);

            // Mark employee inactive
            var employee = exit.Employee;
            employee.Status = "Inactive";
            employee.LastWorkingDay = exit.LastWorkingDay;
            employee.ExitReason = exit.Reason;

            await AutoRevokeAccessAsync(employee.UserId);
            exit.AccessRevokedDate = DateTime.UtcNow;

            exit.Status = ExitStatus.Completed;
            exit.CompletedAt = DateTime.UtcNow;
            exit.CompletedByEmployeeId = currentEmployee?.EmployeeId;
            exit.ExperienceLetterIssued = issueExperienceLetter;
            exit.RelievingLetterIssued = issueRelievingLetter;

            await _db.SaveChangesAsync();

            await _notificationService.CreateNotificationAsync(
                employee.UserId,
                "Exit Process Completed",
                "Your exit process has been completed. Thank you for your contribution to the organisation.",
                "/");

            await _notificationService.CreateOrgAdminNotificationsAsync(
                exit.OrganizationId,
                "Exit Process Completed",
                $"{employee.FirstName} {employee.LastName}'s exit process has been completed.",
                "/admin/exit");

            return exit;
        }

        // ─────────────────────────────────────────────
        // BACKGROUND JOB
        // ─────────────────────────────────────────────

        public async Task<int> AutoCompleteExpiredExitsAsync()
        {
            var today = DateTime.UtcNow.Date;
            int completedCount = 0;

            var exitsToProcess = await _db.EmployeeExits
                .Include(x => x.Employee)
                .Where(x =>
                    (x.Status == ExitStatus.Approved || x.Status == ExitStatus.ClearanceInProgress) &&
                    x.LastWorkingDay.Date <= today)
                .ToListAsync();

            foreach (var exit in exitsToProcess)
            {
                try
                {
                    // Skip if assets not returned
                    var hasUnreturnedAssets = await _db.AssetAssignments.AnyAsync(a =>
                        a.EmployeeId == exit.EmployeeId && a.ReturnedAt == null);
                    if (hasUnreturnedAssets) continue;

                    // Skip if clearance not complete
                    if (!exit.KnowledgeTransferred || !exit.NoDuesCleared || !exit.TasksCompleted)
                        continue;

                    exit.AssetsReturned = true;
                    exit.AssetsReturnedDate ??= DateTime.UtcNow;

                    await AutoRevokeAccessAsync(exit.Employee.UserId);
                    exit.AccessRevoked = true;
                    exit.AccessRevokedDate = DateTime.UtcNow;
                    exit.NoDuesClearedDate ??= DateTime.UtcNow;

                    exit.Employee.Status = "Inactive";
                    exit.Employee.LastWorkingDay = exit.LastWorkingDay;
                    exit.Employee.ExitReason = exit.Reason;

                    exit.Status = ExitStatus.Completed;
                    exit.CompletedAt = DateTime.UtcNow;

                    await _db.SaveChangesAsync();
                    completedCount++;

                    await _notificationService.CreateOrgAdminNotificationsAsync(
                        exit.OrganizationId,
                        "Exit Auto-Completed",
                        $"{exit.Employee.FirstName} {exit.Employee.LastName}'s exit was auto-completed as their last working day has passed.",
                        "/admin/exit");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ExitBG] Error auto-completing exit {exit.ExitId}: {ex.Message}");
                }
            }

            return completedCount;
        }

        // ─────────────────────────────────────────────
        // HELPER UTILITIES
        // ─────────────────────────────────────────────

        public async Task<List<string>> GetUserRolesAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return [];
            return (await _userManager.GetRolesAsync(user)).ToList();
        }

        // ─────────────────────────────────────────────
        // PRIVATE HELPERS
        // ─────────────────────────────────────────────

        /// <summary>
        /// Loads an exit request and validates the current user is authorised to act on it.
        /// OrgAdmin can act on everything in their org.
        /// HR can only act on employees (non-HR) in their own department.
        /// </summary>
        private async Task<(EmployeeExit exit, Employee? actor)> LoadAndAuthorizeExitActionAsync(Guid exitId)
        {
            var currentUser = await _currentUser.GetUserAsync()
                ?? throw new InvalidOperationException("Unauthorized");

            var roles = await _userManager.GetRolesAsync(currentUser);
            var isOrgAdmin = roles.Contains("OrgAdmin");
            var isHr = roles.Contains("HR");

            var exit = await _db.EmployeeExits
                .Include(x => x.Employee)
                .FirstOrDefaultAsync(x => x.ExitId == exitId)
                ?? throw new InvalidOperationException("Exit request not found.");

            if (exit.OrganizationId != currentUser.OrganizationId)
                throw new UnauthorizedAccessException("You do not have permission to act on this exit request.");

            var currentEmployee = await _db.Employees
                .FirstOrDefaultAsync(e => e.UserId == currentUser.Id);

            if (!isOrgAdmin)
            {
                // HR scope checks
                if (!isHr)
                    throw new UnauthorizedAccessException("Insufficient permissions.");

                if (currentEmployee == null || exit.Employee.DepartmentId != currentEmployee.DepartmentId)
                    throw new UnauthorizedAccessException("HR can only manage exits within their own department.");

                // HR cannot approve/reject HR users' exits — only OrgAdmin can
                var exiteeRoles = await _userManager.GetRolesAsync(
                    await _userManager.FindByIdAsync(exit.Employee.UserId) ??
                    throw new InvalidOperationException("Exit employee user not found."));

                if (exiteeRoles.Contains("HR"))
                    throw new UnauthorizedAccessException(
                        "HR exits can only be managed by OrgAdmin.");

                // HR cannot act on their own exit
                if (currentEmployee.EmployeeId == exit.EmployeeId)
                    throw new UnauthorizedAccessException("You cannot manage your own exit request.");
            }

            return (exit, currentEmployee);
        }

        private async Task<HashSet<string>> GetHrUserIdsInOrgAsync(Guid organizationId)
        {
            var hrUserIds = await (
                from u in _db.Users
                join ur in _db.UserRoles on u.Id equals ur.UserId
                join r in _db.Roles on ur.RoleId equals r.Id
                where u.OrganizationId == organizationId && r.Name == "HR"
                select u.Id
            ).ToListAsync();

            return [.. hrUserIds];
        }

        private async Task AutoCheckAssetsAsync(Guid employeeId, Guid exitId)
        {
            var assignedAssets = await _db.AssetAssignments
                .Include(a => a.Asset)
                .Where(a => a.EmployeeId == employeeId && a.ReturnedAt == null)
                .ToListAsync();

            var exit = await _db.EmployeeExits.FindAsync(exitId);
            if (exit == null) return;

            if (assignedAssets.Count != 0)
            {
                exit.AssetsReturned = false;
                exit.ClearanceRemarks =
                    $"Pending return of {assignedAssets.Count} asset(s): " +
                    string.Join(", ", assignedAssets.Select(a => a.Asset?.Name ?? "Unknown"));

                await _notificationService.CreateOrgAdminNotificationsAsync(
                    exit.OrganizationId,
                    "Assets Pending Return",
                    $"Employee has {assignedAssets.Count} asset(s) assigned. Please collect before completing exit.",
                    $"/admin/exit?exitId={exitId}");
            }
            else
            {
                exit.AssetsReturned = true;
                exit.AssetsReturnedDate = DateTime.UtcNow;
            }

            bool hasPendingTasks = await HasPendingTasksAsync(employeeId);
            exit.TasksCompleted = !hasPendingTasks;
            if (!hasPendingTasks) exit.TasksCompletedDate = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        private async Task AutoReturnAssetsAsync(Guid employeeId)
        {
            var assignments = await _db.AssetAssignments
                .Where(a => a.EmployeeId == employeeId && a.ReturnedAt == null)
                .ToListAsync();

            foreach (var assignment in assignments)
            {
                assignment.ReturnedAt = DateTime.UtcNow;
                var asset = await _db.Assets.FindAsync(assignment.AssetId);
                if (asset != null)
                {
                    asset.Status = AssetStatus.Available;
                    asset.CurrentEmployeeId = null; // Clear the current employee
                    asset.UpdatedAt = DateTime.UtcNow;
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

        private async Task<bool> HasPendingTasksAsync(Guid employeeId)
        {
            return await _db.Tasks.AnyAsync(t =>
                t.AssignedTo == employeeId &&
                t.Status != "Completed");
        }

        private async Task<int> GetMinimumNoticePeriodAsync(Guid organizationId)
        {
            // Reserved for configurable notice period per org — hardcoded to 15 days for now
            await Task.CompletedTask;
            return 15;
        }

        private async Task NotifyExitSubmittedAsync(EmployeeExit exit, Employee employee)
        {
            var pendingAssets = await _db.AssetAssignments.CountAsync(a =>
                a.EmployeeId == employee.EmployeeId && a.ReturnedAt == null);

            var assetNote = pendingAssets > 0
                ? $"\n\nNote: Employee has {pendingAssets} asset(s) assigned that need to be returned."
                : "";

            var submitterRoles = await _userManager.GetRolesAsync(
                await _userManager.FindByIdAsync(employee.UserId)!);

            bool submitterIsHr = submitterRoles.Contains("HR");

            if (submitterIsHr)
            {
                // HR resignation → notify OrgAdmin only
                await _notificationService.CreateOrgAdminNotificationsAsync(
                    employee.OrganizationId,
                    "HR Exit Request",
                    $"HR member {employee.FirstName} {employee.LastName} has submitted their resignation. " +
                    $"Last working day: {exit.LastWorkingDay:dd MMM yyyy}.{assetNote}",
                    "/admin/exit");
            }
            else
            {
                // Employee resignation → notify department HR + OrgAdmin
                var deptHrList = await (
                    from e in _db.Employees
                    join ur in _db.UserRoles on e.UserId equals ur.UserId
                    join r in _db.Roles on ur.RoleId equals r.Id
                    where e.OrganizationId == employee.OrganizationId
                          && e.DepartmentId == employee.DepartmentId
                          && r.Name == "HR"
                    select e
                ).ToListAsync();

                foreach (var hr in deptHrList)
                {
                    await _notificationService.CreateNotificationAsync(
                        hr.UserId,
                        "New Exit Request",
                        $"{employee.FirstName} {employee.LastName} has submitted resignation. " +
                        $"Last working day: {exit.LastWorkingDay:dd MMM yyyy}.{assetNote}",
                        "/admin/exit");
                }

                await _notificationService.CreateOrgAdminNotificationsAsync(
                    employee.OrganizationId,
                    "New Exit Request",
                    $"{employee.FirstName} {employee.LastName} has submitted resignation. " +
                    $"Last working day: {exit.LastWorkingDay:dd MMM yyyy}.{assetNote}",
                    "/admin/exit");
            }
        }
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