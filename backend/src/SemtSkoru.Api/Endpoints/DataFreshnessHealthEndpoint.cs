using Microsoft.Extensions.Caching.Memory;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.Ingestion;

namespace SemtSkoru.Api.Endpoints;

/// <param name="Status">"Ok", "Overdue", or "NeverSynced" (the source's table has no rows at all).</param>
/// <param name="ThresholdHours">How long after LastSuccessfulSyncAt the source counts as overdue
/// (see SourceCadenceSync.SyncOverdueThreshold). Null only for NeverSynced, whose cadence is unknown.</param>
public sealed record SourceSyncHealthDto(
    string Source,
    string? SourceName,
    string? Cadence,
    DateTimeOffset? LastSuccessfulSyncAt,
    double? ThresholdHours,
    double? AgeHours,
    string Status);

/// <param name="Status">"Healthy" (HTTP 200) or "Unhealthy" (HTTP 503).</param>
public sealed record DataFreshnessHealthDto(
    string Status,
    DateTimeOffset CheckedAt,
    IReadOnlyList<SourceSyncHealthDto> Sources);

public static class DataFreshnessHealthEndpoint
{
    // UptimeRobot polls every 5 minutes; this only bounds how often a burst of callers (the
    // endpoint is public and, like /health, exempt from rate limiting) can reach the shared,
    // MaxPoolSize=8 Postgres pool - at most one query per window, regardless of request volume.
    private static readonly TimeSpan ResultCacheDuration = TimeSpan.FromSeconds(30);
    private const string CacheKey = "health:data-freshness";
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    /// <summary>
    /// GET/HEAD /health/data: 200 while every ingestion job's latest successful sync is within its
    /// cadence's overdue threshold, 503 as soon as any source is overdue or has never synced - so an
    /// HTTP uptime monitor (UptimeRobot alerts on any non-2xx) emails when ingestion silently stops.
    /// HEAD returns the same status code with no body (UptimeRobot's monitors send HEAD).
    /// Deliberately separate from /health and /health/live, which stay DB-free liveness pings.
    /// </summary>
    public static void MapDataFreshnessHealthEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/health/data", ["GET", "HEAD"], async (
            HttpContext context,
            IngestionSyncStatusReader reader,
            IMemoryCache cache,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            if (!cache.TryGetValue(CacheKey, out DataFreshnessHealthDto? report))
            {
                // Single-flight: concurrent misses wait for one query instead of each taking a
                // pool connection (IMemoryCache.GetOrCreateAsync does not dedupe on its own).
                await RefreshLock.WaitAsync(ct);
                try
                {
                    if (!cache.TryGetValue(CacheKey, out report))
                    {
                        report = Evaluate(await reader.GetLatestSyncsAsync(ct), timeProvider.GetUtcNow());
                        cache.Set(CacheKey, report, ResultCacheDuration);
                    }
                }
                finally
                {
                    RefreshLock.Release();
                }
            }

            var statusCode = report!.Status == "Healthy"
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable;

            context.Response.Headers.CacheControl = "no-store";
            return HttpMethods.IsHead(context.Request.Method)
                ? Results.StatusCode(statusCode)
                : Results.Json(report, statusCode: statusCode);
        }).DisableRateLimiting();
    }

    internal static DataFreshnessHealthDto Evaluate(IReadOnlyList<IngestedSourceSync> syncs, DateTimeOffset now)
    {
        var sources = syncs.Select(sync =>
        {
            if (sync.Cadence is not { } cadence || sync.LastSuccessfulSyncAt is not { } lastSync)
            {
                return new SourceSyncHealthDto(sync.Source, null, null, null, null, null, "NeverSynced");
            }

            return new SourceSyncHealthDto(
                sync.Source,
                sync.SourceName,
                cadence.ToString(),
                lastSync,
                ThresholdHours: cadence.SyncOverdueThreshold().TotalHours,
                AgeHours: Math.Round((now - lastSync).TotalHours, 1),
                Status: cadence.IsSyncOverdue(lastSync, now) ? "Overdue" : "Ok");
        }).ToList();

        var healthy = sources.All(s => s.Status == "Ok");
        return new DataFreshnessHealthDto(healthy ? "Healthy" : "Unhealthy", now, sources);
    }
}
