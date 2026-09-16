namespace SemtSkoru.Domain;

/// <summary>
/// The AI-generated natural-language comparison sentence for one PAIR of districts (see
/// SemtSkoru.Application.Comparisons.ComparisonSummaryService), e.g. "Kadıköy hava kalitesinde
/// öne çıkarken, Beşiktaş otoparkta daha güçlü." Unlike DistrictSummary (one row per district,
/// pre-generated for all 39 districts by a weekly Hangfire job because 39 rows is cheap), a PAIR
/// of districts has 39-choose-2 = 741 possible combinations - pre-generating all of them weekly
/// would be ~19x the quota cost of the district-summary job for a feature most pairs will never
/// actually be viewed for. So this table is instead populated ON DEMAND, one row per pair a real
/// visitor actually compared, by SemtSkoru.Infrastructure.Comparisons.ComparisonSummaryOrchestrator
/// the first time (and only the first time, per current score signature) that pair is requested -
/// see that class's remarks for the full read-cache-or-generate-then-persist flow.
/// </summary>
public sealed class ComparisonSummary
{
    /// <summary>
    /// The canonical pair key: NeighborhoodIdA is always the lexicographically smaller (ordinal)
    /// of the two real neighborhood ids, and NeighborhoodIdB the larger - regardless of which
    /// order a visitor picked them in on /karsilastir. Without this canonicalization, comparing
    /// (kadikoy, besiktas) and (besiktas, kadikoy) - the exact same pair, picked in the other
    /// order - would silently create two different cache rows and never share a cached summary
    /// (or a Gemini call) between them, defeating the whole point of this cache for the common
    /// case of two visitors picking the same popular pair in opposite click order. See
    /// ComparisonSummaryOrchestrator.Canonicalize.
    /// </summary>
    public required string NeighborhoodIdA { get; init; }

    /// <summary>The lexicographically larger (ordinal) of the two real neighborhood ids - see NeighborhoodIdA.</summary>
    public required string NeighborhoodIdB { get; init; }

    /// <summary>The 1-2 sentence Turkish comparison summary text - see ComparisonSummaryService's
    /// SystemInstruction for the exact grounding rules the model must follow to produce this
    /// (never a claim beyond the two districts' own 6 dimension scores, and never a claim about
    /// which district is "stronger" in a dimension that isn't independently verified against the
    /// real numbers before this text is trusted).</summary>
    public required string SummaryText { get; set; }

    public required DateTimeOffset GeneratedAt { get; set; }

    /// <summary>A deterministic, compact encoding of BOTH districts' six dimension scores' VALUES
    /// at generation time (see ComparisonSummarySignature) - compared against both districts'
    /// CURRENT scores whenever this pair is requested again, to decide whether either district's
    /// numbers actually changed since this summary was written. A mismatch means the cached text
    /// might now describe stale numbers next to the freshly-rendered current scores, so it is
    /// never served as-is - a fresh generation is attempted instead (see
    /// ComparisonSummaryOrchestrator.GetOrGenerateAsync).</summary>
    public required string ScoreSignature { get; set; }
}
