using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SemtSkoru.Infrastructure.Ingestion;

namespace SemtSkoru.Ingest;

public sealed record JobRunResult(IbbIngestionJobInfo Job, IngestionOutcome Outcome, TimeSpan Elapsed);

/// <param name="Results">One entry per job that actually ran, in run order.</param>
/// <param name="NotDue">Jobs skipped by --due because their last successful sync is recent enough.</param>
public sealed record IngestRunSummary(IReadOnlyList<JobRunResult> Results, IReadOnlyList<IbbIngestionJobInfo> NotDue)
{
    public int ExitCode => Results.All(r => r.Outcome.Succeeded) ? 0 : 1;
}

/// <summary>
/// Runs İBB ingestion jobs once, sequentially, each in its own DI scope (so each gets a fresh
/// AppDbContext and at most one pooled DB connection at a time). No Hangfire involved: starting a
/// Hangfire server against the shared Supabase storage would make this process compete with the
/// Render instance's server for every queued job, Gemini jobs included.
/// </summary>
public sealed class IngestionRunner(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<IngestionRunner> logger)
{
    public async Task<IngestRunSummary> RunAsync(
        IReadOnlyList<IbbIngestionJobInfo> jobs, bool onlyDue, CancellationToken ct)
    {
        var toRun = jobs;
        var notDue = new List<IbbIngestionJobInfo>();

        if (onlyDue)
        {
            var syncs = await ReadSyncsAsync(ct);
            var now = timeProvider.GetUtcNow();
            toRun = jobs.Where(j => DuePolicy.IsDue(syncs[j.SyncSource], now)).ToList();
            notDue.AddRange(jobs.Except(toRun));

            foreach (var job in notDue)
            {
                logger.LogInformation(
                    "{Job}: not due (last successful sync {LastSync:yyyy-MM-dd HH:mm} UTC)",
                    job.Name,
                    syncs[job.SyncSource].LastSuccessfulSyncAt);
            }

            if (toRun.Count == 0)
            {
                logger.LogInformation("Nothing due; exiting");
                return new IngestRunSummary([], notDue);
            }
        }

        var results = new List<JobRunResult>();
        foreach (var job in toRun)
        {
            results.Add(await RunOneAsync(job, ct));
        }

        var failed = results.Where(r => !r.Outcome.Succeeded).Select(r => r.Job.Name).ToList();
        if (failed.Count == 0)
        {
            logger.LogInformation("Done: {Count} job(s) succeeded", results.Count);
        }
        else
        {
            logger.LogError(
                "Done: {Failed} of {Count} job(s) failed: {FailedJobs}",
                failed.Count,
                results.Count,
                string.Join(", ", failed));
        }

        return new IngestRunSummary(results, notDue);
    }

    /// <summary>Prints each job's last successful sync and whether --due would run it.</summary>
    public async Task ListAsync(IReadOnlyList<IbbIngestionJobInfo> jobs, TextWriter output, CancellationToken ct)
    {
        var syncs = await ReadSyncsAsync(ct);
        var now = timeProvider.GetUtcNow();

        await output.WriteLineAsync($"{"JOB",-16}{"LAST SUCCESSFUL SYNC (local)",-30}{"DUE AFTER",-11}DUE?");
        foreach (var job in jobs)
        {
            var sync = syncs[job.SyncSource];
            var lastSync = sync.LastSuccessfulSyncAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "never";
            var dueAfter = sync.Cadence is { } cadence ? FormatInterval(DuePolicy.DueAfter(cadence)) : "-";
            var due = DuePolicy.IsDue(sync, now) ? "yes" : "no";
            await output.WriteLineAsync($"{job.Name,-16}{lastSync,-30}{dueAfter,-11}{due}");
        }
    }

    private async Task<JobRunResult> RunOneAsync(IbbIngestionJobInfo job, CancellationToken ct)
    {
        logger.LogInformation("{Job}: starting", job.Name);
        var stopwatch = Stopwatch.StartNew();

        IngestionOutcome outcome;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var instance = (IIngestionJob)scope.ServiceProvider.GetRequiredService(job.JobType);
            outcome = await instance.IngestAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fetch failures are already turned into an outcome inside each job; anything that
            // still escapes (DB unreachable, a constraint violation on save, ...) lands here.
            outcome = IngestionOutcome.Failure($"unhandled error: {IngestionOutcome.Describe(ex)}");
        }

        stopwatch.Stop();
        var seconds = stopwatch.Elapsed.TotalSeconds;
        if (outcome.Succeeded)
        {
            logger.LogInformation(
                "{Job}: OK - {Districts} district reading(s) updated from {Records} source record(s) in {Seconds:0.0}s",
                job.Name,
                outcome.DistrictsUpdated,
                outcome.SourceRecords,
                seconds);
        }
        else
        {
            logger.LogError("{Job}: FAILED after {Seconds:0.0}s - {Reason}", job.Name, seconds, outcome.FailureReason);
        }

        return new JobRunResult(job, outcome, stopwatch.Elapsed);
    }

    private async Task<IReadOnlyDictionary<string, IngestedSourceSync>> ReadSyncsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IngestionSyncStatusReader>();
        var syncs = await reader.GetLatestSyncsAsync(ct);
        return syncs.ToDictionary(s => s.Source);
    }

    private static string FormatInterval(TimeSpan interval) =>
        interval.TotalHours < 48
            ? interval.TotalHours.ToString("0", CultureInfo.InvariantCulture) + "h"
            : interval.TotalDays.ToString("0.#", CultureInfo.InvariantCulture) + "d";
}
