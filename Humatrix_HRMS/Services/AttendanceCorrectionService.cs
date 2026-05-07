using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.DTOs.Humatrix_HRMS.DTOs;
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

        private const int MaxCorrectionDays = 30;

        public AttendanceCorrectionService(
            ApplicationDbContext context,
            CurrentUserService currentUser,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _currentUser = currentUser;
            _userManager = userManager;
        }

        // =========================================================
        // EMPLOYEE — CREATE REQUEST
        // =========================================================
        public async Task RaiseRequestAsync(CreateCorrectionRequestDto dto)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .Include(e => e.Organization)
                .Include(e => e.Shift)
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee profile not found");

            if (employee.Organization == null)
                throw new Exception("Organization not found");

            var tz = GetOrgTimezone(employee.Organization.TimeZoneId);
            var orgToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));

            // Validations
            if (dto.Date > orgToday) throw new Exception("Future dates are not allowed");
            if ((orgToday.DayNumber - dto.Date.DayNumber) > MaxCorrectionDays)
                throw new Exception($"Only last {MaxCorrectionDays} days are allowed");
            if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Trim().Length < 5)
                throw new Exception("Please enter a proper reason (min 5 chars)");

            if (!dto.RequestedCheckIn.HasValue && !dto.RequestedCheckOut.HasValue)
                throw new Exception("Please enter check-in or check-out time");

            // Overnight Shift Rollover Logic
            DateTime? utcCheckIn = null;
            DateTime? utcCheckOut = null;

            if (dto.RequestedCheckIn.HasValue)
            {
                var localCheckIn = dto.Date.ToDateTime(TimeOnly.FromTimeSpan(dto.RequestedCheckIn.Value));
                utcCheckIn = TimeZoneInfo.ConvertTimeToUtc(localCheckIn, tz);
            }

            if (dto.RequestedCheckOut.HasValue)
            {
                var localCheckOut = dto.Date.ToDateTime(TimeOnly.FromTimeSpan(dto.RequestedCheckOut.Value));

                // FIX: If checkout time is earlier than checkin, it belongs to the next day
                if (dto.RequestedCheckIn.HasValue && dto.RequestedCheckOut.Value < dto.RequestedCheckIn.Value)
                {
                    localCheckOut = localCheckOut.AddDays(1);
                }

                utcCheckOut = TimeZoneInfo.ConvertTimeToUtc(localCheckOut, tz);
            }

            // Checks (Holiday, Leave, WFH, Duplicate)
            var isHoliday = await _context.Holidays.AnyAsync(h => h.OrganizationId == employee.OrganizationId && DateOnly.FromDateTime(h.Date) == dto.Date && !h.IsOptional);
            if (isHoliday) throw new Exception("Cannot request correction on holiday");

            var onLeave = await _context.LeaveRequests.AnyAsync(l => l.EmployeeId == employee.EmployeeId && l.Status == "Approved" && DateOnly.FromDateTime(l.FromDate) <= dto.Date && DateOnly.FromDateTime(l.ToDate) >= dto.Date);
            if (onLeave) throw new Exception("You are on approved leave");

            var duplicate = await _context.AttendanceCorrectionRequests.AnyAsync(r => r.EmployeeId == employee.EmployeeId && r.Date == dto.Date && r.Status == "Pending");
            if (duplicate) throw new Exception("A pending request already exists for this date");

            var existingAttendance = await _context.Attendances.FirstOrDefaultAsync(a => a.EmployeeId == employee.EmployeeId && DateOnly.FromDateTime(a.WorkDate) == dto.Date);

            var request = new AttendanceCorrectionRequest
            {
                CorrectionRequestId = Guid.NewGuid(),
                EmployeeId = employee.EmployeeId,
                AttendanceId = existingAttendance?.AttendanceId,
                Date = dto.Date,
                RequestedCheckIn = utcCheckIn,
                RequestedCheckOut = utcCheckOut,
                Reason = dto.Reason.Trim(),
                Status = "Pending",
                AppliedAt = DateTime.UtcNow
            };

            _context.AttendanceCorrectionRequests.Add(request);
            await _context.SaveChangesAsync();
        }

        // =========================================================
        // EMPLOYEE — MY REQUESTS
        // =========================================================
        public async Task<List<CorrectionRequestDto>> GetMyRequestsAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .Include(e => e.Organization)
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee profile not found");

            var tz = GetOrgTimezone(employee.Organization?.TimeZoneId);

            var requests = await _context.AttendanceCorrectionRequests
                .Include(r => r.Attendance)
                .Where(r => r.EmployeeId == employee.EmployeeId)
                .OrderByDescending(r => r.AppliedAt)
                .ToListAsync();

            return requests
                .Select(r => MapToDto(r, tz))
                .ToList();
        }

        // =========================================================
        // HR / ORG ADMIN — ALL REQUESTS
        // =========================================================
        public async Task<List<CorrectionRequestDto>> GetAllForHRAsync(string? status = null)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var currentEmployee = await _context.Employees
                .Include(e => e.Organization)
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee profile not found");

            var roles = await _userManager.GetRolesAsync(user);

            bool isOrgAdmin = roles.Contains("OrgAdmin");
            bool isHR = roles.Contains("HR");

            var tz = GetOrgTimezone(currentEmployee.Organization?.TimeZoneId);

            var query = _context.AttendanceCorrectionRequests
                .Include(r => r.Employee)
                    .ThenInclude(e => e.Department)
                .Include(r => r.Attendance)
                .Where(r =>
                    r.Employee.OrganizationId == currentEmployee.OrganizationId);

            // =========================================================
            // HR CAN SEE ONLY SAME DEPARTMENT
            // =========================================================

            if (isHR && !isOrgAdmin)
            {
                query = query.Where(r =>
                    r.Employee.DepartmentId == currentEmployee.DepartmentId);
            }

            // =========================================================
            // FILTER
            // =========================================================

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(r => r.Status == status);
            }

            var requests = await query
                .OrderByDescending(r => r.AppliedAt)
                .ToListAsync();

            return requests
                .Select(r => MapToDto(r, tz))
                .ToList();
        }

        // =========================================================
        // HR / ADMIN — APPROVE / REJECT
        // =========================================================
        public async Task ReviewRequestAsync(ReviewCorrectionDto dto)
        {
            var user = await _currentUser.GetUserAsync() ?? throw new Exception("Unauthorized");
            var reviewer = await _context.Employees.FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Reviewer profile not found");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var request = await _context.AttendanceCorrectionRequests
                    .Include(r => r.Employee).ThenInclude(e => e.Shift)
                    .Include(r => r.Employee).ThenInclude(e => e.Organization)
                    .Include(r => r.Attendance)
                    .FirstOrDefaultAsync(r => r.CorrectionRequestId == dto.CorrectionRequestId)
                    ?? throw new Exception("Request not found");

                if (request.Status != "Pending") throw new Exception("Request already reviewed");

                if (!dto.Approve)
                {
                    request.Status = "Rejected";
                    request.RejectionReason = dto.RejectionReason;
                    request.ReviewedBy = reviewer.EmployeeId;
                    request.ReviewedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return;
                }

                var attendance = request.Attendance;
                if (attendance == null)
                {
                    attendance = new Attendance
                    {
                        AttendanceId = Guid.NewGuid(),
                        UserId = request.Employee.UserId,
                        EmployeeId = request.EmployeeId,
                        OrganizationId = request.Employee.OrganizationId,
                        WorkDate = request.Date.ToDateTime(TimeOnly.MinValue),
                    };
                    _context.Attendances.Add(attendance);
                }

                attendance.CheckIn = request.RequestedCheckIn;
                attendance.CheckOut = request.RequestedCheckOut;
                attendance.ActualCheckOut = request.RequestedCheckOut;
                attendance.IsManual = true;
                attendance.IsPresent = attendance.CheckIn.HasValue;

                // Apply logic & calculate SystemCheckOut
                ApplyAttendanceCalculation(attendance, request.Employee);

                request.Status = "Approved";
                request.ReviewedBy = reviewer.EmployeeId;
                request.ReviewedAt = DateTime.UtcNow;
                request.AttendanceId = attendance.AttendanceId;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch { await transaction.RollbackAsync(); throw; }
        }

        // =========================================================
        // EMPLOYEE — CANCEL REQUEST
        // =========================================================
        public async Task CancelRequestAsync(Guid requestId)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee not found");

            var request = await _context.AttendanceCorrectionRequests
                .FirstOrDefaultAsync(r =>
                    r.CorrectionRequestId == requestId &&
                    r.EmployeeId == employee.EmployeeId)
                ?? throw new Exception("Request not found");

            if (request.Status != "Pending")
                throw new Exception("Only pending requests can be cancelled");

            request.Status = "Cancelled";

            await _context.SaveChangesAsync();
        }

        // =========================================================
        // GET ORG TIMEZONE
        // =========================================================
        public async Task<string> GetCurrentOrganizationTimeZoneAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .Include(e => e.Organization)
                .FirstOrDefaultAsync(e => e.UserId == user.Id);

            return employee?.Organization?.TimeZoneId
                   ?? "India Standard Time";
        }

        // =========================================================
        // PRIVATE — CALCULATE ATTENDANCE
        // =========================================================
       private void ApplyAttendanceCalculation(Attendance attendance, Employee employee)
        {
            var shift = employee.Shift;
            if (shift == null || !attendance.CheckIn.HasValue || !attendance.CheckOut.HasValue)
            {
                attendance.Status = attendance.CheckIn.HasValue ? AttendanceStatuses.Present : AttendanceStatuses.Absent;
                return;
            }

            var tz = GetOrgTimezone(employee.Organization?.TimeZoneId);
            var checkInLocal = TimeZoneInfo.ConvertTimeFromUtc(attendance.CheckIn.Value, tz);
            var checkOutLocal = TimeZoneInfo.ConvertTimeFromUtc(attendance.CheckOut.Value, tz);

            // 1. Total Hours
            attendance.TotalHours = Math.Round((attendance.CheckOut.Value - attendance.CheckIn.Value).TotalHours, 2);

            // 2. Set SystemCheckOut (Critical for OvertimeService)
            var shiftEndLocal = checkInLocal.Date.Add(shift.EndTime);
            if (shift.EndTime < shift.StartTime) shiftEndLocal = shiftEndLocal.AddDays(1);
            attendance.SystemCheckOut = TimeZoneInfo.ConvertTimeToUtc(shiftEndLocal, tz);

            // 3. Status Logic (Late / Early Exit / Half Day)
            var lateThreshold = shift.StartTime.Add(TimeSpan.FromMinutes(shift.LateAllowanceMinutes));
            bool isLate = checkInLocal.TimeOfDay > lateThreshold;
            bool leftEarly = checkOutLocal < shiftEndLocal;

            if (attendance.TotalHours < shift.MinimumHoursForHalfDay)
                attendance.Status = AttendanceStatuses.ShortHours;
            else if (attendance.TotalHours < shift.MinimumHoursForFullDay)
                attendance.Status = AttendanceStatuses.HalfDay;
            else if (leftEarly)
                attendance.Status = AttendanceStatuses.EarlyExit;
            else
                attendance.Status = isLate ? AttendanceStatuses.Late : AttendanceStatuses.Present;
        }

        // =========================================================
        // PRIVATE — MAP DTO
        // =========================================================
        private CorrectionRequestDto MapToDto(
            AttendanceCorrectionRequest request,
            TimeZoneInfo tz)
        {
            return new CorrectionRequestDto
            {
                CorrectionRequestId = request.CorrectionRequestId,

                EmployeeId = request.EmployeeId,

                EmployeeName = request.Employee != null
                    ? $"{request.Employee.FirstName} {request.Employee.LastName}"
                    : null,

                Department = request.Employee?.Department?.Name,

                OrgTimeZoneId = tz.Id,

                Date = request.Date,

                RequestedCheckIn =
                    ConvertUtcToLocal(request.RequestedCheckIn, tz),

                RequestedCheckOut =
                    ConvertUtcToLocal(request.RequestedCheckOut, tz),

                CurrentCheckIn =
                    ConvertUtcToLocal(request.Attendance?.CheckIn, tz),

                CurrentCheckOut =
                    ConvertUtcToLocal(request.Attendance?.CheckOut, tz),

                Reason = request.Reason,

                Status = request.Status,

                RejectionReason = request.RejectionReason,

                AppliedAt =
                    ConvertUtcToLocalRequired(request.AppliedAt, tz),

                ReviewedAt =
                    ConvertUtcToLocal(request.ReviewedAt, tz),

                AttendanceId = request.AttendanceId
            };
        }

        // =========================================================
        // PRIVATE — UTC HELPERS
        // =========================================================
        private DateTime ConvertUtcToLocalRequired(
            DateTime utc,
            TimeZoneInfo tz)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utc, DateTimeKind.Utc),
                tz);
        }

        private DateTime? ConvertUtcToLocal(
            DateTime? utc,
            TimeZoneInfo tz)
        {
            if (!utc.HasValue)
                return null;

            return TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc),
                tz);
        }

        private TimeZoneInfo GetOrgTimezone(string? timezoneId)
        {
            if (string.IsNullOrWhiteSpace(timezoneId))
                return TimeZoneInfo.Utc;

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            }
            catch
            {
                return TimeZoneInfo.Utc;
            }
        }
    }
}