namespace SemtSkoru.Domain.Tests;

public class DataSourceMetadataTests
{
    private static DataSourceMetadata CreateMetadata(DateTimeOffset lastSuccessfulSyncAt) =>
        new(
            SourceName: "Test Source",
            SourceUrl: "https://example.com",
            SourceLicense: "Test License",
            FetchedAt: lastSuccessfulSyncAt,
            PublishedAt: lastSuccessfulSyncAt,
            LastSuccessfulSyncAt: lastSuccessfulSyncAt);

    [Fact]
    public void GetFreshness_returns_fresh_when_synced_one_day_ago()
    {
        var now = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
        var metadata = CreateMetadata(now - TimeSpan.FromDays(1));

        var result = metadata.GetFreshness(now);

        Assert.Equal(DataFreshnessStatus.Fresh, result);
    }

    [Fact]
    public void GetFreshness_returns_fresh_at_exactly_the_seven_day_boundary()
    {
        var now = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
        var metadata = CreateMetadata(now - TimeSpan.FromDays(7));

        var result = metadata.GetFreshness(now);

        Assert.Equal(DataFreshnessStatus.Fresh, result);
    }

    [Fact]
    public void GetFreshness_returns_stale_when_synced_more_than_seven_days_ago()
    {
        var now = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
        var metadata = CreateMetadata(now - TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));

        var result = metadata.GetFreshness(now);

        Assert.Equal(DataFreshnessStatus.Stale, result);
    }
}
