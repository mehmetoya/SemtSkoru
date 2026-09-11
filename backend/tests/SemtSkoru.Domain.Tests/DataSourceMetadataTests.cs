namespace SemtSkoru.Domain.Tests;

public class DataSourceMetadataTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);

    private static DataSourceMetadata Create(SourceCadence cadence, DateTimeOffset publishedAt, DateTimeOffset lastSuccessfulSyncAt) =>
        new(
            SourceName: "Test Source",
            SourceUrl: "https://example.com",
            SourceLicense: "Test License",
            FetchedAt: lastSuccessfulSyncAt,
            PublishedAt: publishedAt,
            LastSuccessfulSyncAt: lastSuccessfulSyncAt,
            Cadence: cadence);

    // Live cadence: freshness tracks how old the published reading itself is.

    [Fact]
    public void Live_is_fresh_when_the_published_reading_is_one_day_old()
    {
        var metadata = Create(SourceCadence.Live, publishedAt: Now - TimeSpan.FromDays(1), lastSuccessfulSyncAt: Now);

        Assert.Equal(DataFreshnessStatus.Fresh, metadata.GetFreshness(Now));
    }

    [Fact]
    public void Live_is_fresh_at_exactly_the_seven_day_boundary()
    {
        var metadata = Create(SourceCadence.Live, publishedAt: Now - TimeSpan.FromDays(7), lastSuccessfulSyncAt: Now);

        Assert.Equal(DataFreshnessStatus.Fresh, metadata.GetFreshness(Now));
    }

    [Fact]
    public void Live_is_stale_when_the_published_reading_is_more_than_seven_days_old_even_if_sync_just_succeeded()
    {
        // The key regression this guards against: a job that runs successfully every day must not
        // mask a source that has stopped publishing new readings.
        var metadata = Create(
            SourceCadence.Live,
            publishedAt: Now - TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1),
            lastSuccessfulSyncAt: Now);

        Assert.Equal(DataFreshnessStatus.Stale, metadata.GetFreshness(Now));
    }

    // Periodic cadence: an old PublishedAt is normal; freshness tracks whether ingestion itself still works.

    [Fact]
    public void Periodic_is_fresh_when_synced_recently_even_though_the_data_is_a_year_old()
    {
        var metadata = Create(
            SourceCadence.Periodic,
            publishedAt: Now - TimeSpan.FromDays(400),
            lastSuccessfulSyncAt: Now - TimeSpan.FromDays(1));

        Assert.Equal(DataFreshnessStatus.Fresh, metadata.GetFreshness(Now));
    }

    [Fact]
    public void Periodic_is_stale_when_ingestion_itself_has_not_succeeded_in_a_long_time()
    {
        var metadata = Create(
            SourceCadence.Periodic,
            publishedAt: Now - TimeSpan.FromDays(400),
            lastSuccessfulSyncAt: Now - TimeSpan.FromDays(90));

        Assert.Equal(DataFreshnessStatus.Stale, metadata.GetFreshness(Now));
    }

    // StaticSnapshot cadence: always Historical, regardless of either timestamp.

    [Fact]
    public void StaticSnapshot_is_always_historical()
    {
        var metadata = Create(SourceCadence.StaticSnapshot, publishedAt: Now - TimeSpan.FromDays(600), lastSuccessfulSyncAt: Now);

        Assert.Equal(DataFreshnessStatus.Historical, metadata.GetFreshness(Now));
    }
}
