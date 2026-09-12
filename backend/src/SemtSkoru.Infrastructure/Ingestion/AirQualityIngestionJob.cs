using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Infrastructure.Ingestion;

/// <summary>
/// Pulls the latest air quality reading for every seeded district and upserts it, with full
/// source provenance metadata. Scheduled daily via Hangfire (see Program.cs).
///
/// Matches İBB's own stations to districts by real point-in-polygon test against each
/// district's boundary (same technique as TrafficIngestionJob) rather than by station name or
/// the station list's own free-text "Adress" field - live-verified (2026-09-12) that both are
/// unreliable: a station literally named "Kartal" is addressed in Pendik, and "Adress" formats
/// are inconsistent ("İstanbul / X - Turkey" vs "İstanbul - X" vs, for a mobile unit, "İBB
/// HAKİM"). Only 28 stations exist city-wide (verified live), covering 18 of 39 districts -
/// the other 21 legitimately get no reading, never a guessed/interpolated one.
/// </summary>
public sealed class AirQualityIngestionJob(
    IAirQualityApiClient client,
    AppDbContext db,
    ILogger<AirQualityIngestionJob> logger,
    TimeProvider timeProvider)
{
    private const string SourceName = "İBB Hava Kalitesi İstasyon Ölçüm Sonuçları Web Servisi";
    private const string SourceUrl = "https://api.ibb.gov.tr/havakalitesi/OpenDataPortalHandler/GetAQIByStationId";
    private const string SourceLicense = "İstanbul Büyükşehir Belediyesi Açık Veri Lisansı";

    public async Task RunAsync(CancellationToken ct)
    {
        var neighborhoods = await db.Neighborhoods.ToListAsync(ct);

        IReadOnlyList<AirQualityStationDto> stations;
        try
        {
            stations = await client.GetStationsAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch air quality station list; skipping this run");
            return;
        }

        foreach (var neighborhood in neighborhoods)
        {
            var matchingStations = stations
                .Where(s => neighborhood.Boundary.Contains(new Point(s.Longitude, s.Latitude)))
                .ToList();

            if (matchingStations.Count == 0)
            {
                logger.LogInformation("No air quality station found within neighborhood {NeighborhoodId}", neighborhood.Id);
                continue;
            }

            await IngestOneAsync(neighborhood.Id, matchingStations, ct);
        }
    }

    private async Task IngestOneAsync(string neighborhoodId, IReadOnlyList<AirQualityStationDto> stations, CancellationToken ct)
    {
        var readings = new List<AirQualityReadingDto>();
        foreach (var station in stations)
        {
            try
            {
                var reading = await client.GetLatestReadingAsync(station.Id, ct);
                if (reading is not null)
                {
                    readings.Add(reading);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Air quality fetch failed for station {StationId} in neighborhood {NeighborhoodId}", station.Id, neighborhoodId);
            }
        }

        if (readings.Count == 0)
        {
            logger.LogWarning("Air quality source returned no data for neighborhood {NeighborhoodId}", neighborhoodId);
            return;
        }

        // A district with multiple real stations gets the average of all of them rather than
        // an arbitrary pick - more representative, and no real reading is discarded.
        var averageAqi = readings.Average(r => r.AqiIndex);
        var latestReadTime = readings.Max(r => r.ReadTime);

        var now = timeProvider.GetUtcNow();
        var metadata = new DataSourceMetadata(
            SourceName,
            SourceUrl,
            SourceLicense,
            FetchedAt: now,
            PublishedAt: latestReadTime,
            LastSuccessfulSyncAt: now,
            Cadence: SourceCadence.Live);

        var existing = await db.AirQualityReadings.FindAsync([neighborhoodId], ct);
        if (existing is null)
        {
            db.AirQualityReadings.Add(new AirQualityReading
            {
                NeighborhoodId = neighborhoodId,
                AqiIndex = averageAqi,
                ReadingTime = latestReadTime,
                Source = metadata,
            });
        }
        else
        {
            existing.AqiIndex = averageAqi;
            existing.ReadingTime = latestReadTime;
            existing.Source = metadata;
        }

        await db.SaveChangesAsync(ct);
    }
}
