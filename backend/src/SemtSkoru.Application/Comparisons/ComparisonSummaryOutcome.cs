namespace SemtSkoru.Application.Comparisons;

public enum ComparisonSummaryOutcomeKind
{
    /// <summary>A real, validated comparison summary was produced - see SummaryText.</summary>
    Ok,

    /// <summary>Neither district has a single dimension where BOTH have real data - there is
    /// nothing honest to compare, so the AI was never even called.</summary>
    InsufficientData,

    /// <summary>No Gemini API key is configured yet.</summary>
    NotConfigured,

    /// <summary>The app's own shared-quota guard or Gemini's own rate limit rejected this call.</summary>
    RateLimited,

    /// <summary>Gemini call failed for any other reason (network, timeout, non-success status).</summary>
    Unavailable,

    /// <summary>
    /// Gemini responded, but nothing usable came out of it: unparseable output, every highlight
    /// named a dimension neither/either district lacks data for, every highlight's claimed
    /// "stronger" district didn't match the real numbers (see ComparisonSummaryService), or the
    /// summary text itself was empty/oversized. Never surfaced as a fabricated fallback; always
    /// honest emptiness - the caller simply doesn't persist/show anything for this pair.
    /// </summary>
    NoUsableSummary,
}

public sealed record ComparisonSummaryOutcome(ComparisonSummaryOutcomeKind Kind, string? SummaryText)
{
    public static ComparisonSummaryOutcome Ok(string summaryText) => new(ComparisonSummaryOutcomeKind.Ok, summaryText);

    public static readonly ComparisonSummaryOutcome InsufficientData = new(ComparisonSummaryOutcomeKind.InsufficientData, null);
    public static readonly ComparisonSummaryOutcome NotConfigured = new(ComparisonSummaryOutcomeKind.NotConfigured, null);
    public static readonly ComparisonSummaryOutcome RateLimited = new(ComparisonSummaryOutcomeKind.RateLimited, null);
    public static readonly ComparisonSummaryOutcome Unavailable = new(ComparisonSummaryOutcomeKind.Unavailable, null);
    public static readonly ComparisonSummaryOutcome NoUsableSummary = new(ComparisonSummaryOutcomeKind.NoUsableSummary, null);
}
