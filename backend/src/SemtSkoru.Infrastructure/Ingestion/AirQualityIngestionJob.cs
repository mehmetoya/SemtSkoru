using Microsoft.Extensions.Logging;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Infrastructure.Ingestion;

/// <summary>
/// Pulls the latest air quality reading for each of the 3 MVP districts and upserts it,
/// with full source provenance metadata. Scheduled daily via Hangfire (see Program.cs).
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

    // Station ids resolved and verified in Task 4 — see docs/data-sources.md, section 1.
    private static readonly IReadOnlyDictionary<string, string> StationIdByNeighborhoodId =
        new Dictionary<string, string>
        {
            ["kadikoy"] = "ecafeb15-905e-4257-a25a-72accf287e2a",
            ["uskudar"] = "a30101a6-349c-4f0d-b965-a68f2c6781e9",
            ["besiktas"] = "179cd958-11aa-4e7a-8fa4-6eb2c852c2f6",
        };

    public async Task RunAsync(CancellationToken ct)
    {
        foreach (var (neighborhoodId, stationId) in StationIdByNeighborhoodId)
        {
            await IngestOneAsync(neighborhoodId, stationId, ct);
        }
    }

    private async Task IngestOneAsync(string neighborhoodId, string stationId, CancellationToken ct)
    {
        AirQualityReadingDto? reading;
        try
        {
            reading = await client.GetLatestReadingAsync(stationId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Air quality fetch failed for neighborhood {NeighborhoodId}", neighborhoodId);
            return;
        }

        if (reading is null)
        {
            logger.LogWarning("Air quality source returned no data for neighborhood {NeighborhoodId}", neighborhoodId);
            return;
        }

        var now = timeProvider.GetUtcNow();
        var metadata = new DataSourceMetadata(
            SourceName,
            SourceUrl,
            SourceLicense,
            FetchedAt: now,
            PublishedAt: reading.ReadTime,
            LastSuccessfulSyncAt: now,
            Cadence: SourceCadence.Live);

        var existing = await db.AirQualityReadings.FindAsync([neighborhoodId], ct);
        if (existing is null)
        {
            db.AirQualityReadings.Add(new AirQualityReading
            {
                NeighborhoodId = neighborhoodId,
                AqiIndex = reading.AqiIndex,
                ReadingTime = reading.ReadTime,
                Source = metadata,
            });
        }
        else
        {
            existing.AqiIndex = reading.AqiIndex;
            existing.ReadingTime = reading.ReadTime;
            existing.Source = metadata;
        }

        await db.SaveChangesAsync(ct);
    }
}
