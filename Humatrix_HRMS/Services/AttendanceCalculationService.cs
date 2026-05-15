using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Models;

namespace Humatrix_HRMS.Services
{
    /// <summary>
    /// The ONLY place where attendance totals, overtime, status, and SystemCheckOut
    /// are computed. Every service (CheckOut, AutoCheckout, Correction approval,
    /// OT approval) calls this — no duplicate logic anywhere.
    ///
    /// CONTRACT:
    ///   • attendance.CheckIn  must already be set (UTC, Kind=Utc or Unspecified-from-EF).
    ///   • attendance.CheckOut must already be set (UTC) when you call Recalculate.
    ///   • attendance.Status   should already reflect Late/Present from CheckIn
    ///     (this method PRESERVES the Late flag).
    ///   • All timestamps written to attendance fields are UTC.
    ///   • This method never calls the database — pure calculation only.
    /// </summary>
    public class AttendanceCalculationService
    {
        // ── Delegate constants to the single source of truth ─────────────────────
        private const int OT_GRACE_MIN = AttendanceConstants.OvertimeGraceMinutes;
        private const double MIN_OT_HOURS = AttendanceConstants.MinOvertimeHours;
        private const double MAX_OT_HOURS = AttendanceConstants.MaxOvertimeHoursPerDay;

        // =========================================================================
        // PUBLIC ENTRY POINT
        // =========================================================================

        /// <summary>
        /// Recalculates TotalHours, OvertimeHours, NeedsOvertimeApproval,
        /// SystemCheckOut, and Status on an attendance record after any checkout
        /// time change.
        ///
        /// SAFE to call repeatedly — idempotent given the same inputs.
        /// </summary>
        public void Recalculate(
            Attendance attendance,
            Shift? shift,
            TimeZoneInfo tz)
        {
            // ── Guard: must have check-in ─────────────────────────────────────────
            if (attendance.CheckIn == null)
            {
                ClearComputedFields(attendance);
                return;
            }

            // ── Normalise UTC kind (EF returns Unspecified) ───────────────────────
            var checkInUtc = TimeHelper.EnsureUtc(attendance.CheckIn.Value);

            // ── No checkout yet ───────────────────────────────────────────────────
            if (attendance.CheckOut == null)
            {
                attendance.TotalHours = null;
                attendance.OvertimeHours = 0;
                attendance.ApprovedOvertimeHours = 0;
                attendance.NeedsOvertimeApproval = false;
                // Status remains as set during check-in (Late / Present)
                // unless it was already set to a terminal value by HR.
                return;
            }

            var checkOutUtc = TimeHelper.EnsureUtc(attendance.CheckOut.Value);

            // ── Total hours (UTC subtraction — timezone-safe) ─────────────────────
            var totalHours = (checkOutUtc - checkInUtc).TotalHours;
            if (totalHours < 0) totalHours = 0; // safety guard
            attendance.TotalHours = Math.Round(totalHours, 2);

            // ── No shift — minimal status, no overtime ────────────────────────────
            if (shift == null)
            {
                attendance.Status = AttendanceStatuses.Present;
                attendance.NeedsOvertimeApproval = false;
                attendance.OvertimeHours = 0;
                // SystemCheckOut = checkOut when no shift configured
                attendance.SystemCheckOut = checkOutUtc;
                return;
            }

            // ── Convert to org-local for shift boundary comparisons ───────────────
            var checkInLocal = TimeHelper.ToOrgLocal(checkInUtc, tz);
            var checkOutLocal = TimeHelper.ToOrgLocal(checkOutUtc, tz);

            // shiftDate = the calendar date on which the shift started
            // For overnight shifts the check-in anchor day is WorkDate, which the
            // caller should ensure matches the record.WorkDate.
            var shiftDate = checkInLocal.Date;
            var shiftStartLocal = shiftDate.Add(shift.StartTime);
            var shiftEndLocal = TimeHelper.GetShiftEndLocal(shiftDate, shift);

            // ── SystemCheckOut — set once, never overwrite if already set by a
            //    previous correct calculation, UNLESS we are explicitly recalculating
            //    (e.g., after correction). Always stamp it.
            var shiftEndUtc = TimeHelper.ToUtc(shiftEndLocal, tz);
            attendance.SystemCheckOut = DateTime.SpecifyKind(shiftEndUtc, DateTimeKind.Utc);

            // Employee must actually overlap the shift window
            //bool overlapsShift =
            //    checkInLocal < shiftEndLocal &&
            //    checkOutLocal > checkInLocal;

            //// If attendance is completely outside shift timing,
            //// do NOT allow overtime
            //if (!overlapsShift)
            //{
            //    attendance.NeedsOvertimeApproval = false;
            //    attendance.OvertimeHours = 0;
            //}
            //else
            //{

                //    // ── Overtime ──────────────────────────────────────────────────────────
                //    var overtimeThreshold = shiftEndLocal.AddMinutes(OT_GRACE_MIN);
                //bool wasLate = attendance.Status == AttendanceStatuses.Late;

                //if (checkOutLocal > overtimeThreshold)
                //{
                //    var rawOtHours = (checkOutLocal - shiftEndLocal).TotalHours;
                //    rawOtHours = Math.Min(rawOtHours, MAX_OT_HOURS);

                //    if (rawOtHours >= MIN_OT_HOURS)
                //    {
                //        attendance.NeedsOvertimeApproval = true;
                //        attendance.OvertimeHours = Math.Round(rawOtHours, 2);
                //    }
                //    else
                //    {
                //        attendance.NeedsOvertimeApproval = false;
                //        attendance.OvertimeHours = 0;
                //    }
                //}
                //else
                //{
                //    attendance.NeedsOvertimeApproval = false;
                //    attendance.OvertimeHours = 0;
                //}

                // ── Overtime ──────────────────────────────────────────────────────────
                // ── Overtime ──────────────────────────────────────────────────────────
                var overtimeThreshold = shiftEndLocal.AddMinutes(OT_GRACE_MIN);
                bool wasLate = attendance.Status == AttendanceStatuses.Late;

                // IMPORTANT FIX:
                // Employee must actually overlap the shift window
                bool overlapsShift =
                    checkInLocal < shiftEndLocal &&
                    checkOutLocal > shiftStartLocal;

                if (!overlapsShift)
                {
                    attendance.NeedsOvertimeApproval = false;
                    attendance.OvertimeHours = 0;
                }
                else
                {
                    if (checkOutLocal > overtimeThreshold)
                    {
                        var rawOtHours = (checkOutLocal - shiftEndLocal).TotalHours;
                        rawOtHours = Math.Min(rawOtHours, MAX_OT_HOURS);

                        if (rawOtHours >= MIN_OT_HOURS)
                        {
                            attendance.NeedsOvertimeApproval = true;
                            attendance.OvertimeHours = Math.Round(rawOtHours, 2);
                        }
                        else
                        {
                            attendance.NeedsOvertimeApproval = false;
                            attendance.OvertimeHours = 0;
                        }
                    }
                    else
                    {
                        attendance.NeedsOvertimeApproval = false;
                        attendance.OvertimeHours = 0;
                    }
                }
                //if (checkOutLocal > overtimeThreshold)
                //    {
                //        var rawOtHours = (checkOutLocal - shiftEndLocal).TotalHours;
                //        rawOtHours = Math.Min(rawOtHours, MAX_OT_HOURS);

                //        if (rawOtHours >= MIN_OT_HOURS)
                //        {
                //            attendance.NeedsOvertimeApproval = true;
                //            attendance.OvertimeHours = Math.Round(rawOtHours, 2);
                //        }
                //        else
                //        {
                //            attendance.NeedsOvertimeApproval = false;
                //            attendance.OvertimeHours = 0;
                //        }
                //    }
                //    else
                //    {
                //        attendance.NeedsOvertimeApproval = false;
                //        attendance.OvertimeHours = 0;
                //    }
                //}

                // ── Attendance status ─────────────────────────────────────────────────
                bool leftEarly = checkOutLocal < shiftEndLocal;

                if (totalHours < shift.MinimumHoursForHalfDay)
                    attendance.Status = AttendanceStatuses.ShortHours;
                else if (totalHours < shift.MinimumHoursForFullDay)
                    attendance.Status = AttendanceStatuses.HalfDay;
                else if (leftEarly)
                    attendance.Status = AttendanceStatuses.EarlyExit;
                else
                    attendance.Status = wasLate
                        ? AttendanceStatuses.Late
                        : AttendanceStatuses.Present;
            }
        

