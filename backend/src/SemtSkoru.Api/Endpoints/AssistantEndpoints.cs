using SemtSkoru.Api.RateLimiting;
using SemtSkoru.Application.Assistant;
using SemtSkoru.Application.Localization;

namespace SemtSkoru.Api.Endpoints;

public static class AssistantEndpoints
{
    public static void MapAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        // Dedicated policy, not Standard/Compare: this is the only endpoint that spends a
        // shared, global, hard-capped-per-day third-party quota (Gemini's free tier) rather than
        // just this app's own Postgres connections - see RateLimitingExtensions.cs for the
        // per-IP + global-daily/per-minute reasoning.
        app.MapPost("/api/asistan", async (
            AssistantRequestDto request,
            IDistrictAssistantService assistantService,
            CancellationToken ct) =>
        {
            var outcome = await assistantService.GetRecommendationsAsync(
                request.Prompt, AiLocale.NormalizeOrDefault(request.Locale), ct);

            return outcome.Kind switch
            {
                AssistantOutcomeKind.InvalidRequest => Results.BadRequest(
                    new AssistantResponseDto([], nameof(AssistantOutcomeKind.InvalidRequest), "Lütfen tercihlerinizi birkaç cümleyle yazın.")),

                AssistantOutcomeKind.NotConfigured => Results.Json(
                    new AssistantResponseDto(
                        [],
                        nameof(AssistantOutcomeKind.NotConfigured),
                        "AI Semt Asistanı şu anda yapılandırılmamış. Lütfen daha sonra tekrar deneyin."),
                    statusCode: StatusCodes.Status503ServiceUnavailable),

                AssistantOutcomeKind.RateLimited => Results.Json(
                    new AssistantResponseDto(
                        [],
                        nameof(AssistantOutcomeKind.RateLimited),
                        "AI Semt Asistanı şu anda çok yoğun. Lütfen birkaç dakika sonra tekrar deneyin."),
                    statusCode: StatusCodes.Status429TooManyRequests),

                AssistantOutcomeKind.Unavailable => Results.Json(
                    new AssistantResponseDto(
                        [],
                        nameof(AssistantOutcomeKind.Unavailable),
                        "AI Semt Asistanı şu anda yanıt veremiyor. Lütfen daha sonra tekrar deneyin."),
                    statusCode: StatusCodes.Status502BadGateway),

                AssistantOutcomeKind.NoUsableRecommendations => Results.Ok(
                    new AssistantResponseDto(
                        [],
                        nameof(AssistantOutcomeKind.NoUsableRecommendations),
                        "İsteğiniz için güvenilir bir öneri oluşturamadık. İlçeleri doğrudan karşılaştırmayı deneyebilirsiniz.")),

                _ => Results.Ok(new AssistantResponseDto(
                    outcome.Recommendations.Select(AssistantRecommendationDto.From).ToList(),
                    nameof(AssistantOutcomeKind.Ok),
                    null)),
            };
        }).RequireRateLimiting(RateLimitPolicies.AiAssistant);
    }
}
