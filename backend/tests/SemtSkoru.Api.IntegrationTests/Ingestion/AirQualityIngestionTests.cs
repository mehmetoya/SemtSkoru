using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Ingestion;
using SemtSkoru.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SemtSkoru.Api.IntegrationTests.Ingestion;

public class AirQualityIngestionTests : IAsyncLifetime
{
    // İBB's real "Kadıköy" station (docs/data-sources.md, section 1) - its real coordinate
    // falls within Kadıköy's real seeded boundary, so these tests exercise the actual
    // point-in-polygon matching AirQualityIngestionJob does, not a fabricated fixture.
    private const string KadikoyStationId = "ecafeb15-905e-4257-a25a-72accf287e2a";
    private const string KadikoyStationLocation = "POINT (29.033376300740208 40.990816663897334)";

    // A second real station ("Selimiye"), also within Kadıköy's neighbor Üsküdar, used to
    // test that a district with multiple real stations gets their average.
    private const string SelimiyeStationId = "2054f684-6a42-438f-ae03-bb90445e71e6";
    private const string UskudarStationLocation = "POINT (29.024689534306511 41.014262285219615)";
    private const string SelimiyeStationLocation = "POINT (29.027002050799894 41.003764972742829)";

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

    private static AirQualityIngestionJob CreateJob(AppDbContext context, HttpMessageHandler handler) =>
        new(new AirQualityApiClient(new HttpClient(handler)), context, NullLogger<AirQualityIngestionJob>.Instance, TimeProvider.System);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static string StationsJson(params (string Id, string Location)[] stations) =>
        "[" + string.Join(",", stations.Select(s =>
            $$"""{"Id":"{{s.Id}}","Name":"test","Adress":"test","Location":"{{s.Location}}"}""")) + "]";

    private static HttpMessageHandler RoutingHandler(
        string stationsJson,
        Func<string, HttpResponseMessage> readingResponder) =>
        new FakeHttpMessageHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("GetAQIStations"))
            {
                return JsonResponse(stationsJson);
            }

            var query = req.RequestUri!.Query.TrimStart('?');
            var stationId = query.Split('&')
                .Select(p => p.Split('=', 2))
                .First(p => p[0] == "StationId")[1];
            return readingResponder(Uri.UnescapeDataString(stationId));
        });

    [Fact]
    public async Task RunAsync_writes_a_reading_only_for_the_neighborhood_containing_the_station()
    {
        var handler = RoutingHandler(
            StationsJson((KadikoyStationId, KadikoyStationLocation)),
            _ => JsonResponse("""[{"ReadTime":"2026-09-11T10:00:00","AQI":{"AQIIndex":42.0}}]"""));

        await using var context = CreateContext();
        var job = CreateJob(context, handler);

        await job.RunAsync(CancellationToken.None);

        var readings = await context.AirQualityReadings.ToListAsync();
        var reading = Assert.Single(readings);
        Assert.Equal("kadikoy", reading.NeighborhoodId);
        Assert.Equal(42.0, reading.AqiIndex);
        Assert.Equal("İBB Hava Kalitesi İstasyon Ölçüm Sonuçları Web Servisi", reading.Source.SourceName);
        Assert.Equal(DataFreshnessStatus.Fresh, reading.Source.GetFreshness(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task RunAsync_leaves_districts_with_no_nearby_station_without_a_reading()
    {
        // Only one real station in the whole fixture - the other 38 seeded districts must
        // stay untouched (no fabricated/interpolated reading), matching the real-world
        // finding that only 18 of 39 Istanbul districts have a station nearby.
        var handler = RoutingHandler(
            StationsJson((KadikoyStationId, KadikoyStationLocation)),
            _ => JsonResponse("""[{"ReadTime":"2026-09-11T10:00:00","AQI":{"AQIIndex":42.0}}]"""));

        await using var context = CreateContext();
        var job = CreateJob(context, handler);

        await job.RunAsync(CancellationToken.None);

        var neighborhoodCount = await context.Neighborhoods.CountAsync();
        var readingCount = await context.AirQualityReadings.CountAsync();
        Assert.Equal(39, neighborhoodCount);
        Assert.Equal(1, readingCount);
    }

    [Fact]
    public async Task RunAsync_averages_multiple_real_stations_within_the_same_district()
    {
        var handler = RoutingHandler(
            StationsJson(
                (KadikoyStationId, UskudarStationLocation),
                (SelimiyeStationId, SelimiyeStationLocation)),
            stationId => stationId == KadikoyStationId
                ? JsonResponse("""[{"ReadTime":"2026-09-11T10:00:00","AQI":{"AQIIndex":40.0}}]""")
                : JsonResponse("""[{"ReadTime":"2026-09-11T11:00:00","AQI":{"AQIIndex":60.0}}]"""));

        await using var context = CreateContext();
        var job = CreateJob(context, handler);

        await job.RunAsync(CancellationToken.None);

        var reading = await context.AirQualityReadings.SingleAsync(r => r.NeighborhoodId == "uskudar");
        Assert.Equal(50.0, reading.AqiIndex);
        Assert.Equal(DateTimeOffset.Parse("2026-09-11T11:00:00+03:00"), reading.Source.PublishedAt);
    }

    [Fact]
    public async Task RunAsync_does_not_throw_when_the_station_list_is_unreachable()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("simulated network failure"));

        await using var context = CreateContext();
        var job = CreateJob(context, handler);

        var exception = await Record.ExceptionAsync(() => job.RunAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Empty(await context.AirQualityReadings.ToListAsync());
    }

    [Fact]
    public async Task RunAsync_skips_readings_with_null_AQI_and_uses_the_latest_valid_one()
    {
        // Live-verified against the real İBB endpoint: the newest ReadTime slot can carry a
        // null AQI (not yet computed) while an earlier slot in the same window has a real one.
        var handler = RoutingHandler(
            StationsJson((KadikoyStationId, KadikoyStationLocation)),
            _ => JsonResponse(
                """
                [
                    {"ReadTime":"2026-09-11T11:00:00","AQI":{"AQIIndex":30.0}},
                    {"ReadTime":"2026-09-11T12:00:00","AQI":null}
                ]
                """));

        await using var context = CreateContext();
        var job = CreateJob(context, handler);

        await job.RunAsync(CancellationToken.None);

        var reading = Assert.Single(await context.AirQualityReadings.ToListAsync());
        Assert.Equal(30.0, reading.AqiIndex);
    }

    [Fact]
    public async Task RunAsync_upserts_rather_than_duplicating_on_a_second_run()
    {
        var handler = RoutingHandler(
            StationsJson((KadikoyStationId, KadikoyStationLocation)),
            _ => JsonResponse("""[{"ReadTime":"2026-09-11T11:00:00","AQI":{"AQIIndex":55.0}}]"""));

        await using var context = CreateContext();
        await CreateJob(context, handler).RunAsync(CancellationToken.None);
        await CreateJob(context, handler).RunAsync(CancellationToken.None);

        var reading = Assert.Single(await context.AirQualityReadings.ToListAsync());
        Assert.Equal(55.0, reading.AqiIndex);
    }
}

file sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
