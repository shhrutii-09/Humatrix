using Humatrix_HRMS.Data;
using Humatrix_HRMS.Models;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services.Assets
{
    public static class AssetCodeGenerator
    {
        public static async Task<string> NextCodeAsync(
            ApplicationDbContext db,
            Guid organizationId,
            string category)
        {
            var year = DateTime.UtcNow.Year;
            var prefix = GetPrefix(category);
            var pattern = $"{prefix}-{year}-";

            // Get max number from existing codes
            var maxNumber = await GetMaxNumberAsync(db, organizationId, pattern);
            var nextNumber = maxNumber + 1;

            return $"{prefix}-{year}-{nextNumber:D5}";
        }

        /// <summary>
        /// Generates multiple unique asset codes in sequence for batch creation.
        /// This method ensures each code is unique by reserving numbers sequentially.
        /// </summary>
        /// <summary>
        /// Generates multiple unique asset codes in sequence for batch creation.
        /// This method ensures each code is unique by reserving numbers sequentially.
        /// </summary>
        public static async Task<List<string>> NextCodesAsync(
            ApplicationDbContext db,
            Guid organizationId,
            string category,
            int count)
        {
            if (count <= 0)
                return new List<string>();

            var year = DateTime.UtcNow.Year;
            var prefix = GetPrefix(category);
            var pattern = $"{prefix}-{year}-";

            // Use a lock or ensure we get the latest max number
            // Also include pending changes in the current context
            var existingCodes = await db.Assets
                .Where(a => a.OrganizationId == organizationId && a.AssetCode.StartsWith(pattern))
                .Select(a => a.AssetCode)
                .ToListAsync();

            int max = 0;
            foreach (var code in existingCodes)
            {
                var numPart = code.Substring(pattern.Length);
                if (int.TryParse(numPart, out var num) && num > max)
                    max = num;
            }

            // Also check local entities that are not yet saved (if any)
            var localEntries = db.ChangeTracker.Entries<Asset>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .Where(a => a.OrganizationId == organizationId && a.AssetCode.StartsWith(pattern))
                .Select(a => a.AssetCode)
                .ToList();

            foreach (var code in localEntries)
            {
                var numPart = code.Substring(pattern.Length);
                if (int.TryParse(numPart, out var num) && num > max)
                    max = num;
            }

            // Generate sequential codes
            var codes = new List<string>();
            for (int i = 1; i <= count; i++)
            {
                var nextNumber = max + i;
                codes.Add($"{prefix}-{year}-{nextNumber:D5}");
            }

            return codes;
        }

        private static string GetPrefix(string category)
        {
            return category.Length >= 3
                ? category[..3].ToUpperInvariant()
                : category.ToUpperInvariant().PadRight(3, 'X');
        }

        private static async Task<int> GetMaxNumberAsync(
            ApplicationDbContext db,
            Guid organizationId,
            string pattern)
        {
            var existingCodes = await db.Assets
                .Where(a => a.OrganizationId == organizationId && a.AssetCode.StartsWith(pattern))
                .Select(a => a.AssetCode)
                .ToListAsync();

            int max = 0;
            foreach (var code in existingCodes)
            {
                var numPart = code.Substring(pattern.Length);
                if (int.TryParse(numPart, out var num) && num > max)
                    max = num;
            }

            return max;
        }
    }
}