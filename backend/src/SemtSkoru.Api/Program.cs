using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.IO.Converters;
using Scalar.AspNetCore;
using SemtSkoru.Api.Endpoints;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Ingestion;
using SemtSkoru.Infrastructure.Persistence;
using SemtSkoru.Infrastructure.Scoring;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Default ayarlı değil. Yerel geliştirme için README'deki " +
        "'dotnet user-secrets set' adımını çalıştırın.");

// Frontend (Next.js) and backend run on different ports/origins in dev; without this,
// every browser fetch from web/ silently fails CORS (curl/jsdom tests never surfaced it --
// they don't enforce CORS -- only a real browser hitting the real API does).
var corsOrigin = builder.Configuration["Cors:FrontendOrigin"] ?? "http://localhost:3000";
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(corsOrigin).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new GeoJsonConverterFactory()));
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

builder.Services.AddScoped<INeighborhoodScoringRepository, NeighborhoodScoringRepository>();
builder.Services.AddScoped<INeighborhoodScoringService, NeighborhoodScoringService>();

builder.Services.AddHangfire(config => config
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors();

app.MapNeighborhoodEndpoints();

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
