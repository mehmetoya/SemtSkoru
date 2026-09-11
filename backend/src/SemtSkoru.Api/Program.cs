using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Ingestion;
using SemtSkoru.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default");

builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.UseNetTopologySuite()));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<IAirQualityApiClient, AirQualityApiClient>();
builder.Services.AddScoped<AirQualityIngestionJob>();

// Longer timeout: the green space source is a ~53 MB city-wide GeoJSON download.
builder.Services.AddHttpClient<IGreenSpaceApiClient, GreenSpaceApiClient>(c => c.Timeout = TimeSpan.FromMinutes(3));
builder.Services.AddScoped<GreenSpaceIngestionJob>();

// Longer timeout still: the traffic source streams a ~140 MB CSV (one month, hourly, city-wide).
builder.Services.AddHttpClient<ITrafficDataClient, TrafficDataClient>(c => c.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddScoped<TrafficIngestionJob>();

builder.Services.AddHangfire(config => config
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();

recurringJobs.AddOrUpdate<AirQualityIngestionJob>(
    "air-quality-ingestion",
    job => job.RunAsync(CancellationToken.None),
    Cron.Daily());

recurringJobs.AddOrUpdate<GreenSpaceIngestionJob>(
    "green-space-ingestion",
    job => job.RunAsync(CancellationToken.None),
    Cron.Weekly());

// Monthly, not because the January 2025 snapshot is expected to change, but so an eventual
// real update from İBB (the dataset's own notes say more will come) is picked up automatically.
recurringJobs.AddOrUpdate<TrafficIngestionJob>(
    "traffic-ingestion",
    job => job.RunAsync(CancellationToken.None),
    Cron.Monthly());

app.Run();

public partial class Program;
