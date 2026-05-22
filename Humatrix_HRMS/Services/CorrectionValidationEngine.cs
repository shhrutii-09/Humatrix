using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOs;
using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;
using CorrectionTypes = Humatrix_HRMS.Helpers.CorrectionTypes;

namespace Humatrix_HRMS.Services
{
    /// <summary>
    /// Stateless validation engine for attendance correction requests.
    /// All methods are pure: they query the DB and return/throw, never mutate.
    ///
    /// PRODUCTION CONTRACT:
    ///   • Accepts a short-lived DbContext passed in from the calling service.
    ///   • Never calls SaveChanges.
    ///   • Throws CorrectionValidationException for business rule violations.
    ///   • Throws UnauthorizedAccessException for security violations.
    ///   • Thread-safe (no instance state).
    /// </summary>
    public class CorrectionValidationEngine
    {
        // ── Correction type ───────────────────────────────────────────────────────

        public static void AssertValidCorrectionType(string correctionType)
        {
            if (string.IsNullOrWhiteSpace(correctionType))
                throw new CorrectionValidationException("Correction type is required.");

            if (!CorrectionTypes.All.Contains(correctionType))
                throw new CorrectionValidationException(
                    $"Invalid correction type: '{correctionType}'. " +
                    $"Valid types: {string.Join(", ", CorrectionTypes.All)}");
        }

        public static void AssertEmployeeSubmittableType(string correctionType)
        {
            if (!CorrectionTypes.EmployeeSubmittable.Contains(correctionType))
                throw new CorrectionValidationException(
                    $"Correction type '{correctionType}' must be submitted by HR only.");
        }

        // ── Date validation ───────────────────────────────────────────────────────

        public static void AssertValidWorkDate(DateTime workDate, TimeZoneInfo tz)
        {
            var orgToday = TimeHelper.GetOrgDate(tz);

            if (workDate.Date > orgToday)
                throw new CorrectionValidationException(
                    "Correction date cannot be in the future.");

            if (workDate.Date < orgToday.AddDays(-AttendanceConstants.MaxCorrectionLookbackDays))
                throw new CorrectionValidationException(
                    $"Corrections can only be submitted for the last " +
                    $"{AttendanceConstants.MaxCorrectionLookbackDays} calendar days.");
        }

        // ── Org-level business rules ──────────────────────────────────────────────

        public static async Task AssertNotHolidayAsync(
            ApplicationDbContext db,
            Guid organizationId,
            DateTime workDate)
        {
            var isHoliday = await db.Holidays
                .AnyAsync(h =>
                    h.OrganizationId == organizationId &&
                    h.Date.Date == workDate.Date &&
                    !h.IsOptional);

            if (isHoliday)
                throw new CorrectionValidationException(
                    $"{workDate:dd MMM yyyy} is a public holiday. " +
                    "Attendance corrections cannot be submitted for holidays.");
        }

        public static async Task AssertIsWorkingDayAsync(
            ApplicationDbContext db,
            Guid organizationId,
            DateTime workDate)
        {
            var workWeek = await db.WorkWeeks
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.OrganizationId == organizationId);

            if (workWeek == null) return; // no config = all days allowed

            bool isWorking = workDate.DayOfWeek switch
            {
                DayOfWeek.Monday => workWeek.IsMondayWorking,
                DayOfWeek.Tuesday => workWeek.IsTuesdayWorking,
                DayOfWeek.Wednesday => workWeek.IsWednesdayWorking,
                DayOfWeek.Thursday => workWeek.IsThursdayWorking,
                DayOfWeek.Friday => workWeek.IsFridayWorking,
                DayOfWeek.Saturday => workWeek.IsSaturdayWorking,
                DayOfWeek.Sunday => workWeek.IsSundayWorking,
                _ => false
            };

            if (!isWorking)
                throw new CorrectionValidationException(
                    $"{workDate:dddd, dd MMM yyyy} is a non-working day " +
                    "per your organisation's work week configuration.");
        }

        public static async Task AssertNotOnApprovedLeaveAsync(
            ApplicationDbContext db,
            Guid employeeId,
            DateTime workDate)
        {
            bool onLeave = await db.LeaveRequests
                .AnyAsync(l =>
                    l.EmployeeId == employeeId &&
                    l.Status == "Approved" &&
                    l.FromDate.Date <= workDate.Date &&
                    l.ToDate.Date >= workDate.Date);

            if (onLeave)
                throw new CorrectionValidationException(
                    $"Employee has an approved leave on {workDate:dd MMM yyyy}. " +
                    "Attendance correction is not permitted while on approved leave.");
        }

        public static async Task AssertNotOnApprovedWfhAsync(
            ApplicationDbContext db,
            Guid employeeId,
            DateTime workDate)
        {
            bool onWfh = await db.WorkFromHomeRequests
                .AnyAsync(w =>
                    w.EmployeeId == employeeId &&
                    w.Status == "Approved" &&
                    w.Date.Date == workDate.Date);

            if (onWfh)
                throw new CorrectionValidationException(
                    $"Employee has an approved Work-From-Home on {workDate:dd MMM yyyy}. " +
                    "Use the WFH attendance process instead of a correction request.");
        }

