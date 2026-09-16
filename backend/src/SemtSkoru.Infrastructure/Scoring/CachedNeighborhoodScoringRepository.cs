using Microsoft.Extensions.Caching.Memory;
using SemtSkoru.Application.Scoring;

namespace SemtSkoru.Infrastructure.Scoring;

/// <summary>
/// Decorates an <see cref="INeighborhoodScoringRepository"/> (in practice always the real
/// <see cref="NeighborhoodScoringRepository"/> - see its DI registration in Program.cs) with a
/// short in-process cache. Wraps the interface, not the concrete type, so this class is unit
/// testable against a fake repository without a database.
/// </summary>
/// <remarks>
/// The API runs as a single Render free-tier instance with no horizontal scaling (render.yaml,
/// <c>plan: free</c>), so there is no cross-instance cache-coherency problem to solve here - an
/// <see cref="IMemoryCache"/> is strictly simpler and faster than a distributed cache
/// (Redis/Memcached) would be, which would only add a network hop and another free-tier
/// dependency for zero benefit at this scale.
///
/// Both <see cref="GetReadingsAsync"/> and <see cref="GetAllReadingsAsync"/> share a single
/// cached dictionary keyed off the underlying query's own shape (BuildQuery in
/// NeighborhoodScoringRepository is one LEFT JOIN whether or not it's filtered to one
/// neighborhood - fetching all 39 costs about the same as fetching one, dominated by the
/// cross-cloud Render-&gt;Supabase round trip, not row count - see that class's remarks) - so a
/// single district-page view warms the cache for every other district for free.
///
/// TTL matches the frontend's own ISR revalidate window (5 min - see
/// web/app/[locale]/page.tsx / mahalle/[id]/page.tsx) so both layers agree on what "fresh
/// enough" means, and sits comfortably inside the fastest-changing dimension's real update
/// cadence (air quality and parking ingest daily - see the cadence comments next to their
/// Hangfire registrations in Program.cs) - cached data is never staler than a real visitor
/// would notice or the source data itself changes.
///
/// Explicit double-checked locking around the cache miss, not a bare
/// <c>IMemoryCache.GetOrCreateAsync</c> call: that extension method does not de-duplicate
/// concurrent misses for the same key on its own, so without this a burst of concurrent
/// requests arriving right as the entry expires (or right after a cold start - see
/// keep-warm.yml) would each independently re-hit the database. One process-wide semaphore is
/// enough because this cache has exactly one key - there is nothing to shard a lock over.
/// </remarks>
public sealed class CachedNeighborhoodScoringRepository(
    INeighborhoodScoringRepository inner,
    IMemoryCache cache) : INeighborhoodScoringRepository
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private const string AllReadingsCacheKey = "scoring:all-readings";

    // static, not an instance field: this class is registered Scoped (it wraps a Scoped
    // AppDbContext-backed repository), so a NEW CachedNeighborhoodScoringRepository is
    // constructed per request - an instance-level lock would give each concurrent request its
    // own uncontended semaphore and provide no stampede protection at all. IMemoryCache itself
    // is the process-wide singleton actually being guarded here, so the lock guarding access to
    // it must be process-wide too.
    private static readonly SemaphoreSlim PopulateLock = new(1, 1);

    public async Task<NeighborhoodReadings?> GetReadingsAsync(string neighborhoodId, CancellationToken ct)
    {
        var all = await GetAllReadingsAsync(ct);
        return all.GetValueOrDefault(neighborhoodId);
    }

    public async Task<IReadOnlyDictionary<string, NeighborhoodReadings>> GetAllReadingsAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(AllReadingsCacheKey, out IReadOnlyDictionary<string, NeighborhoodReadings>? cached))
        {
            return cached!;
        }

        await PopulateLock.WaitAsync(ct);
        try
        {
            // Re-check: another caller may have already populated it while we waited.
            if (cache.TryGetValue(AllReadingsCacheKey, out cached))
            {
                return cached!;
            }

            var readings = await inner.GetAllReadingsAsync(ct);
            cache.Set(AllReadingsCacheKey, readings, Ttl);
            return readings;
        }
        finally
        {
            PopulateLock.Release();
        }
    }
}
