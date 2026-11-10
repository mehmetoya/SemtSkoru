namespace SemtSkoru.Infrastructure.ExternalApis;

public sealed record AirQualityReadingDto(DateTimeOffset ReadTime, double AqiIndex);

public sealed record AirQualityStationDto(string Id, string Name, double Longitude, double Latitude);

public interface IAirQualityApiClient
{
    /// <summary>
    /// Returns the most recent reading for the given İBB air quality station,
    /// or null if the source returned no data in the lookback window.
    /// </summary>
    Task<AirQualityReadingDto?> GetLatestReadingAsync(string stationId, CancellationToken ct);

    /// <summary>
    /// Returns every İBB air quality station with its real coordinates. Live-verified
    /// (2026-09-12): only 28 stations exist city-wide, covering 18 of Istanbul's 39
    /// districts — the rest legitimately have no station nearby.
    /// </summary>
    Task<IReadOnlyList<AirQualityStationDto>> GetStationsAsync(CancellationToken ct);
}
