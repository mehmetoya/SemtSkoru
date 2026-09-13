using SemtSkoru.Application.Assistant;

namespace SemtSkoru.Api.Endpoints;

public sealed record AsistanRequestDto(string Prompt);

public sealed record AsistanRecommendationDto(
    string NeighborhoodId,
    string NeighborhoodName,
    string Reasoning,
    NeighborhoodScoreDto Score)
{
    public static AsistanRecommendationDto From(AssistantRecommendation recommendation) => new(
        recommendation.NeighborhoodId,
        recommendation.NeighborhoodName,
        recommendation.Reasoning,
        NeighborhoodScoreDto.From(recommendation.Score));
}

/// <summary>
/// Status is a stable machine-readable discriminator for the frontend (mirrors
/// AssistantOutcomeKind); Message is the Turkish, user-facing sentence for non-Ok statuses -
/// see AsistanEndpoints.cs for which HTTP status code each one is returned with.
/// </summary>
public sealed record AsistanResponseDto(
    IReadOnlyList<AsistanRecommendationDto> Recommendations,
    string Status,
    string? Message);
