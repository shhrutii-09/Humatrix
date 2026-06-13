using Humatrix_HRMS.Models;

namespace Humatrix_HRMS.Helpers
{
    public static class TimeHelper
    {
       
        public static TimeZoneInfo GetOrgTimeZone(string? timeZoneId)
        {
            if (string.IsNullOrWhiteSpace(timeZoneId))
                return TimeZoneInfo.Utc;
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch
            {
                return TimeZoneInfo.Utc;
            }
        }

                public static DateTime EnsureUtc(DateTime dateTime)
        {
            return dateTime.Kind == DateTimeKind.Utc
                ? dateTime
                : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        }
        public static DateTime? EnsureUtc(DateTime? dateTime)
            => dateTime.HasValue ? EnsureUtc(dateTime.Value) : null;

        public static DateTime ToOrgLocal(DateTime utcTime, TimeZoneInfo tz)
            => TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(utcTime), tz);

        public static DateTime ToOrgLocal(DateTime utcTime, string? timeZoneId)
            => ToOrgLocal(utcTime, GetOrgTimeZone(timeZoneId));

        public static DateTime? ToOrgLocal(DateTime? utcTime, TimeZoneInfo tz)
            => utcTime.HasValue ? ToOrgLocal(utcTime.Value, tz) : null;



     
        public static DateTime ToUtc(DateTime localTime, TimeZoneInfo tz)
        {
            var unspecified = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
        }

        public static DateTime ToUtc(DateTime localTime, string? timeZoneId)
            => ToUtc(localTime, GetOrgTimeZone(timeZoneId));
  public static DateTime GetOrgDate(string? timeZoneId)
            => ToOrgLocal(DateTime.UtcNow, GetOrgTimeZone(timeZoneId)).Date;

        public static DateTime GetOrgDate(TimeZoneInfo tz)
            => ToOrgLocal(DateTime.UtcNow, tz).Date;
        public static DateTime GetOrgNow(TimeZoneInfo tz)
            => ToOrgLocal(DateTime.UtcNow, tz);

        public static DateTime GetOrgNow(string? timeZoneId)
            => GetOrgNow(GetOrgTimeZone(timeZoneId));

        public static DateTime GetShiftEndLocal(DateTime shiftDate, Shift shift)
        {
            var shiftEnd = shiftDate.Add(shift.EndTime);
            if (shift.EndTime < shift.StartTime)
                shiftEnd = shiftEnd.AddDays(1);
            return shiftEnd;
        }

        public static DateTime GetShiftEndUtc(DateTime shiftDate, Shift shift, TimeZoneInfo tz)
        {
            var local = GetShiftEndLocal(shiftDate, shift);
            return TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(local, DateTimeKind.Unspecified), tz);
        }

        public static string FormatOrgTime(DateTime? utcTime, TimeZoneInfo tz)
        {
            if (!utcTime.HasValue) return "--:--";
            return ToOrgLocal(utcTime.Value, tz).ToString("hh:mm tt");
        }

        public static string FormatOrgTime(DateTime? utcTime, string? timeZoneId = null)
            => FormatOrgTime(utcTime, GetOrgTimeZone(timeZoneId ?? "UTC"));

        public static string FormatOrgDateTime(DateTime? utcTime, TimeZoneInfo tz)
        {
            if (!utcTime.HasValue) return "--";
            return ToOrgLocal(utcTime.Value, tz).ToString("dd MMM yyyy hh:mm tt");
        }


        // Convert UTC to Organization Local Time for DISPLAY

        // Convert Local (Organization) to UTC for STORAGE
        public static DateTime? ToUtc(DateTime? localTime, TimeZoneInfo tz)
            => localTime.HasValue ? ToUtc(localTime.Value, tz) : null;

        // Format for display - USE THIS EVERYWHERE FOR DISPLAY
        public static string FormatTime(DateTime? utcTime, TimeZoneInfo tz)
        {
            if (!utcTime.HasValue) return "--:--";
            return ToOrgLocal(utcTime.Value, tz).ToString("hh:mm tt");
        }

        public static string FormatOrgDateTime(DateTime? utcTime, string? timeZoneId = null)
            => FormatOrgDateTime(utcTime, GetOrgTimeZone(timeZoneId ?? "UTC"));
    }
}