using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SemtSkoru.Api.RateLimiting;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Application.Summaries;
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
            INeighborhoodScoringService scoringService,
            IDistrictSummaryRepository summaryRepository,
            CancellationToken ct) =>
        {
            SetCacheHeader(context);
            var result = await scoringService.GetScoreAsync(id, ct);
            if (result is null)
            {
                return Results.NotFound();
            }

            // A cheap read against whatever DistrictSummaryGenerationJob already wrote (see its
            // remarks) - never a live Gemini call on this request path.
            var summary = await summaryRepository.GetAsync(id, ct);
            var summaryDto = summary is null ? null : new DistrictSummaryDto(summary.SummaryText, summary.GeneratedAt);

            return Results.Ok(NeighborhoodScoreDto.From(result, summaryDto));
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
    }
}