        // ── Duplicate / conflict checks ───────────────────────────────────────────

        //public static async Task AssertNoDuplicatePendingAsync(
        //    ApplicationDbContext db,
        //    Guid employeeId,
        //    DateTime workDate)
        public static async Task AssertNoDuplicatePendingAsync(
    ApplicationDbContext db,
    Guid employeeId,
    DateTime workDate,
    string correctionType)
        {
            bool exists = await db.AttendanceCorrectionRequests
                .AsNoTracking()
                .AnyAsync(r =>
                    r.EmployeeId == employeeId &&
                    r.WorkDate.Date == workDate.Date &&
                    r.CorrectionType == correctionType &&
                    r.Status == CorrectionStatuses.Pending);

            if (exists)
            {
                throw new CorrectionValidationException(
                    $"A pending '{correctionType}' request already exists for this date.");
            }
        }

        public static async Task AssertNoDuplicateApprovedForTypeAsync(
            ApplicationDbContext db,
            Guid employeeId,
            DateTime workDate,
            string correctionType)
        {
            bool alreadyApproved = await db.AttendanceCorrectionRequests
                .AnyAsync(r =>
                    r.EmployeeId == employeeId &&
                    r.WorkDate.Date == workDate.Date &&
                    r.CorrectionType == correctionType &&
                    r.Status == CorrectionStatuses.Approved &&
                    r.IsApplied);

            if (alreadyApproved)
                throw new CorrectionValidationException(
                    $"A '{correctionType}' correction has already been approved and applied " +
                    $"for {workDate:dd MMM yyyy}. Contact HR if further changes are needed.");
        }

        // ── Attendance state vs correction type ───────────────────────────────────

        public static void AssertCorrectionTypeMatchesAttendanceState(
            string correctionType,
            Attendance? existing)
        {
            switch (correctionType)
            {
                case CorrectionTypes.ForgotCheckIn:
                    if (existing?.CheckIn != null)
                        throw new CorrectionValidationException(
                            "A check-in already exists for this date. Use 'Wrong Time' instead.");
                    break;

                case CorrectionTypes.ForgotCheckOut:
                    if (existing == null || existing.CheckIn == null)
                        throw new CorrectionValidationException(
                            "No check-in record found. Cannot request a forgotten check-out.");
                    if (existing.CheckOut != null && !existing.IsAutoCheckedOut)
                        throw new CorrectionValidationException(
                            "A manual check-out already exists. Use 'Wrong Time' instead.");
                    break;

                case CorrectionTypes.WrongTime:
                    if (existing == null)
                        throw new CorrectionValidationException(
                            "No attendance record found for this date. " +
                            "Use 'Absent but Worked' if you were present but not recorded.");
                    break;

                case CorrectionTypes.AbsentButWorked:
                    if (existing?.CheckIn != null)
                        throw new CorrectionValidationException(
                            "An attendance record with check-in already exists for this date. " +
                            "Use 'Wrong Time' to correct it instead.");
                    break;

                case CorrectionTypes.OvertimeCorrection:
                    if (existing == null || existing.CheckIn == null)
                        throw new CorrectionValidationException(
                            "No check-in found. Cannot submit an overtime correction " +
                            "without an existing attendance record.");
                    break;

                case CorrectionTypes.HrManualCorrection:
                    break; // HR can correct any state
            }
        }

        // ── Time consistency ──────────────────────────────────────────────────────

        public static void AssertTimeConsistency(
            string correctionType,
            DateTime? checkInUtc,
            DateTime? checkOutUtc)
        {
            if (CorrectionTypes.RequiresCheckIn.Contains(correctionType) && checkInUtc == null)
                throw new CorrectionValidationException(
                    $"Check-in time is required for '{correctionType}'.");

            if (CorrectionTypes.RequiresCheckOut.Contains(correctionType) && checkOutUtc == null)
                throw new CorrectionValidationException(
                    $"Check-out time is required for '{correctionType}'.");

            if (checkInUtc.HasValue && checkOutUtc.HasValue)
            {
                if (checkOutUtc.Value <= checkInUtc.Value)
                    throw new CorrectionValidationException(
                        "Check-out time must be after check-in time.");

                var span = (checkOutUtc.Value - checkInUtc.Value).TotalHours;
                if (span > AttendanceConstants.MaxWorkingDurationHours)
                    throw new CorrectionValidationException(
                        $"Requested working duration ({span:F1}h) exceeds the maximum " +
                        $"allowed ({AttendanceConstants.MaxWorkingDurationHours}h). " +
                        "Please verify the times or contact HR.");
            }
        }

        // ── Security / role enforcement ───────────────────────────────────────────

