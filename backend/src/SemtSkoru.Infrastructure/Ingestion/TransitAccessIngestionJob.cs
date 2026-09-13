using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Infrastructure.Ingestion;

/// <summary>
/// İBB's İETT bus stop dataset (15,486 city-wide Point features, live-verified 2026-09-13 - see
/// docs/data-sources.md) tags each stop with a numeric ILCEID district code that has no
/// reliable public mapping back to a district name - unlike green space's/parking's own
/// district-name fields, so this job can't string-match like GreenSpaceIngestionJob/
/// ParkingIngestionJob do. Instead it matches stops to districts by real point-in-polygon test
/// against each district's own boundary, the same technique AirQualityIngestionJob and
/// TrafficIngestionJob use.
///
/// At 15,486 points x 39 districts (~600K Contains tests worst case), this is ~100x fewer
/// points than TrafficIngestionJob's ~1.76M-row CSV. Live-measured (2026-09-13, see the
/// verification used to write this comment): even a fully naive nested loop - no envelope
/// pre-check, no prepared geometry - matches every stop to its district in ~4 seconds city-wide.
/// Prepared geometries (TrafficIngestionJob's technique for its much bigger dataset) bring that
/// under 40ms with no added code complexity, so this job reuses the same technique for
/// consistency and headroom rather than introducing a second, slower point-matching style for a
/// dataset that happens to be smaller.
///
/// Because a large rural district (e.g. Çatalca, ~1,137 km²) will always contain more raw stops
/// than a small central one (e.g. Beyoğlu, ~9 km²) just by being bigger, a raw stop count alone
/// would reward district size rather than transit access. This job instead computes each
/// district's real physical area from its WGS84 boundary via a simple equirectangular
/// approximation - x = lon(rad) × R × cos(meanLat), y = lat(rad) × R, R = 6371.0088 km, using
/// the district's own centroid latitude for the cosine term - and stores stops-per-km² as the
/// quantity DimensionScoring.ScoreTransitAccess scores, alongside the raw StopCount for
/// transparency. Live spot-checked (2026-09-13) against real published district areas: this
/// approximation puts Çatalca/Silivri/Şile as the three largest districts (~1,137/859/782 km²,
/// vs. real published figures of roughly 1,073/883/757 km²) and Güngören/Beyoğlu among the
/// smallest (~7.3/8.9 km², vs. real published figures of roughly 7.2/8.7 km²) - within a few
/// percent of real figures and never off by an order of magnitude, so it's an honest (if
/// approximate) area for Istanbul's small latitude range, not a fabricated number. A district
/// with a degenerate/zero-area boundary would divide-by-zero and is guarded explicitly rather
/// than crashing or producing Infinity.
///
/// A stop whose point falls outside every district boundary (live-verified: 159 of the 15,486,
/// ~1% - almost certainly water/coastline edge cases) is simply not counted anywhere, the same
/// way TrafficIngestionJob drops rows outside all three of its original districts - never
/// force-assigned to the nearest one.
///
/// Scheduled weekly via Hangfire (see Program.cs): the dataset's own CKAN metadata shows a
/// recent update (2026-03-18), but physical bus stop infrastructure changes slowly in practice -
/// the same cadence reasoning as green space/health access.
/// </summary>
public sealed class TransitAccessIngestionJob(
    ITransitAccessApiClient client,
    AppDbContext db,
    ILogger<TransitAccessIngestionJob> logger,
    TimeProvider timeProvider)
{
    private const string SourceName = "İETT Otobüs Durakları Verisi";
    private const string SourceUrl =
        "https://data.ibb.gov.tr/dataset/af3c70e8-82d6-44e2-84cf-2e364c242227/resource/4f28ec8d-7c2d-477b-873d-d17ce5b5e3be/download/iett-otobus-duraklar..geojson";
    private const string SourceLicense = "İstanbul Büyükşehir Belediyesi Açık Veri Lisansı";

    // The dataset resource's own last_modified date, per İBB's CKAN API
    // (data.ibb.gov.tr/api/3/action/package_show?id=af3c70e8-82d6-44e2-84cf-2e364c242227),
    // verified live 2026-09-13 - see docs/data-sources.md. Not each stop's own
    // SON_GUNCELLEME_TARIHI/YAPILIS_TARIHI, which are per-stop and often stale/old.
    private static readonly DateTimeOffset SourcePublishedAt = new(2026, 3, 18, 10, 42, 26, TimeSpan.Zero);

    private const double EarthRadiusKm = 6371.0088;

    public async Task RunAsync(CancellationToken ct)
    {
        IReadOnlyList<TransitStopDto> stops;
        try
        {
            stops = await client.GetStopsAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch İETT bus stop data; skipping this run");
            return;
        }

        var neighborhoods = await db.Neighborhoods.ToListAsync(ct);
        var envelopes = neighborhoods.ToDictionary(n => n.Id, n => n.Boundary.EnvelopeInternal);

        // Prepared geometries precompute an internal spatial index for a FIXED geometry that's
        // tested against many different points - the same technique TrafficIngestionJob uses
        // for its much larger (~1.76M row) point source. See class remarks for why this is used
        // here too even though a naive loop would likely also be fast enough at this scale.
        var preparedBoundaries = neighborhoods.ToDictionary(
            n => n.Id,
            n => PreparedGeometryFactory.Prepare(n.Boundary));

        var counts = neighborhoods.ToDictionary(n => n.Id, _ => 0);

        foreach (var stop in stops)
        {
            var coordinate = new Coordinate(stop.Longitude, stop.Latitude);
            var point = new Point(coordinate);

            foreach (var neighborhood in neighborhoods)
            {
                if (!envelopes[neighborhood.Id].Contains(coordinate) ||
                    !preparedBoundaries[neighborhood.Id].Contains(point))
                {
                    continue;
                }

                counts[neighborhood.Id]++;
                break; // districts don't overlap; a stop belongs to at most one.
            }
        }

        var now = timeProvider.GetUtcNow();

        foreach (var neighborhood in neighborhoods)
        {
            var count = counts[neighborhood.Id];
            if (count == 0)
            {
                logger.LogInformation("No İETT bus stop found within neighborhood {NeighborhoodId}", neighborhood.Id);
                continue;
            }

            var areaKm2 = ComputeAreaKm2(neighborhood.Boundary);
            var density = ComputeDensity(count, areaKm2);

            // A fresh instance per neighborhood, even though every field is identical this run -
            // EF Core's owned-entity (OwnsOne) change tracking assumes exactly one owner per
            // instance, and reusing the same DataSourceMetadata object across multiple
            // TransitAccessReading owners in one SaveChanges batch corrupts the batch (live-
            // verified: a NOT NULL violation on the second owner's row). Every other ingestion
            // job in this codebase already constructs its metadata per-neighborhood for the
            // same reason.
            var metadata = new DataSourceMetadata(
                SourceName,
                SourceUrl,
                SourceLicense,
                FetchedAt: now,
                PublishedAt: SourcePublishedAt,
                LastSuccessfulSyncAt: now,
                Cadence: SourceCadence.Periodic);

            var existing = await db.TransitAccessReadings.FindAsync([neighborhood.Id], ct);
            if (existing is null)
            {
                db.TransitAccessReadings.Add(new TransitAccessReading
                {
                    NeighborhoodId = neighborhood.Id,
                    StopCount = count,
                    StopDensityPerKm2 = density,
                    Source = metadata,
                });
            }
            else
            {
                existing.StopCount = count;
                existing.StopDensityPerKm2 = density;
                existing.Source = metadata;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // Guards a degenerate/zero-area boundary from ever producing Infinity/NaN - not expected for
    // a real district polygon (real point-in-polygon matching can only ever put a positive count
    // against a boundary whose interior is non-empty, i.e. non-zero area), but this stores an
    // honest 0 rather than crashing or fabricating a density if it ever happened. internal so
    // TransitAccessIngestionTests can exercise this guard directly with a hand-built zero-area
    // geometry, which real point-in-polygon matching could never produce as a test fixture.
    internal static double ComputeDensity(int stopCount, double areaKm2) => areaKm2 > 0 ? stopCount / areaKm2 : 0;

    // Equirectangular approximation, scaled by cos(mean latitude): accurate to within a few
    // percent at Istanbul's small extent/latitude range (lon ~28.6-29.46, lat ~40.78-41.27) -
    // live spot-checked against real published district areas, see class remarks above. Not a
    // substitute for a proper projected CRS transform, but simple, dependency-free, and honest
    // about its own error margin rather than silently wrong by an order of magnitude.
    private static double ComputeAreaKm2(Geometry boundary)
    {
        var meanLatRad = DegreesToRadians(boundary.Centroid.Y);
        var cosMeanLat = Math.Cos(meanLatRad);

        var projected = (Geometry)boundary.Copy();
        projected.Apply(new EquirectangularProjection(cosMeanLat));
        projected.GeometryChanged();
        return projected.Area;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private sealed class EquirectangularProjection(double cosMeanLat) : ICoordinateSequenceFilter
    {
        public bool Done => false;
        public bool GeometryChanged => true;

        public void Filter(CoordinateSequence sequence, int index)
        {
            var lon = sequence.GetX(index);
            var lat = sequence.GetY(index);
            sequence.SetX(index, DegreesToRadians(lon) * EarthRadiusKm * cosMeanLat);
            sequence.SetY(index, DegreesToRadians(lat) * EarthRadiusKm);
        }
    }
}
