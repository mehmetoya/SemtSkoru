namespace SemtSkoru.Application.Trends;

public interface IDistrictTrendService
{
    /// <summary>
    /// Generates the "what changed" trend summary for ONE district from its already-computed
    /// list of meaningful dimension deltas (see DistrictTrendDeltas.Compute) - never recomputes
    /// scores or fetches a baseline itself. Called by ScoreSnapshotJob, not from any live HTTP
    /// request path, so there is no per-request rate-limit policy here; the job itself paces
    /// repeated calls across districts (see its own remarks).
    /// </summary>
    Task<DistrictTrendOutcome> GenerateTrendAsync(string neighborhoodName, IReadOnlyList<DimensionDelta> deltas, CancellationToken ct);
}
