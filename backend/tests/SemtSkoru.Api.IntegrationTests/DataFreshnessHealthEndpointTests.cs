using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SemtSkoru.Api.IntegrationTests;

// GET/HEAD /health/data - the data-freshness check a second UptimeRobot monitor polls (see
// docs/deployment.md, section 4). It must judge each source on LastSuccessfulSyncAt (did OUR
// ingestion last succeed recently enough for its cadence?), never on the dataset's own
// PublishedAt, which is legitimately years old for traffic/health access.
public class DataFreshnessHealthEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:16-3.4")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using (var context = CreateContext())
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

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), o => o.UseNetTopologySuite())
            .Options;
        return new AppDbContext(options);
    }

    // The real jobs' PublishedAt values for the sources whose dataset date is legitimately old -
    // seeded as-is so these tests prove an ancient PublishedAt alone never makes a source overdue.
    private static readonly DateTimeOffset TrafficSnapshotPublishedAt = new(2025, 1, 31, 23, 59, 59, TimeSpan.Zero);
    private static readonly DateTimeOffset HealthIndexPublishedAt = new(2024, 2, 1, 9, 3, 4, TimeSpan.Zero);

    private static DataSourceMetadata Source(string name, SourceCadence cadence, DateTimeOffset lastSync, DateTimeOffset? publishedAt = null) => new(
        SourceName: name,
        SourceUrl: "https://example.test",
        SourceLicense: "Test License",
        FetchedAt: lastSync,
        PublishedAt: publishedAt ?? lastSync,
        LastSuccessfulSyncAt: lastSync,
        Cadence: cadence);

    /// <summary>Seeds one row per ingested source, each synced an hour ago unless overridden.</summary>
    private async Task SeedAllSourcesAsync(DateTimeOffset? airQualityLastSync = null)
    {
        var recent = DateTimeOffset.UtcNow.AddHours(-1);
        await using var context = CreateContext();

        context.AirQualityReadings.Add(new AirQualityReading
        {
            NeighborhoodId = "kadikoy",
            AqiIndex = 40,
            ReadingTime = airQualityLastSync ?? recent,
            Source = Source("Air", SourceCadence.Live, airQualityLastSync ?? recent),
        });
        context.ParkingReadings.Add(new ParkingReading
        {
            NeighborhoodId = "kadikoy",
            AverageAvailabilityRatio = 0.5,
            FacilityCount = 3,
            Source = Source("Parking", SourceCadence.Live, recent),
        });
        context.GreenSpaceReadings.Add(new GreenSpaceReading
        {
            NeighborhoodId = "kadikoy",
            NearestParkName = "Test Park",
            NearestParkDistanceMeters = 300,
            Source = Source("Green", SourceCadence.Periodic, recent, publishedAt: new DateTimeOffset(2025, 7, 17, 0, 0, 0, TimeSpan.Zero)),
        });
        context.HealthAccessReadings.Add(new HealthAccessReading
        {
            NeighborhoodId = "kadikoy",
            WeightedHealthIndex = 0.7,
            WardCount = 21,
            Source = Source("Health", SourceCadence.Periodic, recent, publishedAt: HealthIndexPublishedAt),
        });
        context.TransitAccessReadings.Add(new TransitAccessReading
        {
            NeighborhoodId = "kadikoy",
            StopCount = 300,
            StopDensityPerKm2 = 7.5,
            Source = Source("Transit", SourceCadence.Periodic, recent),
        });
        context.TrafficReadings.Add(new TrafficReading
        {
            NeighborhoodId = "kadikoy",
            AverageSpeedKmh = 35,
            SampleCount = 1000,
            Source = Source("Traffic", SourceCadence.StaticSnapshot, recent, publishedAt: TrafficSnapshotPublishedAt),
        });

        await context.SaveChangesAsync();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static JsonElement SourceEntry(JsonElement body, string source) =>
        body.GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("source").GetString() == source);

    [Fact]
    public async Task Get_returns_200_and_every_source_ok_when_all_syncs_are_recent()
    {
        await SeedAllSourcesAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        var sources = body.GetProperty("sources").EnumerateArray().ToList();
        Assert.Equal(
            ["airQuality", "parking", "greenSpace", "healthAccess", "transitAccess", "traffic"],
            sources.Select(s => s.GetProperty("source").GetString()));
        Assert.All(sources, s => Assert.Equal("Ok", s.GetProperty("status").GetString()));

        // Traffic's dataset is a frozen January 2025 snapshot and the health index dates from
        // 2024 - both still Ok, because only the last successful sync is judged.
        Assert.Equal("Ok", SourceEntry(body, "traffic").GetProperty("status").GetString());
        Assert.Equal(1500, SourceEntry(body, "traffic").GetProperty("thresholdHours").GetDouble());
        Assert.Equal("Ok", SourceEntry(body, "healthAccess").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Get_returns_503_naming_the_overdue_source_when_one_job_has_stopped_succeeding()
    {
        // Mirrors the real outage: air quality's last successful sync was 19 days ago.
        var lastAirQualitySync = DateTimeOffset.UtcNow.AddDays(-19);
        await SeedAllSourcesAsync(airQualityLastSync: lastAirQualitySync);
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/data");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Unhealthy", body.GetProperty("status").GetString());

        var airQuality = SourceEntry(body, "airQuality");
        Assert.Equal("Overdue", airQuality.GetProperty("status").GetString());
        Assert.Equal("Live", airQuality.GetProperty("cadence").GetString());
        Assert.Equal(60, airQuality.GetProperty("thresholdHours").GetDouble());
        Assert.Equal(lastAirQualitySync, airQuality.GetProperty("lastSuccessfulSyncAt").GetDateTimeOffset(), TimeSpan.FromMilliseconds(1));

        var overdue = body.GetProperty("sources").EnumerateArray()
            .Where(s => s.GetProperty("status").GetString() != "Ok")
            .Select(s => s.GetProperty("source").GetString());
        Assert.Equal(["airQuality"], overdue);
    }

    [Fact]
    public async Task Get_returns_503_with_never_synced_sources_on_an_empty_database()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/data");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.All(
            body.GetProperty("sources").EnumerateArray(),
            s => Assert.Equal("NeverSynced", s.GetProperty("status").GetString()));
    }

    // UptimeRobot's monitors send HEAD - same status code as GET, no body.
    [Fact]
    public async Task Head_returns_the_same_status_code_as_get_with_no_body()
    {
        await SeedAllSourcesAsync();
        var client = _factory.CreateClient();

        var healthy = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/health/data"));

        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
        Assert.Empty(await healthy.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Head_returns_503_with_no_body_when_a_source_is_overdue()
    {
        await SeedAllSourcesAsync(airQualityLastSync: DateTimeOffset.UtcNow.AddDays(-19));
        var client = _factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/health/data"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    // The liveness pings the keep-warm monitor uses must not start failing just because data is
    // stale - they stay DB-free and always 200.
    [Fact]
    public async Task Liveness_endpoints_stay_200_while_data_is_overdue()
    {
        await SeedAllSourcesAsync(airQualityLastSync: DateTimeOffset.UtcNow.AddDays(-19));
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/health/live"))).StatusCode);
    }

    // Like /health, exempt from the global db-pool concurrency limiter: a burst well past its
    // permit+queue budget (5+30) must never 429 the uptime monitor.
    [Fact]
    public async Task Is_not_rate_limited_under_a_burst()
    {
        await SeedAllSourcesAsync();
        var client = _factory.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 60).Select(_ => client.GetAsync("/health/data")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}
