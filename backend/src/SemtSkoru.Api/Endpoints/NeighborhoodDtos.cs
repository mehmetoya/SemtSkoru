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

// Null whenever no cached AI summary exists yet for this district - Gemini not configured, the
// weekly DistrictSummaryGenerationJob hasn't run for it yet, or every generation attempt so far
// came back unusable (see DistrictSummaryOutcome) - never a fabricated placeholder.
public sealed record DistrictSummaryDto(string Text, DateTimeOffset GeneratedAt);

public sealed record NeighborhoodScoreDto(
    string NeighborhoodId,
    DimensionScoreDto AirQuality,
    DimensionScoreDto GreenSpace,
    DimensionScoreDto Transportation,
    DimensionScoreDto Parking,
    DimensionScoreDto HealthAccess,
    DimensionScoreDto TransitAccess,
    int? Overall,
    bool IsComplete,
    DistrictSummaryDto? Summary = null)
{
    // `summary` defaults to null for callers that don't have one handy (e.g. /compare and the AI
    // Semt Asistanı's recommendation cards, which build this same DTO from a NeighborhoodScoreResult
    // that was never joined against DistrictSummaries) - an honestly-absent field there, not a
    // wrong one, since neither of those surfaces renders it today.
    public static NeighborhoodScoreDto From(NeighborhoodScoreResult result, DistrictSummaryDto? summary = null) => new(
        result.NeighborhoodId,
        DimensionScoreDto.From(result.AirQuality),
        DimensionScoreDto.From(result.GreenSpace),
        DimensionScoreDto.From(result.Transportation),
        DimensionScoreDto.From(result.Parking),
        DimensionScoreDto.From(result.HealthAccess),
        DimensionScoreDto.From(result.TransitAccess),
        result.Overall?.Value,
        result.IsComplete,
        summary);
}
