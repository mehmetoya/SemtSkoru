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
}
