using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace SemtSkoru.Api.RateLimiting;

public static class RateLimitingExtensions
{
    // The actual scarce resource is the Postgres connection pool (Program.cs caps it at
    // MaxPoolSize=8, shared with Hangfire's WorkerCount=2 plus its own storage-polling/heartbeat
    // connections - see the comment there for the live-verified reason). EF Core's scoped
    // DbContext opens one connection per request and holds it for that request's whole chain of
    // sequential round trips (up to ~14 for /compare), so "concurrent in-flight API requests" is
    // a good proxy for "connections checked out of the pool right now" - reserve 3 connections
    // of headroom for Hangfire and give the API at most 5 concurrent.
    private const int DbPoolConcurrencyPermitLimit = 5;

    // Extra bursts wait in line for a slot instead of failing outright - at ~70ms/round-trip
    // (live-verified cross-cloud Render->Supabase latency, see NeighborhoodEndpoints.cs), even
    // a full compare (~14 round trips) finishes in ~1s, so 30 queued requests behind 5 running
    // ones clears in a handful of seconds, not a pileup. Sized empirically, not guessed: an
    // earlier QueueLimit=10 (15 admitted total) rejected ~19% of a locally load-tested 21-request
    // burst modeling 12 concurrent "visitors" landing at once (home page + district page +
    // both kart PNG routes, matching web/lib/api-client.ts's and the kart/route.tsx files' real
    // call patterns) - that's exactly the "don't throttle our own real traffic" failure this
    // whole feature must avoid. Re-tested at this value: same 21-request burst and a deliberate
    // 40-concurrent-request spike both cleared with at most one 429 (see this change's commit
    // message for the full local load-test numbers).
    private const int DbPoolConcurrencyQueueLimit = 30;

    // Per-IP abuse backstop, sized per endpoint by real DB cost. These are deliberately
    // generous, not a fine-grained per-minute throttle: the frontend calls this API
    // server-side during SSR from Vercel's small, rotating pool of egress IPs (see
    // web/lib/api-client.ts and web/app/*/kart/route.tsx), so many unrelated real visitors'
    // page loads can legitimately share one IP here. A tight window would either throttle
    // real traffic together or, at this app's actual volume, become a "first N users after a
    // cold start win" gate. These limits exist to cut off the kind of tight request loop the
    // security audit flagged (a handful of concurrent requests against the wrong endpoint) -
    // the db-pool concurrency limiter below is what actually protects the connection pool, and
    // it applies regardless of which IP(s) a caller uses.
    private const int CheapPermitLimitPerMinute = 120;
    private const int StandardPermitLimitPerMinute = 60;
    private const int ComparePermitLimitPerMinute = 30;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    // AI Semt Asistanı (POST /api/asistan) spends a completely different kind of scarce
    // resource than every policy above: Google Gemini's free-tier quota. That quota is metered
    // per Google Cloud project - i.e. shared by every visitor to the whole deployed app at once
    // - and resets once a day, not once a minute. A per-IP-only limiter (like the three above)
    // cannot protect that at all: 20 different visitors on 20 different IPs, each well under
    // their own per-IP cap, could still collectively exhaust the whole day's shared budget and
    // 503 the feature for everyone else until Google's own reset.
    //
    // As of this writing (2026-09-13), ai.google.dev/gemini-api/docs/rate-limits no longer
    // publishes exact per-model free-tier RPM/RPD numbers - it now points to an authenticated
    // AI Studio dashboard (aistudio.google.com/rate-limit) that only the real key's owner can
    // read. Cross-referencing several current third-party trackers of that same dashboard for
    // the Flash-Lite class of model this app uses (GeminiClient.cs) converges on roughly
    // 15 requests/minute and 1,000 requests/day - consistent with the prior gemini-2.5-flash-lite
    // generation's own last-published numbers. Since the exact current number for this app's own
    // key can't be confirmed without that key, these limits are sized as a conservative fraction
    // of the LOWEST figure found, not the average - correct even if the real dashboard number
    // turns out less generous than what's publicly reported:
    private const int AiAssistantGlobalPermitLimitPerMinute = 5; // <= the low end of every reported RPM figure (5-15/min)
    private const int AiAssistantGlobalPermitLimitPerDay = 150; // 15% of the commonly-reported 1,000/day free-tier RPD

