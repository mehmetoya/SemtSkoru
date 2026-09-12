using SemtSkoru.Domain;

namespace SemtSkoru.Application.Scoring;

/// <summary>
/// Read-only access to the raw ingested readings a score is built from. Implemented against
/// EF Core/Postgres in Infrastructure; kept as an interface here so scoring logic is unit
/// testable without a database.
/// </summary>
public interface INeighborhoodScoringRepository
{
    Task<bool> NeighborhoodExistsAsync(string neighborhoodId, CancellationToken ct);

    Task<AirQualityReading?> GetLatestAirQualityAsync(string neighborhoodId, CancellationToken ct);

    Task<GreenSpaceReading?> GetLatestGreenSpaceAsync(string neighborhoodId, CancellationToken ct);

    Task<TrafficReading?> GetLatestTrafficAsync(string neighborhoodId, CancellationToken ct);

    // Bulk variants for scoring every neighborhood at once (GET /api/neighborhoods) - each is a
    // single query regardless of neighborhood count. Live-verified this matters: with 39
    // districts, doing the four single-id queries above once per neighborhood took ~21s end to
    // end against a cross-cloud Render->Supabase connection (Render and Supabase round trips
    // are not free, unlike a co-located dev Postgres) before this was added.
    Task<IReadOnlyList<string>> GetAllNeighborhoodIdsAsync(CancellationToken ct);

    Task<IReadOnlyDictionary<string, AirQualityReading>> GetAllAirQualityAsync(CancellationToken ct);

    Task<IReadOnlyDictionary<string, GreenSpaceReading>> GetAllGreenSpaceAsync(CancellationToken ct);

    Task<IReadOnlyDictionary<string, TrafficReading>> GetAllTrafficAsync(CancellationToken ct);
}