        // =========================================================================
        // REAPPLY AFTER OT APPROVAL / REJECTION (no shift boundary re-calc)
        // =========================================================================

        /// <summary>
        /// Re-evaluates Status and TotalHours after OT approval changes CheckOut.
        /// Does NOT touch SystemCheckOut (it's fixed once calculated).
        /// Preserves the Late flag set at check-in.
        /// </summary>
        public void ReapplyStatusAfterOtReview(Attendance attendance, Shift? shift)
        {
            if (attendance.CheckIn == null || attendance.CheckOut == null)
                return;

            var checkInUtc = TimeHelper.EnsureUtc(attendance.CheckIn.Value);
            var checkOutUtc = TimeHelper.EnsureUtc(attendance.CheckOut.Value);
            var totalHours = (checkOutUtc - checkInUtc).TotalHours;
            if (totalHours < 0) totalHours = 0;
            attendance.TotalHours = Math.Round(totalHours, 2);

            if (shift == null)
            {
                attendance.Status = AttendanceStatuses.Present;
                return;
            }

            bool wasLate = attendance.Status == AttendanceStatuses.Late
                        || attendance.Status == AttendanceStatuses.EarlyExit;

            if (totalHours < shift.MinimumHoursForHalfDay)
                attendance.Status = AttendanceStatuses.ShortHours;
            else if (totalHours < shift.MinimumHoursForFullDay)
                attendance.Status = AttendanceStatuses.HalfDay;
            else
                attendance.Status = wasLate
                    ? AttendanceStatuses.Late
                    : AttendanceStatuses.Present;
        }

        // =========================================================================
        // HELPERS
        // =========================================================================

        private static void ClearComputedFields(Attendance attendance)
        {
            attendance.TotalHours = null;
            attendance.OvertimeHours = 0;
            attendance.ApprovedOvertimeHours = 0;
            attendance.NeedsOvertimeApproval = false;
        }
    }
}