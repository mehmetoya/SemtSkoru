using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SemtSkoru.Application.Comparisons;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.Comparisons;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Persistence;
using SemtSkoru.Infrastructure.Scoring;
using Testcontainers.PostgreSql;

namespace SemtSkoru.Api.IntegrationTests.Comparisons;

// End-to-end proof (real Postgres via Testcontainers, real 39-district seed) that
// ComparisonSummaryOrchestrator's whole point holds up outside of unit tests: a first-time pair
// costs exactly one Gemini call and persists a real cache row; a repeat view of the SAME pair -
// even picked in the opposite order - is a cache hit that spends zero further calls; a real score
// change on either side invalidates the cache; and every failure mode degrades to null, never an
// exception and never a stale/fabricated fallback. Gemini itself is faked out the same way
// DistrictSummaryGenerationJobTests.cs/AssistantEndpointsTests.cs fake it - never a real network
// call in the test suite.
public class ComparisonSummaryOrchestratorTests : IAsyncLifetime
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

    // kadikoy AqiIndex=0 -> a real, deterministic score of 100; uskudar AqiIndex=500 -> a real,
    // deterministic score of 0 (same DimensionScoring mapping every other test in this suite
    // relies on) - a genuine, unambiguous difference for the "stronger district" grounding check.
    private async Task SeedTwoDistrictsWithDistinctAirQualityAsync()
    {
        await using var context = CreateContext();
        context.AirQualityReadings.AddRange(
            new AirQualityReading { NeighborhoodId = "kadikoy", AqiIndex = 0, ReadingTime = DateTimeOffset.UtcNow, Source = TestSource(DateTimeOffset.UtcNow) },
            new AirQualityReading { NeighborhoodId = "uskudar", AqiIndex = 500, ReadingTime = DateTimeOffset.UtcNow, Source = TestSource(DateTimeOffset.UtcNow) });
        await context.SaveChangesAsync();
    }

    private static IConfiguration ConfigWithKey(string? apiKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(apiKey is null ? [] : new Dictionary<string, string?> { ["Gemini:ApiKey"] = apiKey })
            .Build();

    private static ComparisonSummaryOrchestrator CreateOrchestrator(
        AppDbContext context, HttpMessageHandler geminiHandler, string? apiKey = "test-key") => new(
        new ComparisonSummaryService(new GeminiClient(new HttpClient(geminiHandler), ConfigWithKey(apiKey), NullLogger<GeminiClient>.Instance, TimeProvider.System)),
        context,
        TimeProvider.System);

    private static HttpResponseMessage GeminiJson(string modelJsonText) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = modelJsonText } } } } } }),
            Encoding.UTF8,
            "application/json"),
    };

    // kadikoy (a=100) really is stronger than uskudar (b=0) at airQuality - a true claim.
    private const string ValidModelResponse =
        """{"highlights":[{"dimension":"airQuality","strongerDistrict":"a"}],"summary":"Kadıköy hava kalitesinde öne çıkıyor."}""";

    private async Task<NeighborhoodScoreResult> ScoreAsync(AppDbContext context, string id) =>
        (await new NeighborhoodScoringService(new NeighborhoodScoringRepository(context), TimeProvider.System).GetScoreAsync(id, CancellationToken.None))!;

    [Fact]
    public async Task GetOrGenerateAsync_persists_a_grounded_summary_for_a_never_before_seen_pair()
    {
        await SeedTwoDistrictsWithDistinctAirQualityAsync();
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context, new FakeHttpMessageHandler(_ => GeminiJson(ValidModelResponse)));
        var kadikoy = await ScoreAsync(context, "kadikoy");
        var uskudar = await ScoreAsync(context, "uskudar");

        var result = await orchestrator.GetOrGenerateAsync("Kadıköy", kadikoy, "Üsküdar", uskudar, "tr", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Kadıköy hava kalitesinde öne çıkıyor.", result.SummaryText);
        Assert.NotEqual(default, result.GeneratedAt);
        Assert.False(string.IsNullOrWhiteSpace(result.ScoreSignature));

        var persisted = await context.ComparisonSummaries.SingleAsync();
        Assert.Equal("Kadıköy hava kalitesinde öne çıkıyor.", persisted.SummaryText);
    }

    // "kadikoy" < "uskudar" ordinally, so the canonical row is (kadikoy, uskudar) regardless of
    // which one the caller passes as A - proves ComparisonSummaryOrchestrator.Canonicalize.
    [Fact]
    public async Task GetOrGenerateAsync_stores_the_pair_under_its_canonical_ordinal_order_regardless_of_caller_order()
    {
        await SeedTwoDistrictsWithDistinctAirQualityAsync();
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context, new FakeHttpMessageHandler(_ => GeminiJson(ValidModelResponse)));
        var kadikoy = await ScoreAsync(context, "kadikoy");
        var uskudar = await ScoreAsync(context, "uskudar");

        // Called with uskudar as "A" and kadikoy as "B" - the opposite of the district ids' own
        // ordinal order.
        await orchestrator.GetOrGenerateAsync("Üsküdar", uskudar, "Kadıköy", kadikoy, "tr", CancellationToken.None);

        var persisted = await context.ComparisonSummaries.SingleAsync();
        Assert.Equal("kadikoy", persisted.NeighborhoodIdA);
        Assert.Equal("uskudar", persisted.NeighborhoodIdB);
    }

    [Fact]
    public async Task GetOrGenerateAsync_returns_a_cached_summary_without_a_second_gemini_call_when_nothing_changed()
    {
        await SeedTwoDistrictsWithDistinctAirQualityAsync();
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ => { callCount++; return GeminiJson(ValidModelResponse); });

        DateTimeOffset firstGeneratedAt;
        await using (var context = CreateContext())
        {
            var kadikoy = await ScoreAsync(context, "kadikoy");
            var uskudar = await ScoreAsync(context, "uskudar");
            await CreateOrchestrator(context, handler).GetOrGenerateAsync(
                "Kadıköy", kadikoy, "Üsküdar", uskudar, "tr", CancellationToken.None);
        }

        Assert.Equal(1, callCount);

        // Re-read the persisted value through a fresh context rather than trusting the
        // in-memory object GetOrGenerateAsync just returned: Postgres' `timestamp with time
        // zone` only keeps microsecond precision, one digit less than .NET's DateTimeOffset
        // ticks, so an in-memory value can differ from its own round-tripped copy by a few
        // ticks - comparing two DB-round-tripped reads (this one and the one below) avoids that
        // precision mismatch entirely, live-verified as the actual cause of an intermittent CI
        // failure (2026-09-16) comparing the raw in-memory value against a freshly-read one.
        await using (var context = CreateContext())
        {
            firstGeneratedAt = (await context.ComparisonSummaries.SingleAsync()).GeneratedAt;
        }

        // A second request for the SAME pair (this time picked in the opposite order, as a real
        // visitor comparing the same two districts a second time might) must be a pure cache hit.
        await using (var context = CreateContext())
        {
            var kadikoy = await ScoreAsync(context, "kadikoy");
            var uskudar = await ScoreAsync(context, "uskudar");
            var result = await CreateOrchestrator(context, handler).GetOrGenerateAsync(
                "Üsküdar", uskudar, "Kadıköy", kadikoy, "tr", CancellationToken.None);

            Assert.Equal(1, callCount); // no new Gemini call.
            Assert.Equal(firstGeneratedAt, result!.GeneratedAt); // the exact same cached row, untouched.
        }
    }

    [Fact]
    public async Task GetOrGenerateAsync_regenerates_once_either_districts_score_actually_changes()
    {
        await SeedTwoDistrictsWithDistinctAirQualityAsync();
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ => { callCount++; return GeminiJson(ValidModelResponse); });

        await using (var context = CreateContext())
        {
            var kadikoy = await ScoreAsync(context, "kadikoy");
            var uskudar = await ScoreAsync(context, "uskudar");
            await CreateOrchestrator(context, handler).GetOrGenerateAsync("Kadıköy", kadikoy, "Üsküdar", uskudar, "tr", CancellationToken.None);
        }

        Assert.Equal(1, callCount);

        // A real score change on just ONE side of the pair (uskudar's air quality improves) must
        // still invalidate the cached pair and trigger exactly one more Gemini call.
        await using (var context = CreateContext())
        {
            var reading = await context.AirQualityReadings.SingleAsync(r => r.NeighborhoodId == "uskudar");
            reading.AqiIndex = 0;
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var kadikoy = await ScoreAsync(context, "kadikoy");
            var uskudar = await ScoreAsync(context, "uskudar");
            await CreateOrchestrator(context, handler).GetOrGenerateAsync("Kadıköy", kadikoy, "Üsküdar", uskudar, "tr", CancellationToken.None);
        }

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GetOrGenerateAsync_returns_null_and_writes_nothing_when_gemini_is_not_configured()
    {
        await SeedTwoDistrictsWithDistinctAirQualityAsync();
        await using var context = CreateContext();
        var callCount = 0;
        var orchestrator = CreateOrchestrator(
            context,
            new FakeHttpMessageHandler(_ => { callCount++; return GeminiJson(ValidModelResponse); }),
            apiKey: null);
        var kadikoy = await ScoreAsync(context, "kadikoy");
        var uskudar = await ScoreAsync(context, "uskudar");

        var result = await orchestrator.GetOrGenerateAsync("Kadıköy", kadikoy, "Üsküdar", uskudar, "tr", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, callCount);
        Assert.Empty(await context.ComparisonSummaries.ToListAsync());
    }

    [Fact]
    public async Task GetOrGenerateAsync_returns_null_when_every_highlight_the_model_returns_is_hallucinated()
    {
        await SeedTwoDistrictsWithDistinctAirQualityAsync();
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(
            context,
            new FakeHttpMessageHandler(_ => GeminiJson("""{"highlights":[{"dimension":"deniz_manzarasi","strongerDistrict":"a"}],"summary":"Deniz manzaralı."}""")));
        var kadikoy = await ScoreAsync(context, "kadikoy");
        var uskudar = await ScoreAsync(context, "uskudar");

        var result = await orchestrator.GetOrGenerateAsync("Kadıköy", kadikoy, "Üsküdar", uskudar, "tr", CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(await context.ComparisonSummaries.ToListAsync());
    }

    // The specific "never show something uncertain as if it were solid" design choice documented
    // on ComparisonSummaryOrchestrator: once a cached row's signature no longer matches (a real
    // score change happened), a failed regeneration attempt must NOT fall back to serving the
    // now-stale cached text - it must return null, even though a row technically still exists.
    [Fact]
    public async Task GetOrGenerateAsync_returns_null_not_a_stale_row_when_regeneration_fails_after_a_score_change()
    {
        await SeedTwoDistrictsWithDistinctAirQualityAsync();
        var handler = new FakeHttpMessageHandler(_ => GeminiJson(ValidModelResponse));

        await using (var context = CreateContext())
        {
            var kadikoy = await ScoreAsync(context, "kadikoy");
            var uskudar = await ScoreAsync(context, "uskudar");
            var firstResult = await CreateOrchestrator(context, handler).GetOrGenerateAsync(
                "Kadıköy", kadikoy, "Üsküdar", uskudar, "tr", CancellationToken.None);
            Assert.NotNull(firstResult);
        }

        // Score changes (invalidating the cache), and this time Gemini is unreachable.
        await using (var context = CreateContext())
        {
            var reading = await context.AirQualityReadings.SingleAsync(r => r.NeighborhoodId == "uskudar");
            reading.AqiIndex = 0;
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var failingHandler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
            var kadikoy = await ScoreAsync(context, "kadikoy");
            var uskudar = await ScoreAsync(context, "uskudar");
            var result = await CreateOrchestrator(context, failingHandler).GetOrGenerateAsync(
                "Kadıköy", kadikoy, "Üsküdar", uskudar, "tr", CancellationToken.None);

            Assert.Null(result);
        }

        // The stale row from before the score change is still in the table (never deleted), but
        // GetOrGenerateAsync must never have handed it back as if it were current.
        await using (var context = CreateContext())
        {
            Assert.Single(await context.ComparisonSummaries.ToListAsync());
        }
    }

    // The locale-scoping correctness the whole point of ComparisonSummaryConfiguration's
    // composite (NeighborhoodIdA, NeighborhoodIdB, Locale) key exists to guarantee: a pair
    // already cached in ONE locale is still a live Gemini call - not a cache hit - the first time
    // the OTHER locale requests it, and the two locales' cached rows never leak into each other.
    // Also exercises the composite-key FindAsync lookup for the case where only one locale's row
    // exists yet for a pair.
    [Fact]
    public async Task GetOrGenerateAsync_caches_tr_and_en_independently_for_the_same_pair()
    {
        await SeedTwoDistrictsWithDistinctAirQualityAsync();
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ => { callCount++; return GeminiJson(ValidModelResponse); });

        await using (var context = CreateContext())
        {
            var kadikoy = await ScoreAsync(context, "kadikoy");
            var uskudar = await ScoreAsync(context, "uskudar");
            var trResult = await CreateOrchestrator(context, handler).GetOrGenerateAsync(
                "Kadıköy", kadikoy, "Üsküdar", uskudar, "tr", CancellationToken.None);
            Assert.NotNull(trResult);
        }

        Assert.Equal(1, callCount);

        // Same exact pair (same scores, unchanged), requested in "en" for the first time - must
        // still be a live Gemini call, not served from the "tr" cache row.
        await using (var context = CreateContext())
        {
            var kadikoy = await ScoreAsync(context, "kadikoy");
            var uskudar = await ScoreAsync(context, "uskudar");
            var enResult = await CreateOrchestrator(context, handler).GetOrGenerateAsync(
                "Kadıköy", kadikoy, "Üsküdar", uskudar, "en", CancellationToken.None);
            Assert.NotNull(enResult);
        }

        Assert.Equal(2, callCount);

        // Both locale rows now exist, independently, for the same canonical pair.
        await using (var context = CreateContext())
        {
            var persisted = await context.ComparisonSummaries
                .Where(c => c.NeighborhoodIdA == "kadikoy" && c.NeighborhoodIdB == "uskudar")
                .ToListAsync();
            Assert.Equal(2, persisted.Count);
            Assert.Contains(persisted, c => c.Locale == "tr");
            Assert.Contains(persisted, c => c.Locale == "en");
        }

        // A second "tr" request for the same unchanged pair is now a pure cache hit again - the
        // "en" request in between must not have disturbed it.
        await using (var context = CreateContext())
        {
            var kadikoy = await ScoreAsync(context, "kadikoy");
            var uskudar = await ScoreAsync(context, "uskudar");
            await CreateOrchestrator(context, handler).GetOrGenerateAsync(
                "Kadıköy", kadikoy, "Üsküdar", uskudar, "tr", CancellationToken.None);
        }

        Assert.Equal(2, callCount); // no new call.
    }
}

file sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
