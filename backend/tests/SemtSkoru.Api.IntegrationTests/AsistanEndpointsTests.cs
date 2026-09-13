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

// End-to-end proof that the AI Semt Asistanı wiring in Program.cs is correct: a real seeded
// district score flows all the way from Postgres, through DistrictAssistantService's
// grounding/validation, to the HTTP response - with Gemini itself faked out (never a real
// network call in the test suite, matching the house style for the other external API clients).
public class AsistanEndpointsTests : IAsyncLifetime
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
                    // Re-configures the SAME named typed-client registration Program.cs already
                    // set up (AddHttpClient<IDistrictAssistantAiClient, GeminiClient>) with a
                    // fake handler - the standard IHttpClientFactory testing pattern. No real
                    // network call to Gemini ever happens in this test suite.
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
    public async Task A_grounded_recommendation_with_a_hallucinated_id_flows_through_the_full_stack_with_only_the_real_id_kept()
    {
        var publishedAt = DateTimeOffset.UtcNow;
        await using (var context = CreateContext())
        {
            context.AirQualityReadings.Add(new AirQualityReading
            {
                NeighborhoodId = "kadikoy",
                AqiIndex = 0, // -> a real, deterministic score of 100 (see DimensionScoring)
                ReadingTime = DateTimeOffset.UtcNow,
                Source = TestSource(publishedAt),
            });
            await context.SaveChangesAsync();
        }

        var modelJson = """
            {"oneriler":[
                {"id":"kadikoy","aciklama":"Hava kalitesi skoru 100 ile mükemmel."},
                {"id":"hayaliilce","aciklama":"Bu ilçe gerçek değil, model bunu uydurdu."}
            ]}
            """;
        _factory = CreateFactory("test-key", _ => GeminiJson(modelJson));
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/asistan", new { prompt = "Hava kalitesi önemli" });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AsistanResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(nameof(AssistantOutcomeKind.Ok), body.Status);
        var recommendation = Assert.Single(body.Recommendations);
        Assert.Equal("kadikoy", recommendation.NeighborhoodId);
        Assert.Equal("Kadıköy", recommendation.NeighborhoodName);
        Assert.Equal(100, recommendation.Score.AirQuality.Score);
        Assert.DoesNotContain(body.Recommendations, r => r.NeighborhoodId == "hayaliilce");
    }

    [Fact]
    public async Task Returns_503_when_no_api_key_is_configured()
    {
        _factory = CreateFactory(apiKey: null);
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/asistan", new { prompt = "bir istek" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AsistanResponseDto>();
        Assert.Equal(nameof(AssistantOutcomeKind.NotConfigured), body!.Status);
        Assert.False(string.IsNullOrWhiteSpace(body.Message));
    }

    [Fact]
    public async Task Returns_400_for_an_empty_prompt()
    {
        _factory = CreateFactory("test-key", _ => GeminiJson("""{"oneriler":[]}"""));
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/asistan", new { prompt = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Returns_a_usable_empty_response_rather_than_a_guess_when_every_id_is_hallucinated()
    {
        _factory = CreateFactory("test-key", _ => GeminiJson("""{"oneriler":[{"id":"uydurma","aciklama":"x"}]}"""));
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/asistan", new { prompt = "bir istek" });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AsistanResponseDto>();
        Assert.Equal(nameof(AssistantOutcomeKind.NoUsableRecommendations), body!.Status);
        Assert.Empty(body.Recommendations);
        Assert.False(string.IsNullOrWhiteSpace(body.Message));
    }

    [Fact]
    public async Task The_dedicated_rate_limit_policy_eventually_rejects_a_tight_request_loop_with_429()
    {
        _factory = CreateFactory("test-key", _ => GeminiJson("""{"oneriler":[]}"""));
        var client = _factory.CreateClient();

        // Both the per-IP window (5/10min) and the global per-minute budget (5/min) - see
        // RateLimitingExtensions.cs - are exhausted well before this many sequential calls from
        // one client.
        var statusCodes = new List<HttpStatusCode>();
        for (var i = 0; i < 8; i++)
        {
            var response = await client.PostAsJsonAsync("/api/asistan", new { prompt = $"istek {i}" });
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
