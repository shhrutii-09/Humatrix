using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class LeaveService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;

        public LeaveService(ApplicationDbContext context, CurrentUserService currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        // =========================
        // APPLY LEAVE
        // =========================
        public async Task ApplyLeaveAsync(LeaveRequest request)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            if (employee == null)
                throw new Exception("Employee not found");

            if (request.FromDate > request.ToDate)
                throw new Exception("Invalid date range");

            // ✅ Prevent overlapping leaves
            var overlap = await _context.LeaveRequests.AnyAsync(l =>
                l.EmployeeId == employee.EmployeeId &&
                l.Status != "Rejected" &&
                (
                    request.FromDate <= l.ToDate &&
                    request.ToDate >= l.FromDate
                ));

            if (overlap)
                throw new Exception("Leave already applied for selected dates");

            request.EmployeeId = employee.EmployeeId;
            request.Status = "Pending";
            request.AppliedAt = DateTime.UtcNow;

            _context.LeaveRequests.Add(request);
            await _context.SaveChangesAsync();
        }

        // =========================
        // GET MY LEAVES
        // =========================
        public async Task<List<LeaveRequest>> GetMyLeavesAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .FirstAsync(e => e.UserId == user.Id);

            return await _context.LeaveRequests
                .Where(l => l.EmployeeId == employee.EmployeeId)
                .OrderByDescending(l => l.AppliedAt)
                .AsNoTracking()
                .ToListAsync();
        }

        // =========================
        // HR VIEW ALL
        // =========================
        public async Task<List<LeaveRequest>> GetAllAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            return await _context.LeaveRequests
                .Include(l => l.Employee)
                .Where(l => l.Employee.OrganizationId == user.OrganizationId)
                .OrderByDescending(l => l.AppliedAt)
                .AsNoTracking()
                .ToListAsync();
        }

        // =========================
        // APPROVE / REJECT
        // =========================
        public async Task UpdateStatusAsync(Guid id, string status)
        {
            if (status != "Approved" && status != "Rejected")
                throw new Exception("Invalid status");

            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var leave = await _context.LeaveRequests
                .Include(l => l.Employee)
                .FirstOrDefaultAsync(l => l.LeaveRequestId == id);

            if (leave == null)
                throw new Exception("Leave not found");

            if (leave.Status != "Pending")
                throw new Exception("Already processed");

            leave.Status = status;
            leave.ApprovedBy = Guid.Parse(user.Id);
            leave.ReviewedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        }
    }
}
