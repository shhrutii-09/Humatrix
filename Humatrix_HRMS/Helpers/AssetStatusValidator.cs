// Helpers/AssetStatusValidator.cs  (UPDATED — adds Reserved transitions)
namespace Humatrix_HRMS.Helpers
{
    /// <summary>
    /// Single source of truth for all valid asset status transitions.
    ///
    /// State machine:
    ///   Available → Assigned, Reserved, InRepair, Retired, Disposed, Lost
    ///   Reserved  → Available (reservation cancelled), Assigned (fulfilled), InRepair
    ///   Assigned  → Available (return), Lost
    ///   InRepair  → Available (repair completed)
    ///   Lost      → Available (recovered)
    ///
    /// Terminal states: Retired, Disposed — no transitions out.
    /// </summary>
    public static class AssetStatusValidator
    {
        public static bool CanTransition(string current, string next) =>
            (current, next) switch
            {
                // From Available
                ("Available", "Assigned") => true,
                ("Available", "Reserved") => true,
                ("Available", "InRepair") => true,
                ("Available", "Retired") => true,
                ("Available", "Disposed") => true,
                ("Available", "Lost") => true,

                // From Reserved
                ("Reserved", "Available") => true,   // reservation cancelled
                ("Reserved", "Assigned") => true,   // fulfillment assigned
                ("Reserved", "InRepair") => true,   // edge case

                // From Assigned
                ("Assigned", "Available") => true,   // returned
                ("Assigned", "Lost") => true,   // lost while assigned

                // Repair cycle
                ("InRepair", "Available") => true,

                // Recovery
                ("Lost", "Available") => true,

                _ => false
            };

        /// <summary>
        /// Throws <see cref="InvalidOperationException"/> if the transition is not allowed.
        /// </summary>
        public static void Enforce(string current, string next)
        {
            if (!CanTransition(current, next))
                throw new InvalidOperationException(
                    $"Asset cannot transition from '{current}' to '{next}'.");
        }
    }
}
