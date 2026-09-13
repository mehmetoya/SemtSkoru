using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Infrastructure.Ingestion;

/// <summary>
/// Pulls every İSPARK (İBB) parking facility and computes each district's average
/// available-capacity ratio (emptyCapacity / capacity, unweighted across that district's
/// facilities - the same non-weighting choice AirQualityIngestionJob makes for a district
/// with multiple stations). Matches facilities to districts by İSPARK's own "district" field
/// (already the uppercase Turkish ilçe name), the same string-matching technique
/// GreenSpaceIngestionJob uses for İBB's ILCE property - no point-in-polygon test needed here,
/// unlike air quality/traffic, since İSPARK tags each facility with its district itself.
///
/// Live-verified (2026-09-13): ~247 facilities city-wide - not every one of the 39 districts
/// has one, and a district with none gets no reading here, never a guessed/interpolated value.
/// This is also only a small sample of off-street parking (garages/lots İBB operates directly)
/// and says nothing about on-street parking, which İSPARK does not cover - see the scoring
/// comment in DimensionScoring.ScoreParking for the same caveat. Scheduled daily via Hangfire
/// (see Program.cs), matching air quality's cadence since occupancy is a fast-changing signal.
/// </summary>
public sealed class ParkingIngestionJob(
    IParkingApiClient client,
    AppDbContext db,
    ILogger<ParkingIngestionJob> logger,
    TimeProvider timeProvider)
{
    private const string SourceName = "İBB İSPARK Otopark Doluluk Bilgisi";
    private const string SourceUrl = "https://api.ibb.gov.tr/ispark/Park";
    private const string SourceLicense = "İstanbul Büyükşehir Belediyesi Açık Veri Lisansı";

    private static readonly CultureInfo Turkish = new("tr-TR");

    // Live-verified (2026-09-13) against the real endpoint: İSPARK tags all 9 of Eyüpsultan's
    // facilities with "EYÜP" - the district's pre-2019 name, before it was administratively
    // renamed - never "EYÜPSULTAN". Every other one of the 39 districts' Turkish-uppercase name
    // matched an İSPARK "district" value directly; this was the one real exception found. Same
    // kind of source-specific naming quirk as the Kağıthane OSM mismatch (docs/data-sources.md,
    // section 4) - handled explicitly rather than silently dropping a district's real facilities.
    private static readonly Dictionary<string, string> DistrictNameAliases = new()
    {
        ["EYÜPSULTAN"] = "EYÜP",
    };

    public async Task RunAsync(CancellationToken ct)
    {
        IReadOnlyList<ParkingFacilityDto> facilities;
        try
        {
            facilities = await client.GetFacilitiesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch İSPARK parking facilities; skipping this run");
            return;
        }

        var neighborhoods = await db.Neighborhoods.ToListAsync(ct);
        var now = timeProvider.GetUtcNow();

        foreach (var neighborhood in neighborhoods)
        {
            // İSPARK's own "district" field is already an uppercase Turkish district name, the
            // same convention İBB's green space dataset uses for ILCE - Turkish-culture
            // uppercase of our stored display name (dotted İ/dotless I handled correctly by
            // "tr-TR") produces an exact match.
            var districtName = neighborhood.Name.ToUpper(Turkish);
            var isparkDistrictName = DistrictNameAliases.GetValueOrDefault(districtName, districtName);
            var districtFacilities = facilities
                .Where(f => f.District == isparkDistrictName && f.Capacity > 0)
                .ToList();

            if (districtFacilities.Count == 0)
            {
                logger.LogInformation("No İSPARK facility found for neighborhood {NeighborhoodId}", neighborhood.Id);
                continue;
            }

            // Each facility's ratio is clamped to [0,1] before averaging - a stale/inconsistent
            // record could otherwise report more empty capacity than total capacity.
            var averageRatio = districtFacilities
                .Select(f => Math.Clamp((double)f.EmptyCapacity / f.Capacity, 0, 1))
                .Average();

            // İSPARK's response carries no per-facility timestamp (unlike air quality's
            // ReadTime) - it's a live current-state snapshot, so PublishedAt is the fetch time
            // itself, which is the only meaningful "how fresh is this" signal this source gives us.
            var metadata = new DataSourceMetadata(
                SourceName,
                SourceUrl,
                SourceLicense,
                FetchedAt: now,
                PublishedAt: now,
                LastSuccessfulSyncAt: now,
                Cadence: SourceCadence.Live);

            var existing = await db.ParkingReadings.FindAsync([neighborhood.Id], ct);
            if (existing is null)
            {
                db.ParkingReadings.Add(new ParkingReading
                {
                    NeighborhoodId = neighborhood.Id,
                    AverageAvailabilityRatio = averageRatio,
                    FacilityCount = districtFacilities.Count,
                    Source = metadata,
                });
            }
            else
            {
                existing.AverageAvailabilityRatio = averageRatio;
                existing.FacilityCount = districtFacilities.Count;
                existing.Source = metadata;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
