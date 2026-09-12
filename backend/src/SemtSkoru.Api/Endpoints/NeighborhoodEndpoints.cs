using Microsoft.EntityFrameworkCore;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Api.Endpoints;

public static class NeighborhoodEndpoints
{
    public static void MapNeighborhoodEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/neighborhoods", async (AppDbContext db, INeighborhoodScoringService scoringService, CancellationToken ct) =>
        {
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
        });

        app.MapGet("/api/neighborhoods/{id}/score", async (string id, INeighborhoodScoringService scoringService, CancellationToken ct) =>
        {
            var result = await scoringService.GetScoreAsync(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(NeighborhoodScoreDto.From(result));
        });

        app.MapGet("/api/neighborhoods/compare", async (string? a, string? b, INeighborhoodScoringService scoringService, CancellationToken ct) =>
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

            return Results.Ok(new NeighborhoodComparisonDto(NeighborhoodScoreDto.From(scoreA!), NeighborhoodScoreDto.From(scoreB!)));
        });
    }
}
