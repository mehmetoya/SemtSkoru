using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Infrastructure.Ingestion;

/// <summary>
/// Computes an average traffic speed per MVP district from the İBB traffic density dataset.
/// The source is a fixed historical snapshot (January 2025 — see docs/data-sources.md, section 3;
/// the source itself has stopped publishing updates), so results are always tagged
/// SourceCadence.StaticSnapshot / DataFreshnessStatus.Historical, never "live". Scheduled monthly
/// via Hangfire purely so an eventual real update from İBB would be picked up — the ~140 MB source
/// is not expected to change between runs.
/// </summary>
public sealed class TrafficIngestionJob(
    ITrafficDataClient client,
    AppDbContext db,
    ILogger<TrafficIngestionJob> logger,
    TimeProvider timeProvider)
{
    private const string SourceName = "İBB Saatlik Trafik Yoğunluk Veri Seti";
    private const string SourceUrl = "https://data.ibb.gov.tr/dataset/hourly-traffic-density-data-set";
    private const string SourceLicense =
        "İstanbul Büyükşehir Belediyesi Açık Veri Lisansı — TARİHSEL VERİ (Ocak 2025), CANLI DEĞİL";

    // Last day covered by the January 2025 snapshot — see docs/data-sources.md, section 3.
    private static readonly DateTimeOffset SourcePublishedAt = new(2025, 1, 31, 23, 59, 59, TimeSpan.Zero);

    public async Task RunAsync(CancellationToken ct)
    {
        var neighborhoods = await db.Neighborhoods.ToListAsync(ct);
        var envelopes = neighborhoods.ToDictionary(n => n.Id, n => n.Boundary.EnvelopeInternal);
        var sums = neighborhoods.ToDictionary(n => n.Id, _ => (Sum: 0.0, Count: 0));

        try
        {
            await foreach (var row in client.GetTrafficRowsAsync(ct))
            {
                var point = new Coordinate(row.Longitude, row.Latitude);

                foreach (var neighborhood in neighborhoods)
                {
                    if (!envelopes[neighborhood.Id].Contains(point) ||
                        !neighborhood.Boundary.Contains(new Point(point)))
                    {
                        continue;
                    }

                    var current = sums[neighborhood.Id];
                    sums[neighborhood.Id] = (current.Sum + row.AverageSpeedKmh, current.Count + 1);
                    break; // districts don't overlap; a row belongs to at most one.
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traffic data fetch failed; skipping this run");
            return;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var neighborhood in neighborhoods)
        {
            var (sum, count) = sums[neighborhood.Id];
            if (count == 0)
            {
                logger.LogWarning("No traffic rows matched neighborhood {NeighborhoodId}", neighborhood.Id);
                continue;
            }

            var averageSpeed = sum / count;
            var metadata = new DataSourceMetadata(
                SourceName,
                SourceUrl,
                SourceLicense,
                FetchedAt: now,
                PublishedAt: SourcePublishedAt,
                LastSuccessfulSyncAt: now,
                Cadence: SourceCadence.StaticSnapshot);

            var existing = await db.TrafficReadings.FindAsync([neighborhood.Id], ct);
            if (existing is null)
            {
                db.TrafficReadings.Add(new TrafficReading
                {
                    NeighborhoodId = neighborhood.Id,
                    AverageSpeedKmh = averageSpeed,
                    SampleCount = count,
                    Source = metadata,
                });
            }
            else
            {
                existing.AverageSpeedKmh = averageSpeed;
                existing.SampleCount = count;
                existing.Source = metadata;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
