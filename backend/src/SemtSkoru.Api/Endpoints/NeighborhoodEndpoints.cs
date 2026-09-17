using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SemtSkoru.Api.RateLimiting;
using SemtSkoru.Application.Comparisons;
using SemtSkoru.Application.Localization;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Application.Search;
using SemtSkoru.Application.Summaries;
using SemtSkoru.Application.Trends;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Api.Endpoints;

public static class NeighborhoodEndpoints
{
    // Every GET here is safe to cache client-side too, not just behind
    // CachedNeighborhoodScoringRepository server-side: none of this data changes faster than
    // daily (see that class's remarks), so a browser (or any intermediary) serving its own
    // 5-minute-old copy is never meaningfully stale. Matches this repository cache's own TTL so
    // there's one number to reason about, not two slightly-different ones.
    private static readonly TimeSpan ClientCacheMaxAge = TimeSpan.FromMinutes(5);

    private static void SetCacheHeader(HttpContext context) =>
        context.Response.Headers.CacheControl = $"public, max-age={(int)ClientCacheMaxAge.TotalSeconds}";

    public static void MapNeighborhoodEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/neighborhoods", async (HttpContext context, AppDbContext db, INeighborhoodScoringService scoringService, CancellationToken ct) =>
        {
            SetCacheHeader(context);
            var neighborhoods = await db.Neighborhoods
                .OrderBy(n => n.Name)
                .Select(n => new { n.Id, n.Name, n.Boundary })
                .ToListAsync(ct);

            // Bulk-scored (a handful of queries total, not one round trip per neighborhood) -
            // live-verified that 39 sequential GetScoreAsync calls took ~21s end to end against
            // the real Render->Supabase cross-cloud connection, which the frontend's 5-minute
            // ISR cache doesn't fully hide (the page generating that cache entry still pays it).
            var scores = await scoringService.GetAllScoresAsync(ct);
            var result = neighborhoods
                .Select(n => new NeighborhoodSummaryDto(
                    n.Id,
                    n.Name,
                    n.Boundary,
                    scores.GetValueOrDefault(n.Id)?.Overall?.Value))
                .ToList();

            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitPolicies.Standard);

        // Cheap by design: one query, no boundary geometry, no scoring join at all. Exists so
        // callers that only need a district's display name (the downloadable score-card image
        // routes in web/app/mahalle/[id]/kart and web/app/karsilastir/kart) don't have to pull
        // the full scored-and-bounded /api/neighborhoods list just to read one `name`.
        app.MapGet("/api/neighborhoods/names", async (HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            SetCacheHeader(context);
            var names = await db.Neighborhoods
                .OrderBy(n => n.Name)
                .Select(n => new NeighborhoodNameDto(n.Id, n.Name))
                .ToListAsync(ct);

            return Results.Ok(names);
        }).RequireRateLimiting(RateLimitPolicies.Cheap);

        app.MapGet("/api/neighborhoods/{id}/score", async (
            HttpContext context,
            string id,
            string? locale,
            INeighborhoodScoringService scoringService,
            IDistrictSummaryRepository summaryRepository,
            IDistrictTrendRepository trendRepository,
            CancellationToken ct) =>
        {
            SetCacheHeader(context);
            var result = await scoringService.GetScoreAsync(id, ct);
            if (result is null)
            {
                return Results.NotFound();
            }

            // Both are cheap reads against whatever their respective weekly Hangfire job already
            // wrote (DistrictSummaryGenerationJob / ScoreSnapshotJob) - never a live Gemini call
            // on this request path. Locale ("tr"/"en", defaulting to "tr" for a missing/unknown
            // value via AiLocale.NormalizeOrDefault) scopes both reads: a locale the weekly job
            // hasn't caught up on yet for this district honestly returns null here, exactly like
            // Gemini not being configured at all does - see DistrictSummaryRepository's remarks.
            var normalizedLocale = AiLocale.NormalizeOrDefault(locale);
            var summary = await summaryRepository.GetAsync(id, normalizedLocale, ct);
            var summaryDto = summary is null ? null : new DistrictSummaryDto(summary.SummaryText, summary.GeneratedAt);

            var trend = await trendRepository.GetAsync(id, normalizedLocale, ct);
            var trendDto = trend is null ? null : new DistrictTrendDto(trend.SummaryText, trend.GeneratedAt);

            return Results.Ok(NeighborhoodScoreDto.From(result, summaryDto, trendDto));
        }).RequireRateLimiting(RateLimitPolicies.Standard);

