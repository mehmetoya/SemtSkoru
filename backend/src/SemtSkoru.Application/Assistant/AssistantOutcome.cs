namespace SemtSkoru.Application.Assistant;

public enum AssistantOutcomeKind
{
    /// <summary>At least one real, validated recommendation - see Recommendations.</summary>
    Ok,

    /// <summary>The free-text request was empty/whitespace-only.</summary>
    InvalidRequest,

    /// <summary>No Gemini API key is configured yet.</summary>
    NotConfigured,

    /// <summary>The app's own shared-quota guard or Gemini's own rate limit rejected this call.</summary>
    RateLimited,

    /// <summary>Gemini call failed for any other reason (network, timeout, non-success status).</summary>
    Unavailable,

    /// <summary>
    /// Gemini responded, but nothing usable came out of it: unparseable output, or every
    /// recommended id was hallucinated (not one of the real 39 districts) - see
    /// DistrictAssistantService. Never surfaced as a fabricated fallback; always honest emptiness.
    /// </summary>
    NoUsableRecommendations,
}

public sealed record AssistantOutcome(AssistantOutcomeKind Kind, IReadOnlyList<AssistantRecommendation> Recommendations)
{
    public static AssistantOutcome Ok(IReadOnlyList<AssistantRecommendation> recommendations) =>
        new(AssistantOutcomeKind.Ok, recommendations);

    public static readonly AssistantOutcome InvalidRequest = new(AssistantOutcomeKind.InvalidRequest, []);
    public static readonly AssistantOutcome NotConfigured = new(AssistantOutcomeKind.NotConfigured, []);
    public static readonly AssistantOutcome RateLimited = new(AssistantOutcomeKind.RateLimited, []);
    public static readonly AssistantOutcome Unavailable = new(AssistantOutcomeKind.Unavailable, []);
    public static readonly AssistantOutcome NoUsableRecommendations = new(AssistantOutcomeKind.NoUsableRecommendations, []);
}
