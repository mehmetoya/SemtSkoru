using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NetTopologySuite.Algorithm.Distance;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;
using SemtSkoru.Api.Boundaries;
using SemtSkoru.Api.Endpoints;
using SemtSkoru.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace SemtSkoru.Api.IntegrationTests.Boundaries;

// Runs BoundarySimplifier against the REAL seeded OSM boundaries (the migrations' own seed data,
// all 39 districts), not synthetic shapes - the point is to prove the tolerance and the rounding
// fallback hold up on the actual data GET /api/neighborhoods serves, and to record the payload
// saving it buys.
public class SeededBoundarySimplificationTests(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:16-3.4")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    // Same settings the Api serializes with (Program.cs: web defaults + the GeoJSON converter).
    private static readonly JsonSerializerOptions GeoJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new GeoJsonConverterFactory() },
    };

    // The one seeded district with a sub-metre pinch that 5-decimal rounding would close - see
    // BoundarySimplifier's remarks. It's served at 6 decimals instead.
    private const string SixDecimalDistrict = "kucukcekmece";

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

    private async Task<Dictionary<string, Geometry>> LoadStoredBoundariesAsync()
    {
        await using var context = CreateContext();
        return await context.Neighborhoods.AsNoTracking().ToDictionaryAsync(n => n.Id, n => n.Boundary);
    }

    private static bool IsRoundedTo(Geometry geometry, int places) =>
        geometry.Coordinates.All(c => Math.Round(c.X, places) == c.X && Math.Round(c.Y, places) == c.Y);

    private static (int Raw, int Gzip) Measure(byte[] json)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal))
        {
            gzip.Write(json);
        }

        return (json.Length, buffer.ToArray().Length);
    }

    [Fact]
    public async Task Every_seeded_boundary_simplifies_to_a_valid_rounded_geometry_of_the_same_shape()
    {
        var stored = await LoadStoredBoundariesAsync();
        Assert.Equal(39, stored.Count);

        var originalVertices = 0;
        var simplifiedVertices = 0;
        foreach (var (id, original) in stored)
        {
            var simplified = BoundarySimplifier.Simplify(original);

            Assert.True(simplified.IsValid, $"{id} is invalid after simplification");
            Assert.Equal(original.OgcGeometryType, simplified.OgcGeometryType);
            Assert.Equal(original.NumGeometries, simplified.NumGeometries);
            // <=, not <: the 3 districts from the first seed migration (Kadıköy is 63 vertices)
            // are already coarse enough that the tolerance has nothing to drop.
            Assert.True(simplified.NumPoints <= original.NumPoints, $"{id}: {original.NumPoints} -> {simplified.NumPoints}");
            Assert.True(
                IsRoundedTo(simplified, id == SixDecimalDistrict ? BoundarySimplifier.FallbackDecimalPlaces : BoundarySimplifier.DecimalPlaces),
                $"{id} has coordinates that aren't rounded as expected");

            // Visual-quality bound: no stored vertex ends up further than the tolerance (plus
            // rounding) from the served outline, and vice versa.
            var bound = BoundarySimplifier.Tolerance + Math.Pow(10, -BoundarySimplifier.DecimalPlaces);
            Assert.True(DiscreteHausdorffDistance.Distance(original, simplified) <= bound, $"{id} moved too far");

            originalVertices += original.NumPoints;
            simplifiedVertices += simplified.NumPoints;
        }

        output.WriteLine($"vertices: {originalVertices} -> {simplifiedVertices}");
        Assert.True(simplifiedVertices < originalVertices * 0.6);
    }

    [Fact]
    public async Task GetNeighborhoods_serves_the_simplified_boundaries_in_the_same_json_shape_and_much_smaller()
    {
        var stored = await LoadStoredBoundariesAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/neighborhoods");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsByteArrayAsync();
        var served = JsonSerializer.Deserialize<List<NeighborhoodSummaryDto>>(body, GeoJsonOptions);
        Assert.NotNull(served);
        Assert.Equal(39, served.Count);

        // Unchanged contract: still id/name/boundary/overallScore, and each boundary is still the
        // same GeoJSON geometry type the frontend switches on.
        using (var document = JsonDocument.Parse(body))
        {
            var first = document.RootElement[0];
            Assert.Equal(
                ["id", "name", "boundary", "overallScore"],
                first.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.Equal(stored[first.GetProperty("id").GetString()!].GeometryType, first.GetProperty("boundary").GetProperty("type").GetString());
        }

        foreach (var district in served)
        {
            var expected = BoundarySimplifier.Simplify(stored[district.Id]);
            Assert.True(district.Boundary.IsValid);
            Assert.Equal(expected.NumPoints, district.Boundary.NumPoints);
        }

        // Before/after payload, with the exact same serializer settings and the same DTO - the
        // only difference being full-resolution vs simplified boundaries.
        var fullResolution = served
            .Select(n => n with { Boundary = stored[n.Id] })
            .ToList();
        var before = Measure(JsonSerializer.SerializeToUtf8Bytes(fullResolution, GeoJsonOptions));
        var after = Measure(body);
        output.WriteLine($"/api/neighborhoods raw: {before.Raw:N0} -> {after.Raw:N0} bytes");
        output.WriteLine($"/api/neighborhoods gzip: {before.Gzip:N0} -> {after.Gzip:N0} bytes");

        Assert.True(after.Raw < before.Raw / 2, $"raw {before.Raw} -> {after.Raw}");
        Assert.True(after.Gzip < before.Gzip / 2, $"gzip {before.Gzip} -> {after.Gzip}");
    }

    [Fact]
    public async Task Serving_simplified_boundaries_never_changes_the_stored_full_resolution_ones()
    {
        // Ingestion jobs' point-in-polygon tests read Neighborhoods.Boundary directly - it must
        // stay exactly as seeded no matter how often the list endpoint is served.
        var before = await LoadStoredBoundariesAsync();
        var client = _factory.CreateClient();

        (await client.GetAsync("/api/neighborhoods")).EnsureSuccessStatusCode();
        (await client.GetAsync("/api/neighborhoods")).EnsureSuccessStatusCode();

        var after = await LoadStoredBoundariesAsync();
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        Assert.All(before, pair => Assert.True(
            pair.Value.EqualsExact(after[pair.Key]),
            $"{pair.Key}'s stored boundary changed"));
    }

    [Fact]
    public async Task SimplifiedNeighborhoodCache_computes_once_and_reuses_the_result()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());

        IReadOnlyList<SimplifiedNeighborhood> first;
        await using (var context = CreateContext())
        {
            first = await new SimplifiedNeighborhoodCache(context, cache).GetAllAsync(CancellationToken.None);
        }

        // A new scope (new DbContext, as on the next request) must get the very same cached
        // instance back, not a recomputed one.
        IReadOnlyList<SimplifiedNeighborhood> second;
        await using (var context = CreateContext())
        {
            second = await new SimplifiedNeighborhoodCache(context, cache).GetAllAsync(CancellationToken.None);
        }

        Assert.Equal(39, first.Count);
        Assert.Same(first, second);
    }
}
