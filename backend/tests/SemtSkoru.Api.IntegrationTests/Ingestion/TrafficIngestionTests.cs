using System.Globalization;
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

public class TrafficIngestionTests : IAsyncLifetime
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

    private const string CsvHeader = "\"DATE_TIME\",\"LATITUDE\",\"LONGITUDE\",\"GEOHASH\",\"MINIMUM_SPEED\",\"MAXIMUM_SPEED\",\"AVERAGE_SPEED\",\"NUMBER_OF_VEHICLES\"";

    private static string CsvRow(double lat, double lon, double avgSpeed) =>
        string.Create(CultureInfo.InvariantCulture,
            $"\"2025-01-01 00:00:00\",\"{lat}\",\"{lon}\",sxkd1k,10,80,{avgSpeed},5");

    private static HttpMessageHandler FakeCsvHandler(params string[] rows)
    {
        var body = string.Join("\n", new[] { CsvHeader }.Concat(rows));
        return new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/csv"),
        });
    }

    private static TrafficIngestionJob CreateJob(AppDbContext context, HttpMessageHandler handler) =>
        new(new TrafficDataClient(new HttpClient(handler)), context, NullLogger<TrafficIngestionJob>.Instance, TimeProvider.System);

    [Fact]
    public async Task RunAsync_averages_speed_per_district_and_ignores_rows_outside_all_three()
    {
        // Two rows inside Kadıköy's real seeded boundary (Task 6) -> average of the two.
        // One row inside Üsküdar's boundary. No rows for Beşiktaş, and one row far outside
        // Istanbul entirely (must not be attributed to any district).
        var handler = FakeCsvHandler(
            CsvRow(40.98, 29.06, avgSpeed: 40),
            CsvRow(40.981, 29.061, avgSpeed: 60),
            CsvRow(41.03, 29.04, avgSpeed: 25),
            CsvRow(50.0, 10.0, avgSpeed: 999));

        await using var context = CreateContext();
        var job = CreateJob(context, handler);

        await job.RunAsync(CancellationToken.None);

        var readings = await context.TrafficReadings.ToListAsync();

        var kadikoy = Assert.Single(readings, r => r.NeighborhoodId == "kadikoy");
        Assert.Equal(50, kadikoy.AverageSpeedKmh, precision: 3);
        Assert.Equal(2, kadikoy.SampleCount);

        var uskudar = Assert.Single(readings, r => r.NeighborhoodId == "uskudar");
        Assert.Equal(25, uskudar.AverageSpeedKmh, precision: 3);

        Assert.DoesNotContain(readings, r => r.NeighborhoodId == "besiktas");
    }

    [Fact]
    public async Task RunAsync_marks_the_result_as_historical_not_fresh()
    {
        var handler = FakeCsvHandler(CsvRow(40.98, 29.06, avgSpeed: 40));

        await using var context = CreateContext();
        await CreateJob(context, handler).RunAsync(CancellationToken.None);

        var kadikoy = await context.TrafficReadings.SingleAsync(r => r.NeighborhoodId == "kadikoy");

        Assert.Equal(DataFreshnessStatus.Historical, kadikoy.Source.GetFreshness(DateTimeOffset.UtcNow));
        Assert.Contains("Ocak 2025", kadikoy.Source.SourceLicense, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_does_not_throw_when_the_source_is_unreachable()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("simulated network failure"));

        await using var context = CreateContext();
        var job = CreateJob(context, handler);

        var exception = await Record.ExceptionAsync(() => job.RunAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Empty(await context.TrafficReadings.ToListAsync());
    }
}

file sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
