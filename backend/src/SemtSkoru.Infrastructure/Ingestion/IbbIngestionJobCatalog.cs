namespace SemtSkoru.Infrastructure.Ingestion;

/// <param name="Name">Short name used on the SemtSkoru.Ingest command line, e.g. "parking".</param>
/// <param name="RecurringJobId">The Hangfire recurring-job ID Program.cs registers this job under.</param>
/// <param name="SyncSource">The <see cref="IngestionSyncStatusReader"/> source key this job writes,
/// i.e. where its last successful sync can be read back from the database.</param>
/// <param name="JobType">The concrete job class, resolved from DI as an <see cref="IIngestionJob"/>.</param>
public sealed record IbbIngestionJobInfo(string Name, string RecurringJobId, string SyncSource, Type JobType);

/// <summary>
/// Every job that fetches from İBB's servers (api.ibb.gov.tr / data.ibb.gov.tr). Those servers
/// only accept TCP connections from Turkish IP addresses (live-verified 2026-11-10 by probing
/// from 11 countries: only the probe in Turkey connected), so these jobs cannot run on the
/// US-hosted Render instance. Program.cs uses this list to remove their Hangfire recurring
/// registrations when <c>Ingestion:IbbJobsEnabled</c> is false, and the SemtSkoru.Ingest runner
/// uses it to run them from a machine in Turkey instead. Ordered cheapest/most time-sensitive
/// first, which is also the order the runner executes them in.
/// </summary>
public static class IbbIngestionJobCatalog
{
    public static IReadOnlyList<IbbIngestionJobInfo> All { get; } =
    [
        new("air-quality", "air-quality-ingestion", IngestionSyncStatusReader.AirQuality, typeof(AirQualityIngestionJob)),
        new("parking", "parking-ingestion", IngestionSyncStatusReader.Parking, typeof(ParkingIngestionJob)),
        new("health-access", "health-access-ingestion", IngestionSyncStatusReader.HealthAccess, typeof(HealthAccessIngestionJob)),
        new("transit-access", "transit-access-ingestion", IngestionSyncStatusReader.TransitAccess, typeof(TransitAccessIngestionJob)),
        new("green-space", "green-space-ingestion", IngestionSyncStatusReader.GreenSpace, typeof(GreenSpaceIngestionJob)),
        new("traffic", "traffic-ingestion", IngestionSyncStatusReader.Traffic, typeof(TrafficIngestionJob)),
    ];
}
