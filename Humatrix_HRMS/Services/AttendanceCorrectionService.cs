using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    public class AttendanceCorrectionService
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrentUserService _currentUser;
        private readonly UserManager<ApplicationUser> _userManager;

        public AttendanceCorrectionService(
            ApplicationDbContext context,
            CurrentUserService currentUser,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _currentUser = currentUser;
            _userManager = userManager;
        }

        // =========================
        // RAISE REQUEST
        // =========================
        public async Task RaiseRequestAsync(CreateCorrectionRequestDto dto)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee not found");

            var org = await _context.Organizations
                .FirstOrDefaultAsync(o => o.OrganizationId == employee.OrganizationId);

            var tz = GetOrgTimezone(org?.TimeZoneId ?? "UTC");

            if (dto.RequestedCheckIn.HasValue)
                dto.RequestedCheckIn = TimeZoneInfo.ConvertTimeToUtc(dto.RequestedCheckIn.Value, tz);

            if (dto.RequestedCheckOut.HasValue)
                dto.RequestedCheckOut = TimeZoneInfo.ConvertTimeToUtc(dto.RequestedCheckOut.Value, tz);

            var today = TimeHelper.GetOrgDate(org.TimeZoneId);

            if (dto.Date.Date > today)
                throw new Exception("Future date not allowed");

            // ❌ Holiday check
            var isHoliday = await _context.Holidays.AnyAsync(h =>
                h.OrganizationId == employee.OrganizationId &&
                h.Date.Date == dto.Date.Date &&
                !h.IsOptional);

            if (isHoliday)
                throw new Exception("Cannot request on holiday");

            // ❌ Leave check
            var isOnLeave = await _context.LeaveRequests.AnyAsync(l =>
                l.EmployeeId == employee.EmployeeId &&
                l.Status == "Approved" &&
                l.FromDate.Date <= dto.Date.Date &&
                l.ToDate.Date >= dto.Date.Date);

            if (isOnLeave)
                throw new Exception("You are on leave");

            // ❌ WFH check
            var isWFH = await _context.WorkFromHomeRequests.AnyAsync(w =>
                w.EmployeeId == employee.EmployeeId &&
                w.Status == "Approved" &&
                w.Date.Date == dto.Date.Date);

            if (isWFH)
                throw new Exception("You are on Work From Home");

            var workWeek = await _context.WorkWeeks
                .FirstOrDefaultAsync(w => w.OrganizationId == employee.OrganizationId);

            if (workWeek != null && !DateHelper.IsWorkingDay(dto.Date.Date, workWeek))
                throw new Exception("Not a working day");

            var already = await _context.AttendanceCorrectionRequests.AnyAsync(r =>
                r.EmployeeId == employee.EmployeeId &&
                r.Date.Date == dto.Date.Date &&
                r.Status == "Pending");

            if (already)
                throw new Exception("Already requested");

            if (string.IsNullOrWhiteSpace(dto.Reason))
                throw new Exception("Reason required");

            if (dto.RequestedCheckIn.HasValue && dto.RequestedCheckOut.HasValue &&
                dto.RequestedCheckIn >= dto.RequestedCheckOut)
                throw new Exception("Invalid time range");

            var att = await _context.Attendances
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employee.EmployeeId &&
                    a.WorkDate.Date == dto.Date.Date);

            var request = new AttendanceCorrectionRequest
            {
                EmployeeId = employee.EmployeeId,
                AttendanceId = att?.AttendanceId,
                Date = dto.Date.Date,
                RequestedCheckIn = dto.RequestedCheckIn,
                RequestedCheckOut = dto.RequestedCheckOut,
                Reason = dto.Reason,
                Status = "Pending",
                AppliedAt = DateTime.UtcNow
            };

            _context.AttendanceCorrectionRequests.Add(request);
            await _context.SaveChangesAsync();
        }

        // =========================
        // HR REVIEW
        // =========================
        public async Task ReviewRequestAsync(ReviewCorrectionDto dto)
        {
            var hrUser = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                var request = await _context.AttendanceCorrectionRequests
                    .Include(r => r.Employee).ThenInclude(e => e.Shift)
                    .Include(r => r.Employee).ThenInclude(e => e.Organization)
                    .FirstOrDefaultAsync(r => r.CorrectionRequestId == dto.CorrectionRequestId)
                    ?? throw new Exception("Request not found");

                if (request.Status != "Pending")
                    throw new Exception("Already reviewed");

                if (request.Employee.OrganizationId != hrUser.OrganizationId)
                    throw new Exception("Unauthorized");

                // ❌ Re-check business rules (CRITICAL)
                var isOnLeave = await _context.LeaveRequests.AnyAsync(l =>
                    l.EmployeeId == request.EmployeeId &&
                    l.Status == "Approved" &&
                    l.FromDate.Date <= request.Date &&
                    l.ToDate.Date >= request.Date);

                if (isOnLeave)
                    throw new Exception("Employee is on leave");

                var isWFH = await _context.WorkFromHomeRequests.AnyAsync(w =>
                    w.EmployeeId == request.EmployeeId &&
                    w.Status == "Approved" &&
                    w.Date.Date == request.Date);

                if (isWFH)
                    throw new Exception("Employee is on WFH");

                request.ReviewedBy = Guid.Parse(hrUser.Id);
                request.ReviewedAt = DateTime.UtcNow;

                if (!dto.Approve)
                {
                    request.Status = "Rejected";
                    request.RejectionReason = dto.RejectionReason;

                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();
                    return;
                }

                request.Status = "Approved";

                var att = request.AttendanceId.HasValue
                    ? await _context.Attendances.FindAsync(request.AttendanceId.Value)
                    : null;

                if (att == null)
                {
                    att = new Attendance
                    {
                        AttendanceId = Guid.NewGuid(),
                        UserId = request.Employee.UserId,
                        EmployeeId = request.Employee.EmployeeId,
                        OrganizationId = request.Employee.OrganizationId,
                        WorkDate = request.Date
                    };

                    _context.Attendances.Add(att);
                    request.AttendanceId = att.AttendanceId;
                }

                if (request.RequestedCheckIn.HasValue)
                    att.CheckIn = request.RequestedCheckIn.Value;

                if (request.RequestedCheckOut.HasValue)
                    att.CheckOut = request.RequestedCheckOut.Value;

                ApplyAttendanceCalculation(att, request.Employee);

                att.IsManual = true;
                att.UpdatedBy = Guid.Parse(hrUser.Id);
                att.IsPresent = att.CheckIn.HasValue;

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // =========================
        // DIRECT EDIT (HR)
        // =========================
        public async Task DirectEditAsync(ManualAttendanceEditDto dto)
        {
            var hrUser = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                var att = await _context.Attendances
                    .Include(a => a.Employee).ThenInclude(e => e.Shift)
                    .Include(a => a.Employee).ThenInclude(e => e.Organization)
                    .FirstOrDefaultAsync(a => a.AttendanceId == dto.AttendanceId)
                    ?? throw new Exception("Attendance not found");

                if (att.Employee.OrganizationId != hrUser.OrganizationId)
                    throw new Exception("Unauthorized");

                if (dto.CheckIn.HasValue && dto.CheckOut.HasValue &&
                    dto.CheckOut <= dto.CheckIn)
                    throw new Exception("Invalid time range");

                if (dto.CheckIn.HasValue) att.CheckIn = dto.CheckIn.Value;
                if (dto.CheckOut.HasValue) att.CheckOut = dto.CheckOut.Value;

                ApplyAttendanceCalculation(att, att.Employee);

                // ✅ SAFE STATUS OVERRIDE
                if (!string.IsNullOrEmpty(dto.StatusOverride))
                {
                    var allowed = new[]
                    {
                        AttendanceStatuses.Present,
                        AttendanceStatuses.Late,
                        AttendanceStatuses.HalfDay,
                        AttendanceStatuses.ShortHours,
                        AttendanceStatuses.Absent
                    };

                    if (!allowed.Contains(dto.StatusOverride))
                        throw new Exception("Invalid status");

                    att.Status = dto.StatusOverride;
                }

                att.IsManual = true;
                att.UpdatedBy = Guid.Parse(hrUser.Id);
                att.IsPresent = att.CheckIn.HasValue;

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // =========================
        // COMMON CALCULATION
        // =========================
        private void ApplyAttendanceCalculation(Attendance att, Employee emp)
        {
            if (!att.CheckIn.HasValue || !att.CheckOut.HasValue)
                return;

            var hours = (att.CheckOut.Value - att.CheckIn.Value).TotalHours;

            if (hours <= 0 || hours > 24)
                throw new Exception("Invalid hours");

            att.TotalHours = hours;

            var shift = emp.Shift;

            if (shift == null)
            {
                att.Status = AttendanceStatuses.Present;
                return;
            }

            if (hours > shift.MinimumHoursForFullDay)
            {
                att.OvertimeHours = hours - shift.MinimumHoursForFullDay;
                att.NeedsOvertimeApproval = true;
            }
            else
            {
                att.OvertimeHours = 0;
                att.NeedsOvertimeApproval = false;
            }

            var tz = GetOrgTimezone(emp.Organization?.TimeZoneId ?? "UTC");
            var checkInLocal = TimeZoneInfo.ConvertTimeFromUtc(att.CheckIn.Value, tz);

            var lateTime = shift.StartTime.Add(TimeSpan.FromMinutes(shift.LateAllowanceMinutes));
            bool isLate = checkInLocal.TimeOfDay > lateTime;

            if (hours < shift.MinimumHoursForHalfDay)
                att.Status = AttendanceStatuses.ShortHours;
            else if (hours < shift.MinimumHoursForFullDay)
                att.Status = AttendanceStatuses.HalfDay;
            else
                att.Status = isLate
                    ? AttendanceStatuses.Late
                    : AttendanceStatuses.Present;
        }

        private TimeZoneInfo GetOrgTimezone(string timeZoneId)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
            catch { return TimeZoneInfo.Utc; }
        }
    }
}