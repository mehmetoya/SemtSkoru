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

public class ParkingIngestionTests : IAsyncLifetime
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

    private static string FacilityJson(string parkName, string district, int capacity, int emptyCapacity, int isOpen = 1) =>
        $$"""
        {"parkID":1,"parkName":"{{parkName}}","lat":"41.0","lng":"29.0","capacity":{{capacity}},"emptyCapacity":{{emptyCapacity}},"workHours":"24 Saat","parkType":"AÇIK OTOPARK","freeTime":0,"district":"{{district}}","isOpen":{{isOpen}}}
        """;

    private static HttpMessageHandler FakeParkingHandler(params string[] facilities)
    {
        var body = "[" + string.Join(",", facilities) + "]";
        return new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }

    private static ParkingIngestionJob CreateJob(AppDbContext context, HttpMessageHandler handler) =>
        new(new ParkingApiClient(new HttpClient(handler)), context, NullLogger<ParkingIngestionJob>.Instance, TimeProvider.System);

    [Fact]
    public async Task RunAsync_averages_availability_across_facilities_in_the_same_district()
    {
        var handler = FakeParkingHandler(
            FacilityJson("Kadıköy Açık", "KADIKÖY", capacity: 100, emptyCapacity: 100), // 100% available
            FacilityJson("Kadıköy Kapalı", "KADIKÖY", capacity: 100, emptyCapacity: 0), // 0% available
            FacilityJson("Üsküdar Otopark", "ÜSKÜDAR", capacity: 50, emptyCapacity: 25)); // 50% available

        await using var context = CreateContext();
        var job = CreateJob(context, handler);

        await job.RunAsync(CancellationToken.None);

        var kadikoy = await context.ParkingReadings.SingleAsync(r => r.NeighborhoodId == "kadikoy");
        Assert.Equal(0.5, kadikoy.AverageAvailabilityRatio, precision: 3);
        Assert.Equal(2, kadikoy.FacilityCount);

        var uskudar = await context.ParkingReadings.SingleAsync(r => r.NeighborhoodId == "uskudar");
        Assert.Equal(0.5, uskudar.AverageAvailabilityRatio, precision: 3);
        Assert.Equal(1, uskudar.FacilityCount);

        Assert.Equal("İBB İSPARK Otopark Doluluk Bilgisi", kadikoy.Source.SourceName);
        Assert.Equal(DataFreshnessStatus.Fresh, kadikoy.Source.GetFreshness(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task RunAsync_matches_eyupsultan_against_ispark_old_district_name_EYUP()
    {
        // Live-verified (2026-09-13): İSPARK tags Eyüpsultan's real facilities with "EYÜP" - the
        // district's pre-2019 name - never "EYÜPSULTAN". This is the one real exception among
        // all 39 districts; ParkingIngestionJob's DistrictNameAliases handles it explicitly.
        var handler = FakeParkingHandler(FacilityJson("Eyüp Otoparkı", "EYÜP", capacity: 100, emptyCapacity: 60));

        await using var context = CreateContext();
        var job = CreateJob(context, handler);

        await job.RunAsync(CancellationToken.None);

        var eyupsultan = await context.ParkingReadings.SingleAsync(r => r.NeighborhoodId == "eyupsultan");
        Assert.Equal(0.6, eyupsultan.AverageAvailabilityRatio, precision: 3);
    }

    [Fact]
    public async Task RunAsync_leaves_districts_with_no_facility_without_a_reading()
    {
        // Only one real facility in the whole fixture - the other 38 seeded districts must
        // stay untouched (no fabricated/interpolated reading), matching the real-world finding
        // that not every one of Istanbul's 39 districts has an İSPARK facility.
        var handler = FakeParkingHandler(FacilityJson("Kadıköy Açık", "KADIKÖY", capacity: 100, emptyCapacity: 40));

        await using var context = CreateContext();
        var job = CreateJob(context, handler);

        await job.RunAsync(CancellationToken.None);

        var neighborhoodCount = await context.Neighborhoods.CountAsync();
        var readingCount = await context.ParkingReadings.CountAsync();
        Assert.Equal(39, neighborhoodCount);
        Assert.Equal(1, readingCount);
        Assert.DoesNotContain(await context.ParkingReadings.ToListAsync(), r => r.NeighborhoodId == "besiktas");
    }

    [Fact]
    public async Task RunAsync_ignores_facilities_with_zero_capacity_to_avoid_dividing_by_zero()
    {
        var handler = FakeParkingHandler(
            FacilityJson("Broken Sensor", "KADIKÖY", capacity: 0, emptyCapacity: 0),
            FacilityJson("Real Facility", "KADIKÖY", capacity: 100, emptyCapacity: 80));

        await using var context = CreateContext();
        var job = CreateJob(context, handler);

        await job.RunAsync(CancellationToken.None);

        var kadikoy = await context.ParkingReadings.SingleAsync(r => r.NeighborhoodId == "kadikoy");
        Assert.Equal(0.8, kadikoy.AverageAvailabilityRatio, precision: 3);
        Assert.Equal(1, kadikoy.FacilityCount);
    }

    [Fact]
    public async Task RunAsync_does_not_throw_when_the_source_is_unreachable()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("simulated network failure"));

        await using var context = CreateContext();
        var job = CreateJob(context, handler);

        var exception = await Record.ExceptionAsync(() => job.RunAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Empty(await context.ParkingReadings.ToListAsync());
    }

    [Fact]
    public async Task RunAsync_upserts_rather_than_duplicating_on_a_second_run()
    {
        var handler = FakeParkingHandler(FacilityJson("Kadıköy Açık", "KADIKÖY", capacity: 100, emptyCapacity: 30));

        await using var context = CreateContext();
        await CreateJob(context, handler).RunAsync(CancellationToken.None);
        await CreateJob(context, handler).RunAsync(CancellationToken.None);

        var reading = Assert.Single(await context.ParkingReadings.ToListAsync());
        Assert.Equal(0.3, reading.AverageAvailabilityRatio, precision: 3);
    }
}

file sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
