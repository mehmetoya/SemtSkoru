using SemtSkoru.Application.Scoring;
using SemtSkoru.Domain;

namespace SemtSkoru.Application.Comparisons;

/// <summary>
/// The hybrid on-demand-generation-plus-persistent-cache flow for the compare page's AI summary
/// (see SemtSkoru.Infrastructure.Comparisons.ComparisonSummaryOrchestrator for the implementation
/// and its full reasoning). Deliberately NOT a background job like DistrictSummaryGenerationJob:
/// with 39 districts there are 39-choose-2 = 741 possible pairs, so pre-generating all of them
/// weekly would be far more Gemini spend than this feature is worth for the (many) pairs no one
/// ever actually views. Instead, this is called synchronously from the compare-summary endpoint,
/// triggered by a real visitor requesting a real pair.
/// </summary>
public interface IComparisonSummaryOrchestrator
{
    /// <summary>
    /// Returns the cached ComparisonSummary for this pair if one already exists for the pair's
    /// CURRENT combined score signature (a cheap DB read, no Gemini call); otherwise attempts a
    /// live generation, persists it on success, and returns the freshly-generated row. Returns
    /// null - never throws, never fabricates - for every reason a summary isn't available: no
    /// Gemini key configured, the live call fails/times out/is rate-limited, the model's response
    /// wasn't usable, or neither district has a dimension the other also has data for. Callers
    /// (see NeighborhoodEndpoints.cs) must treat null exactly like DistrictSummaryBadge treats a
    /// null summary - the rest of the comparison page still works fully.
    /// </summary>
    Task<ComparisonSummary?> GetOrGenerateAsync(
        string neighborhoodNameA, NeighborhoodScoreResult scoreA,
        string neighborhoodNameB, NeighborhoodScoreResult scoreB,
        CancellationToken ct);
}
