using SemtSkoru.Infrastructure.Ingestion;

namespace SemtSkoru.Ingest;

public enum IngestMode
{
    Run,
    List,
    Help,
}

/// <param name="Jobs">Selected jobs, always in catalog order and without duplicates.</param>
/// <param name="OnlyDue">--due: run only the selected jobs whose last successful sync is old enough.</param>
public sealed record IngestCommandLine(IngestMode Mode, IReadOnlyList<IbbIngestionJobInfo> Jobs, bool OnlyDue)
{
    public static string Usage(IReadOnlyList<IbbIngestionJobInfo> catalog) =>
        $"""
        Usage: SemtSkoru.Ingest <job>... | all | --due [<job>...] | --list | --help

          <job>...   run these jobs now: {string.Join(", ", catalog.Select(j => j.Name))}
          all        run every İBB ingestion job now
          --due      run only jobs whose last successful sync (read from the database) is older
                     than their cadence: daily ~20h, weekly ~6.5 days, monthly ~28 days
          --list     show each job's last successful sync and whether it is due; runs nothing

        Reads the database connection string from ConnectionStrings__Default.
        Exit code: 0 = every job that ran succeeded (or nothing was due), 1 = a job failed,
        2 = bad arguments or configuration, 130 = cancelled.
        """;

    /// <summary>Returns the parsed command line, or an error message for bad arguments.</summary>
    public static (IngestCommandLine? Command, string? Error) Parse(
        IReadOnlyList<string> args, IReadOnlyList<IbbIngestionJobInfo> catalog)
    {
        var mode = IngestMode.Run;
        var onlyDue = false;
        var all = false;
        var selected = new HashSet<IbbIngestionJobInfo>();

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--help" or "-h":
                    return (new IngestCommandLine(IngestMode.Help, [], false), null);
                case "--due":
                    onlyDue = true;
                    continue;
                case "--list":
                    mode = IngestMode.List;
                    continue;
                case "all":
                    all = true;
                    continue;
            }

            if (arg.StartsWith('-'))
            {
                return (null, $"Unknown option '{arg}'.");
            }

            // Accept the Hangfire recurring-job ID too ("parking-ingestion"), since that's the
            // name that shows up in Render logs and the Hangfire dashboard.
            var job = catalog.FirstOrDefault(j =>
                string.Equals(j.Name, arg, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(j.RecurringJobId, arg, StringComparison.OrdinalIgnoreCase));
            if (job is null)
            {
                return (null, $"Unknown job '{arg}'. Known jobs: {string.Join(", ", catalog.Select(j => j.Name))}.");
            }

            selected.Add(job);
        }

        // --due and --list on their own mean "consider every job"; a bare run needs an explicit
        // choice so that a typo'd empty invocation never silently ingests everything.
        if (all || (selected.Count == 0 && (onlyDue || mode == IngestMode.List)))
        {
            return (new IngestCommandLine(mode, catalog, onlyDue), null);
        }

        if (selected.Count == 0)
        {
            return (null, "No job given. Pass job names, 'all' or '--due'.");
        }

        return (new IngestCommandLine(mode, catalog.Where(selected.Contains).ToList(), onlyDue), null);
    }
}
