using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Infrastructure.Ingestion;

/// <summary>
/// For each seeded district, finds the nearest "Park"-typed green space to the district's
/// boundary centroid, searching the full city-wide dataset rather than restricting to parks
/// tagged with the district's own ILCE value — a park just across a district line can be the
/// genuinely nearest one, and the user-facing methodology only ever promises "nearest park",
/// not "nearest park in the same district". Scheduled weekly via Hangfire (see Program.cs) —
/// this data changes far less often than air quality, and the source file is a ~53 MB
/// city-wide download.
/// </summary>
public sealed class GreenSpaceIngestionJob(
    IGreenSpaceApiClient client,
    AppDbContext db,
    ILogger<GreenSpaceIngestionJob> logger,
    TimeProvider timeProvider) : IIngestionJob
{
    private const string SourceName = "İBB İstanbul Kentsel Açık ve Yeşil Alan Koordinatları";
    private const string SourceUrl = "https://data.ibb.gov.tr/dataset/kentsel-acik-ve-yesil-alanlar";
    private const string SourceLicense = "İstanbul Büyükşehir Belediyesi Açık Veri Lisansı";

    // Dataset's own metadata_modified date at the time it was verified — see docs/data-sources.md, section 2.
    private static readonly DateTimeOffset SourcePublishedAt = new(2025, 7, 17, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Hangfire's entry point (see Program.cs); the outcome only matters to SemtSkoru.Ingest.</summary>
    public async Task RunAsync(CancellationToken ct) => await IngestAsync(ct);

    public async Task<IngestionOutcome> IngestAsync(CancellationToken ct)
    {
        IReadOnlyList<ParkFeatureDto> parks;
        try
        {
            parks = await client.GetParksAsync(ct);
        }
        catch (Exception ex)
        {
            return IngestionOutcome.RunFailed(logger, ex, IngestionSyncStatusReader.GreenSpace, "park dataset fetch failed");
        }

        if (parks.Count == 0)
        {
            return IngestionOutcome.RunFailed(logger, null, IngestionSyncStatusReader.GreenSpace, "source returned no parks at all");
        }

        var neighborhoods = await db.Neighborhoods.ToListAsync(ct);
        var now = timeProvider.GetUtcNow();
        var districtsUpdated = 0;

        foreach (var neighborhood in neighborhoods)
        {
            var centroid = neighborhood.Boundary.Centroid.Coordinate;
            var nearest = parks
                .Select(p => (Park: p, Distance: GeoDistance.HaversineMeters(centroid, p.Centroid)))
                .OrderBy(x => x.Distance)
                .First();

            var metadata = new DataSourceMetadata(
                SourceName,
                SourceUrl,
                SourceLicense,
                FetchedAt: now,
                PublishedAt: SourcePublishedAt,
                LastSuccessfulSyncAt: now,
                Cadence: SourceCadence.Periodic);

            var existing = await db.GreenSpaceReadings.FindAsync([neighborhood.Id], ct);
            if (existing is null)
            {
                db.GreenSpaceReadings.Add(new GreenSpaceReading
                {
                    NeighborhoodId = neighborhood.Id,
                    NearestParkName = nearest.Park.Name,
                    NearestParkDistanceMeters = nearest.Distance,
                    Source = metadata,
                });
            }
            else
            {
                existing.NearestParkName = nearest.Park.Name;
                existing.NearestParkDistanceMeters = nearest.Distance;
                existing.Source = metadata;
            }

            districtsUpdated++;
        }

        await db.SaveChangesAsync(ct);
        return IngestionOutcome.Completed(logger, IngestionSyncStatusReader.GreenSpace, parks.Count, districtsUpdated);
    }
}
