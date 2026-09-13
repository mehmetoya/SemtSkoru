using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Infrastructure.Ingestion;

/// <summary>
/// İBB's "34 Dakika İstanbul Sağlık İndeksi" is published at mahalle (neighborhood-within-
/// district) granularity, not district level - unlike every other dimension's source, which
/// either matches a district field 1:1 (green space, parking) or point-in-polygon tests
/// against the district boundary directly (air quality, traffic). This job aggregates it up:
/// for each of our 39 seeded districts, it takes every mahalle İBB tags with that district
/// (ILCE_ADI, matched the same way GreenSpaceIngestionJob matches ILCE - live-verified
/// (2026-09-13) against all 39 real ILCE_ADI values with no naming exception, unlike İSPARK's
/// EYÜP/EYÜPSULTAN quirk) and computes the population-weighted average of SAGLIK_INDEX:
/// Σ(index_i × population_i) / Σ(population_i). A mahalle with zero population (live-verified:
/// 97 of the dataset's 901 wards - likely uninhabited industrial/forest zones) is excluded
/// from both sides of that sum rather than counted as a zero-weighted term, so it can never
/// dilute a district's real average. A district with no populated mahalle at all would be left
/// with no reading (DimensionScore.NoData) rather than a divide-by-zero or a fabricated value -
/// live-verified this never actually happens across the real 39 districts (see
/// docs/data-sources.md), but the code does not assume that stays true forever.
///
/// Scheduled weekly via Hangfire (see Program.cs), matching green space's cadence - like green
/// space, this is a slow-changing published index (dataset last modified 2024-02-01 per İBB's
/// own CKAN metadata), not a live feed like air quality or parking occupancy.
/// </summary>
public sealed class HealthAccessIngestionJob(
    IHealthAccessApiClient client,
    AppDbContext db,
    ILogger<HealthAccessIngestionJob> logger,
    TimeProvider timeProvider)
{
    private const string SourceName = "İBB 34 Dakika İstanbul Sağlık İndeksi";
    private const string SourceUrl =
        "https://data.ibb.gov.tr/dataset/d68cd520-971c-46c1-98cb-6cb66c940604/resource/d90e11be-d5b3-4df2-ba1f-e4356d336ad7/download/saglik_index.geojson";
    private const string SourceLicense = "İstanbul Büyükşehir Belediyesi Açık Veri Lisansı";

    // The GeoJSON resource's own last_modified date, per İBB's CKAN API
    // (data.ibb.gov.tr/api/3/action/package_show?id=d68cd520-971c-46c1-98cb-6cb66c940604),
    // verified live 2026-09-13 - see docs/data-sources.md.
    private static readonly DateTimeOffset SourcePublishedAt = new(2024, 2, 1, 9, 3, 4, TimeSpan.Zero);

    private static readonly CultureInfo Turkish = new("tr-TR");

    public async Task RunAsync(CancellationToken ct)
    {
        IReadOnlyList<HealthWardFeatureDto> wards;
        try
        {
            wards = await client.GetWardsAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch İBB health access index; skipping this run");
            return;
        }

        var neighborhoods = await db.Neighborhoods.ToListAsync(ct);
        var now = timeProvider.GetUtcNow();

        foreach (var neighborhood in neighborhoods)
        {
            // İBB's ILCE_ADI property is the uppercase Turkish district name - live-verified
            // (2026-09-13) to match all 39 seeded districts' Turkish-uppercase display name
            // directly (this dataset already uses "EYÜPSULTAN", never the old "EYÜP" name
            // İSPARK's parking dataset uses, so no alias table is needed here).
            var districtName = neighborhood.Name.ToUpper(Turkish);

            var populatedWards = wards
                .Where(m => m.District == districtName && m.Population > 0)
                .ToList();

            if (populatedWards.Count == 0)
            {
                logger.LogInformation(
                    "No populated ward with a health index found for neighborhood {NeighborhoodId} (ILCE_ADI={District})",
                    neighborhood.Id,
                    districtName);
                continue;
            }

            var totalPopulation = populatedWards.Sum(m => (double)m.Population);
            var weightedIndex = populatedWards.Sum(m => m.HealthIndex * m.Population) / totalPopulation;

            var metadata = new DataSourceMetadata(
                SourceName,
                SourceUrl,
                SourceLicense,
                FetchedAt: now,
                PublishedAt: SourcePublishedAt,
                LastSuccessfulSyncAt: now,
                Cadence: SourceCadence.Periodic);

            var existing = await db.HealthAccessReadings.FindAsync([neighborhood.Id], ct);
            if (existing is null)
            {
                db.HealthAccessReadings.Add(new HealthAccessReading
                {
                    NeighborhoodId = neighborhood.Id,
                    WeightedHealthIndex = weightedIndex,
                    WardCount = populatedWards.Count,
                    Source = metadata,
                });
            }
            else
            {
                existing.WeightedHealthIndex = weightedIndex;
                existing.WardCount = populatedWards.Count;
                existing.Source = metadata;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
