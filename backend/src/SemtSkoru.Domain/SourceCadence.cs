namespace SemtSkoru.Domain;

public enum SourceCadence
{
    /// <summary>Expected to update frequently (e.g. hourly). Freshness is judged by PublishedAt.</summary>
    Live,

    /// <summary>Expected to update infrequently by nature (e.g. yearly). Freshness only checks that
    /// our own ingestion is still reaching the source (LastSuccessfulSyncAt) — an old PublishedAt is normal.</summary>
    Periodic,

    /// <summary>A fixed historical snapshot the source itself will never update again. Always reported
    /// as DataFreshnessStatus.Historical, independent of when ingestion last ran.</summary>
    StaticSnapshot
}

/// <summary>
/// How often OUR ingestion job for a source of a given cadence is expected to succeed - an
/// operational question (is ingestion still reaching the source?), deliberately separate from
/// <see cref="DataSourceMetadata.GetFreshness"/>, which answers the user-facing one (is the data
/// itself current?). Judged on LastSuccessfulSyncAt for EVERY cadence, never PublishedAt: a frozen
/// snapshot's or a yearly index's PublishedAt is legitimately old, but the job that re-ingests it
/// still runs on a schedule and its failure is still worth knowing about.
/// </summary>
public static class SourceCadenceSync
{
    /// <summary>
    /// Extra time beyond <see cref="ExpectedSyncInterval"/> before a source counts as overdue:
    /// one further full interval (tolerates a single transient failed run without paging anyone -
    /// the next scheduled run usually recovers it) plus this fixed slack for the free-tier host
    /// waking from sleep, Hangfire catching up on the missed trigger, and the job's own runtime.
    /// </summary>
    public static readonly TimeSpan CatchUpSlack = TimeSpan.FromHours(12);

    /// <summary>
    /// The interval each cadence's ingestion job is scheduled at in Program.cs (Cron.Daily /
    /// Cron.Weekly / Cron.Monthly). Keep the two in step if either changes.
    /// </summary>
    public static TimeSpan ExpectedSyncInterval(this SourceCadence cadence) => cadence switch
    {
        SourceCadence.Live => TimeSpan.FromDays(1),
        SourceCadence.Periodic => TimeSpan.FromDays(7),
        // Cron.Monthly() fires on the 1st, so consecutive runs are at most 31 days apart.
        SourceCadence.StaticSnapshot => TimeSpan.FromDays(31),
        _ => throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Unhandled source cadence."),
    };

    /// <summary>Expected interval + grace (one more interval + <see cref="CatchUpSlack"/>):
    /// Live 2.5 days, Periodic 14.5 days, StaticSnapshot 62.5 days.</summary>
    public static TimeSpan SyncOverdueThreshold(this SourceCadence cadence) =>
        cadence.ExpectedSyncInterval() * 2 + CatchUpSlack;

    public static bool IsSyncOverdue(this SourceCadence cadence, DateTimeOffset lastSuccessfulSyncAt, DateTimeOffset now) =>
        now - lastSuccessfulSyncAt > cadence.SyncOverdueThreshold();
}
