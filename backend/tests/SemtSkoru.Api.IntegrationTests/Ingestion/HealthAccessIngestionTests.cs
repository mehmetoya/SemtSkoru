using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Ingestion;
using SemtSkoru.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SemtSkoru.Api.IntegrationTests.Ingestion;

public class HealthAccessIngestionTests : IAsyncLifetime
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

    // Geometry is never read by HealthAccessApiClient (only the properties are), but every
    // feature still needs a structurally valid Polygon for GeoJsonConverterFactory to parse it.
    private static readonly JsonArray DummyRing = new(
        new JsonArray(29.0, 41.0),
        new JsonArray(29.1, 41.0),
        new JsonArray(29.1, 41.1),
        new JsonArray(29.0, 41.1),
        new JsonArray(29.0, 41.0));

    private static JsonObject MahalleFeature(string ilce, string mahalle, double? healthIndex, int? population) =>
        new()
        {
            ["type"] = "Feature",
            ["properties"] = new JsonObject
            {
                ["ILCE_ADI"] = ilce,
                ["MAHALLE_ADI"] = mahalle,
                ["KISI_SAYISI"] = population,
                ["SAGLIK_INDEX"] = healthIndex,
            },
            ["geometry"] = new JsonObject
            {
                ["type"] = "Polygon",
                ["coordinates"] = new JsonArray(DummyRing.DeepClone()),
            },
        };

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

    private static HealthAccessIngestionJob CreateJob(AppDbContext context, HttpMessageHandler handler) =>
        new(new HealthAccessApiClient(new HttpClient(handler)), context, NullLogger<HealthAccessIngestionJob>.Instance, TimeProvider.System);

    [Fact]
    public async Task RunAsync_excludes_zero_population_mahalles_from_the_weighted_average()
    {
        // Kadıköy: one real, populated mahalle (index 80) and one uninhabited one (index 0,
        // population 0) - the uninhabited mahalle must not drag the average down at all, not
        // even by a zero-weighted term, so the result should equal the populated mahalle's own
        // index exactly, not some average that includes the zero.
        var features = new JsonArray(
            MahalleFeature("KADIKÖY", "Populated", healthIndex: 80, population: 1000),
            MahalleFeature("KADIKÖY", "Uninhabited", healthIndex: 0, population: 0));

        await using var context = CreateContext();
        var job = CreateJob(context, FakeGeoJsonHandler(features));

        await job.RunAsync(CancellationToken.None);

        var kadikoy = await context.HealthAccessReadings.SingleAsync(r => r.NeighborhoodId == "kadikoy");
        Assert.Equal(80, kadikoy.WeightedHealthIndex, precision: 3);
        Assert.Equal(1, kadikoy.MahalleCount);
    }

    [Fact]
    public async Task RunAsync_computes_a_population_weighted_not_simple_average_across_mahalles()
    {
        // Two populated Üsküdar mahalles with very different populations and indices: a simple
        // (unweighted) average would be (10 + 90) / 2 = 50, but the real population-weighted
        // average is (10*9000 + 90*1000) / 10000 = 18.
        var features = new JsonArray(
            MahalleFeature("ÜSKÜDAR", "Big Low-Index", healthIndex: 10, population: 9000),
            MahalleFeature("ÜSKÜDAR", "Small High-Index", healthIndex: 90, population: 1000));

        await using var context = CreateContext();
        var job = CreateJob(context, FakeGeoJsonHandler(features));

        await job.RunAsync(CancellationToken.None);

        var uskudar = await context.HealthAccessReadings.SingleAsync(r => r.NeighborhoodId == "uskudar");
        Assert.Equal(18, uskudar.WeightedHealthIndex, precision: 3);
        Assert.Equal(2, uskudar.MahalleCount);

        Assert.Equal("İBB 34 Dakika İstanbul Sağlık İndeksi", uskudar.Source.SourceName);
        Assert.Equal(DataFreshnessStatus.Fresh, uskudar.Source.GetFreshness(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task RunAsync_leaves_a_district_with_no_populated_mahalle_without_a_reading()
    {
        // Beşiktaş only has an uninhabited mahalle in this fixture - it must be left with no
        // reading at all (DimensionScore.NoData downstream), never a fabricated 0 or an average
        // that divides by zero population.
        var features = new JsonArray(
            MahalleFeature("KADIKÖY", "Populated", healthIndex: 50, population: 100),
            MahalleFeature("BEŞİKTAŞ", "Only Uninhabited Mahalle", healthIndex: 40, population: 0));

        await using var context = CreateContext();
        var job = CreateJob(context, FakeGeoJsonHandler(features));

        await job.RunAsync(CancellationToken.None);

        var readings = await context.HealthAccessReadings.ToListAsync();
        Assert.Contains(readings, r => r.NeighborhoodId == "kadikoy");
        Assert.DoesNotContain(readings, r => r.NeighborhoodId == "besiktas");
    }

    [Fact]
    public async Task RunAsync_leaves_districts_with_no_mahalle_at_all_without_a_reading()
    {
        var features = new JsonArray(MahalleFeature("KADIKÖY", "Populated", healthIndex: 50, population: 100));

        await using var context = CreateContext();
        var job = CreateJob(context, FakeGeoJsonHandler(features));

        await job.RunAsync(CancellationToken.None);

        var neighborhoodCount = await context.Neighborhoods.CountAsync();
        var readingCount = await context.HealthAccessReadings.CountAsync();
        Assert.Equal(39, neighborhoodCount);
        Assert.Equal(1, readingCount);
    }

    [Fact]
    public async Task RunAsync_does_not_throw_when_the_source_is_unreachable()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("simulated network failure"));

        await using var context = CreateContext();
        var job = CreateJob(context, handler);

        var exception = await Record.ExceptionAsync(() => job.RunAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Empty(await context.HealthAccessReadings.ToListAsync());
    }

    [Fact]
    public async Task RunAsync_upserts_rather_than_duplicating_on_a_second_run()
    {
        var features = new JsonArray(MahalleFeature("KADIKÖY", "Populated", healthIndex: 60, population: 100));

        await using var context = CreateContext();
        var handler = FakeGeoJsonHandler(features);
        await CreateJob(context, handler).RunAsync(CancellationToken.None);
        await CreateJob(context, handler).RunAsync(CancellationToken.None);

        var reading = Assert.Single(await context.HealthAccessReadings.ToListAsync());
        Assert.Equal(60, reading.WeightedHealthIndex, precision: 3);
    }
}

file sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
