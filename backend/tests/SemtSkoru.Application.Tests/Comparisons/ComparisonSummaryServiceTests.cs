using SemtSkoru.Application.Assistant;
using SemtSkoru.Application.Comparisons;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Domain;

namespace SemtSkoru.Application.Tests.Comparisons;

// Mirrors DistrictSummaryServiceTests.cs's role for the single-district summary: proves
// SemtSkoru's "never fabricate" principle holds for the compare-page AI summary even when the
// model misbehaves. This feature has one MORE way to misbehave than the single-district one does:
// it can claim district A beats district B at a dimension when the real numbers say otherwise (or
// say the same thing). These tests prove that specific claim is independently verified against
// the real scores, not just trusted because it's well-formed. Locale ("tr"/"en" - see AiLocale)
// only ever changes the free-text "summary" the model wrote - the grounding/validation logic
// below has no locale-specific branch and this file proves that stays true for both supported
// locales.
public class ComparisonSummaryServiceTests
{
    private static DimensionScore Scored(int value) =>
        new(new Score(value), DataFreshnessStatus.Fresh, "Test Source", DateTimeOffset.UtcNow);

    // Kadıköy: strong air quality (90), weak parking (20), no green space data.
    private static NeighborhoodScoreResult KadikoyScore() => new(
        "kadikoy", Scored(90), DimensionScore.NoData, Scored(50), Scored(20), Scored(50), Scored(50), new Score(52));

    // Beşiktaş: weak air quality (30), strong parking (80), no green space data either.
    private static NeighborhoodScoreResult BesiktasScore() => new(
        "besiktas", Scored(30), DimensionScore.NoData, Scored(50), Scored(80), Scored(50), Scored(50), new Score(48));

    private static NeighborhoodScoreResult Unscored(string id) => new(
        id, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData,
        DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, null);

    private static ComparisonSummaryService CreateService(FakeAiClient aiClient) => new(aiClient);

    [Fact]
    public async Task GenerateComparisonAsync_returns_InsufficientData_without_calling_the_ai_when_neither_district_shares_a_scored_dimension()
    {
        var aiClient = new FakeAiClient { Response = "irrelevant" };

        var outcome = await CreateService(aiClient).GenerateComparisonAsync(
            "Boş A", Unscored("bosA"), "Boş B", Unscored("bosB"), "tr", CancellationToken.None);

        Assert.Equal(ComparisonSummaryOutcomeKind.InsufficientData, outcome.Kind);
        Assert.Null(outcome.SummaryText);
        Assert.False(aiClient.WasCalled);
    }

