using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.Ingestion;

namespace SemtSkoru.Ingest;

/// <summary>
/// Decides, for <c>--due</c>, whether a job should run now. Judged on the same signal
/// /health/data alerts on - the newest LastSuccessfulSyncAt in the database for that source (see
/// IngestionSyncStatusReader) - rather than on a local state file, so the runner and the
/// monitor can never disagree about what's stale, and a successful run from anywhere (another
/// machine, a manual run) counts.
/// </summary>
public static class DuePolicy
{
    /// <summary>
    /// Deliberately a few hours SHORTER than <see cref="SourceCadenceSync.ExpectedSyncInterval"/>
    /// (1 / 7 / 31 days): the LaunchAgent fires at fixed times of day (09:00, 13:00, 19:00), so a
    /// job that succeeded at 09:05 yesterday must already be due at 09:00 today, not at 09:05.
    /// </summary>
    public static TimeSpan DueAfter(SourceCadence cadence) => cadence switch
    {
        SourceCadence.Live => TimeSpan.FromHours(20),
        SourceCadence.Periodic => TimeSpan.FromDays(6.5),
        SourceCadence.StaticSnapshot => TimeSpan.FromDays(28),
        _ => throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Unhandled source cadence."),
    };

    /// <summary>A source never ingested into this database is always due.</summary>
    public static bool IsDue(IngestedSourceSync sync, DateTimeOffset now) =>
        sync.LastSuccessfulSyncAt is not { } lastSync
        || sync.Cadence is not { } cadence
        || now - lastSync >= DueAfter(cadence);
}
