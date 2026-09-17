using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SemtSkoru.Api.Endpoints;
using SemtSkoru.Application.Assistant;
using SemtSkoru.Application.Search;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SemtSkoru.Api.IntegrationTests;

// End-to-end proof that GET /api/neighborhoods/search's wiring in Program.cs is correct: a real
// seeded district score flows all the way from Postgres, through DistrictSearchService's
// dimension-hallucination guard and real-score ranking, to the HTTP response - with Gemini itself
// faked out (never a real network call in the test suite, matching AssistantEndpointsTests' own
// house style) - AND that this endpoint shares the same AiAssistant rate-limit budget as
// POST /api/asistan rather than getting its own separate allowance.
public class DistrictSearchEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:16-3.4").Build();
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

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

    private WebApplicationFactory<Program> CreateFactory(string? apiKey, Func<HttpRequestMessage, HttpResponseMessage>? geminiResponder = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
            if (apiKey is not null)
            {
                builder.UseSetting("Gemini:ApiKey", apiKey);
            }

            if (geminiResponder is not null)
            {
                builder.ConfigureTestServices(services =>
                {
                    // Same standard IHttpClientFactory testing pattern as AssistantEndpointsTests:
                    // re-configures the SAME named typed-client registration Program.cs set up
                    // (AddHttpClient<IDistrictAssistantAiClient, GeminiClient>) - this endpoint
                    // reuses that exact client/registration, not a second one.
                    services.AddHttpClient<IDistrictAssistantAiClient, SemtSkoru.Infrastructure.ExternalApis.GeminiClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(geminiResponder));
                });
            }
        });

    private static HttpResponseMessage GeminiJson(string modelJsonText) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = modelJsonText } } } } } }),
            Encoding.UTF8,
            "application/json"),
    };

    [Fact]
    public async Task A_query_that_maps_to_a_real_dimension_returns_only_the_real_district_that_scores_well_on_it()
    {
        var publishedAt = DateTimeOffset.UtcNow;
        await using (var context = CreateContext())
        {
            context.AirQualityReadings.AddRange(
                new AirQualityReading
                {
                    NeighborhoodId = "kadikoy",
                    AqiIndex = 0, // -> a real, deterministic score of 100 (see DimensionScoring)
                    ReadingTime = DateTimeOffset.UtcNow,
                    Source = TestSource(publishedAt),
                },
                new AirQualityReading
                {
                    NeighborhoodId = "uskudar",
                    AqiIndex = 500, // -> a real, deterministic score of 0 - well below the "good" cutoff
                    ReadingTime = DateTimeOffset.UtcNow,
                    Source = TestSource(publishedAt),
                });
            await context.SaveChangesAsync();
        }

        var modelJson = """{"dimensions":["airQuality"]}""";
        _factory = CreateFactory("test-key", _ => GeminiJson(modelJson));
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods/search?q=hava%20kalitesi%20iyi%20ilçeler");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DistrictSearchResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(nameof(DistrictSearchOutcomeKind.Ok), body.Status);
        Assert.Equal(["airQuality"], body.Dimensions);
        var matchedId = Assert.Single(body.MatchedIds);
        Assert.Equal("kadikoy", matchedId);
        Assert.DoesNotContain("uskudar", body.MatchedIds);
    }

    [Fact]
    public async Task A_hallucinated_dimension_is_dropped_and_a_real_one_alongside_it_is_kept()
    {
        var publishedAt = DateTimeOffset.UtcNow;
        await using (var context = CreateContext())
        {
            context.AirQualityReadings.Add(new AirQualityReading
            {
                NeighborhoodId = "kadikoy",
                AqiIndex = 0,
                ReadingTime = DateTimeOffset.UtcNow,
                Source = TestSource(publishedAt),
            });
            await context.SaveChangesAsync();
        }

        var modelJson = """{"dimensions":["airQuality","fiyat"]}""";
        _factory = CreateFactory("test-key", _ => GeminiJson(modelJson));
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods/search?q=ucuz%20ve%20hava%20kalitesi%20iyi%20ilçeler");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DistrictSearchResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(["airQuality"], body.Dimensions);
    }

    [Fact]
    public async Task Returns_503_when_no_api_key_is_configured()
    {
        _factory = CreateFactory(apiKey: null);
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods/search?q=bir%20sorgu");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DistrictSearchResponseDto>();
        Assert.Equal(nameof(DistrictSearchOutcomeKind.NotConfigured), body!.Status);
        Assert.False(string.IsNullOrWhiteSpace(body.Message));
    }

    [Fact]
    public async Task Returns_400_for_an_empty_query()
    {
        _factory = CreateFactory("test-key", _ => GeminiJson("""{"dimensions":[]}"""));
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods/search?q=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Returns_an_honest_no_usable_criteria_response_rather_than_every_district_for_an_off_topic_query()
    {
        _factory = CreateFactory("test-key", _ => GeminiJson("""{"dimensions":[]}"""));
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods/search?q=en%20ucuz%20ilçeler");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DistrictSearchResponseDto>();
        Assert.Equal(nameof(DistrictSearchOutcomeKind.NoUsableCriteria), body!.Status);
        Assert.Empty(body.MatchedIds);
        Assert.False(string.IsNullOrWhiteSpace(body.Message));
    }

    [Fact]
    public async Task The_shared_AiAssistant_rate_limit_policy_eventually_rejects_a_tight_request_loop_with_429()
    {
        _factory = CreateFactory("test-key", _ => GeminiJson("""{"dimensions":[]}"""));
        var client = _factory.CreateClient();

        // Both the per-IP window (5/10min) and the global per-minute budget (5/min) - see
        // RateLimitingExtensions.cs - are exhausted well before this many sequential calls from
        // one client, exactly like AssistantEndpointsTests' own equivalent test for POST
        // /api/asistan - proving this endpoint is under the SAME policy, not a separate one.
        var statusCodes = new List<HttpStatusCode>();
        for (var i = 0; i < 8; i++)
        {
            var response = await client.GetAsync($"/api/neighborhoods/search?q=sorgu%20{i}");
            statusCodes.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statusCodes);
    }

    [Fact]
    public async Task Search_and_the_ai_assistant_endpoint_share_the_same_global_per_minute_gemini_budget()
    {
        // The global per-minute/per-day limiters in RateLimitingExtensions.AiAssistantPartition()
        // are constructed ONCE and closed over by every caller of the "ai-assistant" named policy
        // regardless of which endpoint requested it - this proves that sharing holds across
        // /api/neighborhoods/search and /api/asistan, not just within one endpoint's own loop.
        _factory = CreateFactory("test-key", _ => GeminiJson("""{"dimensions":[]}"""));
        var client = _factory.CreateClient();

        var statusCodes = new List<HttpStatusCode>();
        for (var i = 0; i < 8; i++)
        {
            // Alternate between the two endpoints - if the budget were tracked separately per
            // endpoint, neither loop alone would ever hit the shared 5/minute global cap.
            var response = i % 2 == 0
                ? await client.GetAsync($"/api/neighborhoods/search?q=sorgu%20{i}")
                : await client.PostAsJsonAsync("/api/asistan", new { prompt = $"istek {i}" });
            statusCodes.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statusCodes);
    }
}

file sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
