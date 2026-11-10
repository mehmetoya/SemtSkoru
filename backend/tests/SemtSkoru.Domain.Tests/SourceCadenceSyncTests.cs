namespace SemtSkoru.Domain.Tests;

public class SourceCadenceSyncTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);

    // Threshold = expected interval (the Hangfire schedule) + one more interval + 12h catch-up slack.
    [Theory]
    [InlineData(SourceCadence.Live, 60)]               // daily jobs: 2.5 days
    [InlineData(SourceCadence.Periodic, 348)]          // weekly jobs: 14.5 days
    [InlineData(SourceCadence.StaticSnapshot, 1500)]   // monthly job: 62.5 days
    public void Overdue_threshold_tolerates_one_missed_run_plus_slack(SourceCadence cadence, double expectedHours)
    {
        Assert.Equal(TimeSpan.FromHours(expectedHours), cadence.SyncOverdueThreshold());
    }

    [Fact]
    public void A_daily_source_is_not_overdue_after_a_single_missed_run()
    {
        Assert.False(SourceCadence.Live.IsSyncOverdue(Now - TimeSpan.FromHours(49), Now));
    }

    [Fact]
    public void A_daily_source_is_not_overdue_exactly_at_the_threshold()
    {
        Assert.False(SourceCadence.Live.IsSyncOverdue(Now - SourceCadence.Live.SyncOverdueThreshold(), Now));
    }

    [Fact]
    public void A_daily_source_is_overdue_once_past_the_threshold()
    {
        Assert.True(SourceCadence.Live.IsSyncOverdue(
            Now - SourceCadence.Live.SyncOverdueThreshold() - TimeSpan.FromSeconds(1), Now));
    }

    [Fact]
    public void A_static_snapshot_is_still_judged_on_its_sync_even_though_its_data_is_always_historical()
    {
        // GetFreshness always says Historical for this cadence; the sync check is independent.
        Assert.True(SourceCadence.StaticSnapshot.IsSyncOverdue(Now - TimeSpan.FromDays(70), Now));
        Assert.False(SourceCadence.StaticSnapshot.IsSyncOverdue(Now - TimeSpan.FromDays(40), Now));
    }
}
