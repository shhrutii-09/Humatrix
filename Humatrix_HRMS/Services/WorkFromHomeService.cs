using Humatrix_HRMS.Data;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Infrastructure.Services;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Humatrix_HRMS.Infrastructure.Constants;

namespace Humatrix_HRMS.Services
{
    public class WorkFromHomeService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;
        private readonly HRPolicyValidationService _policy;
        private readonly NotificationService _notificationService;
        private readonly ApprovalWorkflowService _approvalWorkflowService;
        private readonly NotificationEngine _notificationEngine;
        private readonly UserManager<ApplicationUser> _userManager;

        public WorkFromHomeService(
       ApplicationDbContext context,
       CurrentUserService currentUser,
       HRPolicyValidationService policy,
       NotificationService notificationService,
       UserManager<ApplicationUser> userManager,
       ApprovalWorkflowService approvalWorkflowService,
       NotificationEngine notificationEngine)
        {
            _context = context;
            _currentUser = currentUser;
            _policy = policy;
            _notificationService = notificationService;

            _userManager = userManager;
            _approvalWorkflowService = approvalWorkflowService;
            _notificationEngine = notificationEngine;
        }

        // ─────────────────────────────────────
        // APPLY
        // ─────────────────────────────────────
        public async Task ApplyAsync(WorkFromHomeRequest request)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .Include(e => e.Department)
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee not found");

            // GET USER ROLES
            var userRoles = await _userManager.GetRolesAsync(user);

            var isHr = userRoles.Contains("HR");
            var isOrgAdmin = userRoles.Contains("OrgAdmin");

            var requesterRole =
                isOrgAdmin ? "OrgAdmin" :
                isHr ? "HR" :
                "Employee";

            var orgId = employee.OrganizationId;

            var orgToday = await _policy.GetOrgTodayAsync(orgId);

            // PAST DATE CHECK
            if (request.Date.Date < orgToday)
                throw new Exception("Cannot apply for past date");

            // CENTRAL POLICY CHECK
            await _policy.AssertEmployeeCanActAsync(
                orgId,
                employee.EmployeeId,
                request.Date,
                "WFH");

            // DUPLICATE WFH CHECK
            var wfhExists = await _context.WorkFromHomeRequests.AnyAsync(w =>
                w.EmployeeId == employee.EmployeeId &&
                w.Date.Date == request.Date.Date &&
                w.Status != "Rejected" &&
                w.Status != "Cancelled");

            if (wfhExists)
                throw new Exception("WFH already applied for this date");

            // ATTENDANCE ALREADY EXISTS
            var attendanceExists = await _context.Attendances.AnyAsync(a =>
                a.EmployeeId == employee.EmployeeId &&
                a.WorkDate == request.Date);

            if (attendanceExists)
                throw new Exception("Attendance already marked for this date");

            // SAVE REQUEST
            request.EmployeeId = employee.EmployeeId;
            request.Status = "Pending";
            request.AppliedAt = DateTime.UtcNow;

            request.RequestedByRole = requesterRole;

            request.ApprovalLevel =
                requesterRole == "HR"
                    ? "OrgAdmin"
                    : "HR";

            // SAFETY FOR OLD DATA CONSISTENCY
            if (string.IsNullOrWhiteSpace(request.RequestedByRole))
            {
                request.RequestedByRole = "Employee";
            }

            if (string.IsNullOrWhiteSpace(request.ApprovalLevel))
            {
                request.ApprovalLevel = "HR";
            }

            using var tx = await _context.Database.BeginTransactionAsync();

            _context.WorkFromHomeRequests.Add(request);

            await _context.SaveChangesAsync();

            // ==========================================
            // CREATE APPROVAL WORKFLOW
            // ==========================================

            await _approvalWorkflowService.SubmitAsync(
                _context,
                ApprovalRequestTypes.WorkFromHome,
                request.Id,
                employee.OrganizationId,
                employee.EmployeeId,
                applicantRole: requesterRole);

            await tx.CommitAsync();

            // ==========================================
            // SEND NOTIFICATIONS
            // ==========================================

