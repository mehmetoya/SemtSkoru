using Microsoft.EntityFrameworkCore;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Infrastructure.Ingestion;

/// <summary>Latest successful ingestion of one source. Null SourceName/Cadence/LastSuccessfulSyncAt
/// means the source's table has no rows at all - it has never been ingested in this database.</summary>
public sealed record IngestedSourceSync(
    string Source,
    string? SourceName,
    SourceCadence? Cadence,
    DateTimeOffset? LastSuccessfulSyncAt);

/// <summary>
/// Reads, for every ingested source, the newest LastSuccessfulSyncAt across its per-district rows -
/// in ONE round trip (a UNION ALL of six tiny GROUP BY aggregates over tables of at most 39 rows),
/// no scoring pass. Backs GET/HEAD /health/data.
///
/// Why LastSuccessfulSyncAt and not PublishedAt: every ingestion job stamps it with "now" only on
/// the path that actually writes a reading, and a failed run returns before touching any row - so
/// MAX(LastSuccessfulSyncAt) per table is exactly "the last run that ingested anything". PublishedAt
/// is the dataset's own date and is legitimately ancient for some sources (traffic is a frozen
/// January 2025 snapshot, the health index dates from 2024).
/// </summary>
public sealed class IngestionSyncStatusReader(AppDbContext db)
{
    // Keys match the dimension property names the score API already exposes (camelCase JSON), so
    // "airQuality" in a /health/data alert means the same thing it does everywhere else.
    public const string AirQuality = "airQuality";
    public const string GreenSpace = "greenSpace";
    public const string Traffic = "traffic";
    public const string Parking = "parking";
    public const string HealthAccess = "healthAccess";
    public const string TransitAccess = "transitAccess";

    public static readonly IReadOnlyList<string> AllSources =
        [AirQuality, Parking, GreenSpace, HealthAccess, TransitAccess, Traffic];

    public async Task<IReadOnlyList<IngestedSourceSync>> GetLatestSyncsAsync(CancellationToken ct)
    {
        // Grouped by (SourceName, Cadence) rather than collapsed to one row per table: every job
        // writes a single SourceName/Cadence today, but if either ever changes, rows for a district
        // that stopped matching keep the old values - the newest group still wins below.
        var rows = await db.AirQualityReadings
            .GroupBy(r => new { r.Source.SourceName, r.Source.Cadence })
            .Select(g => new SyncRow { Source = AirQuality, SourceName = g.Key.SourceName, Cadence = g.Key.Cadence, LastSuccessfulSyncAt = g.Max(r => r.Source.LastSuccessfulSyncAt) })
            .Concat(db.ParkingReadings
                .GroupBy(r => new { r.Source.SourceName, r.Source.Cadence })
                .Select(g => new SyncRow { Source = Parking, SourceName = g.Key.SourceName, Cadence = g.Key.Cadence, LastSuccessfulSyncAt = g.Max(r => r.Source.LastSuccessfulSyncAt) }))
            .Concat(db.GreenSpaceReadings
                .GroupBy(r => new { r.Source.SourceName, r.Source.Cadence })
                .Select(g => new SyncRow { Source = GreenSpace, SourceName = g.Key.SourceName, Cadence = g.Key.Cadence, LastSuccessfulSyncAt = g.Max(r => r.Source.LastSuccessfulSyncAt) }))
            .Concat(db.HealthAccessReadings
                .GroupBy(r => new { r.Source.SourceName, r.Source.Cadence })
                .Select(g => new SyncRow { Source = HealthAccess, SourceName = g.Key.SourceName, Cadence = g.Key.Cadence, LastSuccessfulSyncAt = g.Max(r => r.Source.LastSuccessfulSyncAt) }))
            .Concat(db.TransitAccessReadings
                .GroupBy(r => new { r.Source.SourceName, r.Source.Cadence })
                .Select(g => new SyncRow { Source = TransitAccess, SourceName = g.Key.SourceName, Cadence = g.Key.Cadence, LastSuccessfulSyncAt = g.Max(r => r.Source.LastSuccessfulSyncAt) }))
            .Concat(db.TrafficReadings
                .GroupBy(r => new { r.Source.SourceName, r.Source.Cadence })
                .Select(g => new SyncRow { Source = Traffic, SourceName = g.Key.SourceName, Cadence = g.Key.Cadence, LastSuccessfulSyncAt = g.Max(r => r.Source.LastSuccessfulSyncAt) }))
            .AsNoTracking()
            .ToListAsync(ct);

        var latestBySource = rows
            .GroupBy(r => r.Source)
            .ToDictionary(g => g.Key, g => g.MaxBy(r => r.LastSuccessfulSyncAt)!);

        return AllSources
            .Select(source => latestBySource.TryGetValue(source, out var row)
                ? new IngestedSourceSync(source, row.SourceName, row.Cadence, row.LastSuccessfulSyncAt)
                : new IngestedSourceSync(source, null, null, null))
            .ToList();
    }

    private sealed class SyncRow
    {
        public required string Source { get; init; }
        public required string SourceName { get; init; }
        public required SourceCadence Cadence { get; init; }
        public required DateTimeOffset LastSuccessfulSyncAt { get; init; }
    }
}
