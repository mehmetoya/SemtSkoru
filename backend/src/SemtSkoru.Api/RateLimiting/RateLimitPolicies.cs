namespace SemtSkoru.Api.RateLimiting;

/// <summary>
/// Names of the per-IP abuse-window policies registered in <see cref="RateLimitingExtensions"/>,
/// applied via <c>RequireRateLimiting</c> on individual endpoints. Kept separate from the
/// <c>db-pool</c> global concurrency limiter, which protects the shared Postgres connection pool
/// itself regardless of caller identity - see RateLimitingExtensions.cs for the full rationale.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>One DB round trip, no scoring join (e.g. GET /api/neighborhoods/names).</summary>
    public const string Cheap = "cheap";

    /// <summary>~7 DB round trips: one district's full score, or the bulk-scored full list.</summary>
    public const string Standard = "standard";

    /// <summary>~14 DB round trips: runs the "standard" scoring path twice (GET /api/neighborhoods/compare).</summary>
    public const string Compare = "compare";
}
