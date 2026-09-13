using SemtSkoru.Application.Assistant;

namespace SemtSkoru.Api.Endpoints;

public sealed record AssistantRequestDto(string Prompt);

public sealed record AssistantRecommendationDto(
    string NeighborhoodId,
    string NeighborhoodName,
    string Reasoning,
    NeighborhoodScoreDto Score)
{
    public static AssistantRecommendationDto From(AssistantRecommendation recommendation) => new(
        recommendation.NeighborhoodId,
        recommendation.NeighborhoodName,
        recommendation.Reasoning,
        NeighborhoodScoreDto.From(recommendation.Score));
}

/// <summary>
/// Status is a stable machine-readable discriminator for the frontend (mirrors
/// AssistantOutcomeKind); Message is the Turkish, user-facing sentence for non-Ok statuses -
/// see AssistantEndpoints.cs for which HTTP status code each one is returned with.
/// </summary>
public sealed record AssistantResponseDto(
    IReadOnlyList<AssistantRecommendationDto> Recommendations,
    string Status,
    string? Message);
