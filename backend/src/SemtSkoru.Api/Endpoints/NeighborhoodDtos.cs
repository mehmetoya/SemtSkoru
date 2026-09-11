using SemtSkoru.Application.Scoring;

namespace SemtSkoru.Api.Endpoints;

public sealed record NeighborhoodSummaryDto(string Id, string Name);

public sealed record DimensionScoreDto(int? Score, string? Freshness)
{
    public static DimensionScoreDto From(DimensionScore dimension) =>
        new(dimension.Value?.Value, dimension.Freshness?.ToString());
}

public sealed record NeighborhoodComparisonDto(NeighborhoodScoreDto A, NeighborhoodScoreDto B);

public sealed record NeighborhoodScoreDto(
    string NeighborhoodId,
    DimensionScoreDto AirQuality,
    DimensionScoreDto GreenSpace,
    DimensionScoreDto Transportation,
    int? Overall,
    bool IsComplete)
{
    public static NeighborhoodScoreDto From(NeighborhoodScoreResult result) => new(
        result.NeighborhoodId,
        DimensionScoreDto.From(result.AirQuality),
        DimensionScoreDto.From(result.GreenSpace),
        DimensionScoreDto.From(result.Transportation),
        result.Overall?.Value,
        result.IsComplete);
}
