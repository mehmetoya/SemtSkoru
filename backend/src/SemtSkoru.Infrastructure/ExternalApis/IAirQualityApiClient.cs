namespace SemtSkoru.Infrastructure.ExternalApis;

public sealed record AirQualityReadingDto(DateTimeOffset ReadTime, double AqiIndex);

public interface IAirQualityApiClient
{
    /// <summary>
    /// Returns the most recent reading for the given İBB air quality station,
    /// or null if the source returned no data in the lookback window.
    /// </summary>
    Task<AirQualityReadingDto?> GetLatestReadingAsync(string stationId, CancellationToken ct);
}