        app.MapGet("/api/neighborhoods/compare", async (HttpContext context, string? a, string? b, INeighborhoodScoringService scoringService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return Results.BadRequest(new { error = "Query parameters 'a' and 'b' are both required." });
            }

            SetCacheHeader(context);
            var scoreA = await scoringService.GetScoreAsync(a, ct);
            var scoreB = await scoringService.GetScoreAsync(b, ct);

            var unknownIds = new[] { (id: a, score: scoreA), (id: b, score: scoreB) }
                .Where(x => x.score is null)
                .Select(x => x.id)
                .ToArray();

            if (unknownIds.Length > 0)
            {
                return Results.BadRequest(new { error = $"Unknown neighborhood id(s): {string.Join(", ", unknownIds)}" });
            }

            return Results.Ok(new NeighborhoodComparisonDto(NeighborhoodScoreDto.From(scoreA!), NeighborhoodScoreDto.From(scoreB!)));
        }).RequireRateLimiting(RateLimitPolicies.Compare);

        // Deliberately a SEPARATE endpoint from GET /api/neighborhoods/compare above, not a field
        // bolted onto that one's response, for two reasons: (1) that endpoint only ever does cheap
        // DB reads today and is rate-limited under RateLimitPolicies.Compare (a per-IP DB-cost
        // budget) - this one may make a live Gemini call and MUST share the same scarce,
        // app-wide, per-day Gemini budget as POST /api/asistan (RateLimitPolicies.AiAssistant -
        // see RateLimiting/RateLimitingExtensions.cs), not get its own separate allowance; (2) the
        // frontend can render the score table immediately from the (fast, DB-only) compare
        // response while this one resolves independently and possibly slowly (a never-before-seen
        // pair's first view is a live several-second Gemini call) - see
        // web/lib/hooks/useComparisonSummary.ts and web/components/ComparisonSummaryBadge.tsx.
        app.MapGet("/api/neighborhoods/compare/summary", async (
            string? a,
            string? b,
            string? locale,
            AppDbContext db,
            INeighborhoodScoringService scoringService,
            IComparisonSummaryOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return Results.BadRequest(new { error = "Query parameters 'a' and 'b' are both required." });
            }

            var scoreA = await scoringService.GetScoreAsync(a, ct);
            var scoreB = await scoringService.GetScoreAsync(b, ct);

            var unknownIds = new[] { (id: a, score: scoreA), (id: b, score: scoreB) }
                .Where(x => x.score is null)
                .Select(x => x.id)
                .ToArray();

            if (unknownIds.Length > 0)
            {
                return Results.BadRequest(new { error = $"Unknown neighborhood id(s): {string.Join(", ", unknownIds)}" });
            }

            var names = await db.Neighborhoods
                .Where(n => n.Id == a || n.Id == b)
                .ToDictionaryAsync(n => n.Id, n => n.Name, ct);

            var summary = await orchestrator.GetOrGenerateAsync(
                names.GetValueOrDefault(a, a), scoreA!, names.GetValueOrDefault(b, b), scoreB!,
                AiLocale.NormalizeOrDefault(locale), ct);

            // Deliberately just { summary } - null covers every honest reason one might not be
            // available right now (no key configured, the live call failed/timed out/was
            // rate-limited, or nothing usable could be grounded in the real scores). The frontend
            // treats this exactly like DistrictSummaryBadge treats a null district summary: render
            // nothing, never an error for the rest of the page.
            return Results.Ok(new ComparisonSummaryResponseDto(summary is null ? null : ComparisonSummaryDto.From(summary)));
        }).RequireRateLimiting(RateLimitPolicies.AiAssistant);

        // The home page's natural-language district search/filter box. GET (not POST) despite
        // calling the same shared Gemini budget as the AI features above: this is a read with no
        // side effects, `q` is a short free-text query that's RESTful as a query string (same
        // style as GET /api/neighborhoods/compare's `a`/`b` above), and a GET is trivially
        // shareable/bookmarkable/cacheable-by-intent even though this particular response isn't
        // cached server-side (see DistrictSearchService's remarks for why: free-text queries are
        // too varied for a persistent cache to meaningfully hit, exactly like POST /api/asistan).
        // RequireRateLimiting(AiAssistant) - NOT a new policy - is the whole point of reusing that
        // named policy: this endpoint draws from the SAME per-IP + global-per-minute +
        // global-per-day Gemini budget as every other AI feature, never a separate allowance.
        app.MapGet("/api/neighborhoods/search", async (
            string? q,
            IDistrictSearchService searchService,
            CancellationToken ct) =>
        {
            var outcome = await searchService.SearchAsync(q ?? "", ct);

            return outcome.Kind switch
            {
                DistrictSearchOutcomeKind.InvalidRequest => Results.BadRequest(
                    new DistrictSearchResponseDto([], [], nameof(DistrictSearchOutcomeKind.InvalidRequest),
                        "Lütfen aramak istediğiniz tercihi birkaç kelimeyle yazın.")),

                DistrictSearchOutcomeKind.NotConfigured => Results.Json(
                    new DistrictSearchResponseDto([], [], nameof(DistrictSearchOutcomeKind.NotConfigured),
                        "Akıllı arama şu anda yapılandırılmamış. Lütfen daha sonra tekrar deneyin."),
                    statusCode: StatusCodes.Status503ServiceUnavailable),

                DistrictSearchOutcomeKind.RateLimited => Results.Json(
                    new DistrictSearchResponseDto([], [], nameof(DistrictSearchOutcomeKind.RateLimited),
                        "Akıllı arama şu anda çok yoğun. Lütfen birkaç dakika sonra tekrar deneyin."),
                    statusCode: StatusCodes.Status429TooManyRequests),

                DistrictSearchOutcomeKind.Unavailable => Results.Json(
                    new DistrictSearchResponseDto([], [], nameof(DistrictSearchOutcomeKind.Unavailable),
                        "Akıllı arama şu anda yanıt veremiyor. Lütfen daha sonra tekrar deneyin."),
                    statusCode: StatusCodes.Status502BadGateway),

                DistrictSearchOutcomeKind.NoUsableCriteria => Results.Ok(
                    new DistrictSearchResponseDto([], [], nameof(DistrictSearchOutcomeKind.NoUsableCriteria),
                        "Bu sorguyu anlayamadık. Lütfen farklı bir şekilde ifade etmeyi deneyin.")),

                _ => Results.Ok(DistrictSearchResponseDto.From(outcome)),
            };
        }).RequireRateLimiting(RateLimitPolicies.AiAssistant);
    }
}
