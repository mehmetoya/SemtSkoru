using SemtSkoru.Application.Assistant;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Application.Summaries;
using SemtSkoru.Domain;

namespace SemtSkoru.Application.Tests.Summaries;

// The most important test file in this feature (mirrors DistrictAssistantServiceTests.cs's own
// role for the assistant): SemtSkoru's one non-negotiable principle ("never fabricate or estimate
// data") extends to this AI summary as "never let the model's text past validation unless every
// dimension it cites is real AND actually has data for this district." These tests prove that
// boundary holds even when the model misbehaves - an invented dimension key, a dimension with no
// data, invalid JSON - not just that it should in theory.
public class DistrictSummaryServiceTests
{
    private static readonly DimensionScore Full = new(new Score(83), DataFreshnessStatus.Fresh, "Test Source", DateTimeOffset.UtcNow);

    private static NeighborhoodScoreResult FullyScored() => new(
        "kadikoy", Full, Full, Full, Full, Full, Full, new Score(83));

    // greenSpace has no data - the district-page-honesty ("Veri yok") case this feature must
    // never paper over.
    private static NeighborhoodScoreResult PartiallyScored() => new(
        "besiktas", Full, DimensionScore.NoData, Full, Full, Full, Full, new Score(83));

    private static NeighborhoodScoreResult Unscored() => new(
        "uskudar", DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData,
        DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, null);

    private static DistrictSummaryService CreateService(FakeAiClient aiClient) => new(aiClient);

    [Fact]
    public async Task GenerateSummaryAsync_returns_InsufficientData_without_calling_the_ai_when_the_district_has_no_scores_at_all()
    {
        var aiClient = new FakeAiClient { Response = "irrelevant" };

        var outcome = await CreateService(aiClient).GenerateSummaryAsync("Üsküdar", Unscored(), CancellationToken.None);

        Assert.Equal(DistrictSummaryOutcomeKind.InsufficientData, outcome.Kind);
        Assert.Null(outcome.SummaryText);
        Assert.False(aiClient.WasCalled);
    }

    [Fact]
    public async Task GenerateSummaryAsync_returns_Ok_with_a_valid_grounded_highlight()
    {
        var aiClient = new FakeAiClient
        {
            Response = """
                {"highlights":[{"dimension":"airQuality","strength":"strong"}],
                 "summary":"Bu ilçe hava kalitesinde güçlü."}
                """,
        };

        var outcome = await CreateService(aiClient).GenerateSummaryAsync("Kadıköy", FullyScored(), CancellationToken.None);

        Assert.Equal(DistrictSummaryOutcomeKind.Ok, outcome.Kind);
        Assert.Equal("Bu ilçe hava kalitesinde güçlü.", outcome.SummaryText);
    }

    [Fact]
    public async Task GenerateSummaryAsync_drops_a_hallucinated_dimension_key_but_keeps_a_real_one()
    {
        // "sahilKonumu" ("coastal location" in Turkish) is not one of the 6 real dimension keys -
        // exactly the shape of a model inventing something that isn't real. It must never let the
        // summary through on its own, but a real, co-occurring valid highlight still can.
        var aiClient = new FakeAiClient
        {
            Response = """
                {"highlights":[
                    {"dimension":"airQuality","strength":"strong"},
                    {"dimension":"sahilKonumu","strength":"strong"}
                 ],
                 "summary":"Bu ilçe hava kalitesinde güçlü."}
                """,
        };

        var outcome = await CreateService(aiClient).GenerateSummaryAsync("Kadıköy", FullyScored(), CancellationToken.None);

        Assert.Equal(DistrictSummaryOutcomeKind.Ok, outcome.Kind);
        Assert.Equal("Bu ilçe hava kalitesinde güçlü.", outcome.SummaryText);
    }

    // THE key hallucination-rejection test: a highlight that names a REAL dimension key but one
    // this district has no data for is just as much an unsupported claim as an invented district
    // fact ("sahil kenarındadır") would be - the whole summary must be rejected before it ever
    // reaches a caller, not partially trusted.
    [Fact]
    public async Task GenerateSummaryAsync_rejects_the_entire_summary_when_every_highlight_cites_a_dimension_with_no_data()
    {
        var aiClient = new FakeAiClient
        {
            Response = """
                {"highlights":[{"dimension":"greenSpace","strength":"weak"}],
                 "summary":"Bu ilçe yeşil alanda zayıf."}
                """,
        };

        var outcome = await CreateService(aiClient).GenerateSummaryAsync("Beşiktaş", PartiallyScored(), CancellationToken.None);

        Assert.Equal(DistrictSummaryOutcomeKind.NoUsableSummary, outcome.Kind);
        Assert.Null(outcome.SummaryText);
    }

