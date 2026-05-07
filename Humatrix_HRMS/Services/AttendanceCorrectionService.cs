using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs.AttendanceCorrections;
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
        private readonly AttendanceCalculationService _attendanceCalculationService;

        public AttendanceCorrectionService(
            ApplicationDbContext context,
            CurrentUserService currentUser,
            UserManager<ApplicationUser> userManager,
            AttendanceCalculationService attendanceCalculationService)
        {
            _context = context;
            _currentUser = currentUser;
            _userManager = userManager;
            _attendanceCalculationService = attendanceCalculationService;
        }
    

        // =========================================================================
        // CREATE REQUEST
        // =========================================================================
        public async Task CreateRequestAsync(CreateAttendanceCorrectionDto dto)
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = user.OrganizationId ?? Guid.Empty;

            var employee = await _context.Employees
                .Include(e => e.Shift)
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee not found");

            var org = await _context.Organizations
                .FirstOrDefaultAsync(o => o.OrganizationId == orgId)
                ?? throw new Exception("Organization not found");

            var today = TimeHelper.GetOrgDate(org.TimeZoneId);

            // ───────────────── VALIDATIONS ─────────────────

            if (dto.WorkDate.Date > today.Date)
                throw new Exception("Future attendance cannot be corrected");

            if ((today.Date - dto.WorkDate.Date).Days > 7)
                throw new Exception("Attendance correction window expired");

            var alreadyPending = await _context.AttendanceCorrectionRequests
                .AnyAsync(x =>
                    x.EmployeeId == employee.EmployeeId &&
                    x.WorkDate.Date == dto.WorkDate.Date &&
                    x.Status == AttendanceCorrectionStatuses.Pending);

            if (alreadyPending)
                throw new Exception("A pending request already exists");

            if (string.IsNullOrWhiteSpace(dto.Reason))
                throw new Exception("Reason is required");

            if (dto.RequestedCheckIn != null &&
                dto.RequestedCheckOut != null)
            {
                if (dto.RequestedCheckOut <= dto.RequestedCheckIn)
                    throw new Exception("Check-out must be after check-in");

                var hours =
                    (dto.RequestedCheckOut.Value - dto.RequestedCheckIn.Value)
                    .TotalHours;

                if (hours > 18)
                    throw new Exception("Invalid attendance duration");
            }

            // ───────────────── EXISTING ATTENDANCE ─────────────────

            var attendance = await _context.Attendances
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employee.EmployeeId &&
                    a.WorkDate.Date == dto.WorkDate.Date);

            var request = new AttendanceCorrectionRequest
            {
                AttendanceCorrectionRequestId = Guid.NewGuid(),

                OrganizationId = orgId,

                EmployeeId = employee.EmployeeId,

                AttendanceId = attendance?.AttendanceId,

                WorkDate = dto.WorkDate.Date,

                RequestType = dto.RequestType,

                ExistingCheckIn = attendance?.CheckIn,
                ExistingCheckOut = attendance?.CheckOut,

                //RequestedCheckIn = dto.RequestedCheckIn,
                //RequestedCheckOut = dto.RequestedCheckOut,

                RequestedCheckIn =
    dto.RequestedCheckIn.HasValue
    ? TimeZoneInfo.ConvertTimeToUtc(
        DateTime.SpecifyKind(
            dto.RequestedCheckIn.Value,
            DateTimeKind.Unspecified),
        GetOrgTimezone(org.TimeZoneId))
    : null,

                RequestedCheckOut =
    dto.RequestedCheckOut.HasValue
    ? TimeZoneInfo.ConvertTimeToUtc(
        DateTime.SpecifyKind(
            dto.RequestedCheckOut.Value,
            DateTimeKind.Unspecified),
        GetOrgTimezone(org.TimeZoneId))
    : null,

                Reason = dto.Reason,

                Status = AttendanceCorrectionStatuses.Pending,

                CreatedAt = DateTime.UtcNow
            };

            _context.AttendanceCorrectionRequests.Add(request);

            await _context.SaveChangesAsync();
        }

        // =========================================================================
        // MY REQUESTS
        // =========================================================================
        public async Task<List<AttendanceCorrectionListDto>> GetMyRequestsAsync()
        {
            var user = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == user.Id)
                ?? throw new Exception("Employee not found");

            return await _context.AttendanceCorrectionRequests
    .Include(x => x.Employee)
        .ThenInclude(e => e.Department)
                .Where(x => x.EmployeeId == employee.EmployeeId)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new AttendanceCorrectionListDto
                {
                    AttendanceCorrectionRequestId =
                        x.AttendanceCorrectionRequestId,

                    EmployeeName =
                        x.Employee.FirstName + " " + x.Employee.LastName,

                    Department = x.Employee.Department.Name,

                    WorkDate = x.WorkDate,

                    RequestType = x.RequestType,

                    ExistingCheckIn = x.ExistingCheckIn,
                    ExistingCheckOut = x.ExistingCheckOut,

                    RequestedCheckIn = x.RequestedCheckIn,
                    RequestedCheckOut = x.RequestedCheckOut,

                    Reason = x.Reason,

                    Status = x.Status,

                    HRRemarks = x.HRRemarks,

                    CreatedAt = x.CreatedAt
                })
                .ToListAsync();
        }

        // =========================================================================
        // HR PENDING REQUESTS
        // =========================================================================
        public async Task<List<AttendanceCorrectionListDto>>
            GetPendingRequestsAsync()
        {
            var currentUser = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var currentEmployee = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == currentUser.Id)
                ?? throw new Exception("Employee not found");

            bool isOrgAdmin =
                await _userManager.IsInRoleAsync(currentUser, "OrgAdmin");

            var query = _context.AttendanceCorrectionRequests
                .Include(x => x.Employee)
                    .ThenInclude(e => e.Department)
                .Where(x => x.Status == AttendanceCorrectionStatuses.Pending);

            // HR sees only own department
            if (!isOrgAdmin)
            {
                query = query.Where(x =>
                    x.Employee.DepartmentId ==
                    currentEmployee.DepartmentId);
            }

            return await query
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new AttendanceCorrectionListDto
                {
                    AttendanceCorrectionRequestId =
                        x.AttendanceCorrectionRequestId,

                    EmployeeName =
                        x.Employee.FirstName + " " + x.Employee.LastName,

                    Department = x.Employee.Department.Name,

                    WorkDate = x.WorkDate,

                    RequestType = x.RequestType,

                    ExistingCheckIn = x.ExistingCheckIn,
                    ExistingCheckOut = x.ExistingCheckOut,

                    RequestedCheckIn = x.RequestedCheckIn,
                    RequestedCheckOut = x.RequestedCheckOut,

                    Reason = x.Reason,

                    Status = x.Status,

                    CreatedAt = x.CreatedAt
                })
                .ToListAsync();
        }


        // =========================================================================
        // HR ACTION
        // =========================================================================
        public async Task ProcessRequestAsync(
            HRActionAttendanceCorrectionDto dto)
        {
            var currentUser = await _currentUser.GetUserAsync()
                ?? throw new Exception("Unauthorized");

            var orgId = currentUser.OrganizationId ?? Guid.Empty;

            var hrEmployee = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == currentUser.Id)
                ?? throw new Exception("Employee not found");

            var request = await _context.AttendanceCorrectionRequests
                .Include(x => x.Employee)
                    .ThenInclude(e => e.Shift)
                .Include(x => x.Attendance)
                .FirstOrDefaultAsync(x =>
                    x.AttendanceCorrectionRequestId ==
                    dto.AttendanceCorrectionRequestId)
                ?? throw new Exception("Request not found");

            if (request.OrganizationId != orgId)
                throw new Exception("Unauthorized");

            if (request.Status != AttendanceCorrectionStatuses.Pending)
                throw new Exception("Request already processed");

            bool isOrgAdmin =
                await _userManager.IsInRoleAsync(currentUser, "OrgAdmin");

            bool isHR =
                await _userManager.IsInRoleAsync(currentUser, "HR");

            if (!isHR && !isOrgAdmin)
                throw new Exception("Unauthorized");

            // HR can only approve own department
            if (!isOrgAdmin)
            {
                if (request.Employee.DepartmentId !=
                    hrEmployee.DepartmentId)
                {
                    throw new Exception(
                        "You can only manage your department requests");
                }
            }

            // ─────────────────────────────────────────────────────────────
            // REJECT
            // ─────────────────────────────────────────────────────────────
            if (!dto.Approve)
            {
                if (string.IsNullOrWhiteSpace(dto.HRRemarks))
                    throw new Exception("Rejection remark is required");

                request.Status =
                    AttendanceCorrectionStatuses.Rejected;

                request.HRRemarks = dto.HRRemarks;

                request.ReviewedAt = DateTime.UtcNow;

                request.ReviewedByEmployeeId =
                    hrEmployee.EmployeeId;

                await _context.SaveChangesAsync();

                return;
            }

            // ─────────────────────────────────────────────────────────────
            // APPROVE
            // ─────────────────────────────────────────────────────────────

            var org = await _context.Organizations
                .FirstOrDefaultAsync(o =>
                    o.OrganizationId == orgId)
                ?? throw new Exception("Organization not found");

            var tz = GetOrgTimezone(org.TimeZoneId);

            var attendance = request.Attendance;

            // Create attendance if absent record didn't exist
            if (attendance == null)
            {
                attendance = new Attendance
                {
                    AttendanceId = Guid.NewGuid(),

                    OrganizationId = request.OrganizationId,

                    EmployeeId = request.EmployeeId,

                    UserId = request.Employee.UserId,

                    WorkDate = request.WorkDate.Date,

                    IsPresent = true,

                    CreatedAt = DateTime.UtcNow
                };

                _context.Attendances.Add(attendance);

                request.AttendanceId = attendance.AttendanceId;
            }

            // ─────────────────────────────────────────────────────────────
            // APPLY CORRECTIONS
            // ─────────────────────────────────────────────────────────────

            attendance.CheckIn =
                request.RequestedCheckIn;

            attendance.CheckOut =
                request.RequestedCheckOut;

            attendance.ActualCheckOut =
                request.RequestedCheckOut;

            attendance.IsManual = true;

            attendance.IsAutoCheckedOut = false;

            attendance.UpdatedAt = DateTime.UtcNow;

            attendance.UpdatedBy = hrEmployee.EmployeeId;

            attendance.IsPresent =
                attendance.CheckIn != null;

            // ─────────────────────────────────────────────────────────────
            // RECALCULATE
            // ─────────────────────────────────────────────────────────────

            _attendanceCalculationService.RecalculateAttendance(
                attendance,
                request.Employee.Shift,
                tz);

            // ─────────────────────────────────────────────────────────────
            // COMPLETE REQUEST
            // ─────────────────────────────────────────────────────────────

            request.Status =
                AttendanceCorrectionStatuses.Approved;

            request.HRRemarks = dto.HRRemarks;

            request.IsApplied = true;

            request.ReviewedAt = DateTime.UtcNow;

            request.ReviewedByEmployeeId =
                hrEmployee.EmployeeId;

            await _context.SaveChangesAsync();
        }

        private TimeZoneInfo GetOrgTimezone(string? timeZoneId)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(
                    timeZoneId ?? "UTC");
            }
            catch
            {
                return TimeZoneInfo.Utc;
            }
        }

        public async Task<List<AttendanceCorrectionListDto>>
    GetAllRequestsAsync()
        {
            return await _context.AttendanceCorrectionRequests
                .Include(x => x.Employee)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new AttendanceCorrectionListDto
                {
                    AttendanceCorrectionRequestId = x.AttendanceCorrectionRequestId,

                    WorkDate = x.WorkDate,

                    RequestType = x.RequestType,

                    RequestedCheckIn = x.RequestedCheckIn,

                    RequestedCheckOut = x.RequestedCheckOut,

                    Reason = x.Reason,

                    Status = x.Status,

                    HRRemarks = x.HRRemarks,

                    EmployeeName = x.Employee.FirstName
                })
                .ToListAsync();
        }


        public async Task ApproveAsync(Guid correctionId)
        {
            var correction = await _context.AttendanceCorrectionRequests
                .FirstOrDefaultAsync(x =>
                    x.AttendanceCorrectionRequestId == correctionId);

            if (correction == null)
                throw new Exception("Correction request not found.");

            var attendance = await _context.Attendances
                .FirstOrDefaultAsync(x =>
                    x.EmployeeId == correction.EmployeeId &&
                    x.WorkDate.Date == correction.WorkDate.Date);

            if (attendance == null)
            {
                attendance = new Attendance
                {
                    AttendanceId = Guid.NewGuid(),
                    EmployeeId = correction.EmployeeId,
                    WorkDate = correction.WorkDate.Date,
                    CheckIn = correction.RequestedCheckIn,
                    CheckOut = correction.RequestedCheckOut,
                    Status = "Present",
                    IsManual = true
                };

                _context.Attendances.Add(attendance);
            }
            else
            {
                attendance.CheckIn = correction.RequestedCheckIn;
                attendance.CheckOut = correction.RequestedCheckOut;
                attendance.IsManual = true;

                if (attendance.Status == "Absent")
                    attendance.Status = "Present";
            }

            correction.Status =
                AttendanceCorrectionStatuses.Approved;

            await _context.SaveChangesAsync();
        }


        public async Task RejectAsync(
    Guid correctionId,
    string remarks)
        {
            var correction = await _context.AttendanceCorrectionRequests
                .FirstOrDefaultAsync(x =>
                    x.AttendanceCorrectionRequestId == correctionId);

            if (correction == null)
                throw new Exception("Correction request not found.");

            correction.Status =
                AttendanceCorrectionStatuses.Rejected;

            correction.HRRemarks = remarks;

            await _context.SaveChangesAsync();
        }

    }
}