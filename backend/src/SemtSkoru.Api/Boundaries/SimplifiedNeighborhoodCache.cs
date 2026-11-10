using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NetTopologySuite.Geometries;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Api.Boundaries;

public sealed record SimplifiedNeighborhood(string Id, string Name, Geometry Boundary);

/// <summary>
/// Every district's id, name and display-simplified boundary (see
/// <see cref="BoundarySimplifier"/>), ordered by name, computed once and held in the
/// process-wide <see cref="IMemoryCache"/>. Backs GET /api/neighborhoods.
/// </summary>
/// <remarks>
/// Caches the whole projection, not just the simplified geometry, because that's what makes the
/// cache actually pay off on the request path: a hit needs no Neighborhoods query at all, so the
/// ~1 MB of full-resolution boundary data is no longer dragged across the Render-&gt;Supabase
/// cross-cloud connection on every list request just to be thrown away by the simplifier.
/// Simplifying all 39 districts takes tens of milliseconds - cheap, but pointless to repeat for
/// data that never changes between requests.
///
/// Ids, names and boundaries are only ever written by EF migrations (seed data - no ingestion
/// job writes to Neighborhoods), so the cached value is good for the life of the process. The
/// TTL is just a backstop: migrations are applied by hand with <c>dotnet ef database update</c>
/// (README), not at startup, so one could land on the live database without the app restarting -
/// an hour bounds how long the list could keep serving the old boundaries after that. Much longer
/// than CachedNeighborhoodScoringRepository's 5 minutes because scores change daily and this
/// data effectively never does.
///
/// Same IMemoryCache-over-distributed-cache reasoning and same double-checked-locking stampede
/// guard as CachedNeighborhoodScoringRepository (see its remarks) - including a static lock,
/// since this class is Scoped (it takes the Scoped AppDbContext) and so is constructed per
/// request.
/// </remarks>
public sealed class SimplifiedNeighborhoodCache(AppDbContext db, IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);
    private const string CacheKey = "neighborhoods:simplified-boundaries";

    private static readonly SemaphoreSlim PopulateLock = new(1, 1);

    public async Task<IReadOnlyList<SimplifiedNeighborhood>> GetAllAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<SimplifiedNeighborhood>? cached))
        {
            return cached!;
        }

        await PopulateLock.WaitAsync(ct);
        try
        {
            // Re-check: another caller may have already populated it while we waited.
            if (cache.TryGetValue(CacheKey, out cached))
            {
                return cached!;
            }

            // AsNoTracking: the full-resolution entities are only read here to derive a
            // separate simplified copy, never modified or saved - and BoundarySimplifier never
            // mutates its input either, so nothing here can write a simplified geometry back.
            var neighborhoods = await db.Neighborhoods
                .AsNoTracking()
                .OrderBy(n => n.Name)
                .Select(n => new { n.Id, n.Name, n.Boundary })
                .ToListAsync(ct);

            IReadOnlyList<SimplifiedNeighborhood> simplified = neighborhoods
                .Select(n => new SimplifiedNeighborhood(n.Id, n.Name, BoundarySimplifier.Simplify(n.Boundary)))
                .ToList();

            cache.Set(CacheKey, simplified, Ttl);
            return simplified;
        }
        finally
        {
            PopulateLock.Release();
        }
    }
}
