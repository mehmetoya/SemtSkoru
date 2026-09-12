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

        return BuildResult(neighborhoodId, airQuality, greenSpace, traffic, now);
    }

    public async Task<IReadOnlyDictionary<string, NeighborhoodScoreResult>> GetAllScoresAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        var ids = await repository.GetAllNeighborhoodIdsAsync(ct);
        var airQuality = await repository.GetAllAirQualityAsync(ct);
        var greenSpace = await repository.GetAllGreenSpaceAsync(ct);
        var traffic = await repository.GetAllTrafficAsync(ct);

        var results = new Dictionary<string, NeighborhoodScoreResult>(ids.Count);
        foreach (var id in ids)
        {
            airQuality.TryGetValue(id, out var air);
            greenSpace.TryGetValue(id, out var green);
            traffic.TryGetValue(id, out var traf);
            results[id] = BuildResult(id, air, green, traf, now);
        }

        return results;
    }

    private static NeighborhoodScoreResult BuildResult(
        string neighborhoodId,
        AirQualityReading? airQuality,
        GreenSpaceReading? greenSpace,
        TrafficReading? traffic,
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

        var overall = Overall(airQualityScore, greenSpaceScore, transportationScore);

        return new NeighborhoodScoreResult(neighborhoodId, airQualityScore, greenSpaceScore, transportationScore, overall);
    }

    // Equal-weight average of whatever dimensions have data. SPEC.md explicitly puts
    // profile-based weighting (renter vs. remote worker vs. family) out of scope for v1.
    private static Score? Overall(params DimensionScore[] dimensions)
    {
        var values = dimensions.Where(d => d.HasData).Select(d => d.Value!.Value.Value).ToArray();
        return values.Length == 0 ? null : new Score((int)Math.Round(values.Average()));
    }
}
