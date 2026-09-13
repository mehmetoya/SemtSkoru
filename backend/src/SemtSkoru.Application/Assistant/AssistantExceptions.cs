namespace SemtSkoru.Application.Assistant;

/// <summary>No Gemini API key is configured (Gemini:ApiKey). Expected in every environment until the real key is added.</summary>
public sealed class AiAssistantNotConfiguredException() : Exception("Gemini API key is not configured.");

/// <summary>The upstream AI provider itself rejected the call with a rate-limit response (its own HTTP 429).</summary>
public sealed class AiAssistantRateLimitedException() : Exception("The AI provider rate-limited this request.");

/// <summary>Any other upstream failure: network error, timeout, non-success status, or an empty/unparseable envelope.</summary>
public sealed class AiAssistantUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
