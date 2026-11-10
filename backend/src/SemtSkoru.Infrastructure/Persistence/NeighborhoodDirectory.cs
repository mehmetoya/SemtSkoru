using Microsoft.EntityFrameworkCore;
using SemtSkoru.Application.Assistant;

namespace SemtSkoru.Infrastructure.Persistence;

/// <summary>
/// Id -> display name for every neighborhood - one cheap query, no boundary geometry and no
/// scoring join (same shape as the GET /api/neighborhoods/names endpoint). Kept separate from
/// NeighborhoodScoringRepository, which is scoped to raw scoring readings, not identity.
/// </summary>
public sealed class NeighborhoodDirectory(AppDbContext db) : INeighborhoodDirectory
{
    public async Task<IReadOnlyDictionary<string, string>> GetAllNamesAsync(CancellationToken ct) =>
        await db.Neighborhoods.ToDictionaryAsync(n => n.Id, n => n.Name, ct);
}
