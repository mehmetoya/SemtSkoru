using Microsoft.EntityFrameworkCore;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Api.Endpoints;

public static class NeighborhoodEndpoints
{
    public static void MapNeighborhoodEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/neighborhoods", async (AppDbContext db, CancellationToken ct) =>
        {
            var neighborhoods = await db.Neighborhoods
                .OrderBy(n => n.Name)
                .Select(n => new NeighborhoodSummaryDto(n.Id, n.Name))
                .ToListAsync(ct);

            return Results.Ok(neighborhoods);
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
