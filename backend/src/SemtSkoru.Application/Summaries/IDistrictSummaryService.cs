using SemtSkoru.Application.Scoring;

namespace SemtSkoru.Application.Summaries;

public interface IDistrictSummaryService
{
    /// <summary>
    /// Generates the "standout traits" summary for ONE district from its own already-computed
    /// score only (see NeighborhoodScoreResult) - never recomputes or fakes it. Called by
    /// DistrictSummaryGenerationJob, not from any live HTTP request path, so there is no
    /// per-request rate-limit policy here; the job itself is responsible for pacing repeated
    /// calls across districts (see its own remarks).
    /// </summary>
    Task<DistrictSummaryOutcome> GenerateSummaryAsync(string neighborhoodName, NeighborhoodScoreResult score, CancellationToken ct);
}
