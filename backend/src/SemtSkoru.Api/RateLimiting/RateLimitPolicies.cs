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

    /// <summary>
    /// POST /api/asistan (AI Semt Asistanı). Unlike the three policies above, this endpoint's
    /// scarce resource isn't this app's own DB pool - it's Google Gemini's free-tier quota,
    /// which is shared globally across every visitor to the whole app and resets once a day.
    /// A per-IP-only policy can't protect that: a handful of visitors spread across different
    /// IPs could still exhaust the whole day's budget for everyone else. See
    /// RateLimitingExtensions.cs for the combined per-IP + global-per-minute + global-per-day
    /// policy this name maps to.
    /// </summary>
    public const string AiAssistant = "ai-assistant";
}
