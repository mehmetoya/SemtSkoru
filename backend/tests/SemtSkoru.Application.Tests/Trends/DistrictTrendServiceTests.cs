using SemtSkoru.Application.Assistant;
using SemtSkoru.Application.Trends;

namespace SemtSkoru.Application.Tests.Trends;

// Mirrors DistrictSummaryServiceTests's role for this feature: SemtSkoru's "never fabricate"
// principle extends here as "never let the model's text past validation unless every change it
// cites is one of the real, already-computed deltas it was given, with the real direction of
// that delta" - and separately, "never even ask the model anything when there's nothing real to
// compare" (the cold-start-shaped case: an empty delta list). These tests prove both boundaries
// hold even when the model misbehaves, not just that they should in theory.
public class DistrictTrendServiceTests
{
    private static readonly DimensionDelta ParkingIncrease = new("parking", 45, 67);
    private static readonly DimensionDelta AirQualityDecrease = new("airQuality", 80, 60);

    private static DistrictTrendService CreateService(FakeAiClient aiClient) => new(aiClient);

    [Fact]
    public async Task GenerateTrendAsync_returns_NoMeaningfulChange_without_calling_the_ai_when_given_no_deltas()
    {
        // This is exactly the shape of the cold-start state ScoreSnapshotJob is in for every
        // district until a baseline snapshot old enough to compare exists - see that job's
        // remarks. It never actually calls this service with an empty list (it skips first,
        // saving the Gemini call AND the pacing delay), but this service must never assume that
        // guarantee holds and must degrade the same honest way if it ever were called like this.
        var aiClient = new FakeAiClient { Response = "irrelevant" };

        var outcome = await CreateService(aiClient).GenerateTrendAsync("Üsküdar", [], CancellationToken.None);

        Assert.Equal(DistrictTrendOutcomeKind.NoMeaningfulChange, outcome.Kind);
        Assert.Null(outcome.SummaryText);
        Assert.False(aiClient.WasCalled);
    }

    [Fact]
    public async Task GenerateTrendAsync_returns_Ok_with_a_valid_grounded_change()
    {
        var aiClient = new FakeAiClient
        {
            Response = """
                {"changes":[{"dimension":"parking","direction":"increased"}],
                 "summary":"Otopark skoru belirgin şekilde arttı."}
                """,
        };

        var outcome = await CreateService(aiClient).GenerateTrendAsync("Kadıköy", [ParkingIncrease], CancellationToken.None);

        Assert.Equal(DistrictTrendOutcomeKind.Ok, outcome.Kind);
        Assert.Equal("Otopark skoru belirgin şekilde arttı.", outcome.SummaryText);
    }

    [Fact]
    public async Task GenerateTrendAsync_drops_a_hallucinated_dimension_key_but_keeps_a_real_one()
    {
        var aiClient = new FakeAiClient
        {
            Response = """
                {"changes":[
                    {"dimension":"parking","direction":"increased"},
                    {"dimension":"denizManzarasi","direction":"increased"}
                 ],
                 "summary":"Otopark skoru belirgin şekilde arttı."}
                """,
        };

        var outcome = await CreateService(aiClient).GenerateTrendAsync("Kadıköy", [ParkingIncrease], CancellationToken.None);

        Assert.Equal(DistrictTrendOutcomeKind.Ok, outcome.Kind);
        Assert.Equal("Otopark skoru belirgin şekilde arttı.", outcome.SummaryText);
    }

    // THE key hallucination-rejection test: a change that names a REAL delta's dimension but
    // claims the WRONG direction is just as much an unsupported/false claim as an invented
    // district fact would be - it must never reach a caller.
    [Fact]
    public async Task GenerateTrendAsync_rejects_the_entire_summary_when_every_change_claims_the_wrong_direction()
    {
        var aiClient = new FakeAiClient
        {
            Response = """
                {"changes":[{"dimension":"parking","direction":"decreased"}],
                 "summary":"Otopark skoru azaldı."}
                """,
        };

        var outcome = await CreateService(aiClient).GenerateTrendAsync("Kadıköy", [ParkingIncrease], CancellationToken.None);

        Assert.Equal(DistrictTrendOutcomeKind.NoUsableSummary, outcome.Kind);
        Assert.Null(outcome.SummaryText);
    }

