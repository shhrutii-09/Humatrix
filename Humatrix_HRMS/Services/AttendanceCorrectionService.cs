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

            var orgToday = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));

            // =========================================================
            // VALIDATIONS
            // =========================================================

            if (dto.Date > orgToday)
                throw new Exception("Future dates are not allowed");

            if ((orgToday.DayNumber - dto.Date.DayNumber) > MaxCorrectionDays)
                throw new Exception($"Only last {MaxCorrectionDays} days are allowed");

            if (string.IsNullOrWhiteSpace(dto.Reason))
                throw new Exception("Reason is required");

            if (dto.Reason.Trim().Length < 5)
                throw new Exception("Please enter proper reason");

            if (!dto.RequestedCheckIn.HasValue &&
                !dto.RequestedCheckOut.HasValue)
            {
                throw new Exception("Please enter check-in or check-out time");
            }

            if (dto.RequestedCheckIn.HasValue &&
                dto.RequestedCheckOut.HasValue &&
                dto.RequestedCheckIn >= dto.RequestedCheckOut)
            {
                throw new Exception("Check-in must be before check-out");
            }

            // =========================================================
            // HOLIDAY CHECK
            // =========================================================

            var isHoliday = await _context.Holidays.AnyAsync(h =>
                h.OrganizationId == employee.OrganizationId &&
                DateOnly.FromDateTime(h.Date) == dto.Date &&
                !h.IsOptional);

            if (isHoliday)
                throw new Exception("Cannot request correction on holiday");

            // =========================================================
            // LEAVE CHECK
            // =========================================================

            var onLeave = await _context.LeaveRequests.AnyAsync(l =>
                l.EmployeeId == employee.EmployeeId &&
                l.Status == "Approved" &&
                DateOnly.FromDateTime(l.FromDate) <= dto.Date &&
                DateOnly.FromDateTime(l.ToDate) >= dto.Date);

            if (onLeave)
                throw new Exception("You are on approved leave");

            // =========================================================
            // WFH CHECK
            // =========================================================

            var onWfh = await _context.WorkFromHomeRequests.AnyAsync(w =>
                w.EmployeeId == employee.EmployeeId &&
                w.Status == "Approved" &&
                DateOnly.FromDateTime(w.Date) == dto.Date);

            if (onWfh)
                throw new Exception("Approved WFH exists for this date");

            // =========================================================
            // DUPLICATE CHECK
            // =========================================================

            var duplicate = await _context.AttendanceCorrectionRequests.AnyAsync(r =>
                r.EmployeeId == employee.EmployeeId &&
                r.Date == dto.Date &&
                r.Status == "Pending");

            if (duplicate)
                throw new Exception("Pending request already exists");

            // =========================================================
            // LOCAL TIME → UTC
            // =========================================================

            DateTime? utcCheckIn = null;
            DateTime? utcCheckOut = null;

            if (dto.RequestedCheckIn.HasValue)
            {
                var localCheckIn = new DateTime(
                    dto.Date.Year,
                    dto.Date.Month,
                    dto.Date.Day,
                    dto.RequestedCheckIn.Value.Hours,
                    dto.RequestedCheckIn.Value.Minutes,
                    0,
                    DateTimeKind.Unspecified);

                utcCheckIn = TimeZoneInfo.ConvertTimeToUtc(localCheckIn, tz);
            }

            if (dto.RequestedCheckOut.HasValue)
            {
                var localCheckOut = new DateTime(
                    dto.Date.Year,
                    dto.Date.Month,
                    dto.Date.Day,
                    dto.RequestedCheckOut.Value.Hours,
                    dto.RequestedCheckOut.Value.Minutes,
                    0,
                    DateTimeKind.Unspecified);

                utcCheckOut = TimeZoneInfo.ConvertTimeToUtc(localCheckOut, tz);
            }

            // =========================================================
            // EXISTING ATTENDANCE
            // =========================================================

            var existingAttendance = await _context.Attendances
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employee.EmployeeId &&
                    DateOnly.FromDateTime(a.WorkDate) == dto.Date);

            // =========================================================
            // CREATE REQUEST
            // =========================================================

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
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var reviewer = await _context.Employees
                .Include(e => e.Organization)
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Reviewer profile not found");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var request = await _context.AttendanceCorrectionRequests
                    .Include(r => r.Employee)
                        .ThenInclude(e => e.Department)
                    .Include(r => r.Employee)
                        .ThenInclude(e => e.Organization)
                    .Include(r => r.Employee)
                        .ThenInclude(e => e.Shift)
                    .Include(r => r.Attendance)
                    .FirstOrDefaultAsync(r =>
                        r.CorrectionRequestId == dto.CorrectionRequestId)
                    ?? throw new Exception("Request not found");

                if (request.Status != "Pending")
                    throw new Exception("Request already reviewed");

                if (request.Employee.OrganizationId != reviewer.OrganizationId)
                    throw new Exception("Access denied");

                var roles = await _userManager.GetRolesAsync(user);

                bool isOrgAdmin = roles.Contains("OrgAdmin");
                bool isHR = roles.Contains("HR");

                if (isHR && !isOrgAdmin)
                {
                    if (request.Employee.DepartmentId != reviewer.DepartmentId)
                    {
                        throw new Exception("You can only review your department requests");
                    }
                }

                // =====================================================
                // REJECT
                // =====================================================

                if (!dto.Approve)
                {
                    if (string.IsNullOrWhiteSpace(dto.RejectionReason))
                        throw new Exception("Rejection reason required");

                    request.Status = "Rejected";
                    request.RejectionReason = dto.RejectionReason.Trim();
                    request.ReviewedBy = reviewer.EmployeeId;
                    request.ReviewedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return;
                }

                // =====================================================
                // FORCE UTC KIND
                // =====================================================

                DateTime? correctedCheckIn = request.RequestedCheckIn.HasValue
                    ? DateTime.SpecifyKind(request.RequestedCheckIn.Value, DateTimeKind.Utc)
                    : null;

                DateTime? correctedCheckOut = request.RequestedCheckOut.HasValue
                    ? DateTime.SpecifyKind(request.RequestedCheckOut.Value, DateTimeKind.Utc)
                    : null;

                Attendance? attendance = request.Attendance;

                // =====================================================
                // CREATE NEW ATTENDANCE
                // =====================================================

                if (attendance == null)
                {
                    attendance = new Attendance
                    {
                        AttendanceId = Guid.NewGuid(),

                        UserId = request.Employee.UserId,

                        EmployeeId = request.EmployeeId,

                        OrganizationId = request.Employee.OrganizationId,

                        WorkDate = request.Date.ToDateTime(TimeOnly.MinValue),

                        CheckIn = correctedCheckIn,

                        CheckOut = correctedCheckOut,

                        ActualCheckOut = correctedCheckOut,

                        IsManual = true,

                        IsPresent = true
                    };

                    _context.Attendances.Add(attendance);

                    request.AttendanceId = attendance.AttendanceId;
                }
                else
                {
                    if (correctedCheckIn.HasValue)
                    {
                        attendance.CheckIn = correctedCheckIn;
                    }

                    if (correctedCheckOut.HasValue)
                    {
                        attendance.CheckOut = correctedCheckOut;
                        attendance.ActualCheckOut = correctedCheckOut;
                    }

                    attendance.IsManual = true;
                }

                // =====================================================
                // RECALCULATE
                // =====================================================

                ApplyAttendanceCalculation(attendance, request.Employee);

                attendance.UpdatedBy = reviewer.EmployeeId;

                attendance.IsPresent = attendance.CheckIn.HasValue;

                request.Status = "Approved";

                request.ReviewedBy = reviewer.EmployeeId;

                request.ReviewedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
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
        private void ApplyAttendanceCalculation(
    Attendance attendance,
    Employee employee)
        {
            if (!attendance.CheckIn.HasValue ||
                !attendance.CheckOut.HasValue)
            {
                attendance.Status = AttendanceStatuses.Absent;
                return;
            }

            // FORCE UTC
            var checkInUtc = DateTime.SpecifyKind(
                attendance.CheckIn.Value,
                DateTimeKind.Utc);

            var checkOutUtc = DateTime.SpecifyKind(
                attendance.CheckOut.Value,
                DateTimeKind.Utc);

            var totalHours =
                (checkOutUtc - checkInUtc).TotalHours;

            if (totalHours <= 0 || totalHours > 24)
                throw new Exception("Invalid attendance hours");

            attendance.TotalHours =
                Math.Round(totalHours, 2);

            var shift = employee.Shift;

            if (shift == null)
            {
                attendance.Status = AttendanceStatuses.Present;
                return;
            }

            // =========================================================
            // OVERTIME
            // =========================================================

            if (totalHours > shift.MinimumHoursForFullDay)
            {
                attendance.OvertimeHours =
                    Math.Round(totalHours - shift.MinimumHoursForFullDay, 2);

                attendance.NeedsOvertimeApproval =
                    attendance.OvertimeHours > 0;
            }
            else
            {
                attendance.OvertimeHours = 0;
                attendance.NeedsOvertimeApproval = false;
            }

            // =========================================================
            // LATE CHECK
            // =========================================================

            var tz = GetOrgTimezone(
                employee.Organization?.TimeZoneId);

            var localCheckIn =
                TimeZoneInfo.ConvertTimeFromUtc(checkInUtc, tz);

            var lateLimit =
                shift.StartTime.Add(
                    TimeSpan.FromMinutes(
                        shift.LateAllowanceMinutes));

            bool isLate =
                localCheckIn.TimeOfDay > lateLimit;

            // =========================================================
            // STATUS
            // =========================================================

            if (totalHours < shift.MinimumHoursForHalfDay)
            {
                attendance.Status =
                    AttendanceStatuses.ShortHours;
            }
            else if (totalHours < shift.MinimumHoursForFullDay)
            {
                attendance.Status =
                    AttendanceStatuses.HalfDay;
            }
            else
            {
                attendance.Status =
                    isLate
                    ? AttendanceStatuses.Late
                    : AttendanceStatuses.Present;
            }
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