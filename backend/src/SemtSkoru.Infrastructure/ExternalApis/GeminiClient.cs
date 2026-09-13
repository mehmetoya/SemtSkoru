using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using SemtSkoru.Application.Assistant;

namespace SemtSkoru.Infrastructure.ExternalApis;

/// <summary>
/// Calls Google's Gemini REST API directly (a plain HttpClient POST, no Google SDK - see
/// AirQualityApiClient.cs for the same house style) to power the AI Semt Asistanı feature.
///
/// Model + endpoint verified live against ai.google.dev on 2026-09-13 (that day's copy of
/// ai.google.dev/gemini-api/docs/pricing, last-updated by Google 2026-09-02): "gemini-3.5-flash-lite"
/// is Google's current fastest/cheapest Flash-Lite model, explicitly billed "Free of charge" for
/// input/output on the free tier - a good fit for a low-stakes, low-token, occasional-use feature.
/// ai.google.dev/gemini-api/docs/rate-limits no longer publishes static per-model RPM/RPD/TPM
/// numbers (it now points to an authenticated-only AI Studio dashboard) - see
/// SemtSkoru.Api/RateLimiting/RateLimitingExtensions.cs for how this app's own call budget is
/// sized conservatively around that uncertainty rather than against an unverifiable exact number.
///
/// REST shape (method: models.generateContent) confirmed directly against
/// ai.google.dev/api/generate-content: POST .../v1beta/models/{model}:generateContent with the
/// API key in the `x-goog-api-key` header (per ai.google.dev/gemini-api/docs/get-started) rather
/// than a URL query parameter, so it never ends up in a proxy/access log.
/// </summary>
public sealed class GeminiClient(HttpClient httpClient, IConfiguration configuration) : IDistrictAssistantAiClient
{
    private const string Model = "gemini-3.5-flash-lite";
    private const string Endpoint =
        $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent";

    public async Task<string> GenerateAsync(string systemInstruction, string userPrompt, CancellationToken ct)
    {
        var apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AiAssistantNotConfiguredException();
        }

        var requestBody = new GeminiRequest(
            SystemInstruction: new GeminiContent([new GeminiPart(systemInstruction)]),
            Contents: [new GeminiContent([new GeminiPart(userPrompt)])],
            GenerationConfig: new GeminiGenerationConfig(ResponseMimeType: "application/json"));

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(requestBody),
        };
        request.Headers.Add("x-goog-api-key", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AiAssistantUnavailableException("Gemini isteği ağ hatasıyla başarısız oldu.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new AiAssistantRateLimitedException();
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new AiAssistantUnavailableException(
                    $"Gemini beklenmeyen bir durum kodu döndürdü: {(int)response.StatusCode}.");
            }

            GeminiResponse? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: ct);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
            {
                throw new AiAssistantUnavailableException("Gemini yanıtı ayrıştırılamadı.", ex);
            }

            var text = payload?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new AiAssistantUnavailableException("Gemini boş bir yanıt döndürdü.");
            }

            return text;
        }
    }

    private sealed record GeminiRequest(
        [property: JsonPropertyName("systemInstruction")] GeminiContent SystemInstruction,
        [property: JsonPropertyName("contents")] GeminiContent[] Contents,
        [property: JsonPropertyName("generationConfig")] GeminiGenerationConfig GenerationConfig);

    private sealed record GeminiGenerationConfig(
        [property: JsonPropertyName("responseMimeType")] string ResponseMimeType);

    private sealed record GeminiContent([property: JsonPropertyName("parts")] GeminiPart[] Parts);

    private sealed record GeminiPart([property: JsonPropertyName("text")] string Text);

    private sealed record GeminiResponse(
        [property: JsonPropertyName("candidates")] List<GeminiCandidate>? Candidates);

    private sealed record GeminiCandidate(
        [property: JsonPropertyName("content")] GeminiContent? Content);
}
