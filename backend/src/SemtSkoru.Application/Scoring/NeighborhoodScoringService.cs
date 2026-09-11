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
        var airQualityScore = airQuality is null
            ? DimensionScore.NoData
            : new DimensionScore(
                DimensionScoring.ScoreAirQuality(airQuality.AqiIndex),
                airQuality.Source.GetFreshness(now),
                airQuality.Source.SourceName,
                airQuality.Source.PublishedAt);

        var greenSpace = await repository.GetLatestGreenSpaceAsync(neighborhoodId, ct);
        var greenSpaceScore = greenSpace is null
            ? DimensionScore.NoData
            : new DimensionScore(
                DimensionScoring.ScoreGreenSpace(greenSpace.NearestParkDistanceMeters),
                greenSpace.Source.GetFreshness(now),
                greenSpace.Source.SourceName,
                greenSpace.Source.PublishedAt);

        var traffic = await repository.GetLatestTrafficAsync(neighborhoodId, ct);
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
