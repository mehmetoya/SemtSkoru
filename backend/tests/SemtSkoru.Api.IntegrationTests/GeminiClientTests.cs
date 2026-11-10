using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SemtSkoru.Application.Assistant;
using SemtSkoru.Infrastructure.ExternalApis;

namespace SemtSkoru.Api.IntegrationTests;

// No database needed here - GeminiClient is a pure HttpClient wrapper, exactly like
// AirQualityApiClient - so this mocks the HTTP call the same way AirQualityIngestionTests.cs
// does for the İBB API, rather than making a real (paid/quota-consuming) call to Gemini.
public class GeminiClientTests
{
    private static IConfiguration ConfigWithKey(string? apiKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(apiKey is null ? [] : new Dictionary<string, string?> { ["Gemini:ApiKey"] = apiKey })
            .Build();

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task GenerateAsync_throws_NotConfigured_and_never_calls_out_when_no_api_key_is_set()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("should never be called"));
        var client = new GeminiClient(new HttpClient(handler), ConfigWithKey(null), NullLogger<GeminiClient>.Instance, TimeProvider.System);

        await Assert.ThrowsAsync<AiAssistantNotConfiguredException>(
            () => client.GenerateAsync("system", "user", CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_returns_the_text_from_the_first_candidate()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"candidates":[{"content":{"parts":[{"text":"{\"recommendations\":[]}"}]}}]}"""));
        var client = new GeminiClient(new HttpClient(handler), ConfigWithKey("test-key"), NullLogger<GeminiClient>.Instance, TimeProvider.System);

        var result = await client.GenerateAsync("system", "user", CancellationToken.None);

        Assert.Equal("{\"recommendations\":[]}", result);
    }

    [Fact]
    public async Task GenerateAsync_sends_the_api_key_as_a_header_not_in_the_url_and_posts_to_the_documented_endpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return JsonResponse(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"ok"}]}}]}""");
        });
        var client = new GeminiClient(new HttpClient(handler), ConfigWithKey("super-secret-key"), NullLogger<GeminiClient>.Instance, TimeProvider.System);

        await client.GenerateAsync("system", "user", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent",
            capturedRequest!.RequestUri!.ToString());
        Assert.DoesNotContain("super-secret-key", capturedRequest.RequestUri!.ToString());
        Assert.True(capturedRequest.Headers.TryGetValues("x-goog-api-key", out var values));
        Assert.Equal("super-secret-key", Assert.Single(values!));
    }

    [Fact]
    public async Task GenerateAsync_throws_RateLimited_on_a_429_response()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var client = new GeminiClient(new HttpClient(handler), ConfigWithKey("test-key"), NullLogger<GeminiClient>.Instance, TimeProvider.System);

        await Assert.ThrowsAsync<AiAssistantRateLimitedException>(
            () => client.GenerateAsync("system", "user", CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_throws_Unavailable_on_a_5xx_response()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new GeminiClient(
            new HttpClient(handler), ConfigWithKey("test-key"), NullLogger<GeminiClient>.Instance, new ImmediateTimeProvider());

        await Assert.ThrowsAsync<AiAssistantUnavailableException>(
            () => client.GenerateAsync("system", "user", CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_throws_Unavailable_when_there_are_no_candidates()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, """{"candidates":[]}"""));
        var client = new GeminiClient(new HttpClient(handler), ConfigWithKey("test-key"), NullLogger<GeminiClient>.Instance, TimeProvider.System);

        await Assert.ThrowsAsync<AiAssistantUnavailableException>(
            () => client.GenerateAsync("system", "user", CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_throws_Unavailable_on_a_network_failure()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("simulated network failure"));
        var client = new GeminiClient(new HttpClient(handler), ConfigWithKey("test-key"), NullLogger<GeminiClient>.Instance, TimeProvider.System);

        await Assert.ThrowsAsync<AiAssistantUnavailableException>(
            () => client.GenerateAsync("system", "user", CancellationToken.None));
    }

    // The three tests below cover the diagnostics GeminiClient writes on its failure paths, not
    // just the exception it throws. They exist because a live production failure of every AI
    // feature at once (2026-09-22) could not be diagnosed at all: each distinct cause collapsed
    // into the same opaque AiAssistantUnavailableException and nothing recorded what Google had
    // actually said. Asserting on the log output is the point here - an exception-only assertion
    // would pass just as happily against the version that logged nothing.
    [Fact]
    public async Task GenerateAsync_logs_the_status_code_and_error_body_when_Gemini_rejects_the_request()
    {
        var logger = new CapturingLogger<GeminiClient>();
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.BadRequest,
            """{"error":{"code":400,"message":"API key not valid.","status":"INVALID_ARGUMENT"}}"""));
        var client = new GeminiClient(new HttpClient(handler), ConfigWithKey("test-key"), logger, TimeProvider.System);

        await Assert.ThrowsAsync<AiAssistantUnavailableException>(
            () => client.GenerateAsync("system", "user", CancellationToken.None));

        var message = Assert.Single(logger.Messages);
        Assert.Contains("400", message);
        Assert.Contains("API key not valid.", message);
    }

    // The API key is the one thing in this whole exchange that must never reach a log sink. It is
    // sent as a header (never in the URL - see the endpoint test above), and the error-body
    // logging added alongside these tests must not undo that by echoing a response that happens
    // to quote it back.
    [Fact]
    public async Task GenerateAsync_never_writes_the_api_key_to_the_log()
    {
        var logger = new CapturingLogger<GeminiClient>();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = new GeminiClient(new HttpClient(handler), ConfigWithKey("super-secret-key"), logger, TimeProvider.System);

        await Assert.ThrowsAsync<AiAssistantUnavailableException>(
            () => client.GenerateAsync("system", "user", CancellationToken.None));

        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("super-secret-key", StringComparison.Ordinal));
    }

    // A 200 carrying a candidate with no content parts is exactly what a thinking-enabled model
    // returns when it spends the whole output budget reasoning: without finishReason and
    // thoughtsTokenCount in the log, this is indistinguishable from a network failure downstream.
    [Fact]
    public async Task GenerateAsync_logs_the_finish_reason_when_a_successful_response_carries_no_text()
    {
        var logger = new CapturingLogger<GeminiClient>();
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"candidates":[{"finishReason":"MAX_TOKENS"}],"usageMetadata":{"promptTokenCount":310,"thoughtsTokenCount":512,"totalTokenCount":822}}"""));
        var client = new GeminiClient(new HttpClient(handler), ConfigWithKey("test-key"), logger, TimeProvider.System);

        await Assert.ThrowsAsync<AiAssistantUnavailableException>(
            () => client.GenerateAsync("system", "user", CancellationToken.None));

        var message = Assert.Single(logger.Messages);
        Assert.Contains("MAX_TOKENS", message);
        Assert.Contains("512", message);
    }

    // The four tests below pin the retry behaviour added after the 2026-09-22 incident, where the
    // shared free-tier model answered 503 "This model is currently experiencing high demand" and a
    // single un-retried attempt took every AI feature in the app down with it.
    [Fact]
    public async Task GenerateAsync_retries_an_overloaded_503_and_succeeds_once_the_model_recovers()
    {
        var requestCount = 0;
        var handler = new FakeHttpMessageHandler(_ => ++requestCount == 1
            ? JsonResponse(HttpStatusCode.ServiceUnavailable,
                """{"error":{"code":503,"message":"This model is currently experiencing high demand.","status":"UNAVAILABLE"}}""")
            : JsonResponse(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"recovered"}]}}]}"""));
        var client = new GeminiClient(
            new HttpClient(handler), ConfigWithKey("test-key"), NullLogger<GeminiClient>.Instance, new ImmediateTimeProvider());

        var result = await client.GenerateAsync("system", "user", CancellationToken.None);

        Assert.Equal("recovered", result);
        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task GenerateAsync_gives_up_with_Unavailable_after_three_attempts_when_the_model_stays_overloaded()
    {
        var requestCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            requestCount++;
            return JsonResponse(HttpStatusCode.ServiceUnavailable,
                """{"error":{"code":503,"message":"This model is currently experiencing high demand.","status":"UNAVAILABLE"}}""");
        });
        var timeProvider = new ImmediateTimeProvider();
        var logger = new CapturingLogger<GeminiClient>();
        var client = new GeminiClient(new HttpClient(handler), ConfigWithKey("test-key"), logger, timeProvider);

        await Assert.ThrowsAsync<AiAssistantUnavailableException>(
            () => client.GenerateAsync("system", "user", CancellationToken.None));

        Assert.Equal(3, requestCount);
        // Exponential, and only between attempts - two waits for three tries, never a trailing one.
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], timeProvider.RequestedDelays);
        // The give-up line has to carry Google's own wording, or the next incident is undiagnosable again.
        Assert.Contains(logger.Messages, m => m.Contains("experiencing high demand", StringComparison.Ordinal));
    }

    // 429 means a quota is actually gone, not that the model is momentarily busy. Retrying it
    // spends more of the same scarce shared budget to be told the same thing.
    [Fact]
    public async Task GenerateAsync_does_not_retry_a_429()
    {
        var requestCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        });
        var client = new GeminiClient(
            new HttpClient(handler), ConfigWithKey("test-key"), NullLogger<GeminiClient>.Instance, new ImmediateTimeProvider());

        await Assert.ThrowsAsync<AiAssistantRateLimitedException>(
            () => client.GenerateAsync("system", "user", CancellationToken.None));

        Assert.Equal(1, requestCount);
    }

    // A 4xx is a statement about this exact request (bad key, bad argument); repeating it verbatim
    // cannot change the answer, so it must fail fast rather than make the visitor wait out a backoff.
    [Fact]
    public async Task GenerateAsync_does_not_retry_a_4xx()
    {
        var requestCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            requestCount++;
            return JsonResponse(HttpStatusCode.BadRequest,
                """{"error":{"code":400,"message":"API key not valid.","status":"INVALID_ARGUMENT"}}""");
        });
        var client = new GeminiClient(
            new HttpClient(handler), ConfigWithKey("test-key"), NullLogger<GeminiClient>.Instance, new ImmediateTimeProvider());

        await Assert.ThrowsAsync<AiAssistantUnavailableException>(
            () => client.GenerateAsync("system", "user", CancellationToken.None));

        Assert.Equal(1, requestCount);
    }

    // The fallback is the half of the 2026-09-22 fix that retrying alone cannot cover: that spike
    // lasted more than a day, so three tries against the same overloaded model would all have
    // failed. These two tests pin BOTH halves of the contract - that the fallback is reached, and
    // that it is only ever reached last.
    [Fact]
    public async Task GenerateAsync_falls_back_to_the_secondary_model_on_its_final_attempt()
    {
        var requestedModels = new List<string>();
        var handler = new FakeHttpMessageHandler(req =>
        {
            requestedModels.Add(req.RequestUri!.ToString());
            return requestedModels.Count < 3
                ? JsonResponse(HttpStatusCode.ServiceUnavailable,
                    """{"error":{"code":503,"message":"This model is currently experiencing high demand.","status":"UNAVAILABLE"}}""")
                : JsonResponse(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"from fallback"}]}}]}""");
        });
        var client = new GeminiClient(
            new HttpClient(handler), ConfigWithKey("test-key"), NullLogger<GeminiClient>.Instance, new ImmediateTimeProvider());

        var result = await client.GenerateAsync("system", "user", CancellationToken.None);

        Assert.Equal("from fallback", result);
        Assert.Collection(
            requestedModels,
            url => Assert.Contains("gemini-3.5-flash-lite:generateContent", url, StringComparison.Ordinal),
            url => Assert.Contains("gemini-3.5-flash-lite:generateContent", url, StringComparison.Ordinal),
            url => Assert.Contains("gemini-3.5-flash:generateContent", url, StringComparison.Ordinal));
    }

    // Nothing is remembered between calls: a call that only needed the fallback last time still
    // starts at the primary, so the app returns to it on its own once the spike passes.
    [Fact]
    public async Task GenerateAsync_always_starts_a_fresh_call_on_the_primary_model()
    {
        var requestedModels = new List<string>();
        var handler = new FakeHttpMessageHandler(req =>
        {
            requestedModels.Add(req.RequestUri!.ToString());
            return JsonResponse(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"ok"}]}}]}""");
        });
        var client = new GeminiClient(
            new HttpClient(handler), ConfigWithKey("test-key"), NullLogger<GeminiClient>.Instance, new ImmediateTimeProvider());

        await client.GenerateAsync("system", "user", CancellationToken.None);
        await client.GenerateAsync("system", "user", CancellationToken.None);

        // A healthy primary is never retried and never escalates - two calls, two requests.
        Assert.Equal(2, requestedModels.Count);
        Assert.All(requestedModels, url =>
            Assert.Contains("gemini-3.5-flash-lite:generateContent", url, StringComparison.Ordinal));
    }

    // Measured during the 2026-09-22 incident: a third of the degraded model's failures arrived as
    // a dropped connection rather than a clean 503, so these must be retried too or most of the
    // retry budget goes unspent.
    [Fact]
    public async Task GenerateAsync_retries_a_dropped_connection()
    {
        var requestCount = 0;
        var handler = new FakeHttpMessageHandler(_ => ++requestCount == 1
            ? throw new HttpRequestException("connection reset by peer")
            : JsonResponse(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"recovered"}]}}]}"""));
        var client = new GeminiClient(
            new HttpClient(handler), ConfigWithKey("test-key"), NullLogger<GeminiClient>.Instance, new ImmediateTimeProvider());

        var result = await client.GenerateAsync("system", "user", CancellationToken.None);

        Assert.Equal("recovered", result);
        Assert.Equal(2, requestCount);
    }

    // A timeout is the opposite case: the attempt already consumed the per-attempt budget, so
    // retrying it would make a visitor wait out the same wall clock again for the same answer.
    [Fact]
    public async Task GenerateAsync_does_not_retry_an_attempt_that_timed_out()
    {
        var requestCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            requestCount++;
            // What HttpClient.Timeout surfaces as, with the caller's own token never cancelled.
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout");
        });
        var client = new GeminiClient(
            new HttpClient(handler), ConfigWithKey("test-key"), NullLogger<GeminiClient>.Instance, new ImmediateTimeProvider());

        await Assert.ThrowsAsync<AiAssistantUnavailableException>(
            () => client.GenerateAsync("system", "user", CancellationToken.None));

        Assert.Equal(1, requestCount);
    }
}

file sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}

// Records the rendered text of every log message GeminiClient writes, so a test can assert on
// what a real log sink would have received (including that the API key is absent from it).
file sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
}

// Task.Delay(TimeSpan, TimeProvider, CancellationToken) schedules through TimeProvider.CreateTimer,
// so firing the callback straight away makes GeminiClient's retry backoff complete instantly while
// still recording what it ASKED to wait. That is what lets the backoff test assert the real 1s/2s
// exponential shape without the suite spending three seconds per run sleeping through it.
file sealed class ImmediateTimeProvider : TimeProvider
{
    public List<TimeSpan> RequestedDelays { get; } = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        RequestedDelays.Add(dueTime);
        return new ImmediateTimer(callback, state);
    }

    private sealed class ImmediateTimer : ITimer
    {
        public ImmediateTimer(TimerCallback callback, object? state) =>
            ThreadPool.QueueUserWorkItem(_ => callback(state));

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
