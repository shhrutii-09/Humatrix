namespace Humatrix_HRMS.Helpers
{
    public static class TimeHelper
    {
        public static DateTime GetOrgNow(string timeZoneId)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            }
            catch
            {
                return DateTime.UtcNow;
            }
        }

        public static DateTime GetOrgDate(string timeZoneId)
        {
            return GetOrgNow(timeZoneId).Date;
        }
    }
}
