using SemtSkoru.Domain;

namespace SemtSkoru.Application.Scoring;

public sealed class NeighborhoodScoringService(
    INeighborhoodScoringRepository repository,
    TimeProvider timeProvider) : INeighborhoodScoringService
{
    public async Task<NeighborhoodScoreResult?> GetScoreAsync(string neighborhoodId, CancellationToken ct)
    {
        if (!await repository.NeighborhoodExistsAsync(neighborhoodId, ct))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();

        var airQuality = await repository.GetLatestAirQualityAsync(neighborhoodId, ct);
        var greenSpace = await repository.GetLatestGreenSpaceAsync(neighborhoodId, ct);
        var traffic = await repository.GetLatestTrafficAsync(neighborhoodId, ct);
        var parking = await repository.GetLatestParkingAsync(neighborhoodId, ct);
        var healthAccess = await repository.GetLatestHealthAccessAsync(neighborhoodId, ct);
        var transitAccess = await repository.GetLatestTransitAccessAsync(neighborhoodId, ct);

        return BuildResult(neighborhoodId, airQuality, greenSpace, traffic, parking, healthAccess, transitAccess, now);
    }

    public async Task<IReadOnlyDictionary<string, NeighborhoodScoreResult>> GetAllScoresAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        var ids = await repository.GetAllNeighborhoodIdsAsync(ct);
        var airQuality = await repository.GetAllAirQualityAsync(ct);
        var greenSpace = await repository.GetAllGreenSpaceAsync(ct);
        var traffic = await repository.GetAllTrafficAsync(ct);
        var parking = await repository.GetAllParkingAsync(ct);
        var healthAccess = await repository.GetAllHealthAccessAsync(ct);
        var transitAccess = await repository.GetAllTransitAccessAsync(ct);

        var results = new Dictionary<string, NeighborhoodScoreResult>(ids.Count);
        foreach (var id in ids)
        {
            airQuality.TryGetValue(id, out var air);
            greenSpace.TryGetValue(id, out var green);
            traffic.TryGetValue(id, out var traf);
            parking.TryGetValue(id, out var park);
            healthAccess.TryGetValue(id, out var health);
            transitAccess.TryGetValue(id, out var transit);
            results[id] = BuildResult(id, air, green, traf, park, health, transit, now);
        }

        return results;
    }

    private static NeighborhoodScoreResult BuildResult(
        string neighborhoodId,
        AirQualityReading? airQuality,
        GreenSpaceReading? greenSpace,
        TrafficReading? traffic,
        ParkingReading? parking,
        HealthAccessReading? healthAccess,
        TransitAccessReading? transitAccess,
        DateTimeOffset now)
    {
        var airQualityScore = airQuality is null
            ? DimensionScore.NoData
            : new DimensionScore(
                DimensionScoring.ScoreAirQuality(airQuality.AqiIndex),
                airQuality.Source.GetFreshness(now),
                airQuality.Source.SourceName,
                airQuality.Source.PublishedAt);

        var greenSpaceScore = greenSpace is null
            ? DimensionScore.NoData
            : new DimensionScore(
                DimensionScoring.ScoreGreenSpace(greenSpace.NearestParkDistanceMeters),
                greenSpace.Source.GetFreshness(now),
                greenSpace.Source.SourceName,
                greenSpace.Source.PublishedAt);

        var transportationScore = traffic is null
            ? DimensionScore.NoData
            : new DimensionScore(
                DimensionScoring.ScoreTransportation(traffic.AverageSpeedKmh),
                traffic.Source.GetFreshness(now),
                traffic.Source.SourceName,
                traffic.Source.PublishedAt);

        var parkingScore = parking is null
            ? DimensionScore.NoData
            : new DimensionScore(
                DimensionScoring.ScoreParking(parking.AverageAvailabilityRatio),
                parking.Source.GetFreshness(now),
                parking.Source.SourceName,
                parking.Source.PublishedAt);

        var healthAccessScore = healthAccess is null
            ? DimensionScore.NoData
            : new DimensionScore(
                DimensionScoring.ScoreHealthAccess(healthAccess.WeightedHealthIndex),
                healthAccess.Source.GetFreshness(now),
                healthAccess.Source.SourceName,
                healthAccess.Source.PublishedAt);

        var transitAccessScore = transitAccess is null
            ? DimensionScore.NoData
            : new DimensionScore(
                DimensionScoring.ScoreTransitAccess(transitAccess.StopDensityPerKm2),
                transitAccess.Source.GetFreshness(now),
                transitAccess.Source.SourceName,
                transitAccess.Source.PublishedAt);

        var overall = Overall(
            airQualityScore, greenSpaceScore, transportationScore, parkingScore, healthAccessScore, transitAccessScore);

        return new NeighborhoodScoreResult(
            neighborhoodId, airQualityScore, greenSpaceScore, transportationScore, parkingScore, healthAccessScore,
            transitAccessScore, overall);
    }

    // Equal-weight average of whatever dimensions have data. SPEC.md explicitly puts
    // profile-based weighting (renter vs. remote worker vs. family) out of scope for v1.
    private static Score? Overall(params DimensionScore[] dimensions)
    {
        var values = dimensions.Where(d => d.HasData).Select(d => d.Value!.Value.Value).ToArray();
        return values.Length == 0 ? null : new Score((int)Math.Round(values.Average()));
    }
}
