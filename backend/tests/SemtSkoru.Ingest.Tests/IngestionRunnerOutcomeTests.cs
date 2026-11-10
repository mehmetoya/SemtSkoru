using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SemtSkoru.Infrastructure.Ingestion;

namespace SemtSkoru.Ingest.Tests;

// Explicit (non --due) runs never touch the database outside the jobs themselves, so these use
// fake jobs and no container.
public class IngestionRunnerOutcomeTests
{
    private readonly RunLog _runLog = new();
    private readonly CapturingLoggerProvider _logs = new();

    private IngestionRunner CreateRunner()
    {
        var services = new ServiceCollection()
            .AddLogging(b => b.AddProvider(_logs))
            .AddSingleton(_runLog)
            .AddSingleton(TimeProvider.System)
            .AddScoped<SucceedingJob>()
            .AddScoped<SkippingJob>()
            .AddScoped<ThrowingJob>()
            .AddScoped<CancellingJob>()
            .AddSingleton<IngestionRunner>();
        return services.BuildServiceProvider().GetRequiredService<IngestionRunner>();
    }

    private static IbbIngestionJobInfo Job(string name, Type type) =>
        new(name, $"{name}-ingestion", IngestionSyncStatusReader.Parking, type);

    [Fact]
    public async Task All_succeeding_jobs_exit_zero_and_log_their_counts()
    {
        var summary = await CreateRunner().RunAsync([Job("parking", typeof(SucceedingJob))], onlyDue: false, CancellationToken.None);

        Assert.Equal(0, summary.ExitCode);
        var result = Assert.Single(summary.Results);
        Assert.True(result.Outcome.Succeeded);
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Information && e.Message.StartsWith("parking: starting"));
        Assert.Contains(_logs.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.StartsWith("parking: OK - 31 district reading(s) updated from 247 source record(s)"));
    }

    [Fact]
    public async Task A_job_that_skips_its_run_counts_as_failed_and_makes_the_exit_code_non_zero()
    {
        var summary = await CreateRunner().RunAsync(
            [Job("air-quality", typeof(SkippingJob)), Job("parking", typeof(SucceedingJob))],
            onlyDue: false,
            CancellationToken.None);

        Assert.Equal(1, summary.ExitCode);
        Assert.False(summary.Results[0].Outcome.Succeeded);
        Assert.True(summary.Results[1].Outcome.Succeeded);
        Assert.Contains(_logs.Entries, e =>
            e.Level == LogLevel.Error &&
            e.Message.StartsWith("air-quality: FAILED") &&
            e.Message.Contains("station list fetch failed: TaskCanceledException: timed out"));
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("1 of 2 job(s) failed: air-quality"));
    }

    [Fact]
    public async Task A_job_that_throws_is_reported_as_failed_and_later_jobs_still_run()
    {
        var summary = await CreateRunner().RunAsync(
            [Job("traffic", typeof(ThrowingJob)), Job("parking", typeof(SucceedingJob))],
            onlyDue: false,
            CancellationToken.None);

        Assert.Equal(1, summary.ExitCode);
        Assert.Equal(["throwing", "succeeding"], _runLog.Ran);
        Assert.Contains("InvalidOperationException: database exploded", summary.Results[0].Outcome.FailureReason);
        Assert.Contains(_logs.Entries, e =>
            e.Level == LogLevel.Error && e.Message.StartsWith("traffic: FAILED") && e.Message.Contains("database exploded"));
    }

    [Fact]
    public async Task Cancellation_stops_the_run_instead_of_being_reported_as_a_job_failure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateRunner().RunAsync([Job("parking", typeof(CancellingJob))], onlyDue: false, cts.Token));
    }

    private sealed class CancellingJob : IIngestionJob
    {
        public Task<IngestionOutcome> IngestAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(IngestionOutcome.Success(1, 1));
        }
    }
}
