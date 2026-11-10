using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Caching.Memory;
using NetTopologySuite.IO.Converters;
using Scalar.AspNetCore;
using SemtSkoru.Api.Boundaries;
using SemtSkoru.Api.Endpoints;
using SemtSkoru.Api.RateLimiting;
using SemtSkoru.Application.Assistant;
using SemtSkoru.Application.Comparisons;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Application.Search;
using SemtSkoru.Application.Summaries;
using SemtSkoru.Application.Trends;
using SemtSkoru.Infrastructure.Comparisons;
using SemtSkoru.Infrastructure.DependencyInjection;
using SemtSkoru.Infrastructure.ExternalApis;
using SemtSkoru.Infrastructure.Ingestion;
using SemtSkoru.Infrastructure.Persistence;
using SemtSkoru.Infrastructure.Scoring;
using SemtSkoru.Infrastructure.Summaries;
using SemtSkoru.Infrastructure.Trends;

var builder = WebApplication.CreateBuilder(args);

var rawConnectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Default ayarlı değil. Yerel geliştirme için README'deki " +
        "'dotnet user-secrets set' adımını çalıştırın.");

// Supabase's free-tier Session Pooler hard-caps concurrent client connections at 15
// total; live-verified a deploy crash ("EMAXCONNSESSION ... limited to pool_size: 15")
// because EF Core's and Hangfire's independent ADO.NET pools each default to a max of
// 100. Both use this same capped string, so they share Npgsql's process-wide pool for
// it and can never together approach the server-side limit. The 15 is shared with every OTHER
// process on the project too - notably the local İBB ingestion runner (SemtSkoru.Ingest, capped
// at 2), so 8 here leaves headroom for it plus ad-hoc dashboard/psql sessions.
var connectionString = InfrastructureServiceCollectionExtensions.WithMaxPoolSize(rawConnectionString, maxPoolSize: 8);

// İBB's servers (api.ibb.gov.tr, data.ibb.gov.tr) accept TCP connections only from Turkish IP
// addresses (live-verified 2026-11-10: probes from the US, Canada, Germany, the Netherlands,
// France, the UK, Poland, Bulgaria, Romania and Singapore all timed out; only Turkey connected).
// The production host (Render, Oregon) therefore can't run the six İBB ingestion jobs - they'd
// time out on every run - so production sets this to false (render.yaml) and runs them from a
// machine in Turkey with SemtSkoru.Ingest instead (docs/deployment.md). Defaults to true so local
// development and the test suite keep the Hangfire-scheduled behaviour.
var ibbIngestionJobsEnabled = builder.Configuration.GetValue("Ingestion:IbbJobsEnabled", defaultValue: true);

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

// /api/neighborhoods ships GeoJSON boundary polygons for all 39 districts in one response
// (display-simplified - see BoundarySimplifier) - JSON/GeoJSON compresses very well (typically 70-90% smaller with gzip/Brotli).
// Safe over HTTPS here specifically because these are public, unauthenticated, non-personalized
// responses with no secret/user-reflected content - the usual HTTPS-compression concern
// (CRIME/BREACH, which needs a secret mixed with attacker-influenced input in the same
// compressed stream) doesn't apply.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new GeoJsonConverterFactory()));
builder.Services.AddAppDbContext(connectionString);

builder.Services.AddSingleton(TimeProvider.System);

// The six İBB ingestion jobs and their HttpClients (timeouts included) live in Infrastructure so
// the standalone SemtSkoru.Ingest runner registers exactly the same thing. Registered even when
// Ingestion:IbbJobsEnabled is false: a job instance Hangfire had already enqueued before the
// switch flipped must still resolve (it then just times out harmlessly) rather than crash.
builder.Services.AddIbbIngestionJobs();

// Backs GET/HEAD /health/data (see Endpoints/DataFreshnessHealthEndpoint.cs).
builder.Services.AddScoped<IngestionSyncStatusReader>();

// CachedNeighborhoodScoringRepository wraps the real repository with a short in-process cache -
// see its remarks for why IMemoryCache (not a distributed cache) is the right call for a
// single-instance free-tier deployment.
builder.Services.AddMemoryCache();
builder.Services.AddScoped<NeighborhoodScoringRepository>();
builder.Services.AddScoped<INeighborhoodScoringRepository>(sp => new CachedNeighborhoodScoringRepository(
    sp.GetRequiredService<NeighborhoodScoringRepository>(), sp.GetRequiredService<IMemoryCache>()));
builder.Services.AddScoped<INeighborhoodScoringService, NeighborhoodScoringService>();
// GET /api/neighborhoods' display-simplified boundaries, cached in the same IMemoryCache - see
// SimplifiedNeighborhoodCache/BoundarySimplifier. Scoped because it takes the Scoped AppDbContext.
builder.Services.AddScoped<SimplifiedNeighborhoodCache>();

