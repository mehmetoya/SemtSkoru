namespace SemtSkoru.Infrastructure.ExternalApis;

public sealed record TrafficRowDto(double Latitude, double Longitude, double AverageSpeedKmh);

public interface ITrafficDataClient
{
    /// <summary>
    /// Streams every row of the İBB hourly traffic density CSV. The dataset itself is a fixed
    /// historical snapshot (currently frozen at January 2025 — see docs/data-sources.md, section 3),
    /// so rows are streamed rather than buffered: the source file is on the order of 140 MB for a
    /// single month.
    /// </summary>
    IAsyncEnumerable<TrafficRowDto> GetTrafficRowsAsync(CancellationToken ct);
}
