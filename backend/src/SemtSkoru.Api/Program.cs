using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.IO.Converters;
using Npgsql;
using Scalar.AspNetCore;
using SemtSkoru.Api.Endpoints;
using SemtSkoru.Api.RateLimiting;
using SemtSkoru.Application.Assistant;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Application.Summaries;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Ingestion;
using SemtSkoru.Infrastructure.Persistence;
using SemtSkoru.Infrastructure.Scoring;
using SemtSkoru.Infrastructure.Summaries;

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

// Ensures a future unhandled exception (e.g. a transient DB error under load) renders as a
// generic ProblemDetails response instead of an ASP.NET Core stack trace, even though no
// code path is currently known to leak one - a code-level backstop, not a reaction to an
// observed leak.
builder.Services.AddProblemDetails();

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

builder.Services.AddHttpClient<IParkingApiClient, ParkingApiClient>();
builder.Services.AddScoped<ParkingIngestionJob>();

builder.Services.AddHttpClient<IHealthAccessApiClient, HealthAccessApiClient>();
builder.Services.AddScoped<HealthAccessIngestionJob>();

builder.Services.AddHttpClient<ITransitAccessApiClient, TransitAccessApiClient>();
builder.Services.AddScoped<TransitAccessIngestionJob>();

builder.Services.AddScoped<INeighborhoodScoringRepository, NeighborhoodScoringRepository>();
builder.Services.AddScoped<INeighborhoodScoringService, NeighborhoodScoringService>();

// AI Semt Asistanı - see GeminiClient.cs for the model/endpoint and
// RateLimiting/RateLimitingExtensions.cs for why this gets its own dedicated rate-limit policy.
// A short, bounded timeout: this feature calls Gemini synchronously inside an HTTP request a
// real visitor is waiting on, unlike the ingestion clients above which run as background jobs.
builder.Services.AddHttpClient<IDistrictAssistantAiClient, GeminiClient>(c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddScoped<INeighborhoodDirectory, NeighborhoodDirectory>();
builder.Services.AddScoped<IDistrictAssistantService, DistrictAssistantService>();

// AI district summary ("Öne Çıkan Özellikler") - reuses the SAME IDistrictAssistantAiClient/
// GeminiClient/API key as the assistant above (see DistrictSummaryService's remarks for why one
// client is enough for both features) rather than a second HttpClient registration. Generated
// entirely by DistrictSummaryGenerationJob on its own Hangfire schedule below, never live on a
// request path, so this service needs no rate-limit policy of its own.
builder.Services.AddScoped<IDistrictSummaryService, DistrictSummaryService>();
builder.Services.AddScoped<IDistrictSummaryRepository, DistrictSummaryRepository>();
builder.Services.AddScoped<DistrictSummaryGenerationJob>();

// See RateLimiting/RateLimitingExtensions.cs for the policies and their rationale: every
// endpoint here is public and unauthenticated (no API keys - out of scope), and DB round trips
// are the resource actually worth protecting given MaxPoolSize=8 above.
builder.Services.AddApiRateLimiting();

builder.Services.AddHangfire(config => config
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
// Default worker count is Environment.ProcessorCount * 5, which can be misleadingly
// high in a container with a small CPU quota; kept low since this app's recurring
// jobs never run concurrently with each other and each worker can hold a connection.
builder.Services.AddHangfireServer(options => options.WorkerCount = 2);

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
else
{
    // Not meaningful for server-to-server/API traffic (no browser ever sees this API's own
    // HTTPS responses directly), but a one-line hardening gap otherwise - the frontend
    // (Vercel) already sends its own HSTS header for real browser navigation.
    app.UseHsts();
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
app.UseRateLimiter();

// Cheap liveness ping: a free-tier host that spins the container down after idle time
// (e.g. Render) wakes it back up on any request, at which point Hangfire's own recurring-job
// scheduler catches up on whatever was missed while asleep. See .github/workflows/daily-wake.yml.
// /health/live is the same check under the path an external uptime monitor (UptimeRobot)
// was already configured to poll - added as an alias rather than asking that config to change.
// Both verbs are mapped explicitly: Minimal APIs don't auto-answer HEAD for a GET-only
// route the way MVC controllers do, and UptimeRobot's monitor sends HEAD - live-verified
// (2026-09-12) that a GET-only mapping here 405s every HEAD check.
// Exempt from rate limiting (.DisableRateLimiting()): these don't touch the DB, and an uptime
// monitor or Render's own wake-up ping getting 429'd would be exactly the wrong failure mode.
var healthHandler = () => Results.Ok();
app.MapMethods("/health", ["GET", "HEAD"], healthHandler).DisableRateLimiting();
app.MapMethods("/health/live", ["GET", "HEAD"], healthHandler).DisableRateLimiting();

app.MapNeighborhoodEndpoints();
app.MapAssistantEndpoints();

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

// Daily, matching air quality's cadence - parking occupancy is a fast-changing, live signal,
// not a slow-moving one like green space or the frozen traffic snapshot.
recurringJobs.AddOrUpdate<ParkingIngestionJob>(
    "parking-ingestion",
    job => job.RunAsync(CancellationToken.None),
    Cron.Daily());

// Weekly, matching green space's cadence, not parking's/air quality's daily one - İBB's health
// access index is a slow-changing published index (its GeoJSON resource was last modified
// 2024-02-01, per İBB's own CKAN metadata), not a live feed.
recurringJobs.AddOrUpdate<HealthAccessIngestionJob>(
    "health-access-ingestion",
    job => job.RunAsync(CancellationToken.None),
    Cron.Weekly());

// Weekly, matching green space's/health access's cadence - the İETT bus stop dataset's own CKAN
// metadata shows a recent update (2026-03-18), but physical bus stop infrastructure changes
// slowly in practice, not a live feed like air quality/parking occupancy.
recurringJobs.AddOrUpdate<TransitAccessIngestionJob>(
    "transit-access-ingestion",
    job => job.RunAsync(CancellationToken.None),
    Cron.Weekly());

// Weekly - deliberately the SAME bucket as green space/health access/transit access above, not
// daily like air quality/parking. This is the one recurring job whose cost is measured in a
// separate, shared, hard-capped-per-day THIRD-PARTY quota (Gemini's free tier - see
// RateLimiting/RateLimitingExtensions.cs) rather than just this app's own DB/API-fetch time, so
// its cadence is chosen against that budget, not against how often the underlying scores change:
// worst case (every district's score signature changed) this job spends 39 Gemini calls per run.
// Daily, that would be up to 39 calls EVERY day against the same 150-request shared daily budget
// the interactive AI Semt Asistanı also depends on - over a quarter of the whole day's budget,
// every day, before a single real visitor asks the assistant anything. Weekly, the same worst
// case amortizes to under 6 calls/day, and DistrictSummaryGenerationJob's own score-signature
// skip (see its remarks) means a real run is usually far cheaper than that, since only air
// quality/parking update daily and a district's *standout* dimensions rarely flip on a single
// day's noise. A short lag between a real score change and this text catching up is an
// acceptable trade for not competing with the assistant for the same scarce quota.
recurringJobs.AddOrUpdate<DistrictSummaryGenerationJob>(
    "district-summary-generation",
    job => job.RunAsync(CancellationToken.None),
    Cron.Weekly());

app.Run();

public partial class Program;
