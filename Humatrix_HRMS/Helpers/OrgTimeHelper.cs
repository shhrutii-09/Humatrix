namespace Humatrix_HRMS.Helpers
{
    public static class OrgTimeHelper
    {
        public static DateTime? ToOrgTime(DateTime? utcTime, string? timeZoneId)
        {
            if (!utcTime.HasValue)
                return null;

            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(
                    string.IsNullOrWhiteSpace(timeZoneId)
                        ? "UTC"
                        : timeZoneId);

                return TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(utcTime.Value, DateTimeKind.Utc),
                    tz);
            }
            catch
            {
                return utcTime;
            }
        }
    }
}