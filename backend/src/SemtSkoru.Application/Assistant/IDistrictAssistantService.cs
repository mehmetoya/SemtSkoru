namespace SemtSkoru.Application.Assistant;

public interface IDistrictAssistantService
{
    /// <summary>Generates recommendations for a free-text preference query, with the model's
    /// free-text "reasoning" written in the given locale ("tr" or "en" - see
    /// SemtSkoru.Application.Localization.AiLocale, which an unrecognized/missing value normalizes
    /// to "tr" through). Locale only affects that prose, never the grounding/validation logic.</summary>
    Task<AssistantOutcome> GetRecommendationsAsync(string userQuery, string locale, CancellationToken ct);
}
