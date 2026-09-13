namespace SemtSkoru.Application.Assistant;

/// <summary>
/// A single grounded-generation call to whatever LLM backs the AI Semt Asistanı (currently
/// Google Gemini - see SemtSkoru.Infrastructure.ExternalApis.GeminiClient). Takes a system
/// instruction plus a user-facing prompt that already contains the real per-district data to
/// reason over, and returns the model's raw text response for DistrictAssistantService to parse
/// and validate. This interface makes no promise the response is trustworthy - that's exactly
/// why DistrictAssistantService never returns it to a caller unvalidated.
/// </summary>
public interface IDistrictAssistantAiClient
{
    /// <exception cref="AiAssistantNotConfiguredException">No API key is configured.</exception>
    /// <exception cref="AiAssistantRateLimitedException">The upstream provider itself rate-limited this call.</exception>
    /// <exception cref="AiAssistantUnavailableException">Any other upstream failure (network error, timeout, non-success status, empty response).</exception>
    Task<string> GenerateAsync(string systemInstruction, string userPrompt, CancellationToken ct);
}
