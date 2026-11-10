namespace SemtSkoru.Infrastructure.ExternalApis;

public sealed record TransitStopDto(double Longitude, double Latitude);

public interface ITransitAccessApiClient
{
    /// <summary>
    /// Returns every real bus stop Point feature from İBB's İETT "Otobüs Durakları Verisi"
    /// dataset, city-wide. Only coordinates are returned - the dataset's ILCEID (district) field
    /// is a numeric code with no reliable public mapping to a district name, so callers match
    /// stops to districts by point-in-polygon test against the district's own boundary instead
    /// (see TransitAccessIngestionJob), the same way AirQualityIngestionJob/TrafficIngestionJob
    /// match their own point sources.
    /// </summary>
    Task<IReadOnlyList<TransitStopDto>> GetStopsAsync(CancellationToken ct);
}
