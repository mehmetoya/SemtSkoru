using SemtSkoru.Domain;

namespace SemtSkoru.Application.Scoring;

/// <summary>
/// The six raw readings a score is built from, for one neighborhood - any of them may be
/// null (no data ingested yet for that dimension). A neighborhood that exists but has no
/// readings at all is a real, valid state (e.g. right after the boundary seed migration,
/// before any ingestion job has run) - distinct from the neighborhood not existing, which
/// GetReadingsAsync/GetAllReadingsAsync represent by the id being absent, not by this
/// record being populated with all-null fields.
/// </summary>
public sealed record NeighborhoodReadings(
    AirQualityReading? AirQuality,
    GreenSpaceReading? GreenSpace,
    TrafficReading? Traffic,
    ParkingReading? Parking,
    HealthAccessReading? HealthAccess,
    TransitAccessReading? TransitAccess);

/// <summary>
/// Read-only access to the raw ingested readings a score is built from. Implemented against
/// EF Core/Postgres in Infrastructure; kept as an interface here so scoring logic is unit
/// testable without a database.
/// </summary>
public interface INeighborhoodScoringRepository
{
    // One query, not "check existence then fetch six dimensions separately" - a neighborhood's
    // six reading tables are all keyed 1:1 by NeighborhoodId, so a single LEFT JOIN from
    // Neighborhoods returns exactly one row (or none, if the id doesn't exist) with whichever
    // dimensions have data already populated and the rest null.
    Task<NeighborhoodReadings?> GetReadingsAsync(string neighborhoodId, CancellationToken ct);

    // Bulk variant for scoring every neighborhood at once (GET /api/neighborhoods) - one query
    // regardless of neighborhood count, same LEFT JOIN shape as GetReadingsAsync but unfiltered.
    Task<IReadOnlyDictionary<string, NeighborhoodReadings>> GetAllReadingsAsync(CancellationToken ct);
}
