using Humatrix_HRMS.Helpers;
using Humatrix_HRMS.Models;

namespace Humatrix_HRMS.Services
{
    public class AttendanceCalculationService
    {
        private const int OVERTIME_GRACE_MINUTES = 15;
        private const double MIN_OVERTIME_HOURS = 0.5;
        private const double MAX_OVERTIME_HOURS_DAY = 4.0;

        public void RecalculateAttendance(
            Attendance attendance,
            Shift? shift,
            TimeZoneInfo tz)
        {
            if (attendance.CheckIn == null)
                return;

            // ─────────────────────────────────────────────────────────────
            // NO CHECKOUT
            // ─────────────────────────────────────────────────────────────
            if (attendance.CheckOut == null)
            {
                attendance.TotalHours = null;
                attendance.OvertimeHours = 0;
                attendance.ApprovedOvertimeHours = 0;
                attendance.NeedsOvertimeApproval = false;

                attendance.Status =
                    attendance.Status == AttendanceStatuses.Late
                    ? AttendanceStatuses.Late
                    : AttendanceStatuses.Present;

                return;
            }

            // ─────────────────────────────────────────────────────────────
            // TOTAL HOURS
            // ─────────────────────────────────────────────────────────────
            var totalHours =
                (attendance.CheckOut.Value - attendance.CheckIn.Value)
                .TotalHours;

            attendance.TotalHours = Math.Round(totalHours, 2);

            // ─────────────────────────────────────────────────────────────
            // NO SHIFT
            // ─────────────────────────────────────────────────────────────
            if (shift == null)
            {
                attendance.Status = AttendanceStatuses.Present;

                attendance.NeedsOvertimeApproval = false;
                attendance.OvertimeHours = 0;

                return;
            }

            // ─────────────────────────────────────────────────────────────
            // SHIFT CALCULATIONS
            // ─────────────────────────────────────────────────────────────
            var checkInLocal =
                TimeZoneInfo.ConvertTimeFromUtc(
                    attendance.CheckIn.Value,
                    tz);

            var checkOutLocal =
                TimeZoneInfo.ConvertTimeFromUtc(
                    attendance.CheckOut.Value,
                    tz);

            var shiftDate = checkInLocal.Date;

            var shiftEnd = shiftDate.Add(shift.EndTime);

            // Overnight shift
            if (shift.EndTime < shift.StartTime)
                shiftEnd = shiftEnd.AddDays(1);

            // ─────────────────────────────────────────────────────────────
            // SYSTEM CHECKOUT
            // ─────────────────────────────────────────────────────────────
            attendance.SystemCheckOut =
                TimeZoneInfo.ConvertTimeToUtc(shiftEnd, tz);

            // ─────────────────────────────────────────────────────────────
            // OVERTIME
            // ─────────────────────────────────────────────────────────────
            var overtimeThreshold =
                shiftEnd.AddMinutes(OVERTIME_GRACE_MINUTES);

            if (checkOutLocal > overtimeThreshold)
            {
                var overtimeHours =
                    (checkOutLocal - shiftEnd).TotalHours;

                overtimeHours =
                    Math.Min(overtimeHours, MAX_OVERTIME_HOURS_DAY);

                if (overtimeHours >= MIN_OVERTIME_HOURS)
                {
                    attendance.NeedsOvertimeApproval = true;

                    attendance.OvertimeHours =
                        Math.Round(overtimeHours, 2);
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

            // ─────────────────────────────────────────────────────────────
            // STATUS
            // ─────────────────────────────────────────────────────────────
            bool leftEarly = checkOutLocal < shiftEnd;

            bool wasLate =
                attendance.Status == AttendanceStatuses.Late;

            if (totalHours < shift.MinimumHoursForHalfDay)
            {
                attendance.Status = AttendanceStatuses.ShortHours;
            }
            else if (totalHours < shift.MinimumHoursForFullDay)
            {
                attendance.Status = AttendanceStatuses.HalfDay;
            }
            else if (leftEarly)
            {
                attendance.Status = AttendanceStatuses.EarlyExit;
            }
            else
            {
                attendance.Status =
                    wasLate
                    ? AttendanceStatuses.Late
                    : AttendanceStatuses.Present;
            }
        }
    }
}