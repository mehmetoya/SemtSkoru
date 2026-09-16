namespace SemtSkoru.Application.Trends;

public enum DistrictTrendOutcomeKind
{
    /// <summary>A real, validated trend summary was produced - see SummaryText.</summary>
    Ok,

    /// <summary>No dimension's delta since the baseline snapshot was large enough to be worth
    /// narrating (see DistrictTrendDeltas.MinimumMeaningfulDelta) - the AI was never even
    /// called. Covers both "nothing really changed" and the defensive case of being called with
    /// an empty delta list at all (e.g. the cold-start state where no baseline snapshot exists
    /// yet - see ScoreSnapshotJob, which normally never calls this service in that state, but
    /// this service never assumes that guarantee holds).</summary>
    NoMeaningfulChange,

    /// <summary>No Gemini API key is configured yet.</summary>
    NotConfigured,

    /// <summary>The app's own shared-quota guard or Gemini's own rate limit rejected this call.</summary>
    RateLimited,

    /// <summary>Gemini call failed for any other reason (network, timeout, non-success status).</summary>
    Unavailable,

    /// <summary>
    /// Gemini responded, but nothing usable came out of it: unparseable output, every cited
    /// change named a dimension outside the real delta list or claimed the wrong direction, or
    /// the summary text itself was empty/oversized. Never surfaced as a fabricated fallback;
    /// always honest emptiness - the caller simply doesn't persist/update anything for this
    /// district.
    /// </summary>
    NoUsableSummary,
}

public sealed record DistrictTrendOutcome(DistrictTrendOutcomeKind Kind, string? SummaryText)
{
    public static DistrictTrendOutcome Ok(string summaryText) => new(DistrictTrendOutcomeKind.Ok, summaryText);

    public static readonly DistrictTrendOutcome NoMeaningfulChange = new(DistrictTrendOutcomeKind.NoMeaningfulChange, null);
    public static readonly DistrictTrendOutcome NotConfigured = new(DistrictTrendOutcomeKind.NotConfigured, null);
    public static readonly DistrictTrendOutcome RateLimited = new(DistrictTrendOutcomeKind.RateLimited, null);
    public static readonly DistrictTrendOutcome Unavailable = new(DistrictTrendOutcomeKind.Unavailable, null);
    public static readonly DistrictTrendOutcome NoUsableSummary = new(DistrictTrendOutcomeKind.NoUsableSummary, null);
}
