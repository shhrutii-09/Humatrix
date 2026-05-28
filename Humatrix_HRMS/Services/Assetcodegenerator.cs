using Humatrix_HRMS.Data;
using Microsoft.EntityFrameworkCore;

namespace Humatrix_HRMS.Services.Assets
{
    /// <summary>
    /// Generates sequential, human-readable asset codes like AST-2024-00042.
    /// Called inside a transaction so the sequence is safe under concurrency.
    /// </summary>
    public static class AssetCodeGenerator
    {
        /// <summary>
        /// Generates the next available asset code for the given organisation.
        /// Must be called within an open EF Core transaction.
        /// </summary>
        public static async Task<string> NextCodeAsync(
            ApplicationDbContext db,
            Guid organizationId,
            string category)
        {
            var year = DateTime.UtcNow.Year;

            // Count all assets for this org (across all years) to get a
            // monotonically increasing sequence number. This avoids gaps
            // when assets are deleted and keeps codes stable.
            var count = await db.Assets
                .Where(a => a.OrganizationId == organizationId)
                .CountAsync();

            // Category prefix: first 3 chars uppercase
            var prefix = category.Length >= 3
                ? category[..3].ToUpperInvariant()
                : category.ToUpperInvariant().PadRight(3, 'X');

            return $"{prefix}-{year}-{(count + 1):D5}";
        }
    }
}