using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.DependencyInjection;
using SemtSkoru.Infrastructure.Ingestion;
using SemtSkoru.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SemtSkoru.Ingest.Tests;

// --due reads each source's newest LastSuccessfulSyncAt from the real database (the same signal
// /health/data uses), so these run against a migrated PostGIS container, never a real Supabase.
public class IngestionRunnerDueTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:16-3.4")
        .Build();

    private readonly RunLog _runLog = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), o => o.UseNetTopologySuite())
            .Options);

    private IServiceProvider CreateServices(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(_runLog)
            .AddSingleton(TimeProvider.System)
            .AddAppDbContext(_postgres.GetConnectionString())
            .AddScoped<IngestionSyncStatusReader>()
            .AddScoped<SucceedingJob>()
            .AddSingleton<IngestionRunner>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static DataSourceMetadata SyncedAt(DateTimeOffset at, SourceCadence cadence) =>
        new("Test Source", "https://example.test", "Test License", at, at, at, cadence);

    [Fact]
    public async Task Due_runs_only_sources_whose_last_successful_sync_is_old_enough_or_missing()
    {
        var now = DateTimeOffset.UtcNow;
        await using (var context = CreateContext())
        {
            // Parking synced 2h ago: a daily source, not due yet.
            context.ParkingReadings.Add(new ParkingReading
            {
                NeighborhoodId = "kadikoy",
                AverageAvailabilityRatio = 0.5,
                FacilityCount = 1,
                Source = SyncedAt(now.AddHours(-2), SourceCadence.Live),
            });
            // Air quality synced 21h ago: past the ~20h daily threshold, due.
            context.AirQualityReadings.Add(new AirQualityReading
            {
                NeighborhoodId = "kadikoy",
                AqiIndex = 40,
                ReadingTime = now.AddHours(-21),
                Source = SyncedAt(now.AddHours(-21), SourceCadence.Live),
            });
            await context.SaveChangesAsync();
        }

        // Fake jobs pointed at real sync sources - this test is about selection, not ingestion.
        IbbIngestionJobInfo[] jobs =
        [
            new("air-quality", "air-quality-ingestion", IngestionSyncStatusReader.AirQuality, typeof(SucceedingJob)),
            new("parking", "parking-ingestion", IngestionSyncStatusReader.Parking, typeof(SucceedingJob)),
            new("traffic", "traffic-ingestion", IngestionSyncStatusReader.Traffic, typeof(SucceedingJob)),
        ];

        var runner = CreateServices().GetRequiredService<IngestionRunner>();
        var summary = await runner.RunAsync(jobs, onlyDue: true, CancellationToken.None);

        Assert.Equal(["air-quality", "traffic"], summary.Results.Select(r => r.Job.Name)); // traffic: never synced
        Assert.Equal(["parking"], summary.NotDue.Select(j => j.Name));
        Assert.Equal(0, summary.ExitCode);
    }

    [Fact]
    public async Task A_successful_real_run_makes_the_next_due_run_a_no_op_and_a_failed_one_does_not()
    {
        var facilitiesJson = """
            [{"parkID":1,"parkName":"Kadıköy Açık","lat":"41.0","lng":"29.0","capacity":100,"emptyCapacity":40,"workHours":"24 Saat","parkType":"AÇIK OTOPARK","freeTime":0,"district":"KADIKÖY","isOpen":1}]
            """;
        var sourceUp = false;
        var handler = new FakeHttpMessageHandler(_ => sourceUp
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(facilitiesJson, Encoding.UTF8, "application/json") }
            : throw new HttpRequestException("simulated İBB geo-block timeout"));

        // The real ParkingIngestionJob through the shared registrations, with every HttpClient's
        // network replaced - so nothing here can reach İBB (or anything else).
        var services = CreateServices(s => s
            .AddIbbIngestionJobs()
            .ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler)));
        var runner = services.GetRequiredService<IngestionRunner>();
        var parking = IbbIngestionJobCatalog.All.Single(j => j.Name == "parking");

        // 1. Source unreachable: the job swallows the error (as on Hangfire), but the run fails...
        var failed = await runner.RunAsync([parking], onlyDue: true, CancellationToken.None);
        Assert.Equal(1, failed.ExitCode);
        Assert.Contains("simulated İBB geo-block timeout", failed.Results.Single().Outcome.FailureReason);

        // 2. ...and nothing was recorded, so the next --due run tries again and succeeds.
        sourceUp = true;
        var succeeded = await runner.RunAsync([parking], onlyDue: true, CancellationToken.None);
        Assert.Equal(0, succeeded.ExitCode);
        Assert.Equal(1, succeeded.Results.Single().Outcome.DistrictsUpdated);

        // 3. The success moved LastSuccessfulSyncAt in the database, so the next --due run skips it.
        var skipped = await runner.RunAsync([parking], onlyDue: true, CancellationToken.None);
        Assert.Empty(skipped.Results);
        Assert.Equal(["parking"], skipped.NotDue.Select(j => j.Name));
        Assert.Equal(0, skipped.ExitCode);
    }
}

file sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
