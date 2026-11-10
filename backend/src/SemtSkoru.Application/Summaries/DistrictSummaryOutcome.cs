namespace SemtSkoru.Application.Summaries;

public enum DistrictSummaryOutcomeKind
{
    /// <summary>A real, validated summary was produced - see SummaryText.</summary>
    Ok,

    /// <summary>The district has no scored dimension at all yet (no readings ingested) - nothing
    /// to summarize, so the AI was never even called.</summary>
    InsufficientData,

    /// <summary>No Gemini API key is configured yet.</summary>
    NotConfigured,

    /// <summary>The app's own shared-quota guard or Gemini's own rate limit rejected this call.</summary>
    RateLimited,

    /// <summary>Gemini call failed for any other reason (network, timeout, non-success status).</summary>
    Unavailable,

    /// <summary>
    /// Gemini responded, but nothing usable came out of it: unparseable output, every highlight
    /// named a dimension that isn't real or has no data for this district (see
    /// DistrictSummaryService), or the summary text itself was empty/oversized. Never surfaced as
    /// a fabricated fallback; always honest emptiness - the caller simply doesn't persist/update
    /// anything for this district.
    /// </summary>
    NoUsableSummary,
}

public sealed record DistrictSummaryOutcome(DistrictSummaryOutcomeKind Kind, string? SummaryText)
{
    public static DistrictSummaryOutcome Ok(string summaryText) => new(DistrictSummaryOutcomeKind.Ok, summaryText);

    public static readonly DistrictSummaryOutcome InsufficientData = new(DistrictSummaryOutcomeKind.InsufficientData, null);
    public static readonly DistrictSummaryOutcome NotConfigured = new(DistrictSummaryOutcomeKind.NotConfigured, null);
    public static readonly DistrictSummaryOutcome RateLimited = new(DistrictSummaryOutcomeKind.RateLimited, null);
    public static readonly DistrictSummaryOutcome Unavailable = new(DistrictSummaryOutcomeKind.Unavailable, null);
    public static readonly DistrictSummaryOutcome NoUsableSummary = new(DistrictSummaryOutcomeKind.NoUsableSummary, null);
}
