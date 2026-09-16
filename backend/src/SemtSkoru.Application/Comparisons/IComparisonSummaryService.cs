using SemtSkoru.Application.Scoring;

namespace SemtSkoru.Application.Comparisons;

public interface IComparisonSummaryService
{
    /// <summary>
    /// Generates the natural-language comparison sentence for ONE pair of districts from their
    /// own already-computed scores only (see NeighborhoodScoreResult) - never recomputes or fakes
    /// them. A single live call to the configured AI client (no caching, no persistence) - see
    /// SemtSkoru.Infrastructure.Comparisons.ComparisonSummaryOrchestrator for the layer that
    /// caches this result keyed by the pair's canonical id + score signature, and only calls this
    /// on a cache miss.
    /// </summary>
    Task<ComparisonSummaryOutcome> GenerateComparisonAsync(
        string neighborhoodNameA, NeighborhoodScoreResult scoreA,
        string neighborhoodNameB, NeighborhoodScoreResult scoreB,
        CancellationToken ct);
}