    // Per-IP layer: not what protects the shared budget above (the global limiters do that
    // regardless of caller identity) - this exists so ONE enthusiastic or scripted visitor can't
    // burn through a disproportionate slice of that shared daily budget alone before anyone else
    // gets a turn. Generous enough for a real visitor to rephrase their preferences a few times.
    private const int AiAssistantPerIpPermitLimit = 5;
    private static readonly TimeSpan AiAssistantPerIpWindow = TimeSpan.FromMinutes(10);

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Applies to every request (health checks opt out explicitly via
            // .DisableRateLimiting() in Program.cs) regardless of endpoint-specific policy -
            // ASP.NET Core combines a GlobalLimiter with any per-endpoint RequireRateLimiting
            // policy with AND semantics, so both must grant a lease.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetConcurrencyLimiter("db-pool", _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = DbPoolConcurrencyPermitLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = DbPoolConcurrencyQueueLimit,
                }));

            options.AddPolicy(RateLimitPolicies.Cheap, ClientIpPartition(CheapPermitLimitPerMinute));
            options.AddPolicy(RateLimitPolicies.Standard, ClientIpPartition(StandardPermitLimitPerMinute));
            options.AddPolicy(RateLimitPolicies.Compare, ClientIpPartition(ComparePermitLimitPerMinute));
            options.AddPolicy(RateLimitPolicies.AiAssistant, AiAssistantPartition());

            options.OnRejected = async (context, ct) =>
            {
                // Fixed/sliding window leases carry a real Retry-After; the db-pool concurrency
                // limiter doesn't (there's no fixed clock to a freed slot), so fall back to a
                // short, cheap-to-retry hint - slots turn over in well under a second normally.
                var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? (int)Math.Ceiling(retryAfter.TotalSeconds)
                    : 1;

                context.HttpContext.Response.Headers["Retry-After"] =
                    retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "Too many requests. Please retry later.", retryAfterSeconds },
                    cancellationToken: ct);
            };
        });

        return services;
    }

    private static Func<HttpContext, RateLimitPartition<string>> ClientIpPartition(int permitLimitPerMinute) =>
        httpContext =>
        {
            // By the time this runs, UseForwardedHeaders (Program.cs) has already rewritten
            // Connection.RemoteIpAddress from X-Forwarded-For for Render's trusted edge-proxy
            // hop - the same trust boundary the app already relies on for HTTPS redirection -
            // so this is the real client IP, not a client-spoofable header read directly. A
            // request where it still can't be determined shares one fallback bucket rather than
            // bypassing the limit entirely.
            var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permitLimitPerMinute,
                Window = Window,
                SegmentsPerWindow = 4,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            });
        };

    // Combines a per-IP limiter with two truly GLOBAL limiters under one named policy. ASP.NET
    // Core's rate limiter middleware only lets one named policy apply to a given endpoint
    // (RequireRateLimiting replaces rather than stacks), so all three dimensions have to be
    // expressed as a single chained RateLimiter per partition key - see
    // learn.microsoft.com/aspnet/core/performance/rate-limit, "Chain limiters in a named policy".
    private static Func<HttpContext, RateLimitPartition<string>> AiAssistantPartition()
    {
        // Constructed ONCE here (this method itself only runs once, at startup, to build the
        // Func below) rather than inside the per-key factory beneath - every per-IP chain closes
        // over these same two instances, so acquiring a permit from any caller's chain decrements
        // the SAME shared counters. That's what makes this a real cross-visitor global budget
        // instead of yet another per-IP one.
        var globalPerMinute = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = AiAssistantGlobalPermitLimitPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        });
        var globalPerDay = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = AiAssistantGlobalPermitLimitPerDay,
            Window = TimeSpan.FromHours(24),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        });

        return httpContext =>
        {
            var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // Order matters here. The per-IP limiter runs FIRST in the chain: fixed/sliding
            // window limiters don't refund a permit they already granted if a LATER limiter in
            // the chain rejects the request (see the "Chain limiters" doc above), so if the
            // global limiters ran first, one IP looping past its own per-IP cap would still burn
            // a permit from the shared global budget on every one of those excess requests
            // before finally being rejected - exactly the "one abuser starves everyone else"
            // failure this whole policy exists to prevent. Checking per-IP first means an
            // over-eager caller is turned away before ever touching the shared budget.
            return RateLimitPartition.Get(key, _ => RateLimiter.CreateChained(
                new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = AiAssistantPerIpPermitLimit,
                    Window = AiAssistantPerIpWindow,
                    SegmentsPerWindow = 5,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                }),
                globalPerMinute,
                globalPerDay));
        };
    }
}
