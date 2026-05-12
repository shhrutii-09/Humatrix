namespace Humatrix_HRMS.Helpers
{
    /// <summary>
    /// Single source of truth for all attendance-related constants.
    /// Change here; everything else picks it up automatically.
    /// </summary>
    public static class AttendanceConstants
    {
        // ── Overtime ──────────────────────────────────────────────────────────────
        /// <summary>Minutes beyond shift end before overtime eligibility begins.</summary>
        public const int OvertimeGraceMinutes = 15;

        /// <summary>Minimum overtime duration that triggers the approval flow.</summary>
        public const double MinOvertimeHours = 0.25; // 15 min

        /// <summary>Organisation-level hard cap on overtime per day.</summary>
        public const double MaxOvertimeHoursPerDay = 4.0;

        // ── Auto-checkout ─────────────────────────────────────────────────────────
        /// <summary>
        /// Grace window (hours) added after the maximum overtime window before the
        /// background job forces auto-checkout.
        /// Total window = shift end + MaxOvertimeHoursPerDay + AutoCheckoutGraceHours.
        /// </summary>
        public const double AutoCheckoutGraceHours = 0.5;

        // ── Geolocation ───────────────────────────────────────────────────────────
        /// <summary>Extra buffer (metres) added to office radius on checkout only.</summary>
        public const int GeoBufferMetersOnCheckout = 50;
    }

    /// <summary>
    /// Canonical string constants for Attendance.Status.
    /// Use these everywhere — never inline string literals.
    /// </summary>
    public static class AttendanceStatuses
    {
        public const string Present = "Present";
        public const string Late = "Late";
        public const string HalfDay = "Half Day";
        public const string ShortHours = "Short Hours";
        public const string EarlyExit = "Early Exit";
        public const string Absent = "Absent";
        public const string OnLeave = "On Leave";
        public const string HalfDayLeave = "Half Day Leave";
        public const string WorkFromHome = "Work From Home";
        public const string Holiday = "Holiday";
        public const string Weekend = "Weekend";
    }
}