using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class OvertimeService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;

        // ✅ Maximum overtime hours that can be claimed in a single day
        private const double MaxOvertimeHoursPerDay = 4.0;

        public OvertimeService(ApplicationDbContext context, CurrentUserService currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        // ✅ EMPLOYEE: Raise OT request
        public async Task RaiseRequestAsync(CreateOvertimeRequestDto dto)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee not found");

            var attendance = await _context.Attendances
                .Include(a => a.Employee)
                    .ThenInclude(e => e.Shift)
                .FirstOrDefaultAsync(a =>
                    a.AttendanceId == dto.AttendanceId &&
                    a.EmployeeId == employee.EmployeeId)
                ?? throw new Exception("Attendance not found");

            // ✅ OT can only be requested for a fully completed past day
            if (attendance.WorkDate >= DateTime.UtcNow.Date)
                throw new Exception("Overtime can only be requested for a completed workday");

            if (attendance.CheckIn == null || attendance.CheckOut == null)
                throw new Exception("Attendance not completed");

            var shift = attendance.Employee?.Shift
                ?? throw new Exception("Shift not assigned");

            if (dto.ActualCheckOut <= attendance.CheckOut)
                throw new Exception("Actual checkout must be after system checkout");

            // ✅ ActualCheckOut cannot be unreasonably far in the future
            if (dto.ActualCheckOut > attendance.CheckIn.Value.AddHours(shift.MinimumHoursForFullDay + MaxOvertimeHoursPerDay))
                throw new Exception($"Overtime cannot exceed {MaxOvertimeHoursPerDay} hours");

            var totalHours = (dto.ActualCheckOut - attendance.CheckIn.Value).TotalHours;
            var overtime = totalHours - shift.MinimumHoursForFullDay;

            if (overtime <= 0)
                throw new Exception("No overtime available");

            // ✅ Cap overtime at max allowed
            overtime = Math.Min(overtime, MaxOvertimeHoursPerDay);

            var exists = await _context.OvertimeRequests.AnyAsync(r =>
                r.AttendanceId == dto.AttendanceId &&
                r.Status == "Pending");

            if (exists)
                throw new Exception("Overtime request already pending");

            var request = new OvertimeRequest
            {
                EmployeeId = employee.EmployeeId,
                AttendanceId = attendance.AttendanceId,
                Date = attendance.WorkDate,
                RequestedHours = overtime,
                ActualCheckOut = dto.ActualCheckOut,
                Reason = dto.Reason,
                Status = "Pending"
            };

            attendance.NeedsOvertimeApproval = true;

            _context.OvertimeRequests.Add(request);
            await _context.SaveChangesAsync();
        }

        // ✅ HR: View pending
        public async Task<List<OvertimeRequest>> GetPendingAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            return await _context.OvertimeRequests
                .Include(r => r.Employee)
                .Where(r =>
                    r.Employee.OrganizationId == user.OrganizationId &&
                    r.Status == "Pending")
                .OrderBy(r => r.AppliedAt)
                .ToListAsync();
        }

        // ✅ HR: Approve / Reject
        public async Task ReviewAsync(ReviewOvertimeDto dto)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var request = await _context.OvertimeRequests
                .Include(r => r.Employee)
                .Include(r => r.Attendance)
                    .ThenInclude(a => a.Employee)
                        .ThenInclude(e => e.Shift)
                .FirstOrDefaultAsync(r => r.OvertimeRequestId == dto.OvertimeRequestId)
                ?? throw new Exception("Request not found");

            if (request.Status != "Pending")
                throw new Exception("Already reviewed");

            if (request.Employee.OrganizationId != user.OrganizationId)
                throw new Exception("Unauthorized");

            request.ReviewedBy = Guid.Parse(user.Id);
            request.ReviewedAt = DateTime.UtcNow;

            var attendance = request.Attendance;

            if (!dto.Approve)
            {
                request.Status = "Rejected";
                attendance.NeedsOvertimeApproval = false;
                await _context.SaveChangesAsync();
                return;
            }

            request.Status = "Approved";

            attendance.CheckOut = request.ActualCheckOut;

            var totalHours = (attendance.CheckOut!.Value - attendance.CheckIn!.Value).TotalHours;

            var shift = attendance.Employee.Shift;

            attendance.TotalHours = totalHours;

            // ✅ Cap approved overtime at max allowed
            var rawOvertime = Math.Max(0, totalHours - shift.MinimumHoursForFullDay);
            attendance.OvertimeHours = Math.Min(rawOvertime, MaxOvertimeHoursPerDay);
            attendance.ApprovedOvertimeHours = attendance.OvertimeHours.Value;

            attendance.IsManual = true;
            attendance.NeedsOvertimeApproval = false;

            await _context.SaveChangesAsync();
        }
    }
}