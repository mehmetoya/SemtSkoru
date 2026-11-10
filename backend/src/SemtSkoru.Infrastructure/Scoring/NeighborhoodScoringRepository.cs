using Microsoft.EntityFrameworkCore;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Infrastructure.Scoring;

public sealed class NeighborhoodScoringRepository(AppDbContext db) : INeighborhoodScoringRepository
{
    public async Task<NeighborhoodReadings?> GetReadingsAsync(string neighborhoodId, CancellationToken ct)
    {
        var row = await BuildQuery(db.Neighborhoods.Where(n => n.Id == neighborhoodId)).SingleOrDefaultAsync(ct);
        return row.Readings;
    }

    public async Task<IReadOnlyDictionary<string, NeighborhoodReadings>> GetAllReadingsAsync(CancellationToken ct)
    {
        var rows = await BuildQuery(db.Neighborhoods).ToListAsync(ct);
        return rows.ToDictionary(r => r.NeighborhoodId, r => r.Readings);
    }

    // A single LEFT JOIN from Neighborhoods across all six 1:1 reading tables (each keyed by
    // NeighborhoodId, at most one row per neighborhood) - one round trip regardless of whether
    // this scores one neighborhood or all 39. The previous version did 7 sequential round trips
    // per call (one per dimension plus an existence check); at the ~70ms/round-trip cross-cloud
    // Render->Supabase latency this codebase has already measured elsewhere (see
    // RateLimiting/RateLimitingExtensions.cs), /api/neighborhoods/compare paid that twice - 14
    // round trips total - just calling GetScoreAsync for both districts.
    private IQueryable<(string NeighborhoodId, NeighborhoodReadings Readings)> BuildQuery(
        IQueryable<Neighborhood> neighborhoods) =>
        from n in neighborhoods
        join aq in db.AirQualityReadings on n.Id equals aq.NeighborhoodId into aqg
        from aq in aqg.DefaultIfEmpty()
        join gs in db.GreenSpaceReadings on n.Id equals gs.NeighborhoodId into gsg
        from gs in gsg.DefaultIfEmpty()
        join tr in db.TrafficReadings on n.Id equals tr.NeighborhoodId into trg
        from tr in trg.DefaultIfEmpty()
        join pk in db.ParkingReadings on n.Id equals pk.NeighborhoodId into pkg
        from pk in pkg.DefaultIfEmpty()
        join ha in db.HealthAccessReadings on n.Id equals ha.NeighborhoodId into hag
        from ha in hag.DefaultIfEmpty()
        join ta in db.TransitAccessReadings on n.Id equals ta.NeighborhoodId into tag
        from ta in tag.DefaultIfEmpty()
        select new ValueTuple<string, NeighborhoodReadings>(n.Id, new NeighborhoodReadings(aq, gs, tr, pk, ha, ta));
}
