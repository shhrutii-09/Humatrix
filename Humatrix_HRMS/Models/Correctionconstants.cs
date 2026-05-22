namespace Humatrix_HRMS.Models
{
    /// <summary>
    /// Single source of truth for all attendance correction status strings.
    /// Use these constants everywhere — never hardcode "Pending", "Approved", etc.
    /// </summary>
    public static class CorrectionStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Cancelled = "Cancelled";

        public static readonly IReadOnlyList<string> All =
            new[] { Pending, Approved, Rejected, Cancelled };

        public static bool IsTerminal(string status)
            => status == Approved || status == Rejected || status == Cancelled;
    }

    /// <summary>
    /// Describes WHAT the employee is trying to fix.
    /// These drive the validation logic in AttendanceCorrectionService.
    /// </summary>
    //public static class CorrectionTypes
    //{
    //    /// <summary>Employee forgot to check in; wants to record one.</summary>
    //    public const string ForgotCheckIn = "ForgotCheckIn";

    //    /// <summary>Employee forgot to check out; wants to record one.</summary>
    //    public const string ForgotCheckOut = "ForgotCheckOut";

    //    /// <summary>Both check-in and check-out times are wrong.</summary>
    //    public const string WrongTime = "WrongTime";

    //    /// <summary>
    //    /// Employee was marked Absent but actually worked. Creates a full attendance record.
    //    /// </summary>
    //    public const string AbsentButWorked = "AbsentButWorked";

    //    /// <summary>
    //    /// Overtime-related correction — employee left later than shift end
    //    /// but was auto-checked-out at shift end and needs OT time recorded.
    //    /// </summary>
    //    public const string OvertimeCorrection = "OvertimeCorrection";

    //    /// <summary>
    //    /// HR initiates a full manual correction or override on behalf of an employee.
    //    /// Only HRs can submit this type.
    //    /// </summary>
    //    public const string HrManualCorrection = "HrManualCorrection";

    //    public static readonly IReadOnlyList<string> All = new[]
    //    {
    //        ForgotCheckIn, ForgotCheckOut, WrongTime,
    //        AbsentButWorked, OvertimeCorrection, HrManualCorrection
    //    };

    //    /// <summary>Types where RequestedCheckIn is required.</summary>
    //    public static readonly IReadOnlySet<string> RequiresCheckIn =
    //        new HashSet<string>
    //        {
    //            ForgotCheckIn, WrongTime, AbsentButWorked, HrManualCorrection
    //        };

    //    /// <summary>Types where RequestedCheckOut is required.</summary>
    //    public static readonly IReadOnlySet<string> RequiresCheckOut =
    //        new HashSet<string>
    //        {
    //            ForgotCheckOut, WrongTime, AbsentButWorked,
    //            OvertimeCorrection, HrManualCorrection
    //        };
    //}

    /// <summary>
    /// Action labels stored in the CorrectionAuditLog.
    /// </summary>
    public static class CorrectionAuditActions
    {
        public const string Submitted = "Submitted";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Cancelled = "Cancelled";
        public const string Applied = "Applied";
        public const string HrModified = "HrModified";
        public const string AutoApplied = "AutoApplied";

        public static readonly HashSet<string> All =
        [
            Submitted,
            Approved,
            Rejected,
            Cancelled,
            Applied,
            HrModified,
            AutoApplied
        ];
    }
}