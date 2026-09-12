using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.IO.Converters;
using Npgsql;
using Scalar.AspNetCore;
using SemtSkoru.Api.Endpoints;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Ingestion;
using SemtSkoru.Infrastructure.Persistence;
using SemtSkoru.Infrastructure.Scoring;

var builder = WebApplication.CreateBuilder(args);

var rawConnectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Default ayarlı değil. Yerel geliştirme için README'deki " +
        "'dotnet user-secrets set' adımını çalıştırın.");

// Supabase's free-tier Session Pooler hard-caps concurrent client connections at 15
// total; live-verified a deploy crash ("EMAXCONNSESSION ... limited to pool_size: 15")
// because EF Core's and Hangfire's independent ADO.NET pools each default to a max of
// 100. Both use this same capped string, so they share Npgsql's process-wide pool for
// it and can never together approach the server-side limit.
var connectionString = new NpgsqlConnectionStringBuilder(rawConnectionString) { MaxPoolSize = 8 }.ConnectionString;

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
// Default worker count is Environment.ProcessorCount * 5, which can be misleadingly
// high in a container with a small CPU quota; kept low since this app's recurring
// jobs never run concurrently with each other and each worker can hold a connection.
builder.Services.AddHangfireServer(options => options.WorkerCount = 2);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Render (and similar free-tier hosts) terminate TLS at their edge and forward plain HTTP
// to the container; without trusting X-Forwarded-Proto, UseHttpsRedirection sees "http" on
// every request and redirects right back to the same HTTPS URL on every single call.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

app.UseHttpsRedirection();
app.UseCors();

// Cheap liveness ping: a free-tier host that spins the container down after idle time
// (e.g. Render) wakes it back up on any request, at which point Hangfire's own recurring-job
// scheduler catches up on whatever was missed while asleep. See .github/workflows/daily-wake.yml.
// /health/live is the same check under the path an external uptime monitor (UptimeRobot)
// was already configured to poll - added as an alias rather than asking that config to change.
app.MapGet("/health", () => Results.Ok());
app.MapGet("/health/live", () => Results.Ok());

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
