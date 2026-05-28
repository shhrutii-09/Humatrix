namespace Humatrix_HRMS.Helpers
{
    public static class AssetNotesHelper
    {
        public static string Append(
            string? existingNotes,
            string tag,
            string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return existingNotes ?? string.Empty;

            var formatted = $"[{tag}] {text.Trim()}";

            return string.IsNullOrWhiteSpace(existingNotes)
                ? formatted
                : $"{existingNotes}\n{formatted}";
        }
    }
}