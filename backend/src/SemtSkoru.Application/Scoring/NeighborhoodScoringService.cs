using SemtSkoru.Domain;

namespace SemtSkoru.Application.Scoring;

public sealed class NeighborhoodScoringService(
    INeighborhoodScoringRepository repository,
    TimeProvider timeProvider) : INeighborhoodScoringService
{
    public async Task<NeighborhoodScoreResult?> GetScoreAsync(string neighborhoodId, CancellationToken ct)
    {
        var readings = await repository.GetReadingsAsync(neighborhoodId, ct);
        if (readings is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        return BuildResult(
            neighborhoodId, readings.AirQuality, readings.GreenSpace, readings.Traffic, readings.Parking,
            readings.HealthAccess, readings.TransitAccess, now);
    }

    public async Task<IReadOnlyDictionary<string, NeighborhoodScoreResult>> GetAllScoresAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var allReadings = await repository.GetAllReadingsAsync(ct);

        var results = new Dictionary<string, NeighborhoodScoreResult>(allReadings.Count);
        foreach (var (id, readings) in allReadings)
        {
            results[id] = BuildResult(
                id, readings.AirQuality, readings.GreenSpace, readings.Traffic, readings.Parking,
                readings.HealthAccess, readings.TransitAccess, now);
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
