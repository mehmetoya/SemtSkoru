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
