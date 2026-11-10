using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Ingestion;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Infrastructure.DependencyInjection;

/// <summary>
/// Registrations shared by the web host (SemtSkoru.Api) and the standalone İBB ingestion runner
/// (SemtSkoru.Ingest), so the runner executes exactly the same job classes with exactly the same
/// HttpClient timeouts and DbContext setup as the API - without copying Program.cs.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Supabase's free-tier Session Pooler hard-caps concurrent client connections at 15 total
    /// across EVERY process connected to the project, so each process caps its own Npgsql pool
    /// explicitly rather than taking Npgsql's default of 100.
    /// </summary>
    public static string WithMaxPoolSize(string connectionString, int maxPoolSize, string? applicationName = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = maxPoolSize };
        if (applicationName is not null)
        {
            builder.ApplicationName = applicationName;
        }

        return builder.ConnectionString;
    }

    public static IServiceCollection AddAppDbContext(this IServiceCollection services, string connectionString) =>
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.UseNetTopologySuite()));

    /// <summary>
    /// The six İBB ingestion jobs (see <see cref="IbbIngestionJobCatalog"/>) and their source
    /// clients. Requires <see cref="AddAppDbContext"/> (or an equivalent AppDbContext registration).
    /// </summary>
    public static IServiceCollection AddIbbIngestionJobs(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient<IAirQualityApiClient, AirQualityApiClient>();
        services.AddScoped<AirQualityIngestionJob>();

        // Longer timeout: the green space source is a ~53 MB city-wide GeoJSON download.
        services.AddHttpClient<IGreenSpaceApiClient, GreenSpaceApiClient>(c => c.Timeout = TimeSpan.FromMinutes(3));
        services.AddScoped<GreenSpaceIngestionJob>();

        // Longer timeout still: the traffic source streams a ~140 MB CSV (one month, hourly, city-wide).
        services.AddHttpClient<ITrafficDataClient, TrafficDataClient>(c => c.Timeout = TimeSpan.FromMinutes(5));
        services.AddScoped<TrafficIngestionJob>();

        services.AddHttpClient<IParkingApiClient, ParkingApiClient>();
        services.AddScoped<ParkingIngestionJob>();

        services.AddHttpClient<IHealthAccessApiClient, HealthAccessApiClient>();
        services.AddScoped<HealthAccessIngestionJob>();

        services.AddHttpClient<ITransitAccessApiClient, TransitAccessApiClient>();
        services.AddScoped<TransitAccessIngestionJob>();

        return services;
    }
}
