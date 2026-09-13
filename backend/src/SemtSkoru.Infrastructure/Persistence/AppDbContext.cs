using Microsoft.EntityFrameworkCore;
using SemtSkoru.Domain;

namespace SemtSkoru.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Neighborhood> Neighborhoods => Set<Neighborhood>();
    public DbSet<AirQualityReading> AirQualityReadings => Set<AirQualityReading>();
    public DbSet<GreenSpaceReading> GreenSpaceReadings => Set<GreenSpaceReading>();
    public DbSet<TrafficReading> TrafficReadings => Set<TrafficReading>();
    public DbSet<ParkingReading> ParkingReadings => Set<ParkingReading>();
    public DbSet<HealthAccessReading> HealthAccessReadings => Set<HealthAccessReading>();
    public DbSet<TransitAccessReading> TransitAccessReadings => Set<TransitAccessReading>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