            await _notificationEngine.SendWfhAppliedAsync(
     employeeFullName: $"{employee.FirstName} {employee.LastName}",
     date: request.Date,
     requestId: request.Id,
     organizationId: employee.OrganizationId,
     departmentId: employee.DepartmentId,
     applicantRole: request.RequestedByRole,
     actorUserId: employee.UserId
 );

            //// ==========================================
            //// CREATE ORG ADMIN NOTIFICATIONS
            //// ==========================================

            //if (requesterRole == "HR")
            //{
            //    await _notificationService.CreateOrgAdminNotificationsAsync(
            //        employee.OrganizationId,
            //        "New WFH Request",
            //        $"{employee.FirstName} {employee.LastName} requested WFH on {request.Date:dd MMM yyyy}",
            //        "/hr/wfh");
            //}

            await _notificationService.BroadcastOrgDashboardRefreshAsync(
  employee.OrganizationId);

            await _notificationService.BroadcastHrDashboardRefreshAsync(
                employee.OrganizationId,
                employee.DepartmentId);
        }
      
        // ─────────────────────────────────────
        // APPROVE / REJECT
        // ─────────────────────────────────────
        public async Task UpdateStatusAsync(
     Guid id,
     string status,
     string? reason = null)
        {
            if (status != "Approved" && status != "Rejected")
                throw new Exception("Invalid status");

            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var req = await _context.WorkFromHomeRequests
                .Include(r => r.Employee)
                .ThenInclude(e => e.Department)
                .FirstOrDefaultAsync(r => r.Id == id)
                ?? throw new Exception("Request not found");

            // ORG SECURITY
            if (req.Employee.OrganizationId != user.OrganizationId)
                throw new Exception("Unauthorized");

            // CURRENT USER ROLES
            var userRoles = await _userManager.GetRolesAsync(user);

            var isOrgAdmin = userRoles.Contains("OrgAdmin");
            var isHR = userRoles.Contains("HR");

            // ==========================================
            // HR RESTRICTIONS
            // ==========================================
            if (isHR && !isOrgAdmin)
            {
                var currentEmployee = await _context.Employees
                    .FirstOrDefaultAsync(e => e.UserId == user.Id)
                    ?? throw new Exception("HR employee profile not found");

                // HR CANNOT APPROVE HR REQUESTS
                if (req.RequestedByRole != "Employee")
                    throw new Exception("HR cannot process HR requests");

                // HR ONLY SAME DEPARTMENT
                if (req.Employee.DepartmentId != currentEmployee.DepartmentId)
                    throw new Exception("Unauthorized department access");
            }

            // ALREADY PROCESSED
            if (req.Status != "Pending")
                throw new Exception("Already processed");

            using var tx = await _context.Database.BeginTransactionAsync();

            // ==========================================
            // APPROVE
            // ==========================================
            if (status == "Approved")
            {
                var exists = await _context.Attendances.AnyAsync(a =>
                    a.EmployeeId == req.EmployeeId &&
                    a.WorkDate == req.Date);

                if (exists)
                    throw new Exception("Attendance already exists");

                _context.Attendances.Add(new Attendance
                {
                    AttendanceId = Guid.NewGuid(),
                    EmployeeId = req.EmployeeId,
                    UserId = req.Employee.UserId,
                    OrganizationId = req.Employee.OrganizationId,
                    WorkDate = req.Date,
                    IsPresent = true,
                    Status = AttendanceStatuses.WorkFromHome,
                    IsManual = true
                });

                req.Status = "Approved";
                req.ApprovedBy = Guid.Parse(user.Id);
                await _notificationEngine.SendWfhApprovedAsync(
    employeeUserId: req.Employee.UserId,
    date: req.Date,
    requestId: req.Id,
    organizationId: req.Employee.OrganizationId,
    actorUserId: user.Id
);
                //await _notificationService.CreateNotificationAsync(
                //    req.Employee.UserId,
                //    "WFH Approved",
                //    $"Your Work From Home request for {req.Date:dd MMM yyyy} was approved",
                //    "/employee/wfh");
            }

            // ==========================================
            // REJECT
            // ==========================================
            else
            {
                req.Status = "Rejected";
                req.RejectionReason = reason;
                await _notificationEngine.SendWfhRejectedAsync(
    employeeUserId: req.Employee.UserId,
    date: req.Date,
    requestId: req.Id,
    organizationId: req.Employee.OrganizationId,
    actorUserId: user.Id
);
                //await _notificationService.CreateNotificationAsync(
                //    req.Employee.UserId,
                //    "WFH Rejected",
                //    $"Your Work From Home request for {req.Date:dd MMM yyyy} was rejected",
                //    "/employee/wfh");
            }

            req.ReviewedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await tx.CommitAsync();

            // Refresh OrgAdmin dashboards
            await _notificationService.BroadcastOrgDashboardRefreshAsync(
                req.Employee.OrganizationId);

            // Refresh HR dashboards
            await _notificationService.BroadcastHrDashboardRefreshAsync(
                req.Employee.OrganizationId,
                req.Employee.DepartmentId);
        }

        // ─────────────────────────────────────
        // CANCEL
        // ─────────────────────────────────────
        public async Task CancelAsync(Guid id)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee not found");

            var req = await _context.WorkFromHomeRequests
                .FirstOrDefaultAsync(r =>
                    r.Id == id &&
                    r.EmployeeId == employee.EmployeeId)
                ?? throw new Exception("Request not found");

            if (req.Status != "Pending")
                throw new Exception("Only pending request can be cancelled");

            req.Status = "Cancelled";

            await _context.SaveChangesAsync();
        }

        // ─────────────────────────────────────
        // MY REQUESTS
        // ─────────────────────────────────────
        public async Task<List<WorkFromHomeRequest>> GetMyAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee not found");

            return await _context.WorkFromHomeRequests
                .Where(r => r.EmployeeId == employee.EmployeeId)
                .OrderByDescending(r => r.AppliedAt)
                .AsNoTracking()
                .ToListAsync();
        }

        // ─────────────────────────────────────
        // HR / ORGADMIN VIEW
        // ─────────────────────────────────────
        // ─────────────────────────────────────
        // HR / ORGADMIN VIEW
        // ─────────────────────────────────────
        // ─────────────────────────────────────
        // HR / ORGADMIN VIEW
        // ─────────────────────────────────────
        public async Task<List<WorkFromHomeRequest>> GetAllAsync(string? status = null)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            // GET USER ROLES
            var userRoles = await _context.UserRoles
                .Where(x => x.UserId == user.Id)
                .Join(
                    _context.Roles,
                    ur => ur.RoleId,
                    r => r.Id,
                    (ur, r) => r.Name)
                .ToListAsync();

            var isOrgAdmin = userRoles.Contains("OrgAdmin");
            var isHR = userRoles.Contains("HR");

            // BASE QUERY
            var query = _context.WorkFromHomeRequests
                .Include(r => r.Employee)
                .ThenInclude(e => e.Department)
                .Where(r => r.Employee.OrganizationId == user.OrganizationId);

            // ==========================================
            // HR VIEW
            // ==========================================
            if (isHR && !isOrgAdmin)
            {
                var currentEmployee = await _context.Employees
                    .FirstOrDefaultAsync(e => e.UserId == user.Id)
                    ?? throw new Exception("HR employee profile not found");

                query = query.Where(r =>

                    // SAME DEPARTMENT
                    r.Employee.DepartmentId == currentEmployee.DepartmentId

                    // HR SHOULD NOT SEE OWN REQUESTS
                    && r.EmployeeId != currentEmployee.EmployeeId

                    // ONLY EMPLOYEE REQUESTS
                    &&
                    (
                        r.RequestedByRole == "Employee"
                        ||
                        string.IsNullOrWhiteSpace(r.RequestedByRole)
                    )
                );
            }

            // ==========================================
            // ORGADMIN VIEW
            // ==========================================
            else if (isOrgAdmin)
            {
                // ORGADMIN CAN SEE ALL
            }

            // STATUS FILTER
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(r => r.Status == status);
            }

            return await query
                .OrderByDescending(r => r.AppliedAt)
                .AsNoTracking()
                .ToListAsync();
        }

        private TimeZoneInfo GetOrgTimezone(string timeZoneId)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch
            {
                return TimeZoneInfo.Utc;
            }
        }
    }
}