    [Fact]
    public async Task GenerateTrendAsync_rejects_a_change_citing_a_dimension_that_was_not_in_the_given_delta_list()
    {
        // "airQuality" IS one of the 6 real dimensions, but it was not part of THIS request's
        // delta list - citing it here is just as unsupported as an invented dimension name.
        var aiClient = new FakeAiClient
        {
            Response = """
                {"changes":[{"dimension":"airQuality","direction":"decreased"}],
                 "summary":"Hava kalitesi azaldı."}
                """,
        };

        var outcome = await CreateService(aiClient).GenerateTrendAsync("Kadıköy", [ParkingIncrease], CancellationToken.None);

        Assert.Equal(DistrictTrendOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateTrendAsync_grounds_multiple_real_changes_with_correct_directions()
    {
        var aiClient = new FakeAiClient
        {
            Response = """
                {"changes":[
                    {"dimension":"parking","direction":"increased"},
                    {"dimension":"airQuality","direction":"decreased"}
                 ],
                 "summary":"Otopark skoru arttı, hava kalitesi skoru azaldı."}
                """,
        };

        var outcome = await CreateService(aiClient)
            .GenerateTrendAsync("Kadıköy", [ParkingIncrease, AirQualityDecrease], CancellationToken.None);

        Assert.Equal(DistrictTrendOutcomeKind.Ok, outcome.Kind);
        Assert.Equal("Otopark skoru arttı, hava kalitesi skoru azaldı.", outcome.SummaryText);
    }

    [Fact]
    public async Task GenerateTrendAsync_returns_NoUsableSummary_when_changes_is_empty_even_with_summary_text()
    {
        var aiClient = new FakeAiClient
        {
            Response = """{"changes":[],"summary":"Bir şeyler değişti."}""",
        };

        var outcome = await CreateService(aiClient).GenerateTrendAsync("Kadıköy", [ParkingIncrease], CancellationToken.None);

        Assert.Equal(DistrictTrendOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateTrendAsync_returns_NoUsableSummary_when_the_summary_text_is_empty()
    {
        var aiClient = new FakeAiClient
        {
            Response = """{"changes":[{"dimension":"parking","direction":"increased"}],"summary":""}""",
        };

        var outcome = await CreateService(aiClient).GenerateTrendAsync("Kadıköy", [ParkingIncrease], CancellationToken.None);

        Assert.Equal(DistrictTrendOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateTrendAsync_returns_NoUsableSummary_when_the_summary_text_is_unreasonably_long()
    {
        var aiClient = new FakeAiClient
        {
            Response = $$"""{"changes":[{"dimension":"parking","direction":"increased"}],"summary":"{{new string('a', 500)}}"}""",
        };

        var outcome = await CreateService(aiClient).GenerateTrendAsync("Kadıköy", [ParkingIncrease], CancellationToken.None);

        Assert.Equal(DistrictTrendOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateTrendAsync_returns_NoUsableSummary_when_the_response_is_not_valid_json()
    {
        var aiClient = new FakeAiClient { Response = "Bu bir JSON değil, düz metin." };

        var outcome = await CreateService(aiClient).GenerateTrendAsync("Kadıköy", [ParkingIncrease], CancellationToken.None);

        Assert.Equal(DistrictTrendOutcomeKind.NoUsableSummary, outcome.Kind);
    }

    [Fact]
    public async Task GenerateTrendAsync_tolerates_a_markdown_code_fence_around_the_json()
    {
        var aiClient = new FakeAiClient
        {
            Response = "```json\n{\"changes\":[{\"dimension\":\"parking\",\"direction\":\"increased\"}],\"summary\":\"x\"}\n```",
        };

        var outcome = await CreateService(aiClient).GenerateTrendAsync("Kadıköy", [ParkingIncrease], CancellationToken.None);

        Assert.Equal(DistrictTrendOutcomeKind.Ok, outcome.Kind);
    }

    [Fact]
    public async Task GenerateTrendAsync_returns_NotConfigured_when_the_ai_client_has_no_api_key()
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantNotConfiguredException() };

        var outcome = await CreateService(aiClient).GenerateTrendAsync("Kadıköy", [ParkingIncrease], CancellationToken.None);

        Assert.Equal(DistrictTrendOutcomeKind.NotConfigured, outcome.Kind);
    }

    [Fact]
    public async Task GenerateTrendAsync_returns_RateLimited_when_the_ai_provider_rate_limits_the_call()
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantRateLimitedException() };

        var outcome = await CreateService(aiClient).GenerateTrendAsync("Kadıköy", [ParkingIncrease], CancellationToken.None);

        Assert.Equal(DistrictTrendOutcomeKind.RateLimited, outcome.Kind);
    }

    [Fact]
    public async Task GenerateTrendAsync_returns_Unavailable_on_a_generic_upstream_failure()
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantUnavailableException("network down") };

        var outcome = await CreateService(aiClient).GenerateTrendAsync("Kadıköy", [ParkingIncrease], CancellationToken.None);

        Assert.Equal(DistrictTrendOutcomeKind.Unavailable, outcome.Kind);
    }

    [Fact]
    public async Task GenerateTrendAsync_grounds_the_prompt_in_the_real_delta_values_never_the_reason_for_the_change()
    {
        var aiClient = new FakeAiClient
        {
            Response = """{"changes":[{"dimension":"parking","direction":"increased"}],"summary":"x"}""",
        };

        await CreateService(aiClient).GenerateTrendAsync("Kadıköy", [ParkingIncrease], CancellationToken.None);

        Assert.NotNull(aiClient.CapturedUserPrompt);
        Assert.Contains("\"previousScore\":45", aiClient.CapturedUserPrompt);
        Assert.Contains("\"currentScore\":67", aiClient.CapturedUserPrompt);
        // Never claims to know WHY - the system instruction (not the per-request prompt) is
        // what forbids this; verify here that no reason/cause field is even part of the shape
        // being sent, so there is nothing for the model to latch onto.
        Assert.DoesNotContain("reason", aiClient.CapturedUserPrompt, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeAiClient : IDistrictAssistantAiClient
    {
        public string Response { get; set; } = "";
        public Exception? ExceptionToThrow { get; set; }
        public bool WasCalled { get; private set; }
        public string? CapturedUserPrompt { get; private set; }

        public Task<string> GenerateAsync(string systemInstruction, string userPrompt, CancellationToken ct)
        {
            WasCalled = true;
            CapturedUserPrompt = userPrompt;
            return ExceptionToThrow is not null
                ? Task.FromException<string>(ExceptionToThrow)
                : Task.FromResult(Response);
        }
    }
}
