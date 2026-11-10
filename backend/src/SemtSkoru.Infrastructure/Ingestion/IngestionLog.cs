using Microsoft.Extensions.Logging;

namespace SemtSkoru.Infrastructure.Ingestion;

/// <summary>
/// The one log event every ingestion job emits when a whole run is abandoned (the job still just
/// returns - skip-and-continue is unchanged, the next scheduled run retries). One EventId/EventName
/// and one message shape across all six jobs, so a log search for "IngestionRunFailed" finds every
/// failed run, whichever job it was. /health/data is what turns a streak of these into an alert.
/// </summary>
internal static partial class IngestionLog
{
    [LoggerMessage(
        EventId = 4100,
        EventName = "IngestionRunFailed",
        Level = LogLevel.Warning,
        Message = "IngestionRunFailed: {IngestionSource} ingestion run failed ({FailureReason}); skipping this run")]
    public static partial void RunFailed(ILogger logger, Exception? exception, string ingestionSource, string failureReason);
}