// AI Semt Asistanı - see GeminiClient.cs for the model/endpoint and
// RateLimiting/RateLimitingExtensions.cs for why this gets its own dedicated rate-limit policy.
// A short, bounded timeout: this feature calls Gemini synchronously inside an HTTP request a
// real visitor is waiting on, unlike the ingestion clients above which run as background jobs.
// This is a PER-ATTEMPT timeout, not the ceiling for the whole call - GeminiClient retries a
// transient 503/500 with backoff (see its remarks), and its own TotalRetryBudget bounds the sum.
// Lowered from 20s to 10s when that retry was added so the worst case stayed in the same range:
// a single attempt has never legitimately needed more than a few seconds on this model.
builder.Services.AddHttpClient<IDistrictAssistantAiClient, GeminiClient>(c => c.Timeout = TimeSpan.FromSeconds(10));
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

// Score trend ("Zaman İçindeki Değişim") - takes a weekly ScoreSnapshot of every district's
// scores (the first persisted score history this codebase has ever had - see ScoreSnapshot's
// remarks) and, only once a district has a baseline snapshot old enough to compare against (see
// ScoreSnapshotJob.MinimumBaselineAge), asks Gemini for a short grounded description of what
// changed. Reuses the SAME IDistrictAssistantAiClient as the two AI features above rather than a
// third client registration - see DistrictTrendService's remarks.
builder.Services.AddScoped<IDistrictTrendService, DistrictTrendService>();
builder.Services.AddScoped<IDistrictTrendRepository, DistrictTrendRepository>();
builder.Services.AddScoped<ScoreSnapshotJob>();

// AI compare-page summary ("İlçe Karşılaştırma Özeti") - reuses the SAME
// IDistrictAssistantAiClient/GeminiClient/API key as the two features above (see
// ComparisonSummaryService's remarks) rather than a fourth client registration. Unlike district
// summaries, this is NOT pre-generated by a Hangfire job (39-choose-2 = 741 possible pairs would
// make that ~19x the quota cost for a feature most pairs are never viewed for) - instead it's
// generated on demand and cached per pair by ComparisonSummaryOrchestrator, called directly from
// GET /api/neighborhoods/compare/summary under the SAME shared RateLimitPolicies.AiAssistant
// budget as POST /api/asistan.
builder.Services.AddScoped<IComparisonSummaryService, ComparisonSummaryService>();
builder.Services.AddScoped<IComparisonSummaryOrchestrator, ComparisonSummaryOrchestrator>();

// Natural-language district search (home page search box, GET /api/neighborhoods/search) -
// reuses the SAME IDistrictAssistantAiClient/GeminiClient/API key as the four features above
// rather than a fifth client registration. Deliberately the SAFEST AI feature in this app: the
// model is only ever asked which of the 6 real dimensions a free-text query concerns, never to
// pick, name, or rank a district (see DistrictSearchService's remarks) - so it needs no
// per-district data fetched into its prompt and no locale parameter (there's no free text for it
// to write). Shares the SAME RateLimitPolicies.AiAssistant budget as every other AI feature below.
builder.Services.AddScoped<IDistrictSearchService, DistrictSearchService>();

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
app.UseResponseCompression();
app.UseCors();
app.UseRateLimiter();

// Cheap liveness ping: a free-tier host that spins the container down after idle time
// (e.g. Render) wakes it back up on any request, at which point Hangfire's own recurring-job
// scheduler catches up on whatever was missed while asleep - in production that's only the
// Gemini jobs (district-summary-generation, score-snapshot); the İBB ingestion jobs run from a
// machine in Turkey instead (see Ingestion:IbbJobsEnabled above), so these pings no longer keep
// ingestion running. See .github/workflows/keep-warm.yml.
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

// Data-freshness check for a SECOND uptime monitor (docs/deployment.md, section 4): unlike the
// two liveness pings above it reads the DB (one small query, result cached for 30s), and answers
// 503 when any ingestion job's last successful sync is overdue for its cadence - so a job that
// keeps failing and skipping (İBB timing out from Render's region) emails someone instead of
// silently leaving the UI's data to go Stale. Same GET+HEAD mapping and rate-limit exemption as
// above, for the same UptimeRobot reasons.
app.MapDataFreshnessHealthEndpoint();

app.MapNeighborhoodEndpoints();
app.MapAssistantEndpoints();

var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();

