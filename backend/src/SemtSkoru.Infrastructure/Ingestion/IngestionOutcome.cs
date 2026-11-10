using Microsoft.Extensions.Logging;

namespace SemtSkoru.Infrastructure.Ingestion;

/// <summary>
/// What one ingestion run actually achieved. The jobs deliberately swallow source-fetch
/// failures (log <see cref="IngestionLog.RunFailed"/> and return normally) so a flaky upstream
/// never puts Hangfire into its retry loop - but that also made a run that ingested nothing look
/// exactly like a successful one to any caller. This makes the difference observable to a caller
/// that cares (the local SemtSkoru.Ingest runner) without changing how Hangfire sees the job:
/// Hangfire still calls each job's <c>RunAsync</c>, which discards the outcome.
/// </summary>
/// <param name="Succeeded">True only if at least one district's reading was actually written -
/// the same condition under which MAX(LastSuccessfulSyncAt) moves forward for that source.</param>
/// <param name="SourceRecords">How many records the source returned (stations, facilities, wards, ...).</param>
/// <param name="DistrictsUpdated">How many district readings were upserted.</param>
/// <param name="FailureReason">Why nothing was ingested; null on success.</param>
public sealed record IngestionOutcome(bool Succeeded, int SourceRecords, int DistrictsUpdated, string? FailureReason)
{
    public static IngestionOutcome Success(int sourceRecords, int districtsUpdated) =>
        new(true, sourceRecords, districtsUpdated, null);

    public static IngestionOutcome Failure(string reason, int sourceRecords = 0) =>
        new(false, sourceRecords, 0, reason);

    /// <summary>
    /// Emits the shared run-level <see cref="IngestionLog.RunFailed"/> event and returns the
    /// matching failed outcome, so the log line and the outcome can never disagree.
    /// </summary>
    internal static IngestionOutcome RunFailed(
        ILogger logger, Exception? exception, string ingestionSource, string reason, int sourceRecords = 0)
    {
        IngestionLog.RunFailed(logger, exception, ingestionSource, reason);
        return Failure(exception is null ? reason : $"{reason}: {Describe(exception)}", sourceRecords);
    }

    /// <summary>
    /// End of a run whose fetch succeeded. If no district reading was written, every reading for
    /// this source is still stale, so that counts (and is logged) as a failed run rather than a
    /// vacuous success.
    /// </summary>
    internal static IngestionOutcome Completed(
        ILogger logger, string ingestionSource, int sourceRecords, int districtsUpdated) =>
        districtsUpdated > 0
            ? Success(sourceRecords, districtsUpdated)
            : RunFailed(logger, null, ingestionSource, "no district received a reading", sourceRecords);

    /// <summary>The exception chain flattened to one line, for a concise failure reason.</summary>
    public static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            parts.Add($"{current.GetType().Name}: {current.Message}");
        }

        return string.Join(" -> ", parts);
    }
}

/// <summary>
/// An ingestion job whose outcome can be inspected. Implemented by every İBB ingestion job;
/// each job's Hangfire-facing <c>RunAsync</c> just awaits <see cref="IngestAsync"/> and drops
/// the result, so the recurring jobs on the web host behave exactly as before.
/// </summary>
public interface IIngestionJob
{
    Task<IngestionOutcome> IngestAsync(CancellationToken ct);
}
