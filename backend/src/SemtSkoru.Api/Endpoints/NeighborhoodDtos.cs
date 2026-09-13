using NetTopologySuite.Geometries;
using SemtSkoru.Application.Scoring;

namespace SemtSkoru.Api.Endpoints;

public sealed record NeighborhoodSummaryDto(string Id, string Name, Geometry Boundary, int? OverallScore);

// Deliberately just id+name: callers that only need a district's display name (e.g. the
// downloadable score-card image routes) shouldn't have to pay for a full boundary fetch plus
// a 6-dimension scoring pass across all 39 districts just to resolve one label.
public sealed record NeighborhoodNameDto(string Id, string Name);

public sealed record DimensionScoreDto(int? Score, string? Freshness, string? SourceName, DateTimeOffset? PublishedAt)
{
    public static DimensionScoreDto From(DimensionScore dimension) =>
        new(dimension.Value?.Value, dimension.Freshness?.ToString(), dimension.SourceName, dimension.PublishedAt);
}

public sealed record NeighborhoodComparisonDto(NeighborhoodScoreDto A, NeighborhoodScoreDto B);

public sealed record NeighborhoodScoreDto(
    string NeighborhoodId,
    DimensionScoreDto AirQuality,
    DimensionScoreDto GreenSpace,
    DimensionScoreDto Transportation,
    DimensionScoreDto Parking,
    DimensionScoreDto HealthAccess,
    DimensionScoreDto TransitAccess,
    int? Overall,
    bool IsComplete)
{
    public static NeighborhoodScoreDto From(NeighborhoodScoreResult result) => new(
        result.NeighborhoodId,
        DimensionScoreDto.From(result.AirQuality),
        DimensionScoreDto.From(result.GreenSpace),
        DimensionScoreDto.From(result.Transportation),
        DimensionScoreDto.From(result.Parking),
        DimensionScoreDto.From(result.HealthAccess),
        DimensionScoreDto.From(result.TransitAccess),
        result.Overall?.Value,
        result.IsComplete);
}
