using Humatrix_HRMS.Infrastructure.Constants;

namespace Humatrix_HRMS.Services.Assets
{
    /// <summary>
    /// Enforces valid asset status transitions.
    /// Every status change anywhere in the codebase must pass through CanTransition().
    /// This keeps status logic in one place and prevents invalid states.
    ///
    /// Valid transitions:
    ///   Available  → Assigned  (on assignment)
    ///   Available  → Retired   (admin action)
    ///   Available  → Lost      (admin action)
    ///   Assigned   → Available (on return)
    ///   Assigned   → InRepair  (when repair request approved)
    ///   Assigned   → Retired   (admin action)
    ///   InRepair   → Available (repair completed)
    ///   InRepair   → Retired
    ///   Lost       → Available (if found)
    ///   Retired    → (terminal — no further transitions)
    /// </summary>
    public static class AssetStatusMachine
    {
        private static readonly Dictionary<string, HashSet<string>> _transitions = new()
        {
            [AssetStatus.Available] = new() { AssetStatus.Assigned, AssetStatus.Retired, AssetStatus.Lost },
            [AssetStatus.Assigned] = new() { AssetStatus.Available, AssetStatus.InRepair, AssetStatus.Retired },
            [AssetStatus.InRepair] = new() { AssetStatus.Available, AssetStatus.Retired },
            [AssetStatus.Lost] = new() { AssetStatus.Available },
            [AssetStatus.Retired] = new HashSet<string>()  // terminal
        };

        public static bool CanTransition(string from, string to)
        {
            return _transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
        }

        /// <summary>
        /// Throws InvalidOperationException if the transition is not permitted.
        /// Call this inside any service method that changes asset status.
        /// </summary>
        public static void EnsureCanTransition(string from, string to)
        {
            if (!CanTransition(from, to))
                throw new InvalidOperationException(
                    $"Asset status cannot transition from '{from}' to '{to}'.");
        }
    }
}