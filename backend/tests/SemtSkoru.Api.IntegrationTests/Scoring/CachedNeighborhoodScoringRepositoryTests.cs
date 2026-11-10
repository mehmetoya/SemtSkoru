using Microsoft.Extensions.Caching.Memory;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.Scoring;

namespace SemtSkoru.Api.IntegrationTests.Scoring;

// Pure caching-logic tests - no database needed (CountingRepository below stands in for the
// real NeighborhoodScoringRepository), so these run as fast as a unit test despite living in
// the integration-test project (the natural home for anything referencing Infrastructure - see
// SemtSkoru.Application.Tests, which deliberately can't reference Infrastructure at all).
public class CachedNeighborhoodScoringRepositoryTests
{
    private static readonly NeighborhoodReadings SomeReadings = new(
        AirQuality: null, GreenSpace: null, Traffic: null, Parking: null, HealthAccess: null, TransitAccess: null);

    [Fact]
    public async Task GetAllReadingsAsync_only_hits_the_inner_repository_once_within_the_cache_window()
    {
        var inner = new CountingRepository();
        var sut = new CachedNeighborhoodScoringRepository(inner, new MemoryCache(new MemoryCacheOptions()));

        await sut.GetAllReadingsAsync(CancellationToken.None);
        await sut.GetAllReadingsAsync(CancellationToken.None);
        await sut.GetAllReadingsAsync(CancellationToken.None);

        Assert.Equal(1, inner.AllReadingsCallCount);
    }

    [Fact]
    public async Task GetReadingsAsync_is_served_from_the_same_cached_dictionary_as_GetAllReadingsAsync()
    {
        var inner = new CountingRepository { Readings = { ["kadikoy"] = SomeReadings } };
        var sut = new CachedNeighborhoodScoringRepository(inner, new MemoryCache(new MemoryCacheOptions()));

        // Warms the shared cache entry.
        await sut.GetAllReadingsAsync(CancellationToken.None);
        var result = await sut.GetReadingsAsync("kadikoy", CancellationToken.None);

        Assert.Same(SomeReadings, result);
        Assert.Equal(1, inner.AllReadingsCallCount);
    }

    [Fact]
    public async Task GetReadingsAsync_returns_null_for_an_unknown_id_without_extra_inner_calls()
    {
        var inner = new CountingRepository();
        var sut = new CachedNeighborhoodScoringRepository(inner, new MemoryCache(new MemoryCacheOptions()));

        var result = await sut.GetReadingsAsync("does-not-exist", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(1, inner.AllReadingsCallCount);
    }

    [Fact]
    public async Task Concurrent_callers_on_a_cold_cache_only_trigger_one_inner_call()
    {
        // Reproduces the exact scenario the double-checked lock in GetAllReadingsAsync exists
        // for: a burst of requests arriving at once (a real traffic spike, or several requests
        // that queued up while Render's free-tier container was cold-starting) all missing an
        // empty/expired cache at the same time.
        var inner = new CountingRepository { Delay = TimeSpan.FromMilliseconds(50) };
        var sut = new CachedNeighborhoodScoringRepository(inner, new MemoryCache(new MemoryCacheOptions()));

        var callers = Enumerable.Range(0, 20).Select(_ => sut.GetAllReadingsAsync(CancellationToken.None));
        await Task.WhenAll(callers);

        Assert.Equal(1, inner.AllReadingsCallCount);
    }

    [Fact]
    public async Task Cache_entry_expires_and_is_repopulated_from_the_inner_repository()
    {
        var inner = new CountingRepository();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new CachedNeighborhoodScoringRepository(inner, cache);

        await sut.GetAllReadingsAsync(CancellationToken.None);
        Assert.Equal(1, inner.AllReadingsCallCount);

        cache.Compact(1.0); // evicts everything, simulating TTL expiry without a real 5-minute wait
        await sut.GetAllReadingsAsync(CancellationToken.None);

        Assert.Equal(2, inner.AllReadingsCallCount);
    }

    private sealed class CountingRepository : INeighborhoodScoringRepository
    {
        public Dictionary<string, NeighborhoodReadings> Readings { get; } = [];
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;
        private int _allReadingsCallCount;
        public int AllReadingsCallCount => _allReadingsCallCount;

        public async Task<NeighborhoodReadings?> GetReadingsAsync(string neighborhoodId, CancellationToken ct)
        {
            var all = await GetAllReadingsAsync(ct);
            return all.GetValueOrDefault(neighborhoodId);
        }

        public async Task<IReadOnlyDictionary<string, NeighborhoodReadings>> GetAllReadingsAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _allReadingsCallCount);
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, ct);
            }

            return Readings;
        }
    }
}
