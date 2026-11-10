namespace SemtSkoru.Domain;

public enum DataFreshnessStatus
{
    Fresh,
    Stale,

    /// <summary>
    /// The source is a deliberate static/historical snapshot (see <see cref="SourceCadence.StaticSnapshot"/>).
    /// Not an error condition — the UI should show this permanently, regardless of when ingestion last ran.
    /// </summary>
    Historical
}
