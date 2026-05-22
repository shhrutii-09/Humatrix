using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class LeaveService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;

        private readonly HRPolicyValidationService _policy;

        private readonly NotificationService _notificationService;

        //public LeaveService(
        //    ApplicationDbContext context,
        //    CurrentUserService currentUser,
        //    HRPolicyValidationService policy,
        //    NotificationService notificationService)
        //{
        //    _context = context;
        //    _currentUser = currentUser;
        //    _policy = policy;
        //    _notificationService = notificationService;
        //}

        // ADD to LeaveService fields:
        private readonly UserManager<ApplicationUser> _userManager;

        // UPDATE constructor signature:
        public LeaveService(
            ApplicationDbContext context,
            CurrentUserService currentUser,
            HRPolicyValidationService policy,
            NotificationService notificationService,
            UserManager<ApplicationUser> userManager)   // ← ADD THIS
        {
            _context = context;
            _currentUser = currentUser;
            _policy = policy;
            _notificationService = notificationService;
            _userManager = userManager;   // ← ADD THIS
        }


        // =========================
        // APPLY LEAVE
        // =========================
        public async Task ApplyLeaveAsync(LeaveRequest request)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee not found");

            var orgId = employee.OrganizationId;

            // ── FIX: use org-timezone today, not UTC ──────────────────────────────────
            var today = await _policy.GetOrgTodayAsync(orgId);

            if (request.FromDate.Date < today)
                throw new Exception("Cannot apply leave for past dates");

            if (request.FromDate > request.ToDate)
                throw new Exception("Invalid date range");

            var leaveType = await _context.LeaveTypes
                .FirstOrDefaultAsync(lt => lt.LeaveTypeId == request.LeaveTypeId)
                ?? throw new Exception("Leave type not found");

            if (!leaveType.IsActive)
                throw new Exception("This leave type is no longer available");

            if (leaveType.MinNoticeRequiredDays > 0)
            {
                var daysUntilLeave = (request.FromDate.Date - today).Days;
                if (daysUntilLeave < leaveType.MinNoticeRequiredDays)
                    throw new Exception(
                        $"This leave type requires at least {leaveType.MinNoticeRequiredDays} day(s) advance notice");
            }

            if (request.IsHalfDay && request.FromDate.Date != request.ToDate.Date)
                throw new Exception("Half-day leave can only be for a single day");

            if (request.IsHalfDay && !leaveType.AllowHalfDay)
                throw new Exception("This leave type does not support half-day");

            var workWeek = await _policy.GetWorkWeekAsync(orgId);

            var holidays = await _context.Holidays
                .Where(h =>
                    h.OrganizationId == orgId &&
                    !h.IsOptional &&
                    h.Date.Date >= request.FromDate.Date &&
                    h.Date.Date <= request.ToDate.Date)
                .Select(h => h.Date.Date)
                .ToListAsync();

            decimal totalDays = request.IsHalfDay
                ? 0.5m
                : CountWorkingDays(request.FromDate.Date, request.ToDate.Date, holidays, workWeek);

            if (totalDays == 0)
                throw new Exception("No working days in the selected range (all days are holidays or non-working)");

            // ── Leave overlap (existing leave requests) ───────────────────────────────
            await _policy.AssertNoLeaveConflictAsync(employee.EmployeeId, request.FromDate, request.ToDate);

            // ── WFH conflict across the leave range ───────────────────────────────────
            // Check every working day in range for an existing WFH request
            for (var d = request.FromDate.Date; d <= request.ToDate.Date; d = d.AddDays(1))
            {
                if (!DateHelper.IsWorkingDay(d, workWeek)) continue;
                if (holidays.Contains(d)) continue;

                var wfhConflict = await _policy.GetWfhConflictAsync(employee.EmployeeId, d);
                if (wfhConflict != null)
                    throw new Exception(
                        $"Conflict: A WFH request ({wfhConflict.Status}) exists for {d:dd MMM}. " +
                        $"Cancel the WFH request before applying leave.");
            }

            // ── Balance check ─────────────────────────────────────────────────────────
            var balance = await GetOrCreateBalanceAsync(employee.EmployeeId, request.LeaveTypeId);
            decimal effectiveRemaining = balance.Remaining + balance.CarriedForward;

            if (effectiveRemaining < totalDays)
                throw new Exception(
                    $"Insufficient leave balance. Available: {effectiveRemaining} day(s), Requested: {totalDays} day(s)");

            //using var tx = await _context.Database.BeginTransactionAsync();

            //balance.Pending += totalDays;

            //request.EmployeeId = employee.EmployeeId;
            //request.Employee = employee;
            //request.TotalDays = totalDays;
            //request.Status = "Pending";
            //request.AppliedAt = DateTime.UtcNow;


            // Resolve applicant role before entering transaction
            var applicantRoles = await _userManager.GetRolesAsync(user);
            bool applicantIsHR = applicantRoles.Contains("HR");

            using var tx = await _context.Database.BeginTransactionAsync();

            balance.Pending += totalDays;

            request.EmployeeId = employee.EmployeeId;
            request.Employee = employee;
            request.TotalDays = totalDays;
            request.Status = "Pending";
            request.AppliedAt = DateTime.UtcNow;
            request.ApplicantRole = applicantIsHR ? "HR" : "Employee";

            //_context.LeaveRequests.Add(request);
            //await _context.SaveChangesAsync();
            //await tx.CommitAsync();


            // =========================
            // NOTIFY HR
            // =========================

            //var hrUsers = await _context.Users
            //    .Where(u => u.OrganizationId == employee.OrganizationId)
            //    .ToListAsync();

            //foreach (var hr in hrUsers)
            //{
            //    var roles = await _context.UserRoles
            //        .Where(x => x.UserId == hr.Id)
            //        .Join(
            //            _context.Roles,
            //            ur => ur.RoleId,
            //            r => r.Id,
            //            (ur, r) => r.Name)
            //        .ToListAsync();

            //    if (roles.Contains("HR") || roles.Contains("Admin"))
            //    {
            //        await _notificationService.CreateNotificationAsync(
            //            hr.Id,

            //            "New Leave Request",

            //            $"{employee.FirstName} {employee.LastName} applied for leave",

            //            "/hr/leaves"
            //        );
            //    }
            //}

            // =========================
            // NOTIFY APPROVERS (role-aware, no N+1)
            // =========================

            // Determine who applied: Employee → notify HR; HR → notify OrgAdmin
            //var applicantRoles = await _userManager.GetRolesAsync(user);
            //bool applicantIsHR = applicantRoles.Contains("HR");

            //// Set ApplicantRole on the request for future reference
            //request.ApplicantRole = applicantIsHR ? "HR" : "Employee";

            //string[] targetRoles = applicantIsHR
            //    ? new[] { "OrgAdmin" }
            //    : new[] { "HR", "OrgAdmin" };

            //string notifyTitle = "New Leave Request";
            //string notifyMsg = $"{employee.FirstName} {employee.LastName} applied for {leaveType.Name}";
            //string notifyUrl = "/hr/leaves";

            //var approverIds = await GetRoleUserIdsAsync(employee.OrganizationId, targetRoles);

            //// Save the ApplicantRole change before notifications
            //await _context.SaveChangesAsync();

            //foreach (var approverId in approverIds)
            //{
            //    await _notificationService.CreateNotificationAsync(
            //        approverId, notifyTitle, notifyMsg, notifyUrl);
            //}
            _context.LeaveRequests.Add(request);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            // =========================
            // NOTIFY APPROVERS (role-aware, no N+1, outside tx is acceptable for notifications)
            // =========================
            // ==========================================
            // ROLE-BASED + DEPARTMENT-BASED NOTIFICATIONS
            // ==========================================

            // HR APPLIED → ONLY ORGADMINS
            if (applicantIsHR)
            {
                var orgAdminIds = await GetRoleUserIdsAsync(
                    employee.OrganizationId,
                    "OrgAdmin");

                foreach (var approverId in orgAdminIds)
                {
                    await _notificationService.CreateNotificationAsync(
                        approverId,
                        "New Leave Request",
                        $"{employee.FirstName} {employee.LastName} applied for {leaveType.Name}",
                        "/hr/leaves");
                }
            }

            // EMPLOYEE APPLIED → SAME DEPARTMENT HR + ORGADMIN
            else
            {
                // SAME DEPARTMENT HR
                var hrUsers = await _context.Employees
                    .Where(e =>
                        e.OrganizationId == employee.OrganizationId &&
                        e.DepartmentId == employee.DepartmentId)
                    .Join(
                        _context.Users,
                        e => e.UserId,
                        u => u.Id,
                        (e, u) => new { e, u })
                    .ToListAsync();

                foreach (var item in hrUsers)
                {
                    var roles = await _userManager.GetRolesAsync(item.u);

                    if (roles.Contains("HR"))
                    {
                        await _notificationService.CreateNotificationAsync(
                            item.u.Id,
                            "New Leave Request",
                            $"{employee.FirstName} {employee.LastName} applied for {leaveType.Name}",
                            "/hr/leaves");
                    }
                }

                // ORGADMINS
                var orgAdminIds = await GetRoleUserIdsAsync(
                    employee.OrganizationId,
                    "OrgAdmin");

                foreach (var approverId in orgAdminIds)
                {
                    await _notificationService.CreateNotificationAsync(
                        approverId,
                        "New Leave Request",
                        $"{employee.FirstName} {employee.LastName} applied for {leaveType.Name}",
                        "/hr/leaves");
                }
            }
        }

        // =========================
        // APPROVE / REJECT
        // =========================
        public async Task UpdateStatusAsync(Guid leaveRequestId, string status, string? rejectionReason = null)
        {
            if (status != "Approved" && status != "Rejected")
                throw new Exception("Invalid status");

            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var leave = await _context.LeaveRequests
                .FirstOrDefaultAsync(l => l.LeaveRequestId == leaveRequestId)
                ?? throw new Exception("Leave request not found");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.EmployeeId == leave.EmployeeId)
                ?? throw new Exception("Employee not found");

            //if (employee.OrganizationId != user.OrganizationId)
            //    throw new Exception("Unauthorized");

            //if (leave.Status != "Pending")
            //    throw new Exception("Only pending requests can be reviewed");

            if (employee.OrganizationId != user.OrganizationId)
                throw new Exception("Unauthorized");

            // ── Role-based approval enforcement ──────────────────────────────────────────
            var reviewerRoles = await _userManager.GetRolesAsync(user);
            bool reviewerIsHR = reviewerRoles.Contains("HR");
            bool reviewerIsOrgAdmin = reviewerRoles.Contains("OrgAdmin");

            if (!reviewerIsHR && !reviewerIsOrgAdmin)
                throw new Exception("Unauthorized: Only HR or OrgAdmin can review leave requests");

            // HR cannot approve their own leave or other HR's leave — only OrgAdmin can
            if (!string.IsNullOrEmpty(leave.ApplicantRole) && leave.ApplicantRole == "HR" && !reviewerIsOrgAdmin)
                throw new Exception("Unauthorized: Only OrgAdmin can approve HR leave requests");
            // ─────────────────────────────────────────────────────────────────────────────

            if (leave.Status != "Pending")
                throw new Exception("Only pending requests can be reviewed");

            // ✅ Wrap everything in a transaction
            using var tx = await _context.Database.BeginTransactionAsync();

            var balance = await GetOrCreateBalanceAsync(leave.EmployeeId, leave.LeaveTypeId);

            // Release the pending hold
            balance.Pending = Math.Max(0, balance.Pending - leave.TotalDays);

            if (status == "Approved")
            {
                decimal toDeduct = leave.TotalDays;

                if (balance.CarriedForward > 0)
                {
                    var fromCarry = Math.Min(balance.CarriedForward, toDeduct);
                    balance.CarriedForward -= fromCarry;
                    toDeduct -= fromCarry;
                }

                balance.Used += toDeduct;

                leave.Status = "Approved";
                leave.ApprovedBy = Guid.Parse(user.Id);

                var workWeek = await _context.WorkWeeks
                    .FirstOrDefaultAsync(w => w.OrganizationId == employee.OrganizationId)
                    ?? throw new Exception("Work week not configured");

                var dates = Enumerable.Range(0, (leave.ToDate.Date - leave.FromDate.Date).Days + 1)
                    .Select(d => leave.FromDate.Date.AddDays(d))
                    .ToList();

                // ✅ Load holidays and existing attendance records once — no N+1
                var holidayDates = await _context.Holidays
                    .Where(h =>
                        h.OrganizationId == employee.OrganizationId &&
                        !h.IsOptional &&
                        h.Date.Date >= leave.FromDate.Date &&
                        h.Date.Date <= leave.ToDate.Date)
                    .Select(h => h.Date.Date)
                    .ToListAsync();

                var existingAttendanceDates = await _context.Attendances
                    .Where(a =>
                        a.EmployeeId == leave.EmployeeId &&
                        a.WorkDate >= leave.FromDate.Date &&
                        a.WorkDate <= leave.ToDate.Date)
                    .Select(a => a.WorkDate)
                    .ToListAsync();

                foreach (var date in dates)
                {
                    if (!DateHelper.IsWorkingDay(date, workWeek)) continue;
                    if (holidayDates.Contains(date)) continue;
                    //if (existingAttendanceDates.Contains(date)) continue;
                    if (existingAttendanceDates.Any(d => d.Date == date)) continue;

                    _context.Attendances.Add(new Attendance
                    {
                        AttendanceId = Guid.NewGuid(),
                        UserId = employee.UserId,
                        EmployeeId = employee.EmployeeId,
                        OrganizationId = employee.OrganizationId,
                        WorkDate = date,
                        IsPresent = leave.IsHalfDay && date == leave.FromDate.Date,
                        Status = leave.IsHalfDay && date == leave.FromDate.Date
                            ? AttendanceStatuses.HalfDayLeave
                            : AttendanceStatuses.OnLeave,
                        IsManual = true
                    });
                }

                // =========================
                // NOTIFY EMPLOYEE APPROVED
                // =========================

                if (!string.IsNullOrEmpty(employee.UserId))
                {
                    await _notificationService.CreateNotificationAsync(
                        employee.UserId,

                        "Leave Approved",

                        $"Your leave request from {leave.FromDate:dd MMM yyyy} to {leave.ToDate:dd MMM yyyy} was approved",

                        "/employee/leaves"
                    );
                }

            }
            else
            {
                leave.Status = "Rejected";
                leave.RejectionReason = rejectionReason;

                // =========================
                // NOTIFY EMPLOYEE REJECTED

                // =========================

                if (!string.IsNullOrEmpty(employee.UserId))
                {
                    await _notificationService.CreateNotificationAsync(
                        employee.UserId,

                        "Leave Rejected",

                        $"Your leave request from {leave.FromDate:dd MMM yyyy} to {leave.ToDate:dd MMM yyyy} was rejected",

                        "/employee/leaves"
                    );
                }

            }

            leave.ReviewedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // =========================
        // CANCEL
        // =========================
        public async Task CancelRequestAsync(Guid leaveRequestId)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee not found");

            var leave = await _context.LeaveRequests
                .FirstOrDefaultAsync(l =>
                    l.LeaveRequestId == leaveRequestId &&
                    l.EmployeeId == employee.EmployeeId)
                ?? throw new Exception("Leave request not found");

            //if (leave.Status != "Pending")
            //    throw new Exception("Only pending requests can be cancelled");
            // Replace: if (leave.Status != "Pending")
            // With this robust check:
            if (!string.Equals(leave.Status?.Trim(), "Pending", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Only pending requests can be cancelled. Current status in DB: '{leave.Status}'");
            }
            var balance = await GetOrCreateBalanceAsync(employee.EmployeeId, leave.LeaveTypeId);
            balance.Pending = Math.Max(0, balance.Pending - leave.TotalDays);

            leave.Status = "Cancelled";
            await _context.SaveChangesAsync();

            // =========================
            // NOTIFY HR
            // =========================

            //var hrUsers = await _context.Users
            //    .Where(u => u.OrganizationId == employee.OrganizationId)
            //    .ToListAsync();

            //foreach (var hr in hrUsers)
            //{
            //    var roles = await _context.UserRoles
            //        .Where(x => x.UserId == hr.Id)
            //        .Join(
            //            _context.Roles,
            //            ur => ur.RoleId,
            //            r => r.Id,
            //            (ur, r) => r.Name)
            //        .ToListAsync();

            //    if (roles.Contains("HR") || roles.Contains("Admin"))
            //    {
            //        await _notificationService.CreateNotificationAsync(
            //            hr.Id,

            //            "Leave Cancelled",

            //            $"{employee.FirstName} {employee.LastName} cancelled leave request",

            //            "/hr/leaves"
            //        );
            //    }
            //}
            // Use the role-aware helper — no N+1
            var cancellerRoles = await _userManager.GetRolesAsync(user);
            bool cancellerIsHR = cancellerRoles.Contains("HR");

            //string[] targetRoles = cancellerIsHR
            //    ? new[] { "OrgAdmin" }
            //    : new[] { "HR", "OrgAdmin" };

            //var approverIds = await GetRoleUserIdsAsync(employee.OrganizationId, targetRoles);

            //foreach (var approverId in approverIds)
            //{
            //    await _notificationService.CreateNotificationAsync(
            //        approverId,
            //        "Leave Cancelled",
            //        $"{employee.FirstName} {employee.LastName} cancelled their leave request",
            //        "/hr/leaves");
            //}

            // ==========================================
            // ROLE + DEPARTMENT BASED CANCEL NOTIFICATION
            // ==========================================

            // HR CANCELLED → ONLY ORGADMIN
            if (cancellerIsHR)
            {
                var orgAdminIds = await GetRoleUserIdsAsync(
                    employee.OrganizationId,
                    "OrgAdmin");

                foreach (var approverId in orgAdminIds)
                {
                    await _notificationService.CreateNotificationAsync(
                        approverId,
                        "Leave Cancelled",
                        $"{employee.FirstName} {employee.LastName} cancelled their leave request",
                        "/hr/leaves");
                }
            }

            // EMPLOYEE CANCELLED → SAME DEPARTMENT HR + ORGADMIN
            else
            {
                var hrUsers = await _context.Employees
                    .Where(e =>
                        e.OrganizationId == employee.OrganizationId &&
                        e.DepartmentId == employee.DepartmentId)
                    .Join(
                        _context.Users,
                        e => e.UserId,
                        u => u.Id,
                        (e, u) => new { e, u })
                    .ToListAsync();

                foreach (var item in hrUsers)
                {
                    var roles = await _userManager.GetRolesAsync(item.u);

                    if (roles.Contains("HR"))
                    {
                        await _notificationService.CreateNotificationAsync(
                            item.u.Id,
                            "Leave Cancelled",
                            $"{employee.FirstName} {employee.LastName} cancelled their leave request",
                            "/hr/leaves");
                    }
                }

                var orgAdminIds = await GetRoleUserIdsAsync(
                    employee.OrganizationId,
                    "OrgAdmin");

                foreach (var approverId in orgAdminIds)
                {
                    await _notificationService.CreateNotificationAsync(
                        approverId,
                        "Leave Cancelled",
                        $"{employee.FirstName} {employee.LastName} cancelled their leave request",
                        "/hr/leaves");
                }
            }

        }

        // =========================
        // GET MY LEAVES
        // =========================
        public async Task<List<LeaveRequestDto>> GetMyLeavesAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee not found");

            return await _context.LeaveRequests
                .AsNoTracking()
                .Include(l => l.Employee)  // ✅ needed for ToDto to access Employee name
                .Include(l => l.LeaveType)
                .Where(l => l.EmployeeId == employee.EmployeeId)
                .OrderByDescending(l => l.AppliedAt)
                .Select(l => ToDto(l, null))
                .ToListAsync();
        }

        // =========================
        // GET ALL (HR)
        // =========================
        // =========================
        // GET ALL (HR / OrgAdmin)
        // =========================
        public async Task<List<LeaveRequestDto>> GetAllForHRAsync(string? status = null)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var departments = await _context.Departments
                .AsNoTracking()
                .ToDictionaryAsync(d => d.DepartmentId, d => d.Name);

            // 1. Establish reviewer role credentials
            var reviewerRoles = await _userManager.GetRolesAsync(user);
            bool reviewerIsHR = reviewerRoles.Contains("HR");
            bool reviewerIsOrgAdmin = reviewerRoles.Contains("OrgAdmin");

            var query = _context.LeaveRequests
                .AsNoTracking()
                .Include(l => l.Employee)
                .Include(l => l.LeaveType)
                .Where(l => l.Employee.OrganizationId == user.OrganizationId);

            // 2. ENFORCE BOUNDARY: If the user is HR (and NOT an OrgAdmin), 
            // hide all requests where the applicant is an HR team member.
            //if (reviewerIsHR && !reviewerIsOrgAdmin)
            //{
            //    query = query.Where(l => l.ApplicantRole != "HR");
            //}
            if (reviewerIsHR && !reviewerIsOrgAdmin)
            {
                var currentHrEmployee = await _context.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.UserId == user.Id)
                    ?? throw new Exception("HR employee profile not found");

                query = query.Where(l =>

                    // SAME DEPARTMENT ONLY
                    l.Employee.DepartmentId == currentHrEmployee.DepartmentId

                    // HR CANNOT SEE HR REQUESTS
                    && l.ApplicantRole != "HR"
                );
            }

            if (!string.IsNullOrEmpty(status))
                query = query.Where(l => l.Status == status);

            var list = await query.OrderByDescending(l => l.AppliedAt).ToListAsync();

            return list.Select(l =>
            {
                string? dept = departments.ContainsKey(l.Employee.DepartmentId)
                    ? departments[l.Employee.DepartmentId]
                    : null;

                return ToDto(l, dept);
            }).ToList();
        }

        // =========================
        // GET MY BALANCES
        // =========================
        public async Task<List<LeaveBalanceDto>> GetMyBalancesAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee not found");

            var leaveTypes = await _context.LeaveTypes
                .AsNoTracking()
                .Where(lt => lt.OrganizationId == employee.OrganizationId && lt.IsActive)
                .ToListAsync();

            var balances = new List<LeaveBalanceDto>();

            foreach (var lt in leaveTypes)
            {
                var bal = await GetOrCreateBalanceAsync(employee.EmployeeId, lt.LeaveTypeId);
                balances.Add(new LeaveBalanceDto
                {
                    LeaveTypeName = lt.Name,
                    IsPaid = lt.IsPaid,
                    Allocated = bal.Allocated,
                    Used = bal.Used,
                    Pending = bal.Pending,
                    Remaining = bal.Remaining,
                    CarriedForward = bal.CarriedForward
                });
            }

            return balances;
        }

        // =========================
        // YEARLY BALANCE INIT
        // =========================
        public async Task InitialiseBalancesForYearAsync(Guid organizationId, int year)
        {
            var employees = await _context.Employees
                .Where(e => e.OrganizationId == organizationId && e.Status == "Active")
                .ToListAsync();

            var leaveTypes = await _context.LeaveTypes
                .Where(lt => lt.OrganizationId == organizationId && lt.IsActive)
                .ToListAsync();

            foreach (var employee in employees)
            {
                foreach (var lt in leaveTypes)
                {
                    var exists = await _context.LeaveBalances.AnyAsync(b =>
                        b.EmployeeId == employee.EmployeeId &&
                        b.LeaveTypeId == lt.LeaveTypeId &&
                        b.Year == year);

                    if (exists) continue;

                    decimal carryForward = 0;
                    if (lt.MaxCarryForwardDays > 0)
                    {
                        var prevBalance = await _context.LeaveBalances
                            .FirstOrDefaultAsync(b =>
                                b.EmployeeId == employee.EmployeeId &&
                                b.LeaveTypeId == lt.LeaveTypeId &&
                                b.Year == year - 1);

                        if (prevBalance != null)
                        {
                            carryForward = Math.Min(prevBalance.Remaining, lt.MaxCarryForwardDays);
                        }
                    }

                    _context.LeaveBalances.Add(new LeaveBalance
                    {
                        EmployeeId = employee.EmployeeId,
                        LeaveTypeId = lt.LeaveTypeId,
                        Year = year,
                        Allocated = lt.MaxDaysPerYear,
                        Used = 0,
                        Pending = 0,
                        CarriedForward = carryForward
                    });
                }
            }

            await _context.SaveChangesAsync();
        }

        // =========================
        // JOB STATUS
        // =========================
        public async Task<JobStatusDto> GetYearlyBalanceJobStatusAsync(Guid orgId)
        {
            var log = await _context.YearlyJobLogs
                .Where(x => x.OrganizationId == orgId && x.JobName == "LeaveBalanceInit")
                .OrderByDescending(x => x.ExecutedAt)
                .FirstOrDefaultAsync();

            if (log == null)
                return new JobStatusDto { JobName = "Leave Balance Initialization", Status = "Not Run" };

            return new JobStatusDto
            {
                JobName = "Leave Balance Initialization",
                LastRun = log.ExecutedAt,
                Status = "Success"
            };
        }

        // =========================
        // PRIVATE HELPERS
        // =========================
        //private async Task<LeaveBalance> GetOrCreateBalanceAsync(Guid employeeId, Guid leaveTypeId)
        //{
        //    int year = DateTime.UtcNow.Year;
        private async Task<LeaveBalance> GetOrCreateBalanceAsync(Guid employeeId, Guid leaveTypeId)
        {
            // Use org timezone to determine the current year (avoids UTC year-boundary issues)
            var employee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);

            int year = employee != null
                ? (await _policy.GetOrgTodayAsync(employee.OrganizationId)).Year
                : DateTime.UtcNow.Year;

            var balance = await _context.LeaveBalances
                .FirstOrDefaultAsync(b =>
                    b.EmployeeId == employeeId &&
                    b.LeaveTypeId == leaveTypeId &&
                    b.Year == year);

            if (balance != null) return balance;

            var lt = await _context.LeaveTypes.FindAsync(leaveTypeId)
                ?? throw new Exception("Leave type not found");

            balance = new LeaveBalance
            {
                EmployeeId = employeeId,
                LeaveTypeId = leaveTypeId,
                Year = year,
                Allocated = lt.MaxDaysPerYear,
                Used = 0,
                Pending = 0
            };

            _context.LeaveBalances.Add(balance);
            await _context.SaveChangesAsync();

            return balance;
        }

        private static decimal CountWorkingDays(
            DateTime from,
            DateTime to,
            List<DateTime> holidays,
            WorkWeek workWeek)
        {
            decimal count = 0;

            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (!DateHelper.IsWorkingDay(day, workWeek)) continue;
                if (holidays.Contains(day.Date)) continue;
                count++;
            }

            return count;
        }

        private static LeaveRequestDto ToDto(LeaveRequest l, string? dept) => new()
        {
            LeaveRequestId = l.LeaveRequestId,
            EmployeeName = l.Employee != null
                ? $"{l.Employee.FirstName} {l.Employee.LastName}"
                : "Unknown",
            Department = dept,
            LeaveTypeName = l.LeaveType?.Name ?? "",
            FromDate = l.FromDate,
            ToDate = l.ToDate,
            IsHalfDay = l.IsHalfDay,
            TotalDays = l.TotalDays,
            Reason = l.Reason,
            Status = l.Status,
            AppliedAt = l.AppliedAt,
            ReviewedAt = l.ReviewedAt,
            RejectionReason = l.RejectionReason
        };

        // UI compatibility method (DO NOT REMOVE)
        public async Task CancelLeaveRequestAsync(Guid leaveRequestId)
        {
            await CancelRequestAsync(leaveRequestId);
        }

        public async Task<string> ResolveDailyStatus(Guid employeeId, DateTime date)
        {
            // 1. Holiday
            //var isHoliday = await _context.Holidays.AnyAsync(h =>
            //    h.OrganizationId == _currentUser.OrganizationId &&
            //    h.Date.Date == date.Date &&
            //    !h.IsOptional);
            var user = await _currentUser.GetUserAsync()
    ?? throw new Exception("Unauthorized");

            var orgId = user.OrganizationId ?? Guid.Empty;

            var isHoliday = await _context.Holidays.AnyAsync(h =>
                h.OrganizationId == orgId &&
                h.Date.Date == date.Date &&
                !h.IsOptional);

            if (isHoliday)
                return "Holiday";

            // 2. Leave
            //var leave = await _context.LeaveRequests
            //    .FirstOrDefaultAsync(l =>
            //        l.EmployeeId == employeeId &&
            //        l.Status == "Approved" &&
            //        l.FromDate.Date <= date &&
            //        l.ToDate.Date >= date);

            var leave = await _context.LeaveRequests
    .Include(l => l.LeaveType)
    .FirstOrDefaultAsync(l =>
        l.EmployeeId == employeeId &&
        l.Status == "Approved" &&
        l.FromDate.Date <= date &&
        l.ToDate.Date >= date);

            //if (leave != null)
            //    return $"Leave ({leave.LeaveType})";

            if (leave != null)
                return $"Leave ({leave.LeaveType?.Name ?? "Unknown"})";

            // 3. WFH
            var wfh = await _context.WorkFromHomeRequests
                .FirstOrDefaultAsync(w =>
                    w.EmployeeId == employeeId &&
                    w.Status == "Approved" &&
                    w.Date.Date == date);

            if (wfh != null)
                return AttendanceStatuses.WorkFromHome;

            // 4. Attendance
            var att = await _context.Attendances
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employeeId &&
                    a.WorkDate == date);

            if (att != null)
                return att.Status;

            // 5. Absent
            return AttendanceStatuses.Absent;
        }


        // =========================
        // PRIVATE: GET HR/ORGADMIN USER IDs FOR ORG (no N+1)
        // =========================
        private async Task<List<string>> GetRoleUserIdsAsync(Guid organizationId, params string[] roles)
        {
            // Single join query — no per-user role loop
            return await _context.Users
                .Where(u => u.OrganizationId == organizationId)
                .Join(
                    _context.UserRoles,
                    u => u.Id,
                    ur => ur.UserId,
                    (u, ur) => new { u.Id, ur.RoleId })
                .Join(
                    _context.Roles,
                    x => x.RoleId,
                    r => r.Id,
                    (x, r) => new { x.Id, RoleName = r.Name })
                .Where(x => roles.Contains(x.RoleName))
                .Select(x => x.Id)
                .Distinct()
                .ToListAsync();
        }
    }
}