if (ibbIngestionJobsEnabled)
{
    // The six ingestion schedules below must stay in step with SourceCadenceSync.ExpectedSyncInterval
    // (Domain/SourceCadence.cs) for the cadence each job writes - Live = daily, Periodic = weekly,
    // StaticSnapshot = monthly - since that's what /health/data judges "overdue" against.

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
}
else
{
    // Not calling AddOrUpdate is not enough: Hangfire persists recurring jobs in its own tables
    // (schema "hangfire"), so a previous deploy's registrations would keep firing - and timing
    // out against İBB - forever. Removing them by ID is idempotent, so this runs on every boot.
    foreach (var ibbJob in IbbIngestionJobCatalog.All)
    {
        recurringJobs.RemoveIfExists(ibbJob.RecurringJobId);
    }
}

// Weekly - deliberately the SAME bucket as green space/health access/transit access above, not
// daily like air quality/parking. This is the one recurring job whose cost is measured in a
// separate, shared, hard-capped-per-day THIRD-PARTY quota (Gemini's free tier - see
// RateLimiting/RateLimitingExtensions.cs) rather than just this app's own DB/API-fetch time, so
// its cadence is chosen against that budget, not against how often the underlying scores change:
// worst case (every district's score signature changed, in BOTH supported locales - see
// AiLocale.All) this job spends 39 x 2 = 78 Gemini calls per run. Daily, that would be up to 78
// calls EVERY day against the same 150-request shared daily budget the interactive AI Semt
// Asistanı also depends on - over half the whole day's budget, every day, before a single real
// visitor asks the assistant anything. Weekly, the same worst case amortizes to 78/7 ≈ 11
// calls/day, and DistrictSummaryGenerationJob's own per-locale score-signature skip (see its
// remarks) means a real run is usually far cheaper than that, since only air quality/parking
// update daily and a district's *standout* dimensions rarely flip on a single day's noise. A
// short lag between a real score change and this text catching up is an acceptable trade for not
// competing with the assistant for the same scarce quota.
recurringJobs.AddOrUpdate<DistrictSummaryGenerationJob>(
    "district-summary-generation",
    job => job.RunAsync(CancellationToken.None),
    Cron.Weekly());

// Weekly, the same bucket as green space/health access/transit access/district-summary-generation
// above, for the same reason DistrictSummaryGenerationJob is weekly rather than daily: this job's
// TREND half also spends a THIRD-PARTY quota (Gemini) worst case 39 x 2 = 78 calls/run (39
// districts, both supported locales - see AiLocale.All), and weekly keeps that amortized cost far
// below the shared 150-request daily budget. Its SNAPSHOT half doesn't need weekly cadence for
// its own sake (it writes plain DB rows, no external quota involved, and is locale-agnostic) - it
// runs at this cadence only because it's the same job as the trend generation that does.
//
// Scheduled 6 hours after the other weekly jobs' default midnight slot (Cron.Weekly() with no
// arguments = Monday 00:00 UTC), specifically to avoid ever landing in the same Hangfire
// worker-pool window as district-summary-generation above: that job's own worst-case run takes
// ~6.5 minutes (see its remarks), so a 6-hour gap comfortably separates the two jobs' Gemini
// calls in the normal case where both fire on schedule. This app's free-tier host (Render) can
// spin its container down when idle and only wakes on a request (see the /health comment above
// and .github/workflows/keep-warm.yml) - if it was asleep past BOTH jobs' trigger times, Hangfire
// fires whatever it missed as soon as the process wakes, which could still momentarily line the
// two up despite the staggered schedule. That collision is accepted, not specially guarded
// against: with WorkerCount=2 they would run truly in parallel rather than queue, so the combined
// per-minute Gemini rate could transiently exceed this app's own conservative internal target -
// but each job already treats Gemini's own 429 as an expected, handled outcome (30s backoff, see
// RateLimitBackoff in each job). Unlike before locale support was added, a full 78-call run from
// BOTH jobs at once (156 calls total) is no longer comfortably under the 150-request DAILY cap
// that is this app's real backstop - it would, in that specific worst-case-collision scenario,
// exhaust the day's shared Gemini quota (the last ~6 calls of whichever job runs second would
// come back RateLimited, which both jobs already handle as an expected, non-fatal outcome: they
// back off and simply leave those particular (district, locale) rows stale until the NEXT weekly
// run, never a crash or a fabricated placeholder). This residual risk is accepted rather than
// specially guarded against because it requires BOTH an extended host sleep past both trigger
// times AND every single one of 78 district/locale score signatures changing in one week, which
// has never happened in this app's real usage pattern (see DistrictSummaryGenerationJob's own
// remarks for why that daily figure, not the per-minute one, is what every one of these jobs'
// worst-case reasoning is ultimately bounded by).
recurringJobs.AddOrUpdate<ScoreSnapshotJob>(
    "score-snapshot",
    job => job.RunAsync(CancellationToken.None),
    Cron.Weekly(DayOfWeek.Monday, 6));

app.Run();

public partial class Program;