    [Fact]
    public async Task GenerateComparisonAsync_returns_Ok_with_a_valid_grounded_highlight_that_matches_the_real_numbers()
    {
        var aiClient = new FakeAiClient
        {
            // Kadıköy really does have the higher air quality score (90 > 30) - a true claim.
            Response = """
                {"highlights":[{"dimension":"airQuality","strongerDistrict":"a"}],
                 "summary":"Kadıköy hava kalitesinde öne çıkıyor."}
                """,
        };

        var outcome = await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), "tr", CancellationToken.None);

        Assert.Equal(ComparisonSummaryOutcomeKind.Ok, outcome.Kind);
        Assert.Equal("Kadıköy hava kalitesinde öne çıkıyor.", outcome.SummaryText);
    }

    // Mirrors the test above but for the "en" locale with English free-text "summary" - proves
    // the "stronger district" verification against real numbers doesn't care what language the
    // free text came back in.
    [Fact]
    public async Task GenerateComparisonAsync_returns_Ok_with_a_valid_grounded_highlight_in_english_locale()
    {
        var aiClient = new FakeAiClient
        {
            Response = """
                {"highlights":[{"dimension":"airQuality","strongerDistrict":"a"}],
                 "summary":"Kadıköy stands out in air quality."}
                """,
        };

        var outcome = await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), "en", CancellationToken.None);

        Assert.Equal(ComparisonSummaryOutcomeKind.Ok, outcome.Kind);
        Assert.Equal("Kadıköy stands out in air quality.", outcome.SummaryText);
    }

    [Fact]
    public async Task GenerateComparisonAsync_drops_a_hallucinated_dimension_key_but_keeps_a_real_one()
    {
        var aiClient = new FakeAiClient
        {
            Response = """
                {"highlights":[
                    {"dimension":"airQuality","strongerDistrict":"a"},
                    {"dimension":"sahilKonumu","strongerDistrict":"a"}
                 ],
                 "summary":"Kadıköy hava kalitesinde öne çıkıyor."}
                """,
        };

        var outcome = await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), "tr", CancellationToken.None);

        Assert.Equal(ComparisonSummaryOutcomeKind.Ok, outcome.Kind);
    }

    // THE key hallucination-rejection test unique to this feature: a highlight that names a real,
    // shared dimension but gets WHICH district is stronger wrong must be rejected entirely, even
    // though every other part of it (dimension key, both-have-data) is legitimate.
    [Fact]
    public async Task GenerateComparisonAsync_rejects_the_entire_summary_when_the_claimed_stronger_district_contradicts_the_real_numbers()
    {
        var aiClient = new FakeAiClient
        {
            // Wrong: Beşiktaş's real air quality (30) is LOWER than Kadıköy's (90), not higher.
            Response = """
                {"highlights":[{"dimension":"airQuality","strongerDistrict":"b"}],
                 "summary":"Beşiktaş hava kalitesinde öne çıkıyor."}
                """,
        };

        var outcome = await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), "tr", CancellationToken.None);

        Assert.Equal(ComparisonSummaryOutcomeKind.NoUsableSummary, outcome.Kind);
        Assert.Null(outcome.SummaryText);
    }

    [Fact]
    public async Task GenerateComparisonAsync_rejects_a_highlight_that_calls_a_genuine_tie_a_win()
    {
        var aiClient = new FakeAiClient
        {
            // transportation is 50/50 for both districts in the fixtures above - a real tie.
            Response = """
                {"highlights":[{"dimension":"transportation","strongerDistrict":"a"}],
                 "summary":"Kadıköy ulaşımda öne çıkıyor."}
                """,
        };

        var outcome = await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), "tr", CancellationToken.None);

        Assert.Equal(ComparisonSummaryOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateComparisonAsync_rejects_a_highlight_citing_a_dimension_neither_district_has_data_for()
    {
        var aiClient = new FakeAiClient
        {
            // greenSpace is NoData for both districts in the fixtures above.
            Response = """
                {"highlights":[{"dimension":"greenSpace","strongerDistrict":"a"}],
                 "summary":"Kadıköy yeşil alanda öne çıkıyor."}
                """,
        };

        var outcome = await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), "tr", CancellationToken.None);

        Assert.Equal(ComparisonSummaryOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateComparisonAsync_returns_NoUsableSummary_when_every_dimension_is_hallucinated()
    {
        var aiClient = new FakeAiClient
        {
            Response = """{"highlights":[{"dimension":"uydurma1","strongerDistrict":"a"}],"summary":"x"}""",
        };

        var outcome = await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), "tr", CancellationToken.None);

        Assert.Equal(ComparisonSummaryOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateComparisonAsync_returns_NoUsableSummary_when_highlights_is_empty_even_with_summary_text()
    {
        var aiClient = new FakeAiClient
        {
            Response = """{"highlights":[],"summary":"Bu ilçeler hakkında bir şeyler."}""",
        };

        var outcome = await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), "tr", CancellationToken.None);

        Assert.Equal(ComparisonSummaryOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateComparisonAsync_returns_NoUsableSummary_when_the_summary_text_is_empty()
    {
        var aiClient = new FakeAiClient
        {
            Response = """{"highlights":[{"dimension":"airQuality","strongerDistrict":"a"}],"summary":""}""",
        };

        var outcome = await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), "tr", CancellationToken.None);

        Assert.Equal(ComparisonSummaryOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateComparisonAsync_returns_NoUsableSummary_when_the_summary_text_is_unreasonably_long()
    {
        var aiClient = new FakeAiClient
        {
            Response = $$"""{"highlights":[{"dimension":"airQuality","strongerDistrict":"a"}],"summary":"{{new string('a', 500)}}"}""",
        };

        var outcome = await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), "tr", CancellationToken.None);

        Assert.Equal(ComparisonSummaryOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateComparisonAsync_drops_a_highlight_with_an_invalid_strongerDistrict_value()
    {
        var aiClient = new FakeAiClient
        {
            Response = """{"highlights":[{"dimension":"airQuality","strongerDistrict":"ikisi de"}],"summary":"x"}""",
        };

        var outcome = await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), "tr", CancellationToken.None);

        Assert.Equal(ComparisonSummaryOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateComparisonAsync_returns_NoUsableSummary_when_the_response_is_not_valid_json()
    {
        var aiClient = new FakeAiClient { Response = "Bu bir JSON değil, düz metin." };

        var outcome = await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), "tr", CancellationToken.None);

        Assert.Equal(ComparisonSummaryOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateComparisonAsync_tolerates_a_markdown_code_fence_around_the_json()
    {
        var aiClient = new FakeAiClient
        {
            Response = "```json\n{\"highlights\":[{\"dimension\":\"airQuality\",\"strongerDistrict\":\"a\"}],\"summary\":\"x\"}\n```",
        };

        var outcome = await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), "tr", CancellationToken.None);

        Assert.Equal(ComparisonSummaryOutcomeKind.Ok, outcome.Kind);
    }

    [Fact]
    public async Task GenerateComparisonAsync_returns_NotConfigured_when_the_ai_client_has_no_api_key()
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantNotConfiguredException() };

        var outcome = await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), "tr", CancellationToken.None);

        Assert.Equal(ComparisonSummaryOutcomeKind.NotConfigured, outcome.Kind);
    }

    [Fact]
    public async Task GenerateComparisonAsync_returns_RateLimited_when_the_ai_provider_rate_limits_the_call()
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantRateLimitedException() };

        var outcome = await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), "tr", CancellationToken.None);

        Assert.Equal(ComparisonSummaryOutcomeKind.RateLimited, outcome.Kind);
    }

    [Fact]
    public async Task GenerateComparisonAsync_returns_Unavailable_on_a_generic_upstream_failure()
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantUnavailableException("network down") };

        var outcome = await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), "tr", CancellationToken.None);

        Assert.Equal(ComparisonSummaryOutcomeKind.Unavailable, outcome.Kind);
    }

    [Fact]
    public async Task GenerateComparisonAsync_grounds_the_prompt_in_the_real_scores_including_veri_yok_for_the_missing_dimension()
    {
        var aiClient = new FakeAiClient
        {
            Response = """{"highlights":[{"dimension":"airQuality","strongerDistrict":"a"}],"summary":"x"}""",
        };

        await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), "tr", CancellationToken.None);

        Assert.NotNull(aiClient.CapturedUserPrompt);
        // The real scores (90 and 30) must be present verbatim...
        Assert.Contains("\"airQuality\":90", aiClient.CapturedUserPrompt);
        Assert.Contains("\"airQuality\":30", aiClient.CapturedUserPrompt);
        // ...and the missing green space dimension must show up as an honest null for both
        // districts, never a made-up number - the same "Veri yok" boundary as the rest of the app.
        Assert.Contains("\"greenSpace\":null", aiClient.CapturedUserPrompt);
    }

    // Proves the locale actually reaches the model: the system instruction embeds a
    // human-readable target-language name (see AiLocale.ToLanguageName), not a raw locale code,
    // and it changes with the requested locale.
    [Theory]
    [InlineData("tr", "Türkçe")]
    [InlineData("en", "English")]
    [InlineData("it", "Türkçe")] // unrecognized - normalizes to the "tr" default, see AiLocale.
    [InlineData(null, "Türkçe")] // missing - same default.
    public async Task GenerateComparisonAsync_embeds_the_target_language_name_for_the_requested_locale(
        string? locale, string expectedLanguageName)
    {
        var aiClient = new FakeAiClient { Response = """{"highlights":[],"summary":""}""" };

        await CreateService(aiClient).GenerateComparisonAsync(
            "Kadıköy", KadikoyScore(), "Beşiktaş", BesiktasScore(), locale!, CancellationToken.None);

        Assert.NotNull(aiClient.CapturedSystemInstruction);
        Assert.Contains(expectedLanguageName, aiClient.CapturedSystemInstruction);
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
