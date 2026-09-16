using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Application.Summaries;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Persistence;
using SemtSkoru.Infrastructure.Scoring;
using SemtSkoru.Infrastructure.Summaries;
using Testcontainers.PostgreSql;

namespace SemtSkoru.Api.IntegrationTests.Summaries;

// End-to-end proof (real Postgres via Testcontainers, real 39-district seed) that
// DistrictSummaryGenerationJob's whole point holds up outside of unit tests: it persists a real
// generated summary, it never spends a second Gemini call once a district's scores stop changing,
// and it degrades to a complete no-op - not an exception, not a placeholder row - when Gemini
// isn't configured at all. Gemini itself is faked out the same way AssistantEndpointsTests.cs
// fakes it - never a real network call in the test suite.
public class DistrictSummaryGenerationJobTests : IAsyncLifetime
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
    // false and is skipped by the job before it ever calls Gemini or waits out the pacing delay
    // (see DistrictSummaryGenerationJob's remarks), which is what keeps this test fast.
    private async Task SeedOneScoredDistrictAsync()
    {
        await using var context = CreateContext();
        context.AirQualityReadings.Add(new AirQualityReading
        {
            NeighborhoodId = "kadikoy",
            AqiIndex = 0, // -> a real, deterministic score of 100 (see DimensionScoring)
            ReadingTime = DateTimeOffset.UtcNow,
            Source = TestSource(DateTimeOffset.UtcNow),
        });
        await context.SaveChangesAsync();
    }

    private static IConfiguration ConfigWithKey(string? apiKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(apiKey is null ? [] : new Dictionary<string, string?> { ["Gemini:ApiKey"] = apiKey })
            .Build();

    private static DistrictSummaryGenerationJob CreateJob(
        AppDbContext context, HttpMessageHandler geminiHandler, string? apiKey = "test-key") => new(
        new SemtSkoru.Infrastructure.Persistence.NeighborhoodDirectory(context),
        new NeighborhoodScoringService(new NeighborhoodScoringRepository(context), TimeProvider.System),
        new DistrictSummaryService(new GeminiClient(new HttpClient(geminiHandler), ConfigWithKey(apiKey))),
        context,
        NullLogger<DistrictSummaryGenerationJob>.Instance,
        TimeProvider.System);

    private static HttpResponseMessage GeminiJson(string modelJsonText) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = modelJsonText } } } } } }),
            Encoding.UTF8,
            "application/json"),
    };

    private const string ValidModelResponse =
        """{"highlights":[{"dimension":"airQuality","strength":"strong"}],"summary":"Bu ilçe hava kalitesinde güçlü."}""";

    [Fact]
    public async Task RunAsync_persists_a_grounded_summary_for_a_scored_district_in_both_supported_locales()
    {
        await SeedOneScoredDistrictAsync();
        await using var context = CreateContext();
        var callCount = 0;
        var job = CreateJob(context, new FakeHttpMessageHandler(_ => { callCount++; return GeminiJson(ValidModelResponse); }));

        await job.RunAsync(CancellationToken.None);

        // Both locales, not just "tr": this is the whole point of the locale-awareness change -
        // an English-locale visitor must get an "en" row from the SAME weekly run, not a second
        // deploy's worth of waiting.
        Assert.Equal(2, callCount);
        var summaries = await context.DistrictSummaries.Where(s => s.NeighborhoodId == "kadikoy").ToListAsync();
        Assert.Equal(2, summaries.Count);
        Assert.Contains(summaries, s => s.Locale == "tr");
        Assert.Contains(summaries, s => s.Locale == "en");
        Assert.All(summaries, s =>
        {
            Assert.Equal("Bu ilçe hava kalitesinde güçlü.", s.SummaryText);
            Assert.NotEqual(default, s.GeneratedAt);
            Assert.False(string.IsNullOrWhiteSpace(s.ScoreSignature));
        });
    }

    [Fact]
    public async Task RunAsync_never_creates_a_row_for_a_district_with_no_ingested_data()
    {
        // No readings seeded at all - every one of the 39 districts has HasAnyData == false.
        await using var context = CreateContext();
        var callCount = 0;
        var job = CreateJob(context, new FakeHttpMessageHandler(_ => { callCount++; return GeminiJson(ValidModelResponse); }));

        await job.RunAsync(CancellationToken.None);

        Assert.Equal(0, callCount);
        Assert.Empty(await context.DistrictSummaries.ToListAsync());
    }

    [Fact]
    public async Task RunAsync_skips_a_second_gemini_call_in_either_locale_when_the_districts_scores_have_not_changed()
    {
        await SeedOneScoredDistrictAsync();
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ => { callCount++; return GeminiJson(ValidModelResponse); });

        await using (var context = CreateContext())
        {
            await CreateJob(context, handler).RunAsync(CancellationToken.None);
        }

        Assert.Equal(2, callCount); // one call per locale on the first run.
        List<DistrictSummary> firstRun;
        await using (var context = CreateContext())
        {
            firstRun = await context.DistrictSummaries.AsNoTracking().Where(s => s.NeighborhoodId == "kadikoy").ToListAsync();
        }

        Assert.Equal(2, firstRun.Count);

        // Nothing about kadikoy's scores changed between runs - a second run must not spend
        // another Gemini call FOR EITHER LOCALE, and must leave both existing rows (including
        // their GeneratedAt) exactly as they were, not silently refresh the timestamp for no real
        // reason.
        await using (var context = CreateContext())
        {
            await CreateJob(context, handler).RunAsync(CancellationToken.None);
        }

        Assert.Equal(2, callCount);
        await using (var context = CreateContext())
        {
            var secondRun = await context.DistrictSummaries.AsNoTracking().Where(s => s.NeighborhoodId == "kadikoy").ToListAsync();
            Assert.Equal(2, secondRun.Count);
            foreach (var before in firstRun)
            {
                var after = Assert.Single(secondRun, s => s.Locale == before.Locale);
                Assert.Equal(before.GeneratedAt, after.GeneratedAt);
                Assert.Equal(before.ScoreSignature, after.ScoreSignature);
            }
        }
    }

    [Fact]
    public async Task RunAsync_regenerates_in_both_locales_once_the_districts_score_actually_changes()
    {
        await SeedOneScoredDistrictAsync();
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ => { callCount++; return GeminiJson(ValidModelResponse); });

        await using (var context = CreateContext())
        {
            await CreateJob(context, handler).RunAsync(CancellationToken.None);
        }

        Assert.Equal(2, callCount);

        // A real score change (a fresh, much worse air quality reading) - this must trigger
        // exactly one more Gemini call PER LOCALE (both "tr" and "en" rows are now stale), not
        // zero (stale skip) and not a crash.
        await using (var context = CreateContext())
        {
            var reading = await context.AirQualityReadings.SingleAsync(r => r.NeighborhoodId == "kadikoy");
            reading.AqiIndex = 500;
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            await CreateJob(context, handler).RunAsync(CancellationToken.None);
        }

        Assert.Equal(4, callCount);
    }

    // The specific per-locale-independent skip behavior the brief calls out explicitly: a locale
    // that already has an up-to-date row must be skipped, while a locale with NO row yet (the
    // exact state every "en" row is in immediately after this app added locale support, until the
    // next weekly run) must still be generated - even though the OTHER locale's signature already
    // matches and would, on its own, justify skipping.
    [Fact]
    public async Task RunAsync_generates_only_the_missing_locale_when_the_other_locale_is_already_up_to_date()
    {
        await SeedOneScoredDistrictAsync();
        await using (var context = CreateContext())
        {
            // Simulates the pre-locale-support state: only a "tr" row exists, with a signature
            // that already matches kadikoy's current (unchanged) score.
            context.DistrictSummaries.Add(new DistrictSummary
            {
                NeighborhoodId = "kadikoy",
                Locale = "tr",
                SummaryText = "Zaten var olan Türkçe özet.",
                GeneratedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ScoreSignature = "100|null|null|null|null|null", // matches AqiIndex=0 -> score 100, see SeedOneScoredDistrictAsync.
            });
            await context.SaveChangesAsync();
        }

        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ => { callCount++; return GeminiJson(ValidModelResponse); });
        await using (var context = CreateContext())
        {
            await CreateJob(context, handler).RunAsync(CancellationToken.None);
        }

        // Exactly one call - for "en", which had no row at all. "tr" is skipped because its
        // existing row's signature already matches the district's current score.
        Assert.Equal(1, callCount);
        await using (var context = CreateContext())
        {
            var summaries = await context.DistrictSummaries.Where(s => s.NeighborhoodId == "kadikoy").ToListAsync();
            Assert.Equal(2, summaries.Count);
            var trRow = Assert.Single(summaries, s => s.Locale == "tr");
            Assert.Equal("Zaten var olan Türkçe özet.", trRow.SummaryText); // untouched by this run.
            var enRow = Assert.Single(summaries, s => s.Locale == "en");
            Assert.Equal("Bu ilçe hava kalitesinde güçlü.", enRow.SummaryText); // freshly generated.
        }
    }

    [Fact]
    public async Task RunAsync_does_not_throw_and_writes_nothing_when_gemini_is_not_configured()
    {
        await SeedOneScoredDistrictAsync();
        await using var context = CreateContext();
        var callCount = 0;
        var job = CreateJob(
            context,
            new FakeHttpMessageHandler(_ => { callCount++; return GeminiJson(ValidModelResponse); }),
            apiKey: null);

        var exception = await Record.ExceptionAsync(() => job.RunAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Equal(0, callCount);
        Assert.Empty(await context.DistrictSummaries.ToListAsync());
    }

    [Fact]
    public async Task RunAsync_writes_nothing_when_every_highlight_the_model_returns_is_hallucinated()
    {
        await SeedOneScoredDistrictAsync();
        await using var context = CreateContext();
        var job = CreateJob(
            context,
            new FakeHttpMessageHandler(_ => GeminiJson("""{"highlights":[{"dimension":"deniz_manzarasi","strength":"strong"}],"summary":"Deniz manzaralı bir ilçe."}""")));

        await job.RunAsync(CancellationToken.None);

        Assert.Empty(await context.DistrictSummaries.ToListAsync());
    }
}

file sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
