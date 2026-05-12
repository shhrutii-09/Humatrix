using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models;
using Humatrix_HRMS.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class WorkFromHomeService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;

        private readonly HRPolicyValidationService _policy;

        public WorkFromHomeService(
            ApplicationDbContext context,
            CurrentUserService currentUser,
            HRPolicyValidationService policy)
        {
            _context = context;
            _currentUser = currentUser;
            _policy = policy;
        }
        // ─────────────────────────────────────
        // APPLY
        // ─────────────────────────────────────
        public async Task ApplyAsync(WorkFromHomeRequest request)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee not found");

            var orgId = employee.OrganizationId;
            var orgToday = await _policy.GetOrgTodayAsync(orgId);

            if (request.Date.Date < orgToday)
                throw new Exception("Cannot apply for past date");

            // ── CENTRAL POLICY CHECK ──────────────────────────────────────────────────
            // Checks: holiday, non-working day, leave conflict
            await _policy.AssertEmployeeCanActAsync(orgId, employee.EmployeeId, request.Date, "WFH");

            // ── Duplicate WFH guard ───────────────────────────────────────────────────
            var wfhExists = await _context.WorkFromHomeRequests.AnyAsync(w =>
                w.EmployeeId == employee.EmployeeId &&
                w.Date.Date == request.Date.Date &&
                w.Status != "Rejected" &&
                w.Status != "Cancelled");

            if (wfhExists)
                throw new Exception("WFH already applied for this date");

            // ── Existing attendance guard ─────────────────────────────────────────────
            var attendanceExists = await _context.Attendances.AnyAsync(a =>
                a.EmployeeId == employee.EmployeeId &&
                a.WorkDate == request.Date);

            if (attendanceExists)
                throw new Exception("Attendance already marked for this date");

            request.EmployeeId = employee.EmployeeId;
            request.Status = "Pending";
            request.AppliedAt = DateTime.UtcNow;

            _context.WorkFromHomeRequests.Add(request);
            await _context.SaveChangesAsync();
        }

        // ─────────────────────────────────────
        // APPROVE / REJECT
        // ─────────────────────────────────────
        public async Task UpdateStatusAsync(Guid id, string status, string? reason = null)
        {
            if (status != "Approved" && status != "Rejected")
                throw new Exception("Invalid status");

            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var req = await _context.WorkFromHomeRequests
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.Id == id)
                ?? throw new Exception("Request not found");

            if (req.Employee.OrganizationId != user.OrganizationId)
                throw new Exception("Unauthorized");

            if (req.Status != "Pending")
                throw new Exception("Already processed");

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
            }
            else
            {
                req.Status = "Rejected";
                req.RejectionReason = reason;
            }

            req.ReviewedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        // ─────────────────────────────────────
        // CANCEL (EMPLOYEE)
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
        // GET MY REQUESTS
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
                .ToListAsync();
        }

        // ─────────────────────────────────────
        // HR VIEW
        // ─────────────────────────────────────
        public async Task<List<WorkFromHomeRequest>> GetAllAsync(string? status = null)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var query = _context.WorkFromHomeRequests
                .Include(r => r.Employee)
                .Where(r => r.Employee.OrganizationId == user.OrganizationId);

            if (!string.IsNullOrEmpty(status))
                query = query.Where(r => r.Status == status);

            return await query
                .OrderByDescending(r => r.AppliedAt)
                .ToListAsync();
        }

        private TimeZoneInfo GetOrgTimezone(string timeZoneId)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
            catch { return TimeZoneInfo.Utc; }
        }
    }
}