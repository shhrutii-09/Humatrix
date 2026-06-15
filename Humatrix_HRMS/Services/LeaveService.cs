using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Infrastructure.Constants;
using Humatrix_HRMS.Infrastructure.Services;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class LeaveService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;
        private readonly ApprovalWorkflowService _approvalWorkflowService;
        private readonly NotificationEngine _notificationEngine;
        private readonly HRPolicyValidationService _policy;
        private readonly NotificationService _notificationService;
        private readonly UserManager<ApplicationUser> _userManager;

        public LeaveService(
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

        public async Task<(string Name, string Role)> GetReviewerInfoAsync(Guid? userId)
        {
            if (!userId.HasValue) return ("-", "-");

            var user = await _userManager.FindByIdAsync(userId.Value.ToString());
            if (user == null) return ("System", "Admin");

            var roles = await _userManager.GetRolesAsync(user);
            string name = !string.IsNullOrWhiteSpace(user.FirstName)
                          ? $"{user.FirstName} {user.LastName}"
                          : (user.Email ?? "Org Admin");
            string role = roles.FirstOrDefault() ?? "Admin";

            return (Name: name, Role: role);
        }

        public async Task ApplyLeaveAsync(LeaveRequest request)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee not found");

            var orgId = employee.OrganizationId;
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

            await _policy.AssertNoLeaveConflictAsync(employee.EmployeeId, request.FromDate, request.ToDate);

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

            var balance = await GetOrCreateBalanceAsync(employee.EmployeeId, request.LeaveTypeId);
            decimal effectiveRemaining = balance.Remaining + balance.CarriedForward;

            if (effectiveRemaining < totalDays)
                throw new Exception(
                    $"Insufficient leave balance. Available: {effectiveRemaining} day(s), Requested: {totalDays} day(s)");

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

            _context.LeaveRequests.Add(request);

            await _context.SaveChangesAsync();

            await _approvalWorkflowService.SubmitAsync(
                _context,
                ApprovalRequestTypes.Leave,
                request.LeaveRequestId,
                employee.OrganizationId,
                employee.EmployeeId,
                applicantRole: request.ApplicantRole);

            await tx.CommitAsync();

            await _notificationEngine.SendLeaveAppliedAsync(
                employeeFullName: $"{employee.FirstName} {employee.LastName}",
                leaveTypeName: leaveType.Name,
                leaveRequestId: request.LeaveRequestId,
                organizationId: employee.OrganizationId,
                departmentId: employee.DepartmentId,
                applicantRole: request.ApplicantRole,
                actorUserId: employee.UserId);

            await _notificationService.CreateOrgAdminNotificationsAsync(
                employee.OrganizationId,
                "New Leave Request",
                $"{employee.FirstName} {employee.LastName} applied for {leaveType.Name}",
                "/hr/leaves");

            if (request.ApplicantRole == "HR")
            {
                await _notificationService.CreateOrgAdminNotificationsAsync(
                    employee.OrganizationId,
                    "New Leave Request",
                    $"{employee.FirstName} {employee.LastName} requested leave from {request.FromDate:dd MMM yyyy} to {request.ToDate:dd MMM yyyy}",
                    "/hr/leaves");
            }

            await _notificationService.BroadcastOrgDashboardRefreshAsync(employee.OrganizationId);
            await _notificationService.BroadcastHrDashboardRefreshAsync(employee.OrganizationId, employee.DepartmentId);
        }

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

            if (employee.OrganizationId != user.OrganizationId)
                throw new Exception("Unauthorized");

            var reviewerRoles = await _userManager.GetRolesAsync(user);
            bool reviewerIsHR = reviewerRoles.Contains("HR");
            bool reviewerIsOrgAdmin = reviewerRoles.Contains("OrgAdmin");

            if (!reviewerIsHR && !reviewerIsOrgAdmin)
                throw new Exception("Unauthorized: Only HR or OrgAdmin can review leave requests");

            if (!string.IsNullOrEmpty(leave.ApplicantRole) && leave.ApplicantRole == "HR" && !reviewerIsOrgAdmin)
                throw new Exception("Unauthorized: Only OrgAdmin can approve HR leave requests");

            if (leave.Status != "Pending")
                throw new Exception("Only pending requests can be reviewed");

            using var tx = await _context.Database.BeginTransactionAsync();

            var balance = await GetOrCreateBalanceAsync(leave.EmployeeId, leave.LeaveTypeId);
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
                leave.ApprovedBy = Guid.Parse(user.Id);

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

            await _notificationService.BroadcastOrgDashboardRefreshAsync(employee.OrganizationId);
            await _notificationService.BroadcastHrDashboardRefreshAsync(employee.OrganizationId, employee.DepartmentId);
        }

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

            if (!string.Equals(leave.Status?.Trim(), "Pending", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Only pending requests can be cancelled. Current status in DB: '{leave.Status}'");
            }
            var balance = await GetOrCreateBalanceAsync(employee.EmployeeId, leave.LeaveTypeId);
            balance.Pending = Math.Max(0, balance.Pending - leave.TotalDays);

            leave.Status = "Cancelled";
            await _context.SaveChangesAsync();

            var cancellerRoles = await _userManager.GetRolesAsync(user);
            bool cancellerIsHR = cancellerRoles.Contains("HR");

            if (cancellerIsHR)
            {
                var orgAdminIds = await GetRoleUserIdsAsync(employee.OrganizationId, "OrgAdmin");
                foreach (var approverId in orgAdminIds)
                {
                    await _notificationService.CreateNotificationAsync(
                        approverId,
                        "Leave Cancelled",
                        $"{employee.FirstName} {employee.LastName} cancelled their leave request",
                        "/hr/leaves");
                }
            }
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
                var orgAdminIds = await GetRoleUserIdsAsync(employee.OrganizationId, "OrgAdmin");
                foreach (var approverId in orgAdminIds)
                {
                    await _notificationService.CreateNotificationAsync(
                        approverId,
                        "Leave Cancelled",
                        $"{employee.FirstName} {employee.LastName} cancelled their leave request",
                        "/hr/leaves");
                }
            }
            await _notificationService.BroadcastOrgDashboardRefreshAsync(employee.OrganizationId);
            await _notificationService.BroadcastHrDashboardRefreshAsync(employee.OrganizationId, employee.DepartmentId);
        }

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
                .Include(l => l.Employee)
                .Include(l => l.LeaveType)
                .Where(l => l.EmployeeId == employee.EmployeeId)
                .OrderByDescending(l => l.AppliedAt)
                .Select(l => ToDto(l, null))
                .ToListAsync();
        }

        public async Task<List<LeaveRequestDto>> GetAllForHRAsync(string? status = null)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var departments = await _context.Departments
                .AsNoTracking()
                .ToDictionaryAsync(d => d.DepartmentId, d => d.Name);

            var reviewerRoles = await _userManager.GetRolesAsync(user);
            bool reviewerIsHR = reviewerRoles.Contains("HR");
            bool reviewerIsOrgAdmin = reviewerRoles.Contains("OrgAdmin");

            var query = _context.LeaveRequests
                .AsNoTracking()
                .Include(l => l.Employee)
                .Include(l => l.LeaveType)
                .Where(l => l.Employee.OrganizationId == user.OrganizationId);

            if (reviewerIsHR && !reviewerIsOrgAdmin)
            {
                var currentHrEmployee = await _context.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.UserId == user.Id)
                    ?? throw new Exception("HR employee profile not found");

                query = query.Where(l =>
                    l.Employee.DepartmentId == currentHrEmployee.DepartmentId
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

        private async Task<LeaveBalance> GetOrCreateBalanceAsync(Guid employeeId, Guid leaveTypeId)
        {
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
            RejectionReason = l.RejectionReason,
            ApprovedBy = l.ApprovedBy,
            RequestedByRole = l.ApplicantRole
        };

        public async Task CancelLeaveRequestAsync(Guid leaveRequestId)
        {
            await CancelRequestAsync(leaveRequestId);
        }

        public async Task<string> ResolveDailyStatus(Guid employeeId, DateTime date)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = user.OrganizationId ?? Guid.Empty;

            var isHoliday = await _context.Holidays.AnyAsync(h =>
                h.OrganizationId == orgId &&
                h.Date.Date == date.Date &&
                !h.IsOptional);

            if (isHoliday)
                return "Holiday";

            var leave = await _context.LeaveRequests
                .Include(l => l.LeaveType)
                .FirstOrDefaultAsync(l =>
                    l.EmployeeId == employeeId &&
                    l.Status == "Approved" &&
                    l.FromDate.Date <= date &&
                    l.ToDate.Date >= date);

            if (leave != null)
                return $"Leave ({leave.LeaveType?.Name ?? "Unknown"})";

            var wfh = await _context.WorkFromHomeRequests
                .FirstOrDefaultAsync(w =>
                    w.EmployeeId == employeeId &&
                    w.Status == "Approved" &&
                    w.Date.Date == date);

            if (wfh != null)
                return AttendanceStatuses.WorkFromHome;

            var att = await _context.Attendances
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employeeId &&
                    a.WorkDate == date);

            if (att != null)
                return att.Status;

            return AttendanceStatuses.Absent;
        }

        private async Task<List<string>> GetRoleUserIdsAsync(Guid organizationId, params string[] roles)
        {
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