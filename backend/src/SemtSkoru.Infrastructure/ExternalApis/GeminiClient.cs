using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
///
/// Every failure path here logs what Google actually said before collapsing it into one of the
/// three AiAssistant* exceptions the Application layer understands. Those exceptions are
/// deliberately coarse - callers only need "not configured"/"rate limited"/"unavailable" to pick
/// a user-facing message - but that coarseness previously made a live failure undiagnosable:
/// every distinct cause (revoked key, quota exhausted outside a 429, a model returning no text
/// part because it spent the response budget on thinking tokens) surfaced identically as an
/// opaque "Unavailable" with nothing written anywhere. The log statements below are the only
/// place the real status code, error body, and finishReason survive, so a production failure can
/// be told apart from the host's logs without reproducing it locally against the real key.
/// The API key itself is never logged - only Google's own response.
///
/// Transient 503/500 responses are retried with exponential backoff before the call is given up
/// on, per ai.google.dev/gemini-api/docs/troubleshooting's own guidance for those two codes. This
/// is not speculative hardening: on 2026-09-22 every AI feature in the app was down at once
/// because the shared free-tier model answered
/// `503 UNAVAILABLE "This model is currently experiencing high demand"` and a single un-retried
/// attempt turned that into a hard failure for the visitor. 429 is deliberately NOT retried -
/// that one means a quota was actually exhausted, and retrying it just spends more of the same
/// scarce budget (see RateLimitingExtensions.cs) to get the same answer.
/// </summary>
public sealed class GeminiClient(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<GeminiClient> logger,
    TimeProvider timeProvider) : IDistrictAssistantAiClient
{
    // The model every call starts on - fastest/cheapest of the Flash-Lite line and free on the
    // tier this app runs against.
    private const string PrimaryModel = "gemini-3.5-flash-lite";

    // Tried only as the LAST attempt, and only after the primary has already failed transiently
    // every time before it (see GenerateAsync). "Currently experiencing high demand" is a
    // statement about ONE model's load, not about the API or the key - measured live during the
    // 2026-09-22 incident, six identical calls per model:
    //
    //     gemini-3.5-flash-lite   2/6 succeeded (rest 503 / dropped connection)
    //     gemini-3.1-flash-lite   2/6 succeeded
    //     gemini-3.5-flash        6/6 succeeded
    //
    // Hence a deliberately DIFFERENT line of model rather than another Flash-Lite: the whole
    // Flash-Lite tier was degraded together, so falling back within it (as an earlier draft of
    // this did, before the numbers above existed) would have bought almost nothing. Flash-Lite
    // stays primary because it is the cheaper model and this app runs on the free tier - the
    // fallback only ever carries the traffic the primary already refused twice.
    //
    // Nothing is remembered between calls on purpose: each new call starts again at PrimaryModel,
    // so the app returns to it by itself the moment the spike passes, with no state to reset and
    // no way to get stuck on the fallback. Both models' output is validated against the same
    // hardcoded dimension and district sets as every other AI feature, so a difference in wording
    // between them cannot widen what this app will accept back.
    private const string FallbackModel = "gemini-3.5-flash";

    private static string EndpointFor(string model) =>
        $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

    // Google's error bodies are small JSON objects, but this is defensive: an unexpected HTML
    // error page (e.g. from an intercepting proxy) shouldn't dump kilobytes into the log for
    // every failed call.
    private const int MaxLoggedBodyLength = 1000;

    // Three attempts total, not more: an overloaded model that has already refused twice in a row
    // is unlikely to answer on a fourth try inside the few seconds a visitor is willing to wait,
    // and every extra attempt is another request against the same shared free-tier budget.
    private const int MaxAttempts = 3;

    // Exponential (1s, then 2s), matching the documented backoff shape. Small absolute numbers
    // because this runs inside an HTTP request a real visitor is waiting on - the AI search box
    // and the assistant both block on it - not in a background job that can afford minutes.
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(1);

    // Gate on STARTING another attempt, not a deadline that cuts one short: once this much time
    // has already gone into the call, what is left is returned to the visitor rather than spent
    // waiting out one more backoff. The absolute worst case is therefore this budget plus one
    // more HttpClient.Timeout (Program.cs), i.e. ~35s, and only if every single attempt hangs
    // until its timeout - an overloaded model refuses in well under a second, which is the case
    // this exists for, so in practice three attempts and two backoffs cost about three seconds.
    private static readonly TimeSpan TotalRetryBudget = TimeSpan.FromSeconds(25);

    public async Task<string> GenerateAsync(string systemInstruction, string userPrompt, CancellationToken ct)
    {
        var apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AiAssistantNotConfiguredException();
        }

        var startedAt = timeProvider.GetTimestamp();
        var retryDelay = FirstRetryDelay;

        for (var attempt = 1; ; attempt++)
        {
            // Attempts before the last one retry the primary model (a demand spike is often over
            // within a second or two); the final attempt spends itself on the fallback instead of
            // asking the same overloaded model a third time.
            var model = attempt < MaxAttempts ? PrimaryModel : FallbackModel;

            try
            {
                return await AttemptAsync(model, apiKey, systemInstruction, userPrompt, ct);
            }
            catch (GeminiTransientFailureException transient)
            {
                var elapsed = timeProvider.GetElapsedTime(startedAt);
                var outOfAttempts = attempt >= MaxAttempts;
                var outOfBudget = elapsed + retryDelay >= TotalRetryBudget;
                if (outOfAttempts || outOfBudget)
                {
                    // The one log line that matters when this feature is down: it says the model
                    // was reachable and simply kept refusing, which is a very different thing to
                    // chase than a bad key or a parsing bug.
                    logger.LogWarning(
                        "Gemini still failing transiently ({Cause}) for model {Model} after {Attempt} attempt(s) "
                            + "over {ElapsedMs}ms; giving up ({Reason}). Detail: {Detail}",
                        transient.Reason,
                        model,
                        attempt,
                        (int)elapsed.TotalMilliseconds,
                        outOfAttempts ? "attempt limit" : "time budget",
                        transient.Detail);
                    throw new AiAssistantUnavailableException(
                        $"Gemini yanıt veremedi ({transient.Reason}).", transient);
                }

                logger.LogInformation(
                    "Gemini failed transiently ({Cause}) for model {Model} on attempt {Attempt} of {MaxAttempts}; "
                        + "retrying in {DelayMs}ms (next attempt uses {NextModel})",
                    transient.Reason,
                    model,
                    attempt,
                    MaxAttempts,
                    (int)retryDelay.TotalMilliseconds,
                    attempt + 1 < MaxAttempts ? PrimaryModel : FallbackModel);

                await Task.Delay(retryDelay, timeProvider, ct);
                retryDelay *= 2;
            }
        }
    }

    // One attempt: builds a fresh request (an HttpRequestMessage cannot be sent twice), sends it,
    // and either returns the model's text or throws. A retryable server-side failure leaves here
    // as GeminiTransientFailureException so the caller above owns the retry decision and the
    // logging that goes with it; every other failure is already final and throws the matching
    // AiAssistant* exception directly.
    private async Task<string> AttemptAsync(
        string model, string apiKey, string systemInstruction, string userPrompt, CancellationToken ct)
    {
        var requestBody = new GeminiRequest(
            SystemInstruction: new GeminiContent([new GeminiPart(systemInstruction)]),
            Contents: [new GeminiContent([new GeminiPart(userPrompt)])],
            GenerationConfig: new GeminiGenerationConfig(ResponseMimeType: "application/json"));

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointFor(model))
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
        catch (HttpRequestException ex)
        {
            // A connection that never established or was dropped mid-flight. Retried, because it
            // is the same overload wearing different clothes: probing the degraded model during
            // the 2026-09-22 incident, a third of the failures came back as a dropped connection
            // rather than a clean 503, and giving up on those would waste two thirds of the
            // retry budget this class exists to spend.
            throw new GeminiTransientFailureException($"{ex.GetType().Name}", ex.Message, ex);
        }
        catch (Exception ex)
        {
            // Everything else - in practice the HttpClient's own per-attempt timeout (Program.cs),
            // which arrives as a TaskCanceledException with ct NOT cancelled. Deliberately NOT
            // retried, unlike the dropped connection above: an attempt that burned its whole
            // timeout has already spent most of the budget a waiting visitor has to give.
            logger.LogWarning(
                ex,
                "Gemini request to model {Model} timed out or failed unrecoverably ({ExceptionType})",
                model,
                ex.GetType().Name);
            throw new AiAssistantUnavailableException("Gemini isteği ağ hatasıyla başarısız oldu.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // Read the body BEFORE branching on the status: Google puts the actual reason
                // (e.g. API_KEY_INVALID, PERMISSION_DENIED, or the "experiencing high demand"
                // text that distinguishes an overloaded model from a broken one) in the error
                // payload, not the status code alone. A bare "429" or "400" in a log is not
                // enough to act on; the body is.
                var errorBody = await ReadBodyForLoggingAsync(response, ct);
                var statusCode = (int)response.StatusCode;

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    logger.LogWarning(
                        "Gemini rate-limited the request to model {Model} (HTTP 429). Response body: {ErrorBody}",
                        model,
                        errorBody);
                    throw new AiAssistantRateLimitedException();
                }

                if (IsRetryable(response.StatusCode))
                {
                    throw new GeminiTransientFailureException($"HTTP {statusCode}", errorBody);
                }

                logger.LogWarning(
                    "Gemini returned an unexpected status {StatusCode} for model {Model}. Response body: {ErrorBody}",
                    statusCode,
                    model,
                    errorBody);
                throw new AiAssistantUnavailableException(
                    $"Gemini beklenmeyen bir durum kodu döndürdü: {statusCode}.");
            }

            GeminiResponse? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: ct);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
            {
                logger.LogWarning(ex, "Gemini returned a 2xx body that could not be parsed as its documented response shape");
                throw new AiAssistantUnavailableException("Gemini yanıtı ayrıştırılamadı.", ex);
            }

            var candidate = payload?.Candidates?.FirstOrDefault();
            var text = candidate?.Content?.Parts?.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                // A 200 with no usable text is the failure mode that looks most like a bug in THIS
                // app but usually isn't one: finishReason says whether the model was cut off
                // (MAX_TOKENS - on a thinking-enabled model the reasoning tokens are spent from
                // the same output budget, so the response can end before any text part is
                // emitted), stopped for safety (SAFETY/PROHIBITED_CONTENT), or whether the prompt
                // itself was blocked (promptFeedback.blockReason, in which case there is no
                // candidate at all). Logging all three plus the token counts is what makes that
                // distinction possible after the fact.
                logger.LogWarning(
                    "Gemini returned no usable text for model {Model}. finishReason={FinishReason}, "
                        + "candidates={CandidateCount}, blockReason={BlockReason}, "
                        + "promptTokens={PromptTokens}, candidateTokens={CandidateTokens}, "
                        + "thoughtTokens={ThoughtTokens}, totalTokens={TotalTokens}",
                    model,
                    candidate?.FinishReason ?? "(none)",
                    payload?.Candidates?.Count ?? 0,
                    payload?.PromptFeedback?.BlockReason ?? "(none)",
                    payload?.UsageMetadata?.PromptTokenCount,
                    payload?.UsageMetadata?.CandidatesTokenCount,
                    payload?.UsageMetadata?.ThoughtsTokenCount,
                    payload?.UsageMetadata?.TotalTokenCount);
                throw new AiAssistantUnavailableException("Gemini boş bir yanıt döndürdü.");
            }

            return text;
        }
    }

    // 503 is the one actually observed in production (an overloaded shared free-tier model); 500
    // is included because Google documents the same "retry with backoff" handling for it. Every
    // other non-success status is a statement about THIS request (bad key, bad argument, quota
    // gone) that repeating it verbatim cannot change.
    private static bool IsRetryable(HttpStatusCode status) =>
        status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.InternalServerError;

    // Internal to this class: a failure the caller above may choose to retry. Never escapes
    // GenerateAsync - it is always converted into one of the AiAssistant* exceptions the
    // Application layer knows how to turn into a user-facing message.
    private sealed class GeminiTransientFailureException(string reason, string detail, Exception? innerException = null)
        : Exception($"Gemini transient failure: {reason}", innerException)
    {
        /// <summary>Short, log-friendly cause: "HTTP 503", or the exception type for a dropped connection.</summary>
        public string Reason { get; } = reason;

        /// <summary>Google's own error body, or the network exception's message.</summary>
        public string Detail { get; } = detail;
    }

    // Never throws: this only ever runs on a path that is already failing, and losing the log
    // detail must not turn a clean AiAssistant* exception into an unrelated one.
    private static async Task<string> ReadBodyForLoggingAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
            {
                return "(empty)";
            }

            return body.Length <= MaxLoggedBodyLength
                ? body
                : body[..MaxLoggedBodyLength] + "... (truncated)";
        }
        catch (Exception ex)
        {
            return $"(could not be read: {ex.GetType().Name})";
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
        [property: JsonPropertyName("candidates")] List<GeminiCandidate>? Candidates,
        [property: JsonPropertyName("promptFeedback")] GeminiPromptFeedback? PromptFeedback,
        [property: JsonPropertyName("usageMetadata")] GeminiUsageMetadata? UsageMetadata);

    private sealed record GeminiCandidate(
        [property: JsonPropertyName("content")] GeminiContent? Content,
        [property: JsonPropertyName("finishReason")] string? FinishReason);

    private sealed record GeminiPromptFeedback(
        [property: JsonPropertyName("blockReason")] string? BlockReason);

    // Diagnostics only - never used to make a decision, just logged when a response comes back
    // without usable text. thoughtsTokenCount is the one that tells a "spent the whole budget
    // thinking" truncation apart from an ordinary empty answer.
    private sealed record GeminiUsageMetadata(
        [property: JsonPropertyName("promptTokenCount")] int? PromptTokenCount,
        [property: JsonPropertyName("candidatesTokenCount")] int? CandidatesTokenCount,
        [property: JsonPropertyName("thoughtsTokenCount")] int? ThoughtsTokenCount,
        [property: JsonPropertyName("totalTokenCount")] int? TotalTokenCount);
}
