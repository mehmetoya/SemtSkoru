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

    [Fact]
    public async Task RunAsync_writes_a_reading_with_source_metadata_for_every_neighborhood()
    {
        var handler = new FakeHttpMessageHandler(_ =>
        {
            const string json = """[{"ReadTime":"2026-09-11T10:00:00","AQI":{"AQIIndex":42.0}}]""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        await using var context = CreateContext();
        var job = CreateJob(context, handler);

        await job.RunAsync(CancellationToken.None);

        var readings = await context.AirQualityReadings.ToListAsync();
        Assert.Equal(3, readings.Count);
        Assert.All(readings, r => Assert.Equal(42.0, r.AqiIndex));
        Assert.All(readings, r => Assert.Equal(
            "İBB Hava Kalitesi İstasyon Ölçüm Sonuçları Web Servisi", r.Source.SourceName));
        Assert.All(readings, r => Assert.Equal(DataFreshnessStatus.Fresh, r.Source.GetFreshness(DateTimeOffset.UtcNow)));
    }

    [Fact]
    public async Task RunAsync_does_not_throw_when_the_source_is_unreachable()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("simulated network failure"));

        await using var context = CreateContext();
        var job = CreateJob(context, handler);

        var exception = await Record.ExceptionAsync(() => job.RunAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Empty(await context.AirQualityReadings.ToListAsync());
    }

    [Fact]
    public async Task RunAsync_upserts_rather_than_duplicating_on_a_second_run()
    {
        var handler = new FakeHttpMessageHandler(_ =>
        {
            const string json = """[{"ReadTime":"2026-09-11T11:00:00","AQI":{"AQIIndex":55.0}}]""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        await using var context = CreateContext();
        await CreateJob(context, handler).RunAsync(CancellationToken.None);
        await CreateJob(context, handler).RunAsync(CancellationToken.None);

        var readings = await context.AirQualityReadings.ToListAsync();
        Assert.Equal(3, readings.Count);
        Assert.All(readings, r => Assert.Equal(55.0, r.AqiIndex));
    }
}

file sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
