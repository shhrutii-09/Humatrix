namespace Humatrix_HRMS.Helpers
{
    public static class CorrectionTypes
    {
        // =========================================================================
        // EMPLOYEE TYPES
        // =========================================================================

        public const string ForgotCheckIn = "ForgotCheckIn";

        public const string ForgotCheckOut = "ForgotCheckOut";

        public const string WrongTime = "WrongTime";

        public const string AbsentButWorked = "AbsentButWorked";

        public const string OvertimeCorrection = "OvertimeCorrection";

        // =========================================================================
        // HR TYPES
        // =========================================================================

        public const string HrManualCorrection = "HrManualCorrection";

        // =========================================================================
        // ALL TYPES
        // =========================================================================

        public static readonly HashSet<string> All =
        [
            ForgotCheckIn,
            ForgotCheckOut,
            WrongTime,
            AbsentButWorked,
            OvertimeCorrection,
            HrManualCorrection
        ];

        // =========================================================================
        // EMPLOYEE SUBMITTABLE TYPES
        // =========================================================================

        public static readonly HashSet<string> EmployeeSubmittable =
        [
            ForgotCheckIn,
            ForgotCheckOut,
            WrongTime,
            AbsentButWorked,
            OvertimeCorrection
        ];

        // =========================================================================
        // TYPES REQUIRING CHECK-IN
        // =========================================================================

        public static readonly HashSet<string> RequiresCheckIn =
        [
            ForgotCheckIn,
            WrongTime,
            AbsentButWorked,
            HrManualCorrection
        ];

        // =========================================================================
        // TYPES REQUIRING CHECK-OUT
        // =========================================================================

        public static readonly HashSet<string> RequiresCheckOut =
        [
            ForgotCheckOut,
            WrongTime,
            OvertimeCorrection,
            HrManualCorrection
        ];
    }
}