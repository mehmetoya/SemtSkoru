using SemtSkoru.Infrastructure.Ingestion;

namespace SemtSkoru.Ingest.Tests;

public class IngestCommandLineTests
{
    private static readonly IReadOnlyList<IbbIngestionJobInfo> Catalog = IbbIngestionJobCatalog.All;

    private static IngestCommandLine ParseOk(params string[] args)
    {
        var (command, error) = IngestCommandLine.Parse(args, Catalog);
        Assert.Null(error);
        return Assert.IsType<IngestCommandLine>(command);
    }

    private static string ParseError(params string[] args)
    {
        var (command, error) = IngestCommandLine.Parse(args, Catalog);
        Assert.Null(command);
        return Assert.IsType<string>(error);
    }

    [Fact]
    public void Due_alone_considers_every_job()
    {
        var command = ParseOk("--due");

        Assert.Equal(IngestMode.Run, command.Mode);
        Assert.True(command.OnlyDue);
        Assert.Equal(Catalog, command.Jobs);
    }

    [Fact]
    public void All_runs_every_job_unconditionally()
    {
        var command = ParseOk("all");

        Assert.False(command.OnlyDue);
        Assert.Equal(Catalog, command.Jobs);
    }

    [Fact]
    public void Named_jobs_run_in_catalog_order_without_duplicates_and_accept_Hangfire_ids()
    {
        var command = ParseOk("traffic", "PARKING", "parking-ingestion");

        Assert.Equal(["parking", "traffic"], command.Jobs.Select(j => j.Name));
        Assert.False(command.OnlyDue);
    }

    [Fact]
    public void Due_can_be_narrowed_to_named_jobs()
    {
        var command = ParseOk("--due", "air-quality");

        Assert.True(command.OnlyDue);
        Assert.Equal(["air-quality"], command.Jobs.Select(j => j.Name));
    }

    [Fact]
    public void List_alone_lists_every_job()
    {
        var command = ParseOk("--list");

        Assert.Equal(IngestMode.List, command.Mode);
        Assert.Equal(Catalog, command.Jobs);
    }

    [Fact]
    public void Help_wins_over_everything_else()
    {
        Assert.Equal(IngestMode.Help, ParseOk("parking", "--help").Mode);
    }

    [Fact]
    public void No_arguments_is_an_error_rather_than_silently_running_everything()
    {
        Assert.Contains("No job given", ParseError());
    }

    [Fact]
    public void Unknown_job_names_and_options_are_rejected()
    {
        Assert.Contains("Unknown job 'district-summary-generation'", ParseError("district-summary-generation"));
        Assert.Contains("Unknown option '--force'", ParseError("--force"));
    }
}