    [Fact]
    public async Task GenerateSummaryAsync_returns_NoUsableSummary_when_every_dimension_is_hallucinated()
    {
        var aiClient = new FakeAiClient
        {
            Response = """{"highlights":[{"dimension":"uydurma1","strength":"strong"}],"summary":"x"}""",
        };

        var outcome = await CreateService(aiClient).GenerateSummaryAsync("Kadıköy", FullyScored(), CancellationToken.None);

        Assert.Equal(DistrictSummaryOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateSummaryAsync_returns_NoUsableSummary_when_highlights_is_empty_even_with_summary_text()
    {
        var aiClient = new FakeAiClient
        {
            Response = """{"highlights":[],"summary":"Bu ilçe hakkında bir şeyler."}""",
        };

        var outcome = await CreateService(aiClient).GenerateSummaryAsync("Kadıköy", FullyScored(), CancellationToken.None);

        Assert.Equal(DistrictSummaryOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateSummaryAsync_returns_NoUsableSummary_when_the_summary_text_is_empty()
    {
        var aiClient = new FakeAiClient
        {
            Response = """{"highlights":[{"dimension":"airQuality","strength":"strong"}],"summary":""}""",
        };

        var outcome = await CreateService(aiClient).GenerateSummaryAsync("Kadıköy", FullyScored(), CancellationToken.None);

        Assert.Equal(DistrictSummaryOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateSummaryAsync_returns_NoUsableSummary_when_the_summary_text_is_unreasonably_long()
    {
        var aiClient = new FakeAiClient
        {
            Response = $$"""{"highlights":[{"dimension":"airQuality","strength":"strong"}],"summary":"{{new string('a', 500)}}"}""",
        };

        var outcome = await CreateService(aiClient).GenerateSummaryAsync("Kadıköy", FullyScored(), CancellationToken.None);

        Assert.Equal(DistrictSummaryOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateSummaryAsync_drops_a_highlight_with_an_invalid_strength_value()
    {
        var aiClient = new FakeAiClient
        {
            Response = """{"highlights":[{"dimension":"airQuality","strength":"aşırı güçlü"}],"summary":"x"}""",
        };

        var outcome = await CreateService(aiClient).GenerateSummaryAsync("Kadıköy", FullyScored(), CancellationToken.None);

        Assert.Equal(DistrictSummaryOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateSummaryAsync_returns_NoUsableSummary_when_the_response_is_not_valid_json()
    {
        var aiClient = new FakeAiClient { Response = "Bu bir JSON değil, düz metin." };

        var outcome = await CreateService(aiClient).GenerateSummaryAsync("Kadıköy", FullyScored(), CancellationToken.None);

        Assert.Equal(DistrictSummaryOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateSummaryAsync_tolerates_a_markdown_code_fence_around_the_json()
    {
        var aiClient = new FakeAiClient
        {
            Response = "```json\n{\"highlights\":[{\"dimension\":\"airQuality\",\"strength\":\"strong\"}],\"summary\":\"x\"}\n```",
        };

        var outcome = await CreateService(aiClient).GenerateSummaryAsync("Kadıköy", FullyScored(), CancellationToken.None);

        Assert.Equal(DistrictSummaryOutcomeKind.Ok, outcome.Kind);
    }

    [Fact]
    public async Task GenerateSummaryAsync_returns_NotConfigured_when_the_ai_client_has_no_api_key()
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantNotConfiguredException() };

        var outcome = await CreateService(aiClient).GenerateSummaryAsync("Kadıköy", FullyScored(), CancellationToken.None);

        Assert.Equal(DistrictSummaryOutcomeKind.NotConfigured, outcome.Kind);
    }

    [Fact]
    public async Task GenerateSummaryAsync_returns_RateLimited_when_the_ai_provider_rate_limits_the_call()
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantRateLimitedException() };

        var outcome = await CreateService(aiClient).GenerateSummaryAsync("Kadıköy", FullyScored(), CancellationToken.None);

        Assert.Equal(DistrictSummaryOutcomeKind.RateLimited, outcome.Kind);
    }

    [Fact]
    public async Task GenerateSummaryAsync_returns_Unavailable_on_a_generic_upstream_failure()
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantUnavailableException("network down") };

        var outcome = await CreateService(aiClient).GenerateSummaryAsync("Kadıköy", FullyScored(), CancellationToken.None);

        Assert.Equal(DistrictSummaryOutcomeKind.Unavailable, outcome.Kind);
    }

    [Fact]
    public async Task GenerateSummaryAsync_grounds_the_prompt_in_the_real_scores_including_veri_yok_for_the_missing_dimension()
    {
        var aiClient = new FakeAiClient
        {
            Response = """{"highlights":[{"dimension":"airQuality","strength":"strong"}],"summary":"x"}""",
        };

        await CreateService(aiClient).GenerateSummaryAsync("Beşiktaş", PartiallyScored(), CancellationToken.None);

        Assert.NotNull(aiClient.CapturedUserPrompt);
        // The real score (83) for a fully-scored dimension must be present verbatim...
        Assert.Contains("\"airQuality\":83", aiClient.CapturedUserPrompt);
        // ...and the missing green space dimension must show up as an honest null, never a
        // made-up number - the same "Veri yok" boundary as the rest of the app.
        Assert.Contains("\"greenSpace\":null", aiClient.CapturedUserPrompt);
    }

    private sealed class FakeAiClient : IDistrictAssistantAiClient
    {
        public string Response { get; set; } = "";
        public Exception? ExceptionToThrow { get; set; }
        public bool WasCalled { get; private set; }
        public string? CapturedUserPrompt { get; private set; }
        public string? CapturedSystemInstruction { get; private set; }

        public Task<string> GenerateAsync(string systemInstruction, string userPrompt, CancellationToken ct)
        {
            WasCalled = true;
            CapturedSystemInstruction = systemInstruction;
            CapturedUserPrompt = userPrompt;
            return ExceptionToThrow is not null
                ? Task.FromException<string>(ExceptionToThrow)
                : Task.FromResult(Response);
        }
    }
}
