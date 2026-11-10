using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.Ingestion;

namespace SemtSkoru.Ingest.Tests;

public class DuePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private static IngestedSourceSync SyncedAgo(SourceCadence cadence, TimeSpan ago) =>
        new("parking", "test source", cadence, Now - ago);

    [Fact]
    public void A_source_never_ingested_into_this_database_is_due()
    {
        Assert.True(DuePolicy.IsDue(new IngestedSourceSync("parking", null, null, null), Now));
    }

    [Theory]
    // Daily (Live): ~20h, so yesterday's 09:05 success is due again at today's 09:00 slot.
    [InlineData(SourceCadence.Live, 19.9, false)]
    [InlineData(SourceCadence.Live, 20, true)]
    [InlineData(SourceCadence.Live, 23.9, true)]
    // Weekly (Periodic): ~6.5 days.
    [InlineData(SourceCadence.Periodic, 6 * 24, false)]
    [InlineData(SourceCadence.Periodic, 6.5 * 24, true)]
    // Monthly (StaticSnapshot - the traffic job): ~28 days.
    [InlineData(SourceCadence.StaticSnapshot, 27 * 24, false)]
    [InlineData(SourceCadence.StaticSnapshot, 28 * 24, true)]
    public void Due_once_the_last_successful_sync_is_at_least_the_cadence_threshold_old(
        SourceCadence cadence, double hoursAgo, bool expectedDue)
    {
        Assert.Equal(expectedDue, DuePolicy.IsDue(SyncedAgo(cadence, TimeSpan.FromHours(hoursAgo)), Now));
    }

    [Theory]
    [InlineData(SourceCadence.Live)]
    [InlineData(SourceCadence.Periodic)]
    [InlineData(SourceCadence.StaticSnapshot)]
    public void Due_threshold_is_shorter_than_the_schedule_interval_health_data_expects(SourceCadence cadence)
    {
        // If --due waited as long as the schedule interval itself, a fixed-time-of-day launchd
        // slot would routinely land minutes short of it and skip a whole day/week.
        Assert.True(DuePolicy.DueAfter(cadence) < cadence.ExpectedSyncInterval());
    }
}
