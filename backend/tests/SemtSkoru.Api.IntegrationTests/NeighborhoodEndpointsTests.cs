using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.IO.Converters;
using SemtSkoru.Api.Endpoints;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SemtSkoru.Api.IntegrationTests;

public class NeighborhoodEndpointsTests : IAsyncLifetime
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

    // NeighborhoodSummaryDto.Boundary is an abstract NTS Geometry; a client that wants it back
    // needs the same GeoJSON converter the Api registers server-side (Program.cs).
    private static readonly JsonSerializerOptions GeoJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new GeoJsonConverterFactory() },
    };

    private static DataSourceMetadata TestSource(DateTimeOffset at) => new(
        SourceName: "Test Source",
        SourceUrl: "https://example.test",
        SourceLicense: "Test License",
        FetchedAt: at,
        PublishedAt: at,
        LastSuccessfulSyncAt: at,
        Cadence: SourceCadence.Live);

    [Fact]
    public async Task GetNeighborhoods_returns_all_39_seeded_districts()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods");

        response.EnsureSuccessStatusCode();
        var neighborhoods = await response.Content.ReadFromJsonAsync<List<NeighborhoodSummaryDto>>(GeoJsonOptions);
        Assert.NotNull(neighborhoods);
        Assert.Equal(39, neighborhoods.Count);
        var kadikoy = Assert.Single(neighborhoods, n => n.Id == "kadikoy" && n.Name == "Kadıköy");
        Assert.True(kadikoy.Boundary.IsValid);
        Assert.True(kadikoy.Boundary.Area > 0);
    }

    [Fact]
    public async Task GetNames_returns_id_and_name_pairs_for_all_39_districts_without_scoring()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods/names");

        response.EnsureSuccessStatusCode();
        var names = await response.Content.ReadFromJsonAsync<List<NeighborhoodNameDto>>();
        Assert.NotNull(names);
        Assert.Equal(39, names.Count);
        Assert.Contains(names, n => n.Id == "kadikoy" && n.Name == "Kadıköy");
        Assert.Contains(names, n => n.Id == "uskudar" && n.Name == "Üsküdar");
    }

    [Fact]
    public async Task GetScore_returns_computed_dimension_scores_for_a_known_district_with_partial_data()
    {
        var publishedAt = DateTimeOffset.UtcNow;
        await using (var context = CreateContext())
        {
            context.AirQualityReadings.Add(new AirQualityReading
            {
                NeighborhoodId = "kadikoy",
                AqiIndex = 0,
                ReadingTime = DateTimeOffset.UtcNow,
                Source = TestSource(publishedAt),
            });
            await context.SaveChangesAsync();
        }

        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods/kadikoy/score");

        response.EnsureSuccessStatusCode();
        var score = await response.Content.ReadFromJsonAsync<NeighborhoodScoreDto>();
        Assert.NotNull(score);
        Assert.Equal("kadikoy", score.NeighborhoodId);
        Assert.Equal(100, score.AirQuality.Score);
        Assert.Equal("Fresh", score.AirQuality.Freshness);
        Assert.Equal("Test Source", score.AirQuality.SourceName);
        // Postgres timestamptz only keeps microsecond precision, so a .NET DateTimeOffset's
        // 100ns tick remainder gets truncated on the round-trip through the DB - exact equality
        // fails intermittently (confirmed on a real Linux CI runner, ~9/10 of the time, since
        // macOS's clock resolution happens to rarely produce a nonzero sub-microsecond tick).
        Assert.Equal(publishedAt, score.AirQuality.PublishedAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Null(score.GreenSpace.Score);
        Assert.Null(score.GreenSpace.SourceName);
        Assert.False(score.IsComplete);
    }

    [Fact]
    public async Task GetScore_returns_404_for_an_unknown_district()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods/nonexistent/score");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Compare_returns_both_districts_scores_side_by_side()
    {
        await using (var context = CreateContext())
        {
            context.AirQualityReadings.AddRange(
                new AirQualityReading { NeighborhoodId = "kadikoy", AqiIndex = 0, ReadingTime = DateTimeOffset.UtcNow, Source = TestSource(DateTimeOffset.UtcNow) },
                new AirQualityReading { NeighborhoodId = "uskudar", AqiIndex = 500, ReadingTime = DateTimeOffset.UtcNow, Source = TestSource(DateTimeOffset.UtcNow) });
            await context.SaveChangesAsync();
        }

        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods/compare?a=kadikoy&b=uskudar");

        response.EnsureSuccessStatusCode();
        var comparison = await response.Content.ReadFromJsonAsync<NeighborhoodComparisonDto>();
        Assert.NotNull(comparison);
        Assert.Equal("kadikoy", comparison.A.NeighborhoodId);
        Assert.Equal(100, comparison.A.AirQuality.Score);
        Assert.Equal("uskudar", comparison.B.NeighborhoodId);
        Assert.Equal(0, comparison.B.AirQuality.Score);
    }

    [Fact]
    public async Task Compare_returns_400_when_a_query_parameter_is_missing()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods/compare?a=kadikoy");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Compare_returns_400_when_an_id_does_not_exist()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods/compare?a=kadikoy&b=nonexistent");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
