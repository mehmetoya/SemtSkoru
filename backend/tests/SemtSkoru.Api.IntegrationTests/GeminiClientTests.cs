using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
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
        var client = new GeminiClient(new HttpClient(handler), ConfigWithKey(null));

        await Assert.ThrowsAsync<AiAssistantNotConfiguredException>(
            () => client.GenerateAsync("system", "user", CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_returns_the_text_from_the_first_candidate()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"candidates":[{"content":{"parts":[{"text":"{\"oneriler\":[]}"}]}}]}"""));
        var client = new GeminiClient(new HttpClient(handler), ConfigWithKey("test-key"));

        var result = await client.GenerateAsync("system", "user", CancellationToken.None);

        Assert.Equal("{\"oneriler\":[]}", result);
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
        var client = new GeminiClient(new HttpClient(handler), ConfigWithKey("super-secret-key"));

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
        var client = new GeminiClient(new HttpClient(handler), ConfigWithKey("test-key"));

        await Assert.ThrowsAsync<AiAssistantRateLimitedException>(
            () => client.GenerateAsync("system", "user", CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_throws_Unavailable_on_a_5xx_response()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new GeminiClient(new HttpClient(handler), ConfigWithKey("test-key"));

        await Assert.ThrowsAsync<AiAssistantUnavailableException>(
            () => client.GenerateAsync("system", "user", CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_throws_Unavailable_when_there_are_no_candidates()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, """{"candidates":[]}"""));
        var client = new GeminiClient(new HttpClient(handler), ConfigWithKey("test-key"));

        await Assert.ThrowsAsync<AiAssistantUnavailableException>(
            () => client.GenerateAsync("system", "user", CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_throws_Unavailable_on_a_network_failure()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("simulated network failure"));
        var client = new GeminiClient(new HttpClient(handler), ConfigWithKey("test-key"));

        await Assert.ThrowsAsync<AiAssistantUnavailableException>(
            () => client.GenerateAsync("system", "user", CancellationToken.None));
    }
}

file sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