        public static void AssertReviewerCanReviewRequest(
       AttendanceCorrectionRequest request,
       Employee? reviewer,
       bool isHr,
       bool isOrgAdmin)
        {
            // =========================================================
            // ORGADMIN
            // =========================================================
            if (isOrgAdmin)
            {
                // OrgAdmin can review ONLY OrgAdmin-level requests
                if (request.ReviewLevel != CorrectionReviewLevels.OrgAdmin)
                {
                    throw new UnauthorizedAccessException(
                        "This request is not assigned to OrgAdmin review.");
                }

                return;
            }

            // =========================================================
            // HR
            // =========================================================
            if (isHr)
            {
                if (reviewer == null)
                {
                    throw new UnauthorizedAccessException(
                        "HR employee profile not found.");
                }

                // Must be HR-level request
                if (request.ReviewLevel != CorrectionReviewLevels.Hr)
                {
                    throw new UnauthorizedAccessException(
                        "This request is not assigned to HR review.");
                }

                return;
            }

            throw new UnauthorizedAccessException(
                "You are not authorised to review correction requests.");
        }

        public static void AssertSameOrganization(
            AttendanceCorrectionRequest request,
            Guid reviewerOrgId)
        {
            if (request.OrganizationId != reviewerOrgId)
                throw new UnauthorizedAccessException(
                    "You cannot access correction requests from another organisation.");
        }

        // ── Payroll lock ──────────────────────────────────────────────────────────

        public static void AssertNotPayrollLocked(AttendanceCorrectionRequest? existing)
        {
            if (existing?.IsPayrollPeriodLocked == true)
                throw new CorrectionValidationException(
                    "This attendance period is locked for payroll processing. " +
                    "Corrections are not permitted. Contact your payroll administrator.");
        }

        // ── Pre-validation for UI (does not throw — returns result) ──────────────

        public static async Task<CorrectionPreValidationResult> PreValidateAsync(
            ApplicationDbContext db,
            Guid organizationId,
            Guid employeeId,
            DateTime workDate,
            string correctionType,
            TimeZoneInfo tz)
        {
            var result = new CorrectionPreValidationResult();

            try { AssertValidWorkDate(workDate, tz); }
            catch { result.IsAllowed = false; result.BlockReason = "Date is outside allowed range."; return result; }

            result.IsHoliday = await db.Holidays
                .AnyAsync(h => h.OrganizationId == organizationId && h.Date.Date == workDate.Date && !h.IsOptional);

            var workWeek = await db.WorkWeeks.AsNoTracking()
                .FirstOrDefaultAsync(w => w.OrganizationId == organizationId);

            if (workWeek != null)
            {
                result.IsWeeklyOff = workDate.DayOfWeek switch
                {
                    DayOfWeek.Monday => !workWeek.IsMondayWorking,
                    DayOfWeek.Tuesday => !workWeek.IsTuesdayWorking,
                    DayOfWeek.Wednesday => !workWeek.IsWednesdayWorking,
                    DayOfWeek.Thursday => !workWeek.IsThursdayWorking,
                    DayOfWeek.Friday => !workWeek.IsFridayWorking,
                    DayOfWeek.Saturday => !workWeek.IsSaturdayWorking,
                    DayOfWeek.Sunday => !workWeek.IsSundayWorking,
                    _ => false
                };
            }

            result.IsOnLeave = await db.LeaveRequests
                .AnyAsync(l => l.EmployeeId == employeeId && l.Status == "Approved"
                    && l.FromDate.Date <= workDate.Date && l.ToDate.Date >= workDate.Date);

            result.IsOnWfh = await db.WorkFromHomeRequests
                .AnyAsync(w => w.EmployeeId == employeeId && w.Status == "Approved"
                    && w.Date.Date == workDate.Date);

            result.HasExistingPending = await db.AttendanceCorrectionRequests
                .AnyAsync(r => r.EmployeeId == employeeId
                    && r.WorkDate.Date == workDate.Date
                    && r.Status == CorrectionStatuses.Pending);

            var attendance = await db.Attendances.AsNoTracking()
                .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.WorkDate.Date == workDate.Date);

            result.AttendanceRecordExists = attendance != null;
            result.ExistingStatus = attendance?.Status;

            //result.IsAllowed = !result.IsHoliday && !result.IsWeeklyOff
            //                 && !result.IsOnLeave && !result.HasExistingPending;
            result.IsAllowed = !result.IsHoliday
                 && !result.IsWeeklyOff
                 && !result.IsOnLeave
                 && !result.IsOnWfh
                 && !result.HasExistingPending;

            //if (!result.IsAllowed)
            //{
            //    if (result.IsHoliday) result.BlockReason = "Public holiday.";
            //    else if (result.IsWeeklyOff) result.BlockReason = "Non-working day.";
            //    else if (result.IsOnLeave) result.BlockReason = "Employee is on approved leave.";
            //    else if (result.HasExistingPending) result.BlockReason = "Pending request already exists.";
            //}

            if (!result.IsAllowed)
            {
                if (result.IsHoliday)
                    result.BlockReason = "Public holiday.";

                else if (result.IsWeeklyOff)
                    result.BlockReason = "Non-working day.";

                else if (result.IsOnLeave)
                    result.BlockReason = "Employee is on approved leave.";

                else if (result.IsOnWfh)
                    result.BlockReason = "Employee is on approved Work From Home.";

                else if (result.HasExistingPending)
                    result.BlockReason = "Pending request already exists.";
            }

            return result;
        }
    }
}