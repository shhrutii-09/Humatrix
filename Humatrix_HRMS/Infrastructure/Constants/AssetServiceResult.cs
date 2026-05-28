// ─────────────────────────────────────────────────────────────────────────────
// FILE: Infrastructure/Results/AssetServiceResult.cs
//
// Add this file ONCE to your project if you don't already have a generic
// service-result wrapper.  All new methods in this patch return this type.
// ─────────────────────────────────────────────────────────────────────────────
namespace Humatrix_HRMS.Infrastructure.Results
{
    /// <summary>
    /// Lightweight discriminated union returned by service methods that can fail
    /// with a user-visible message rather than throwing exceptions.
    /// </summary>
    public sealed class AssetServiceResult<T>
    {
        public bool IsSuccess { get; private init; }
        public T? Data { get; private init; }
        public string? Error { get; private init; }

        private AssetServiceResult() { }

        public static AssetServiceResult<T> Ok(T data) =>
            new() { IsSuccess = true, Data = data };

        public static AssetServiceResult<T> Fail(string error) =>
            new() { IsSuccess = false, Error = error };
    }

    /// <summary>Non-generic variant for void operations (e.g. assign, return).</summary>
    public sealed class AssetServiceResult
    {
        public bool IsSuccess { get; private init; }
        public string? Error { get; private init; }

        private AssetServiceResult() { }

        public static AssetServiceResult Ok() => new() { IsSuccess = true };
        public static AssetServiceResult Fail(string e) => new() { IsSuccess = false, Error = e };
    }
}