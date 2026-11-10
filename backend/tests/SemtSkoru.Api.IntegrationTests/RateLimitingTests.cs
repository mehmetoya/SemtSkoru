using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SemtSkoru.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SemtSkoru.Api.IntegrationTests;

// Covers the two independent limiters configured in RateLimiting/RateLimitingExtensions.cs:
// the per-IP sliding-window "compare" policy (abuse backstop, 30/min - see that endpoint's
// RequireRateLimiting call) and the global "db-pool" concurrency limiter (protects the shared
// Postgres pool regardless of caller identity, PermitLimit=5/QueueLimit=10).
public class RateLimitingTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:16-3.4")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), o => o.UseNetTopologySuite())
            .Options;
        await using (var context = new AppDbContext(options))
        {
            await context.Database.MigrateAsync();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString()));
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    // Mirrors how a single "kart" page render actually calls the API (see
    // web/app/mahalle/[id]/kart/route.tsx and web/app/karsilastir/kart/route.tsx: two
    // concurrent fetches per page) fanned out across several concurrent page loads, well under
    // both the db-pool concurrency permit+queue (5+10=15) and every per-IP window - proves
    // realistic normal traffic isn't caught in the abuse backstop meant for a runaway client.
    [Fact]
    public async Task Realistic_concurrent_ssr_style_traffic_is_not_rate_limited()
    {
        var client = _factory.CreateClient();

        var pageLoads = Enumerable.Range(0, 5).SelectMany(_ => new[]
        {
            client.GetAsync("/api/neighborhoods/kadikoy/score"),
            client.GetAsync("/api/neighborhoods/names"),
        });

        var responses = await Task.WhenAll(pageLoads);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task Compare_per_IP_window_rejects_a_tight_request_loop_with_429_and_Retry_After()
    {
        var client = _factory.CreateClient();

        // The "compare" policy allows 30 requests/minute per client IP (see
        // RateLimitingExtensions.ComparePermitLimitPerMinute) - a real user comparing two
        // districts a handful of times a minute never gets close to this; a script looping the
        // endpoint does. Requests are sequential (not concurrent) so this exercises only the
        // per-IP sliding window, not the separate db-pool concurrency limiter below.
        var statusCodes = new List<HttpStatusCode>();
        HttpResponseMessage? rejected = null;
        for (var i = 0; i < 31; i++)
        {
            var response = await client.GetAsync("/api/neighborhoods/compare?a=kadikoy&b=uskudar");
            statusCodes.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
                break;
            }
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statusCodes);
        // Every request before the rejection succeeded - the limit wasn't tripped early by
        // something other than raw request count.
        Assert.All(statusCodes.SkipLast(1), s => Assert.Equal(HttpStatusCode.OK, s));

        Assert.NotNull(rejected);
        Assert.True(rejected!.Headers.TryGetValues("Retry-After", out var retryAfterValues));
        var retryAfterSeconds = int.Parse(retryAfterValues!.Single());
        Assert.InRange(retryAfterSeconds, 1, 60);

        var body = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("error", out _));
        Assert.True(body.TryGetProperty("retryAfterSeconds", out var retryAfterInBody));
        Assert.Equal(retryAfterSeconds, retryAfterInBody.GetInt32());
    }

    [Fact]
    public async Task A_burst_far_beyond_the_db_pool_concurrency_budget_gets_some_429s_not_a_pool_exhaustion_crash()
    {
        var client = _factory.CreateClient();

        // PermitLimit=5 + QueueLimit=10 admits at most 15 requests at once system-wide,
        // regardless of caller IP (this is the backstop that actually protects the
        // MaxPoolSize=8 Postgres pool shared with Hangfire - see RateLimitingExtensions.cs).
        // 40 truly concurrent requests to a real multi-round-trip endpoint comfortably
        // overwhelms that budget without relying on exact timing.
        var tasks = Enumerable.Range(0, 40)
            .Select(_ => client.GetAsync("/api/neighborhoods/compare?a=kadikoy&b=uskudar"));

        var responses = await Task.WhenAll(tasks);

        var rejectedCount = responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);

        Assert.Equal(40, rejectedCount + okCount);
        Assert.True(rejectedCount > 0, "Expected at least one 429 from the db-pool concurrency limiter under a 40-request burst.");
        Assert.True(okCount > 0, "Expected some requests to still succeed - the limiter should shed excess load, not the whole burst.");
    }
}
