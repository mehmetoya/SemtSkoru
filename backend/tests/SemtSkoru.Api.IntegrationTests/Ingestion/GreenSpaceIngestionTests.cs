using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Ingestion;
using SemtSkoru.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SemtSkoru.Api.IntegrationTests.Ingestion;

public class GreenSpaceIngestionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:16-3.4")
        .Build();

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

    private static JsonObject SquareFeature(string name, string tur, string ilce, double lon, double lat, double halfSize = 0.0005)
    {
        var ring = new JsonArray(
            new JsonArray(lon - halfSize, lat - halfSize),
            new JsonArray(lon + halfSize, lat - halfSize),
            new JsonArray(lon + halfSize, lat + halfSize),
            new JsonArray(lon - halfSize, lat + halfSize),
            new JsonArray(lon - halfSize, lat - halfSize));

        return new JsonObject
        {
            ["type"] = "Feature",
            ["properties"] = new JsonObject
            {
                ["MAHALLE"] = name,
                ["TUR"] = tur,
                ["ILCE"] = ilce,
            },
            ["geometry"] = new JsonObject
            {
                ["type"] = "Polygon",
                ["coordinates"] = new JsonArray(ring),
            },
        };
    }

    private static HttpMessageHandler FakeGeoJsonHandler(JsonArray features)
    {
        var body = new JsonObject
        {
            ["type"] = "FeatureCollection",
            ["features"] = features,
        }.ToJsonString();

        return new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }

    private static GreenSpaceIngestionJob CreateJob(AppDbContext context, HttpMessageHandler handler) =>
        new(new GreenSpaceApiClient(new HttpClient(handler)), context, NullLogger<GreenSpaceIngestionJob>.Instance, TimeProvider.System);

    [Fact]
    public async Task RunAsync_picks_the_nearer_park_and_ignores_non_park_features()
    {
        // Both points sit well inside Kadıköy's real seeded boundary (Task 6), so whatever the
        // true polygon centroid is, "Near Park" is unambiguously closer than "Far Park" (~150km away).
        var features = new JsonArray(
            SquareFeature("Near Park", "Park", "KADIKÖY", 29.06, 40.98),
            SquareFeature("Far Park", "Park", "KADIKÖY", 30.5, 42.5),
            SquareFeature("Closer Non-Park", "Kamu", "KADIKÖY", 29.061, 40.981),
            SquareFeature("Üsküdar Park", "Park", "ÜSKÜDAR", 29.04, 41.03));

        await using var context = CreateContext();
        var job = CreateJob(context, FakeGeoJsonHandler(features));

        await job.RunAsync(CancellationToken.None);

        var readings = await context.GreenSpaceReadings.ToListAsync();

        var kadikoy = Assert.Single(readings, r => r.NeighborhoodId == "kadikoy");
        Assert.Equal("Near Park", kadikoy.NearestParkName);
        Assert.True(kadikoy.NearestParkDistanceMeters < 10_000, "expected the in-district park, not the ~150km-away one");

        var uskudar = Assert.Single(readings, r => r.NeighborhoodId == "uskudar");
        Assert.Equal("Üsküdar Park", uskudar.NearestParkName);

        // Beşiktaş has no park tagged with its own ILCE in this fixture, but the search is
        // city-wide, not restricted by district - it should still get the genuinely nearest
        // real park (Üsküdar Park, across the strait, rather than the ~150km-away "Far Park").
        var besiktas = Assert.Single(readings, r => r.NeighborhoodId == "besiktas");
        Assert.Equal("Üsküdar Park", besiktas.NearestParkName);
    }

    [Fact]
    public async Task RunAsync_does_not_throw_when_the_source_is_unreachable()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("simulated network failure"));

        await using var context = CreateContext();
        var job = CreateJob(context, handler);

        var exception = await Record.ExceptionAsync(() => job.RunAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Empty(await context.GreenSpaceReadings.ToListAsync());
    }
}

file sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
