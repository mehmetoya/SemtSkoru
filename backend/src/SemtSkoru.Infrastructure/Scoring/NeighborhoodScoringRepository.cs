using Microsoft.EntityFrameworkCore;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Infrastructure.Scoring;

public sealed class NeighborhoodScoringRepository(AppDbContext db) : INeighborhoodScoringRepository
{
    public Task<bool> NeighborhoodExistsAsync(string neighborhoodId, CancellationToken ct) =>
        db.Neighborhoods.AnyAsync(n => n.Id == neighborhoodId, ct);

    public async Task<AirQualityReading?> GetLatestAirQualityAsync(string neighborhoodId, CancellationToken ct) =>
        await db.AirQualityReadings.FindAsync([neighborhoodId], ct);

    public async Task<GreenSpaceReading?> GetLatestGreenSpaceAsync(string neighborhoodId, CancellationToken ct) =>
        await db.GreenSpaceReadings.FindAsync([neighborhoodId], ct);

    public async Task<TrafficReading?> GetLatestTrafficAsync(string neighborhoodId, CancellationToken ct) =>
        await db.TrafficReadings.FindAsync([neighborhoodId], ct);

    public async Task<IReadOnlyList<string>> GetAllNeighborhoodIdsAsync(CancellationToken ct) =>
        await db.Neighborhoods.Select(n => n.Id).ToListAsync(ct);

    public async Task<IReadOnlyDictionary<string, AirQualityReading>> GetAllAirQualityAsync(CancellationToken ct) =>
        await db.AirQualityReadings.ToDictionaryAsync(r => r.NeighborhoodId, ct);

    public async Task<IReadOnlyDictionary<string, GreenSpaceReading>> GetAllGreenSpaceAsync(CancellationToken ct) =>
        await db.GreenSpaceReadings.ToDictionaryAsync(r => r.NeighborhoodId, ct);

    public async Task<IReadOnlyDictionary<string, TrafficReading>> GetAllTrafficAsync(CancellationToken ct) =>
        await db.TrafficReadings.ToDictionaryAsync(r => r.NeighborhoodId, ct);
}
