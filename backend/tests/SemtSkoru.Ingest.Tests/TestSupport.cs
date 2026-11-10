using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SemtSkoru.Infrastructure.Ingestion;

namespace SemtSkoru.Ingest.Tests;

/// <summary>Captures formatted log lines so tests can assert on what the runner reports.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<(LogLevel, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((logLevel, formatter(state, exception)));
    }
}

/// <summary>Records which fake jobs ran, in order.</summary>
internal sealed class RunLog
{
    public ConcurrentQueue<string> Ran { get; } = new();
}

internal sealed class SucceedingJob(RunLog log) : IIngestionJob
{
    public Task<IngestionOutcome> IngestAsync(CancellationToken ct)
    {
        log.Ran.Enqueue("succeeding");
        return Task.FromResult(IngestionOutcome.Success(sourceRecords: 247, districtsUpdated: 31));
    }
}

/// <summary>Mirrors the real jobs' swallowed-fetch-failure path: returns normally, but failed.</summary>
internal sealed class SkippingJob(RunLog log) : IIngestionJob
{
    public Task<IngestionOutcome> IngestAsync(CancellationToken ct)
    {
        log.Ran.Enqueue("skipping");
        return Task.FromResult(IngestionOutcome.Failure("station list fetch failed: TaskCanceledException: timed out"));
    }
}

internal sealed class ThrowingJob(RunLog log) : IIngestionJob
{
    public Task<IngestionOutcome> IngestAsync(CancellationToken ct)
    {
        log.Ran.Enqueue("throwing");
        throw new InvalidOperationException("database exploded");
    }
}
