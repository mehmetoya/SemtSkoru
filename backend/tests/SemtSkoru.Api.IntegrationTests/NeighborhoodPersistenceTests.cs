using Microsoft.EntityFrameworkCore;
using SemtSkoru.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SemtSkoru.Api.IntegrationTests;

public class NeighborhoodPersistenceTests : IAsyncLifetime
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

    [Fact]
    public async Task Migration_seeds_all_39_istanbul_districts_with_valid_boundaries()
    {
        await using var context = CreateContext();

        var neighborhoods = await context.Neighborhoods.ToListAsync();

        Assert.Equal(39, neighborhoods.Count);
        Assert.All(neighborhoods, n => Assert.NotNull(n.Boundary));
        Assert.All(neighborhoods, n => Assert.True(n.Boundary.IsValid));
        Assert.Contains(neighborhoods, n => n.Id == "kadikoy" && n.Name == "Kadıköy");
        Assert.Contains(neighborhoods, n => n.Id == "uskudar" && n.Name == "Üsküdar");
        Assert.Contains(neighborhoods, n => n.Id == "besiktas" && n.Name == "Beşiktaş");
        // Adalar and Şile are multi-island districts (MultiPolygon), unlike the other 37.
        Assert.Contains(neighborhoods, n => n.Id == "adalar" && n.Name == "Adalar");
        Assert.Contains(neighborhoods, n => n.Id == "sile" && n.Name == "Şile");
    }
}
