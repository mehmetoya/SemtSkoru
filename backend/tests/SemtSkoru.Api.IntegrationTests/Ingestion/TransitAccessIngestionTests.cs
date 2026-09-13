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

public class TransitAccessIngestionTests : IAsyncLifetime
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

    private static JsonObject StopFeature(double longitude, double latitude) =>
        new()
        {
            ["type"] = "Feature",
            // Real ILCEID/DURUMU/etc. properties are never read by TransitAccessApiClient (only
            // the geometry is) - live-verified (2026-09-13) ILCEID has no reliable public mapping
            // to a district name, so matching happens purely by point-in-polygon test.
            ["properties"] = new JsonObject { ["ILCEID"] = "9999" },
            ["geometry"] = new JsonObject
            {
                ["type"] = "Point",
                ["coordinates"] = new JsonArray(longitude, latitude),
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

    private static TransitAccessIngestionJob CreateJob(AppDbContext context, HttpMessageHandler handler) =>
        new(new TransitAccessApiClient(new HttpClient(handler)), context, NullLogger<TransitAccessIngestionJob>.Instance, TimeProvider.System);

    [Fact]
    public async Task RunAsync_counts_stops_inside_the_real_seeded_district_boundary_and_ignores_stops_outside_all_of_them()
    {
        // Same real seeded Kadıköy/Üsküdar coordinates TrafficIngestionTests already verified
        // fall inside those districts' real boundaries (Task 6) - reused here rather than
        // guessing new ones. One point far outside Istanbul entirely (Germany) must not be
        // attributed to any district.
        var features = new JsonArray(
            StopFeature(29.06, 40.98),
            StopFeature(29.061, 40.981),
            StopFeature(29.04, 41.03),
            StopFeature(10.0, 50.0));

        await using var context = CreateContext();
        var job = CreateJob(context, FakeGeoJsonHandler(features));

        await job.RunAsync(CancellationToken.None);

        var readings = await context.TransitAccessReadings.ToListAsync();

        var kadikoy = Assert.Single(readings, r => r.NeighborhoodId == "kadikoy");
        Assert.Equal(2, kadikoy.StopCount);

        var uskudar = Assert.Single(readings, r => r.NeighborhoodId == "uskudar");
        Assert.Equal(1, uskudar.StopCount);

        Assert.DoesNotContain(readings, r => r.NeighborhoodId == "besiktas");
    }

    [Fact]
    public async Task RunAsync_computes_stop_density_as_count_divided_by_real_district_area()
    {
        var features = new JsonArray(
            StopFeature(29.06, 40.98),
            StopFeature(29.061, 40.981));

        await using var context = CreateContext();
        var job = CreateJob(context, FakeGeoJsonHandler(features));

        await job.RunAsync(CancellationToken.None);

        var kadikoy = await context.TransitAccessReadings.SingleAsync(r => r.NeighborhoodId == "kadikoy");

        Assert.Equal(2, kadikoy.StopCount);
        // Density = count / area(km²): with a real (non-zero) district area and a positive
        // count, density must be a finite positive number, strictly less than the raw count
        // itself (Kadıköy's real area is well over 1 km²).
        Assert.True(kadikoy.StopDensityPerKm2 > 0);
        Assert.True(double.IsFinite(kadikoy.StopDensityPerKm2));
        Assert.True(kadikoy.StopDensityPerKm2 < kadikoy.StopCount);
    }

    [Fact]
    public async Task RunAsync_leaves_a_district_with_no_stop_without_a_reading()
    {
        // Only Kadıköy gets a stop in this fixture - Üsküdar/Beşiktaş and every other seeded
        // district must be left without a reading (DimensionScore.NoData downstream), never a
        // fabricated zero.
        var features = new JsonArray(StopFeature(29.06, 40.98));

        await using var context = CreateContext();
        var job = CreateJob(context, FakeGeoJsonHandler(features));

        await job.RunAsync(CancellationToken.None);

        var neighborhoodCount = await context.Neighborhoods.CountAsync();
        var readingCount = await context.TransitAccessReadings.CountAsync();
        Assert.Equal(39, neighborhoodCount);
        Assert.Equal(1, readingCount);
        Assert.DoesNotContain(await context.TransitAccessReadings.ToListAsync(), r => r.NeighborhoodId == "uskudar");
    }

    [Fact]
    public async Task RunAsync_marks_the_result_as_periodic_not_live()
    {
        var features = new JsonArray(StopFeature(29.06, 40.98));

        await using var context = CreateContext();
        await CreateJob(context, FakeGeoJsonHandler(features)).RunAsync(CancellationToken.None);

        var kadikoy = await context.TransitAccessReadings.SingleAsync(r => r.NeighborhoodId == "kadikoy");

        Assert.Equal("İETT Otobüs Durakları Verisi", kadikoy.Source.SourceName);
        Assert.Equal(DataFreshnessStatus.Fresh, kadikoy.Source.GetFreshness(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task RunAsync_does_not_throw_when_the_source_is_unreachable()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("simulated network failure"));

        await using var context = CreateContext();
        var job = CreateJob(context, handler);

        var exception = await Record.ExceptionAsync(() => job.RunAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Empty(await context.TransitAccessReadings.ToListAsync());
    }

    [Fact]
    public async Task RunAsync_upserts_rather_than_duplicating_on_a_second_run()
    {
        var features = new JsonArray(StopFeature(29.06, 40.98), StopFeature(29.061, 40.981));

        await using var context = CreateContext();
        var handler = FakeGeoJsonHandler(features);
        await CreateJob(context, handler).RunAsync(CancellationToken.None);
        await CreateJob(context, handler).RunAsync(CancellationToken.None);

        var reading = Assert.Single(await context.TransitAccessReadings.ToListAsync());
        Assert.Equal(2, reading.StopCount);
    }
}

file sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}

// Real point-in-polygon matching can never produce a positive stop count against a zero-area
// district boundary (a polygon with an empty interior can never topologically "contain" a
// point), so there's no way to build a real Testcontainers/HTTP fixture that exercises this
// guard - it's tested directly against the internal pure function instead. No Postgres needed.
public class TransitAccessDensityGuardTests
{
    [Fact]
    public void ComputeDensity_divides_count_by_area_when_area_is_positive()
    {
        Assert.Equal(10.0, TransitAccessIngestionJob.ComputeDensity(stopCount: 50, areaKm2: 5), precision: 6);
    }

    [Fact]
    public void ComputeDensity_returns_zero_rather_than_dividing_by_zero_or_negative_area()
    {
        Assert.Equal(0, TransitAccessIngestionJob.ComputeDensity(stopCount: 50, areaKm2: 0));
        Assert.Equal(0, TransitAccessIngestionJob.ComputeDensity(stopCount: 50, areaKm2: -1));
    }

    [Fact]
    public void ComputeDensity_returns_zero_for_zero_stops_regardless_of_area()
    {
        Assert.Equal(0, TransitAccessIngestionJob.ComputeDensity(stopCount: 0, areaKm2: 12.5));
    }
}
