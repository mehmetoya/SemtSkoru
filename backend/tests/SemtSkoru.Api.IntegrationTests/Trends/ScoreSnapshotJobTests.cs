using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Application.Trends;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Persistence;
using SemtSkoru.Infrastructure.Scoring;
using SemtSkoru.Infrastructure.Trends;
using Testcontainers.PostgreSql;

namespace SemtSkoru.Api.IntegrationTests.Trends;

// End-to-end proof (real Postgres via Testcontainers, real 39-district seed) that
// ScoreSnapshotJob's whole point holds up outside of unit tests. The single most important test
// here is the cold-start one: this feature has NO persisted score history anywhere before this
// job's first-ever run, and every district must sit in that state - snapshot taken, but no trend
// text, no exception - for at least a full MinimumBaselineAge after deploy. Gemini is faked out
// the same way DistrictSummaryGenerationJobTests.cs fakes it - never a real network call.
public class ScoreSnapshotJobTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:16-3.4").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), o => o.UseNetTopologySuite())
            .Options;
        return new AppDbContext(options);
    }

    private static DataSourceMetadata TestSource(DateTimeOffset at) => new(
        SourceName: "Test Source",
        SourceUrl: "https://example.test",
        SourceLicense: "Test License",
        FetchedAt: at,
        PublishedAt: at,
        LastSuccessfulSyncAt: at,
        Cadence: SourceCadence.Live);

    // Only kadikoy gets a reading - every other one of the 39 seeded districts has HasAnyData ==
    // false and is skipped before ever snapshotting or calling Gemini, which is what keeps this
    // test fast (mirrors DistrictSummaryGenerationJobTests's own seeding choice).
    private async Task SeedOrUpdateAirQualityAsync(double aqiIndex)
    {
        await using var context = CreateContext();
        var existing = await context.AirQualityReadings.FindAsync("kadikoy");
        if (existing is null)
        {
            context.AirQualityReadings.Add(new AirQualityReading
            {
                NeighborhoodId = "kadikoy",
                AqiIndex = aqiIndex,
                ReadingTime = DateTimeOffset.UtcNow,
                Source = TestSource(DateTimeOffset.UtcNow),
            });
        }
        else
        {
            existing.AqiIndex = aqiIndex;
            existing.Source = TestSource(DateTimeOffset.UtcNow);
        }

        await context.SaveChangesAsync();
    }

    private static IConfiguration ConfigWithKey(string? apiKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(apiKey is null ? [] : new Dictionary<string, string?> { ["Gemini:ApiKey"] = apiKey })
            .Build();

    private static ScoreSnapshotJob CreateJob(
        AppDbContext context, DateTimeOffset now, HttpMessageHandler geminiHandler, string? apiKey = "test-key") => new(
        new NeighborhoodDirectory(context),
        new NeighborhoodScoringService(new NeighborhoodScoringRepository(context), TimeProvider.System),
        new DistrictTrendService(new GeminiClient(new HttpClient(geminiHandler), ConfigWithKey(apiKey))),
        context,
        NullLogger<ScoreSnapshotJob>.Instance,
        new FakeTimeProvider(now));

    private static HttpResponseMessage GeminiJson(string modelJsonText) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = modelJsonText } } } } } }),
            Encoding.UTF8,
            "application/json"),
    };

    private const string ValidModelResponse =
        """{"changes":[{"dimension":"airQuality","direction":"decreased"}],"summary":"Bu ilçenin hava kalitesi skoru belirgin şekilde azaldı."}""";

    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_does_nothing_and_does_not_throw_when_no_district_has_any_ingested_data()
    {
        // The purest cold-start shape: not even a single reading exists yet for any of the 39
        // seeded districts.
        await using var context = CreateContext();
        var callCount = 0;
        var job = CreateJob(context, T0, new FakeHttpMessageHandler(_ => { callCount++; return GeminiJson(ValidModelResponse); }));

        var exception = await Record.ExceptionAsync(() => job.RunAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Equal(0, callCount);
        Assert.Empty(await context.ScoreSnapshots.ToListAsync());
        Assert.Empty(await context.DistrictTrendSummaries.ToListAsync());
    }

    [Fact]
    public async Task RunAsync_takes_a_snapshot_but_generates_no_trend_on_the_first_ever_run_for_a_scored_district()
    {
        // THE required cold-start test: a district WITH real data, but with no prior snapshot to
        // compare against yet, because this is the very first time this job has ever run. A
        // snapshot must be taken (history starts accumulating from today), but nothing about a
        // "trend" can honestly be said yet - no Gemini call, no row, no crash.
        await SeedOrUpdateAirQualityAsync(0); // -> a real, deterministic score of 100 (see DimensionScoring)
        await using var context = CreateContext();
        var callCount = 0;
        var job = CreateJob(context, T0, new FakeHttpMessageHandler(_ => { callCount++; return GeminiJson(ValidModelResponse); }));

        var exception = await Record.ExceptionAsync(() => job.RunAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Equal(0, callCount);
        Assert.Empty(await context.DistrictTrendSummaries.ToListAsync());

        var snapshots = await context.ScoreSnapshots.Where(s => s.NeighborhoodId == "kadikoy").ToListAsync();
        Assert.Equal(2, snapshots.Count); // airQuality + overall (only dimension with any data)
        Assert.All(snapshots, s => Assert.Equal(T0, s.RecordedAt));
        Assert.Contains(snapshots, s => s.Dimension == "airQuality" && s.Score == 100);
        Assert.Contains(snapshots, s => s.Dimension == null && s.Score == 100);
    }

    [Fact]
    public async Task RunAsync_generates_no_trend_when_the_only_snapshot_is_not_old_enough_yet()
    {
        await SeedOrUpdateAirQualityAsync(0);
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ => { callCount++; return GeminiJson(ValidModelResponse); });

        await using (var context = CreateContext())
        {
            await CreateJob(context, T0, handler).RunAsync(CancellationToken.None);
        }

        await SeedOrUpdateAirQualityAsync(500); // a real, meaningful change (score drops toward 0)

        // Only 3 days later - below MinimumBaselineAge (7 days), so the T0 snapshot must not be
        // used as a baseline yet, even though the delta it would produce is very real.
        await using (var context = CreateContext())
        {
            await CreateJob(context, T0.AddDays(3), handler).RunAsync(CancellationToken.None);
        }

        Assert.Equal(0, callCount);
        await using (var context = CreateContext())
        {
            Assert.Empty(await context.DistrictTrendSummaries.ToListAsync());
        }
    }

    [Fact]
    public async Task RunAsync_persists_a_grounded_trend_once_a_meaningful_change_exists_against_an_old_enough_baseline()
    {
        await SeedOrUpdateAirQualityAsync(0);
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ => { callCount++; return GeminiJson(ValidModelResponse); });

        await using (var context = CreateContext())
        {
            await CreateJob(context, T0, handler).RunAsync(CancellationToken.None);
        }

        await SeedOrUpdateAirQualityAsync(500);

        // Exactly at MinimumBaselineAge - the T0 snapshot is now eligible as a baseline.
        await using (var context = CreateContext())
        {
            await CreateJob(context, T0.AddDays(7), handler).RunAsync(CancellationToken.None);
        }

        Assert.Equal(1, callCount);
        await using (var context = CreateContext())
        {
            var trend = await context.DistrictTrendSummaries.SingleAsync(t => t.NeighborhoodId == "kadikoy");
            Assert.Equal("Bu ilçenin hava kalitesi skoru belirgin şekilde azaldı.", trend.SummaryText);
            Assert.NotEqual(default, trend.GeneratedAt);
            Assert.Contains("airQuality", trend.ComparisonSignature);
        }
    }

    [Fact]
    public async Task RunAsync_generates_no_trend_when_nothing_meaningfully_changed_since_the_old_enough_baseline()
    {
        await SeedOrUpdateAirQualityAsync(0);
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ => { callCount++; return GeminiJson(ValidModelResponse); });

        await using (var context = CreateContext())
        {
            await CreateJob(context, T0, handler).RunAsync(CancellationToken.None);
        }

        // No score change at all - same AqiIndex as the baseline snapshot.
        await using (var context = CreateContext())
        {
            await CreateJob(context, T0.AddDays(8), handler).RunAsync(CancellationToken.None);
        }

        Assert.Equal(0, callCount);
        await using (var context = CreateContext())
        {
            Assert.Empty(await context.DistrictTrendSummaries.ToListAsync());
        }
    }

    [Fact]
    public async Task RunAsync_removes_a_stale_trend_once_the_score_reverts_to_match_its_current_baseline()
    {
        await SeedOrUpdateAirQualityAsync(0);
        var handler = new FakeHttpMessageHandler(_ => GeminiJson(ValidModelResponse));

        await using (var context = CreateContext())
        {
            await CreateJob(context, T0, handler).RunAsync(CancellationToken.None); // baseline snapshot @ T0, score 100
        }

        await SeedOrUpdateAirQualityAsync(500); // score drops to ~0
        await using (var context = CreateContext())
        {
            await CreateJob(context, T0.AddDays(8), handler).RunAsync(CancellationToken.None); // generates a real trend
        }

        await using (var context = CreateContext())
        {
            Assert.NotEmpty(await context.DistrictTrendSummaries.ToListAsync());
        }

        // No further score change - by T0+16d, the most recent eligible baseline has rotated
        // forward to the T0+8d snapshot (score ~0), which now equals the still-unchanged current
        // score. The delta that justified the existing trend text no longer holds, so that text
        // must be removed rather than left showing a change that isn't real anymore.
        await using (var context = CreateContext())
        {
            await CreateJob(context, T0.AddDays(16), handler).RunAsync(CancellationToken.None);
        }

        await using (var context = CreateContext())
        {
            Assert.Empty(await context.DistrictTrendSummaries.ToListAsync());
        }
    }

    [Fact]
    public async Task RunAsync_skips_a_second_gemini_call_when_the_comparison_basis_has_not_changed()
    {
        await SeedOrUpdateAirQualityAsync(0);
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ => { callCount++; return GeminiJson(ValidModelResponse); });

        await using (var context = CreateContext())
        {
            await CreateJob(context, T0, handler).RunAsync(CancellationToken.None);
        }

        await SeedOrUpdateAirQualityAsync(500);

        await using (var context = CreateContext())
        {
            await CreateJob(context, T0.AddDays(8), handler).RunAsync(CancellationToken.None);
        }

        Assert.Equal(1, callCount);
        DistrictTrendSummary firstRun;
        await using (var context = CreateContext())
        {
            firstRun = await context.DistrictTrendSummaries.AsNoTracking().SingleAsync(t => t.NeighborhoodId == "kadikoy");
        }

        // Re-running at the exact same "now" with no further score change (e.g. a process
        // restart re-triggering an overdue job) must not spend a second Gemini call, and must
        // leave the existing row exactly as it was.
        await using (var context = CreateContext())
        {
            await CreateJob(context, T0.AddDays(8), handler).RunAsync(CancellationToken.None);
        }

        Assert.Equal(1, callCount);
        await using (var context = CreateContext())
        {
            var secondRun = await context.DistrictTrendSummaries.AsNoTracking().SingleAsync(t => t.NeighborhoodId == "kadikoy");
            Assert.Equal(firstRun.GeneratedAt, secondRun.GeneratedAt);
            Assert.Equal(firstRun.ComparisonSignature, secondRun.ComparisonSignature);
        }
    }

    [Fact]
    public async Task RunAsync_still_takes_a_snapshot_but_writes_no_trend_when_gemini_is_not_configured()
    {
        await SeedOrUpdateAirQualityAsync(0);
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ => { callCount++; return GeminiJson(ValidModelResponse); });

        await using (var context = CreateContext())
        {
            await CreateJob(context, T0, handler, apiKey: null).RunAsync(CancellationToken.None);
        }

        await SeedOrUpdateAirQualityAsync(500);

        await using (var context = CreateContext())
        {
            var exception = await Record.ExceptionAsync(
                () => CreateJob(context, T0.AddDays(8), handler, apiKey: null).RunAsync(CancellationToken.None));
            Assert.Null(exception);
        }

        Assert.Equal(0, callCount);
        await using (var context = CreateContext())
        {
            Assert.Empty(await context.DistrictTrendSummaries.ToListAsync());
            // Snapshotting itself does not depend on Gemini being configured at all.
            Assert.NotEmpty(await context.ScoreSnapshots.Where(s => s.RecordedAt == T0.AddDays(8)).ToListAsync());
        }
    }

    [Fact]
    public async Task RunAsync_writes_no_trend_when_every_change_the_model_returns_is_hallucinated()
    {
        await SeedOrUpdateAirQualityAsync(0);
        var handler = new FakeHttpMessageHandler(_ => GeminiJson(
            """{"changes":[{"dimension":"denizManzarasi","direction":"increased"}],"summary":"Deniz manzaralı bir ilçe."}"""));

        await using (var context = CreateContext())
        {
            await CreateJob(context, T0, handler).RunAsync(CancellationToken.None);
        }

        await SeedOrUpdateAirQualityAsync(500);

        await using (var context = CreateContext())
        {
            await CreateJob(context, T0.AddDays(8), handler).RunAsync(CancellationToken.None);
        }

        await using (var context = CreateContext())
        {
            Assert.Empty(await context.DistrictTrendSummaries.ToListAsync());
        }
    }
}

file sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

file sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
