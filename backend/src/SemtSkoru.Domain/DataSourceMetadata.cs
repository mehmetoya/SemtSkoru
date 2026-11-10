namespace SemtSkoru.Domain;

public sealed record DataSourceMetadata(
    string SourceName,
    string SourceUrl,
    string SourceLicense,
    DateTimeOffset FetchedAt,
    DateTimeOffset PublishedAt,
    DateTimeOffset LastSuccessfulSyncAt,
    SourceCadence Cadence)
{
    /// <summary>How old a Live source's PublishedAt may be before it's considered Stale.</summary>
    public static readonly TimeSpan LiveStaleThreshold = TimeSpan.FromDays(7);

    /// <summary>How long a Periodic source may go without a successful sync before it's considered Stale.
    /// Much longer than LiveStaleThreshold because an old PublishedAt is expected and normal for these sources.</summary>
    public static readonly TimeSpan PeriodicStaleThreshold = TimeSpan.FromDays(60);

    public DataFreshnessStatus GetFreshness(DateTimeOffset now) => Cadence switch
    {
        SourceCadence.StaticSnapshot => DataFreshnessStatus.Historical,
        SourceCadence.Live => now - PublishedAt > LiveStaleThreshold
            ? DataFreshnessStatus.Stale
            : DataFreshnessStatus.Fresh,
        SourceCadence.Periodic => now - LastSuccessfulSyncAt > PeriodicStaleThreshold
            ? DataFreshnessStatus.Stale
            : DataFreshnessStatus.Fresh,
        _ => throw new ArgumentOutOfRangeException(nameof(Cadence), Cadence, "Unhandled source cadence."),
    };
}
