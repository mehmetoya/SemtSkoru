namespace SemtSkoru.Domain;

public sealed record DataSourceMetadata(
    string SourceName,
    string SourceUrl,
    string SourceLicense,
    DateTimeOffset FetchedAt,
    DateTimeOffset PublishedAt,
    DateTimeOffset LastSuccessfulSyncAt)
{
    public static readonly TimeSpan StaleThreshold = TimeSpan.FromDays(7);

    public DataFreshnessStatus GetFreshness(DateTimeOffset now) =>
        now - LastSuccessfulSyncAt > StaleThreshold
            ? DataFreshnessStatus.Stale
            : DataFreshnessStatus.Fresh;
}
