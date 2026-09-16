using SemtSkoru.Application.Scoring;
using SemtSkoru.Application.Summaries;

namespace SemtSkoru.Application.Comparisons;

/// <summary>
/// Combines both districts' DistrictSummarySignature (see that class's own remarks - it already
/// solves "detect whether a district's score VALUES actually changed" for the single-district
/// summary job) into one signature for a PAIR, used by ComparisonSummaryOrchestrator to decide
/// whether a cached ComparisonSummary row is still valid: a mismatch means at least one of the
/// two districts' scores changed since the cached text was generated, so it must not be served
/// as-is next to the freshly-computed current scores.
/// </summary>
public static class ComparisonSummarySignature
{
    /// <summary>
    /// Callers MUST pass scoreA/scoreB in the same canonical (ordinal-sorted-by-id) order used
    /// everywhere else for this pair (see ComparisonSummary.NeighborhoodIdA/B and
    /// ComparisonSummaryOrchestrator.Canonicalize) - otherwise the same real pair, compared in
    /// the opposite pick order, would compute a different signature and never share a cache hit.
    /// </summary>
    public static string Compute(NeighborhoodScoreResult scoreA, NeighborhoodScoreResult scoreB) =>
        string.Join("||", DistrictSummarySignature.Compute(scoreA), DistrictSummarySignature.Compute(scoreB));
}
