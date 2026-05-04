using Humatrix_HRMS.Models;

namespace Humatrix_HRMS.Helpers
{
    public static class DateHelper
    {
        public static bool IsWorkingDay(DateTime date, WorkWeek ww)
        {
            return date.DayOfWeek switch
            {
                DayOfWeek.Monday => ww.IsMondayWorking,
                DayOfWeek.Tuesday => ww.IsTuesdayWorking,
                DayOfWeek.Wednesday => ww.IsWednesdayWorking,
                DayOfWeek.Thursday => ww.IsThursdayWorking,
                DayOfWeek.Friday => ww.IsFridayWorking,
                DayOfWeek.Saturday => ww.IsSaturdayWorking,
                DayOfWeek.Sunday => ww.IsSundayWorking,
                _ => true
            };
        }
    }
}