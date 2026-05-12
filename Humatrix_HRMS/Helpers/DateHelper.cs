using Humatrix_HRMS.Models;

namespace Humatrix_HRMS.Helpers
{
    /// <summary>
    /// Date-level utilities — working-day checks, shift boundary helpers.
    /// All methods are pure (no I/O) so they can be called from any layer.
    /// </summary>
    public static class DateHelper
    {
        /// <summary>
        /// Returns true when <paramref name="date"/> is a working day
        /// according to the org's WorkWeek configuration.
        /// </summary>
        public static bool IsWorkingDay(DateTime date, WorkWeek workWeek)
        {
            return date.DayOfWeek switch
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
        }

        /// <summary>
        /// Counts calendar days between <paramref name="from"/> and <paramref name="to"/>
        /// (inclusive) that are working days AND not in the <paramref name="holidays"/> set.
        /// </summary>
        public static int CountWorkingDays(
            DateTime from,
            DateTime to,
            WorkWeek workWeek,
            IEnumerable<DateTime> holidays)
        {
            var holidaySet = holidays.Select(h => h.Date).ToHashSet();
            int count = 0;
            for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
            {
                if (IsWorkingDay(d, workWeek) && !holidaySet.Contains(d))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Checks whether a shift that started on <paramref name="startDate"/>
        /// crosses midnight into the next calendar day.
        /// </summary>
        public static bool IsOvernightShift(Shift shift)
            => shift.EndTime < shift.StartTime;

        /// <summary>
        /// For a check-in that occurs AFTER midnight, determines whether it belongs
        /// to the previous calendar day's overnight shift by comparing the check-in
        /// local time against the shift's end time.
        ///
        /// Example: shift 22:00 – 06:00. Employee checks in at 23:30 → WorkDate = today.
        ///          Employee checks in at 00:30 → still on yesterday's shift.
        /// </summary>
        public static bool IsLateNightCheckin(TimeSpan checkInTime, Shift shift)
        {
            if (!IsOvernightShift(shift)) return false;
            // Check-in time is between midnight and shift end → belongs to previous day
            return checkInTime < shift.EndTime;
        }
    }
}