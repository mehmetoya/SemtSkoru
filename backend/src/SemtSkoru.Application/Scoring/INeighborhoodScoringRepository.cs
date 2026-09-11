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
}
