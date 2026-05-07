namespace Humatrix_HRMS.Helpers
{
    public static class TimeHelper
    {
        public static string FormatOrgTime(
            DateTime? utcTime,
            string timeZoneId = "India Standard Time")
        {
            if (!utcTime.HasValue)
                return "--";

            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

            // IMPORTANT
            var utc = DateTime.SpecifyKind(
                utcTime.Value,
                DateTimeKind.Utc);

            return TimeZoneInfo
                .ConvertTimeFromUtc(utc, tz)
                .ToString("hh:mm tt");
        }

    public static DateTime GetOrgDate(string? timeZoneId)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(
                    string.IsNullOrWhiteSpace(timeZoneId)
                        ? "UTC"
                        : timeZoneId);

                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
            }
            catch
            {
                return DateTime.UtcNow.Date;
            }
        }

        //public static DateTime GetOrgDate(string timeZoneId)
        //{
        //    return GetOrgNow(timeZoneId).Date;
        //}


    }
}
