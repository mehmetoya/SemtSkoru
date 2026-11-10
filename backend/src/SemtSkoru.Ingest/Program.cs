using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SemtSkoru.Infrastructure.DependencyInjection;
using SemtSkoru.Infrastructure.Ingestion;

namespace SemtSkoru.Ingest;

/// <summary>
/// Runs the İBB ingestion jobs once against the database in ConnectionStrings__Default, then
/// exits. Meant for a machine in Turkey (İBB rejects non-Turkish IPs - see
/// IbbIngestionJobCatalog), scheduled by scripts/local-ingestion (launchd, <c>--due</c>).
///
/// What it deliberately does NOT do:
/// - start Kestrel or a Hangfire server (a second Hangfire server on the shared storage would
///   compete with Render's for every queued job);
/// - apply EF Core migrations. The API doesn't either - migrations are applied by hand with
///   <c>dotnet ef database update</c> (docs/deployment.md, section 1) - so a schema the runner
///   doesn't match fails loudly on the first query instead of being altered from a laptop.
/// </summary>
internal static class Program
{
    // Supabase's Session Pooler allows 15 client connections for the WHOLE project, and the
    // Render API already caps itself at 8. Jobs run strictly one at a time with one DbContext each,
    // so this process never needs more than one connection; 2 leaves a spare for the
    // IngestionSyncStatusReader scope without ever eating into the API's share.
    private const int MaxPoolSize = 2;

    public static async Task<int> Main(string[] args)
    {
        var catalog = IbbIngestionJobCatalog.All;
        var (command, error) = IngestCommandLine.Parse(args, catalog);
        if (command is null)
        {
            await Console.Error.WriteLineAsync(error);
            await Console.Error.WriteLineAsync(IngestCommandLine.Usage(catalog));
            return 2;
        }

        if (command.Mode == IngestMode.Help)
        {
            Console.WriteLine(IngestCommandLine.Usage(catalog));
            return 0;
        }

        // Args are NOT forwarded to the host: they're this tool's own verbs, not configuration
        // overrides. ContentRootPath is pinned to the install directory because launchd starts
        // processes with "/" as the working directory, where appsettings.json would not be found.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        });

        var rawConnectionString = builder.Configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(rawConnectionString))
        {
            await Console.Error.WriteLineAsync(
                "ConnectionStrings__Default is not set. Run this through the installed wrapper " +
                "(scripts/local-ingestion), which reads it from the macOS Keychain.");
            return 2;
        }

        builder.Services.AddAppDbContext(InfrastructureServiceCollectionExtensions.WithMaxPoolSize(
            rawConnectionString, MaxPoolSize, applicationName: "semtskoru-ingest"));
        builder.Services.AddIbbIngestionJobs();
        builder.Services.AddScoped<IngestionSyncStatusReader>();
        builder.Services.AddSingleton<IngestionRunner>();

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SemtSkoru.Ingest");

        // Ctrl+C in a terminal, or SIGTERM from `launchctl bootout` (install.sh/uninstall.sh):
        // cancel the in-flight job cleanly instead of being killed mid-SaveChanges.
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            cts.Cancel();
        });

        var runner = host.Services.GetRequiredService<IngestionRunner>();
        try
        {
            if (command.Mode == IngestMode.List)
            {
                await runner.ListAsync(command.Jobs, Console.Out, cts.Token);
                return 0;
            }

            logger.LogInformation(
                "Run started ({Mode}: {Jobs})",
                command.OnlyDue ? "due" : "explicit",
                string.Join(", ", command.Jobs.Select(j => j.Name)));
            var summary = await runner.RunAsync(command.Jobs, command.OnlyDue, cts.Token);
            return summary.ExitCode;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            logger.LogWarning("Cancelled");
            return 130;
        }
        catch (Exception ex)
        {
            // Only reachable outside a job (each job's own errors become a FAILED line), e.g. the
            // database being unreachable while --due reads the last-sync timestamps.
            logger.LogError("Run aborted: {Reason}", IngestionOutcome.Describe(ex));
            return 1;
        }
    }
}
