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
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SemtSkoru.Api.IntegrationTests;

// End-to-end proof that GET /api/neighborhoods/compare/summary's wiring in Program.cs is correct:
// a real seeded pair of district scores flows all the way from Postgres, through
// ComparisonSummaryOrchestrator/ComparisonSummaryService's grounding/caching, to the HTTP
// response - with Gemini itself faked out (never a real network call in the test suite, matching
// AssistantEndpointsTests.cs's own house style).
public class ComparisonSummaryEndpointTests : IAsyncLifetime
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

    private async Task SeedTwoDistrictsWithDistinctAirQualityAsync()
    {
        await using var context = CreateContext();
        context.AirQualityReadings.AddRange(
            new AirQualityReading { NeighborhoodId = "kadikoy", AqiIndex = 0, ReadingTime = DateTimeOffset.UtcNow, Source = TestSource(DateTimeOffset.UtcNow) },
            new AirQualityReading { NeighborhoodId = "uskudar", AqiIndex = 500, ReadingTime = DateTimeOffset.UtcNow, Source = TestSource(DateTimeOffset.UtcNow) });
        await context.SaveChangesAsync();
    }

    // Mirrors AssistantEndpointsTests.cs's own CreateFactory: re-configures the SAME typed-client
    // registration (AddHttpClient<IDistrictAssistantAiClient, GeminiClient>) Program.cs already
    // sets up - the standard IHttpClientFactory testing pattern. ComparisonSummaryService depends
    // on that SAME interface, so this one fake covers both /api/asistan and this endpoint.
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

    private const string ValidModelResponse =
        """{"highlights":[{"dimension":"airQuality","strongerDistrict":"a"}],"summary":"Kadıköy hava kalitesinde öne çıkıyor."}""";

    [Fact]
    public async Task A_grounded_comparison_summary_flows_through_the_full_stack()
    {
        await SeedTwoDistrictsWithDistinctAirQualityAsync();
        _factory = CreateFactory("test-key", _ => GeminiJson(ValidModelResponse));
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods/compare/summary?a=kadikoy&b=uskudar");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ComparisonSummaryResponseDto>();
        Assert.NotNull(body?.Summary);
        Assert.Equal("Kadıköy hava kalitesinde öne çıkıyor.", body.Summary.Text);
    }

    [Fact]
    public async Task Returns_a_null_summary_with_200_ok_rather_than_an_error_when_no_api_key_is_configured()
    {
        await SeedTwoDistrictsWithDistinctAirQualityAsync();
        _factory = CreateFactory(apiKey: null);
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods/compare/summary?a=kadikoy&b=uskudar");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ComparisonSummaryResponseDto>();
        Assert.NotNull(body);
        Assert.Null(body.Summary);
    }

    [Fact]
    public async Task Returns_a_null_summary_rather_than_an_error_when_every_highlight_is_hallucinated()
    {
        await SeedTwoDistrictsWithDistinctAirQualityAsync();
        _factory = CreateFactory("test-key", _ => GeminiJson("""{"highlights":[{"dimension":"deniz_manzarasi","strongerDistrict":"a"}],"summary":"x"}"""));
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods/compare/summary?a=kadikoy&b=uskudar");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ComparisonSummaryResponseDto>();
        Assert.NotNull(body);
        Assert.Null(body.Summary);
    }

    [Fact]
    public async Task Returns_400_when_a_query_parameter_is_missing()
    {
        _factory = CreateFactory("test-key");
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods/compare/summary?a=kadikoy");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Returns_400_when_an_id_does_not_exist()
    {
        _factory = CreateFactory("test-key");
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods/compare/summary?a=kadikoy&b=nonexistent");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_second_request_for_the_same_pair_is_served_from_cache_without_a_second_gemini_call()
    {
        await SeedTwoDistrictsWithDistinctAirQualityAsync();
        var callCount = 0;
        _factory = CreateFactory("test-key", _ => { callCount++; return GeminiJson(ValidModelResponse); });
        var client = _factory.CreateClient();

        var first = await client.GetAsync("/api/neighborhoods/compare/summary?a=kadikoy&b=uskudar");
        first.EnsureSuccessStatusCode();
        Assert.Equal(1, callCount);

        // Same pair, opposite query-parameter order - must still be the same canonical cache row.
        var second = await client.GetAsync("/api/neighborhoods/compare/summary?a=uskudar&b=kadikoy");
        second.EnsureSuccessStatusCode();
        Assert.Equal(1, callCount);

        var firstBody = await first.Content.ReadFromJsonAsync<ComparisonSummaryResponseDto>();
        var secondBody = await second.Content.ReadFromJsonAsync<ComparisonSummaryResponseDto>();
        Assert.Equal(firstBody!.Summary!.Text, secondBody!.Summary!.Text);
    }

    // Proves this endpoint spends the SAME shared, global Gemini budget as POST /api/asistan
    // (both map to RateLimitPolicies.AiAssistant - see RateLimiting/RateLimitingExtensions.cs),
    // not a separate allowance of its own: exhausting the global per-minute budget via repeated
    // calls to THIS endpoint must cause /api/asistan to also start getting 429s, and vice versa.
    [Fact]
    public async Task Shares_the_same_global_rate_limit_budget_as_the_ai_assistant_endpoint()
    {
        await SeedTwoDistrictsWithDistinctAirQualityAsync();
        _factory = CreateFactory("test-key", _ => GeminiJson(ValidModelResponse));
        var client = _factory.CreateClient();

        // The global per-minute budget (AiAssistantGlobalPermitLimitPerMinute = 5) is shared
        // across every caller and every AI endpoint - drive it to exhaustion purely through this
        // NEW endpoint's own successful calls (the per-IP window here is 5/10min, so this stays
        // under that too), then prove the OTHER AI endpoint is now also rejected.
        var statusCodes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            var response = await client.GetAsync("/api/neighborhoods/compare/summary?a=kadikoy&b=uskudar");
            statusCodes.Add(response.StatusCode);
        }

        Assert.All(statusCodes, s => Assert.Equal(HttpStatusCode.OK, s));

        var assistantResponse = await client.PostAsJsonAsync("/api/asistan", new { prompt = "hava kalitesi önemli" });

        Assert.Equal(HttpStatusCode.TooManyRequests, assistantResponse.StatusCode);
    }
}

file sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
