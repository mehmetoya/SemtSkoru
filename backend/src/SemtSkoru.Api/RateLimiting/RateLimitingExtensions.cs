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
}
