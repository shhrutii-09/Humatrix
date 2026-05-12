using Humatrix_HRMS.Data;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services
{
    /// <summary>
    /// Central policy validation service for the multi-org HRMS.
    ///
    /// Inject this into every domain service instead of repeating
    /// holiday / workweek / conflict queries inline.
    ///
    /// All methods are read-only (AsNoTracking). They throw descriptive
    /// exceptions on policy violations so callers can surface them directly
    /// to the UI without wrapping them again.
    /// </summary>
    public class HRPolicyValidationService
    {
        private readonly ApplicationDbContext _context;

        public HRPolicyValidationService(ApplicationDbContext context)
        {
            _context = context;
        }

        // =====================================================================
        // TIMEZONE HELPERS
        // =====================================================================

        public async Task<TimeZoneInfo> GetOrgTimezoneAsync(Guid orgId)
        {
            var tzId = await _context.Organizations
                .AsNoTracking()
                .Where(o => o.OrganizationId == orgId)
                .Select(o => o.TimeZoneId)
                .FirstOrDefaultAsync();
            return TimeHelper.GetOrgTimeZone(tzId);
        }

        /// <summary>Returns the current calendar date in the org's local timezone.</summary>
        public async Task<DateTime> GetOrgTodayAsync(Guid orgId)
        {
            var tz = await GetOrgTimezoneAsync(orgId);
            return TimeHelper.GetOrgDate(tz);
        }

        // =====================================================================
        // HOLIDAY CHECKS
        // =====================================================================

        /// <summary>
        /// Returns the mandatory (non-optional) holiday on <paramref name="date"/>
        /// for the org, or null if the day is not a mandatory holiday.
        /// </summary>
        public async Task<Holiday?> GetMandatoryHolidayAsync(Guid orgId, DateTime date)
        {
            return await _context.Holidays
                .AsNoTracking()
                .FirstOrDefaultAsync(h =>
                    h.OrganizationId == orgId &&
                    h.Date.Date == date.Date &&
                    !h.IsOptional);
        }

        public async Task<bool> IsMandatoryHolidayAsync(Guid orgId, DateTime date)
            => await GetMandatoryHolidayAsync(orgId, date) != null;

        // =====================================================================
        // WORK WEEK CHECKS
        // =====================================================================

        /// <summary>
        /// Returns the org's WorkWeek configuration.
        /// Falls back to a default Mon-Fri config when none is configured.
        /// </summary>
        public async Task<WorkWeek> GetWorkWeekAsync(Guid orgId)
        {
            return await _context.WorkWeeks
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.OrganizationId == orgId)
                ?? DefaultWorkWeek();
        }

        public async Task<bool> IsWorkingDayAsync(Guid orgId, DateTime date)
        {
            var ww = await GetWorkWeekAsync(orgId);
            return DateHelper.IsWorkingDay(date, ww);
        }

        // =====================================================================
        // COMPOSITE DATE GUARD
        // =====================================================================

        /// <summary>
        /// Asserts that <paramref name="date"/> is allowed for the given
        /// <paramref name="action"/>. Throws a user-friendly exception on violation.
        ///
        /// Supported actions: "CheckIn" | "Leave" | "WFH" | "Correction" | "Overtime"
        /// </summary>
        public async Task AssertDateIsAllowedAsync(Guid orgId, DateTime date, string action)
        {
            var holiday = await GetMandatoryHolidayAsync(orgId, date);

            if (holiday != null)
            {
                switch (action)
                {
                    case "CheckIn":
                        throw new Exception(
                            $"Check-in blocked: {date:dd MMM} is a mandatory holiday ({holiday.Name}).");
                    case "Leave":
                        throw new Exception(
                            $"Leave not required: {date:dd MMM} is already a mandatory holiday ({holiday.Name}).");
                    case "WFH":
                        throw new Exception(
                            $"WFH blocked: {date:dd MMM} is a mandatory holiday ({holiday.Name}).");
                    case "Correction":
                    case "Overtime":
                        // HR / employee may legitimately correct/claim OT on a holiday
                        // (e.g., emergency shift). HR decides at approval stage.
                        break;
                }
            }

            if (!await IsWorkingDayAsync(orgId, date))
            {
                switch (action)
                {
                    case "CheckIn":
                        throw new Exception(
                            $"Check-in blocked: {date:dddd, dd MMM} is a non-working day.");
                    case "WFH":
                        throw new Exception(
                            $"WFH blocked: {date:dddd, dd MMM} is a non-working day.");
                    case "Overtime":
                        throw new Exception(
                            $"Overtime blocked: {date:dddd, dd MMM} is a non-working day.");
                    case "Leave":
                    case "Correction":
                        // LeaveService handles working-day count upstream.
                        // Corrections on non-working days are allowed for HR review.
                        break;
                }
            }
        }

        // =====================================================================
        // CROSS-MODULE CONFLICT CHECKS
        // =====================================================================

        /// <summary>
        /// Returns an overlapping approved-or-pending leave, or null.
        /// </summary>
        public async Task<LeaveRequest?> GetLeaveConflictAsync(
            Guid employeeId,
            DateTime from,
            DateTime to)
        {
            return await _context.LeaveRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(l =>
                    l.EmployeeId == employeeId &&
                    (l.Status == "Pending" || l.Status == "Approved") &&
                    l.FromDate.Date <= to.Date &&
                    l.ToDate.Date >= from.Date);
        }

        public async Task AssertNoLeaveConflictAsync(
            Guid employeeId,
            DateTime from,
            DateTime to)
        {
            var c = await GetLeaveConflictAsync(employeeId, from, to);
            if (c != null)
                throw new Exception(
                    $"Conflict: A leave request ({c.Status}) already exists " +
                    $"between {c.FromDate:dd MMM} – {c.ToDate:dd MMM}.");
        }

        /// <summary>
        /// Returns an approved/pending WFH request for the given date, or null.
        /// </summary>
        public async Task<WorkFromHomeRequest?> GetWfhConflictAsync(
            Guid employeeId,
            DateTime date)
        {
            return await _context.WorkFromHomeRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(w =>
                    w.EmployeeId == employeeId &&
                    (w.Status == "Pending" || w.Status == "Approved") &&
                    w.Date.Date == date.Date);
        }

        public async Task AssertNoWfhConflictAsync(Guid employeeId, DateTime date)
        {
            var c = await GetWfhConflictAsync(employeeId, date);
            if (c != null)
                throw new Exception(
                    $"Conflict: A WFH request ({c.Status}) already exists for {date:dd MMM}.");
        }

        /// <summary>
        /// Returns a pending/approved correction request for the date, or null.
        /// </summary>
        public async Task<AttendanceCorrectionRequest?> GetCorrectionConflictAsync(
            Guid employeeId,
            DateTime date)
        {
            return await _context.AttendanceCorrectionRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.EmployeeId == employeeId &&
                    r.WorkDate.Date == date.Date &&
                    (r.Status == "Pending" || r.Status == "Approved"));
        }

        public async Task AssertNoCorrectionConflictAsync(Guid employeeId, DateTime date)
        {
            var c = await GetCorrectionConflictAsync(employeeId, date);
            if (c != null)
                throw new Exception(
                    $"Conflict: An attendance correction ({c.Status}) already exists for {date:dd MMM}.");
        }

        /// <summary>
        /// Returns a pending overtime request for the given attendance record, or null.
        /// </summary>
        public async Task<OvertimeRequest?> GetOvertimeConflictAsync(Guid attendanceId)
        {
            return await _context.OvertimeRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.AttendanceId == attendanceId &&
                    r.Status == "Pending");
        }

        public async Task AssertNoOvertimeConflictAsync(Guid attendanceId)
        {
            var c = await GetOvertimeConflictAsync(attendanceId);
            if (c != null)
                throw new Exception(
                    "An overtime request is already pending for this attendance record.");
        }

        // =====================================================================
        // UNIFIED EMPLOYEE-CAN-ACT CHECK
        // =====================================================================

        /// <summary>
        /// One call validates everything: date-level policy (holiday + workweek)
        /// AND cross-module conflicts.
        ///
        /// Throws a descriptive exception on any violation.
        /// Returns normally when all checks pass.
        /// </summary>
        public async Task AssertEmployeeCanActAsync(
            Guid orgId,
            Guid employeeId,
            DateTime date,
            string action)
        {
            // A. Date-level policy
            await AssertDateIsAllowedAsync(orgId, date, action);

            // B. Cross-module conflicts
            switch (action)
            {
                case "CheckIn":
                    await AssertNoLeaveConflictAsync(employeeId, date, date);
                    await AssertNoWfhConflictAsync(employeeId, date);
                    break;

                case "Leave":
                    // Range checked upstream; WFH still blocks single-day leaves.
                    await AssertNoWfhConflictAsync(employeeId, date);
                    break;

                case "WFH":
                    await AssertNoLeaveConflictAsync(employeeId, date, date);
                    break;

                case "Correction":
                    await AssertNoLeaveConflictAsync(employeeId, date, date);
                    await AssertNoWfhConflictAsync(employeeId, date);
                    break;

                    // Overtime: attendance record already exists.
                    // Caller uses AssertNoOvertimeConflictAsync(attendanceId) separately.
            }
        }

        // =====================================================================
        // PRIVATE HELPERS
        // =====================================================================

        private static WorkWeek DefaultWorkWeek() => new()
        {
            IsMondayWorking = true,
            IsTuesdayWorking = true,
            IsWednesdayWorking = true,
            IsThursdayWorking = true,
            IsFridayWorking = true,
            IsSaturdayWorking = false,
            IsSundayWorking = false
        };
    }
}