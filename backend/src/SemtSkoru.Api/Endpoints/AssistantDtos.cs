using SemtSkoru.Application.Assistant;

namespace SemtSkoru.Api.Endpoints;

// Locale is nullable/optional here (unlike the query-string endpoints, which get "missing" for
// free) because this is a POST body a caller might send without it at all - defaults to "tr" the
// same way via SemtSkoru.Application.Localization.AiLocale.NormalizeOrDefault, see AssistantEndpoints.cs.
public sealed record AssistantRequestDto(string Prompt, string? Locale = null);

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
