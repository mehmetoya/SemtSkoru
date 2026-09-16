namespace SemtSkoru.Domain;

/// <summary>
/// The AI-generated "what changed" trend summary for one district (see
/// SemtSkoru.Application.Trends.DistrictTrendService), produced by the same scheduled Hangfire
/// job that takes this district's weekly ScoreSnapshot
/// (SemtSkoru.Infrastructure.Trends.ScoreSnapshotJob) rather than live per page view - mirrors
/// DistrictSummary's own reasoning for why this is a separate, derived, regenerable table rather
/// than a column bolted onto ScoreSnapshot: it isn't a raw historical data point itself, it's
/// text generated FROM comparing two of them.
///
/// Absent (no row for a district) covers several honest reasons, never a fabricated placeholder:
/// Gemini not configured, the weekly job hasn't reached this district yet, this district has no
/// snapshot old enough yet to compare against (the cold-start state - see ScoreSnapshotJob), the
/// real delta since its baseline snapshot wasn't large enough to be worth narrating, or no usable
/// text could be grounded in the real delta(s).
/// </summary>
public sealed class DistrictTrendSummary
{
    public required string NeighborhoodId { get; init; }

    /// <summary>The 1-2 sentence Turkish trend summary text - see DistrictTrendService's
    /// SystemInstruction for the exact grounding rules the model must follow (never a claim
    /// about a dimension outside the real, already-computed delta list, never a guess about
    /// *why* something changed).</summary>
    public required string SummaryText { get; set; }

    public required DateTimeOffset GeneratedAt { get; set; }

    /// <summary>A deterministic, compact encoding of exactly which dimensions were found to have
    /// changed meaningfully and by how much (see DistrictTrendSignature) - compared on every
    /// ScoreSnapshotJob run so a district whose comparison basis hasn't actually changed (same
    /// meaningful deltas as last time) never burns a second Gemini call describing the same
    /// change twice.</summary>
    public required string ComparisonSignature { get; set; }
}
