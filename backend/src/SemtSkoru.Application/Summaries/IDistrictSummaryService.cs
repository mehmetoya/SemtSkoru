using SemtSkoru.Application.Scoring;

namespace SemtSkoru.Application.Summaries;

public interface IDistrictSummaryService
{
    /// <summary>
    /// Generates the "standout traits" summary for ONE district from its own already-computed
    /// score only (see NeighborhoodScoreResult) - never recomputes or fakes it. Called by
    /// DistrictSummaryGenerationJob, not from any live HTTP request path, so there is no
    /// per-request rate-limit policy here; the job itself is responsible for pacing repeated
    /// calls across districts (see its own remarks). The model's free-text "summary" is written
    /// in the given locale ("tr" or "en" - see SemtSkoru.Application.Localization.AiLocale, which
    /// an unrecognized/missing value normalizes to "tr" through); locale never affects grounding.
    /// </summary>
    Task<DistrictSummaryOutcome> GenerateSummaryAsync(
        string neighborhoodName, NeighborhoodScoreResult score, string locale, CancellationToken ct);
}
