using SemtSkoru.Application.Assistant;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Domain;

namespace SemtSkoru.Application.Tests.Assistant;

// The most important test file in this feature: SemtSkoru's one non-negotiable principle
// ("never fabricate or estimate data") extends to the AI assistant as "never let the model's
// text past validation unless every district id it names is one of the real ones we gave it."
// These tests prove that boundary holds even when the model misbehaves - hallucinated ids,
// invalid JSON, too many recommendations, duplicates - not just that it should in theory.
public class DistrictAssistantServiceTests
{
    private static readonly DimensionScore FullDimension = new(new Score(83), DataFreshnessStatus.Fresh, "Test Source", DateTimeOffset.UtcNow);

    private static readonly Dictionary<string, string> RealNames = new()
    {
        ["kadikoy"] = "Kadıköy",
        ["besiktas"] = "Beşiktaş",
        ["uskudar"] = "Üsküdar",
        ["atasehir"] = "Ataşehir",
    };

    private static readonly Dictionary<string, NeighborhoodScoreResult> RealScores = new()
    {
        ["kadikoy"] = FullResult("kadikoy"),
        ["besiktas"] = new NeighborhoodScoreResult(
            "besiktas", FullDimension, DimensionScore.NoData, FullDimension, FullDimension, FullDimension, FullDimension, new Score(80)),
        ["uskudar"] = FullResult("uskudar"),
        ["atasehir"] = FullResult("atasehir"),
    };

    private static NeighborhoodScoreResult FullResult(string id) =>
        new(id, FullDimension, FullDimension, FullDimension, FullDimension, FullDimension, FullDimension, new Score(83));

    private static DistrictAssistantService CreateService(FakeAiClient aiClient) =>
        new(aiClient, new FakeDirectory(RealNames), new FakeScoringService(RealScores));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetRecommendationsAsync_returns_InvalidRequest_without_calling_the_ai_for_empty_input(string query)
    {
        var aiClient = new FakeAiClient { Response = "irrelevant" };

        var outcome = await CreateService(aiClient).GetRecommendationsAsync(query, CancellationToken.None);

        Assert.Equal(AssistantOutcomeKind.InvalidRequest, outcome.Kind);
        Assert.Empty(outcome.Recommendations);
        Assert.False(aiClient.WasCalled);
    }

    [Fact]
    public async Task GetRecommendationsAsync_drops_a_hallucinated_district_id_and_keeps_the_real_one()
    {
        // "hayaliilce" ("imaginary district" in Turkish) is not one of RealNames' 4 ids - this is
        // exactly the case where the model invents something that isn't real. It must never
        // reach a caller.
        var aiClient = new FakeAiClient
        {
            Response = """
                {"oneriler":[
                    {"id":"kadikoy","aciklama":"Hava kalitesi skoru 83 ile yüksek."},
                    {"id":"hayaliilce","aciklama":"Bu ilçe gerçek değil."}
                ],"guven":"yuksek"}
                """,
        };

        var outcome = await CreateService(aiClient).GetRecommendationsAsync("Hava kalitesi önemli", CancellationToken.None);

        Assert.Equal(AssistantOutcomeKind.Ok, outcome.Kind);
        var recommendation = Assert.Single(outcome.Recommendations);
        Assert.Equal("kadikoy", recommendation.NeighborhoodId);
        Assert.DoesNotContain(outcome.Recommendations, r => r.NeighborhoodId == "hayaliilce");
    }

    [Fact]
    public async Task GetRecommendationsAsync_returns_NoUsableRecommendations_when_every_id_is_hallucinated()
    {
        var aiClient = new FakeAiClient
        {
            Response = """{"oneriler":[{"id":"uydurma1","aciklama":"x"},{"id":"uydurma2","aciklama":"y"}]}""",
        };

        var outcome = await CreateService(aiClient).GetRecommendationsAsync("bir istek", CancellationToken.None);

        Assert.Equal(AssistantOutcomeKind.NoUsableRecommendations, outcome.Kind);
        Assert.Empty(outcome.Recommendations);
    }

    [Fact]
    public async Task GetRecommendationsAsync_returns_NoUsableRecommendations_when_the_response_is_not_valid_json()
    {
        var aiClient = new FakeAiClient { Response = "Bu bir JSON değil, düz metin." };

        var outcome = await CreateService(aiClient).GetRecommendationsAsync("bir istek", CancellationToken.None);

        Assert.Equal(AssistantOutcomeKind.NoUsableRecommendations, outcome.Kind);
    }

    [Fact]
    public async Task GetRecommendationsAsync_tolerates_a_markdown_code_fence_around_the_json()
    {
        var aiClient = new FakeAiClient
        {
            Response = "```json\n{\"oneriler\":[{\"id\":\"kadikoy\",\"aciklama\":\"Skoru yüksek.\"}]}\n```",
        };

        var outcome = await CreateService(aiClient).GetRecommendationsAsync("bir istek", CancellationToken.None);

        Assert.Equal(AssistantOutcomeKind.Ok, outcome.Kind);
        Assert.Single(outcome.Recommendations);
    }

    [Fact]
    public async Task GetRecommendationsAsync_caps_recommendations_at_three_even_if_the_model_returns_more()
    {
        var aiClient = new FakeAiClient
        {
            Response = """
                {"oneriler":[
                    {"id":"kadikoy","aciklama":"a"},
                    {"id":"besiktas","aciklama":"b"},
                    {"id":"uskudar","aciklama":"c"},
                    {"id":"atasehir","aciklama":"d"}
                ]}
                """,
        };

        var outcome = await CreateService(aiClient).GetRecommendationsAsync("bir istek", CancellationToken.None);

        Assert.Equal(AssistantOutcomeKind.Ok, outcome.Kind);
        Assert.Equal(3, outcome.Recommendations.Count);
        Assert.Equal(["kadikoy", "besiktas", "uskudar"], outcome.Recommendations.Select(r => r.NeighborhoodId));
    }

    [Fact]
    public async Task GetRecommendationsAsync_drops_a_duplicate_id_keeping_only_the_first_occurrence()
    {
        var aiClient = new FakeAiClient
        {
            Response = """
                {"oneriler":[
                    {"id":"kadikoy","aciklama":"ilk"},
                    {"id":"kadikoy","aciklama":"tekrar"}
                ]}
                """,
        };

        var outcome = await CreateService(aiClient).GetRecommendationsAsync("bir istek", CancellationToken.None);

        var recommendation = Assert.Single(outcome.Recommendations);
        Assert.Equal("ilk", recommendation.Reasoning);
    }

    [Fact]
    public async Task GetRecommendationsAsync_returns_NotConfigured_when_the_ai_client_has_no_api_key()
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantNotConfiguredException() };

        var outcome = await CreateService(aiClient).GetRecommendationsAsync("bir istek", CancellationToken.None);

        Assert.Equal(AssistantOutcomeKind.NotConfigured, outcome.Kind);
    }

    [Fact]
    public async Task GetRecommendationsAsync_returns_RateLimited_when_the_ai_provider_rate_limits_the_call()
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantRateLimitedException() };

        var outcome = await CreateService(aiClient).GetRecommendationsAsync("bir istek", CancellationToken.None);

        Assert.Equal(AssistantOutcomeKind.RateLimited, outcome.Kind);
    }

    [Fact]
    public async Task GetRecommendationsAsync_returns_Unavailable_on_a_generic_upstream_failure()
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantUnavailableException("network down") };

        var outcome = await CreateService(aiClient).GetRecommendationsAsync("bir istek", CancellationToken.None);

        Assert.Equal(AssistantOutcomeKind.Unavailable, outcome.Kind);
    }

    [Fact]
    public async Task GetRecommendationsAsync_grounds_the_prompt_in_the_real_scores_including_veri_yok_for_missing_dimensions()
    {
        var aiClient = new FakeAiClient { Response = """{"oneriler":[{"id":"kadikoy","aciklama":"x"}]}""" };

        await CreateService(aiClient).GetRecommendationsAsync("Çocuklu bir aileyiz, yeşil alan önemli", CancellationToken.None);

        Assert.NotNull(aiClient.CapturedUserPrompt);
        // The real score (83) for a fully-scored district must be present verbatim...
        Assert.Contains("\"genelSkor\":83", aiClient.CapturedUserPrompt);
        // ...and besiktas's missing green space dimension must show up as an honest null, never
        // a made-up number - this is the same "Veri yok" boundary as the rest of the app.
        Assert.Contains("\"besiktas\"", aiClient.CapturedUserPrompt);
        var besiktasIndex = aiClient.CapturedUserPrompt!.IndexOf("\"besiktas\"", StringComparison.Ordinal);
        var besiktasBlock = aiClient.CapturedUserPrompt[besiktasIndex..Math.Min(besiktasIndex + 200, aiClient.CapturedUserPrompt.Length)];
        Assert.Contains("\"yesilAlan\":null", besiktasBlock);
    }

    [Fact]
    public async Task GetRecommendationsAsync_truncates_an_excessively_long_free_text_request()
    {
        var aiClient = new FakeAiClient { Response = """{"oneriler":[{"id":"kadikoy","aciklama":"x"}]}""" };
        var longQuery = new string('a', 650) + "UNIQUE_TAIL_MARKER" + new string('b', 500);

        await CreateService(aiClient).GetRecommendationsAsync(longQuery, CancellationToken.None);

        Assert.DoesNotContain("UNIQUE_TAIL_MARKER", aiClient.CapturedUserPrompt);
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

    private sealed class FakeDirectory(IReadOnlyDictionary<string, string> names) : INeighborhoodDirectory
    {
        public Task<IReadOnlyDictionary<string, string>> GetAllNamesAsync(CancellationToken ct) => Task.FromResult(names);
    }

    private sealed class FakeScoringService(IReadOnlyDictionary<string, NeighborhoodScoreResult> scores) : INeighborhoodScoringService
    {
        public Task<NeighborhoodScoreResult?> GetScoreAsync(string neighborhoodId, CancellationToken ct) =>
            Task.FromResult(scores.GetValueOrDefault(neighborhoodId));

        public Task<IReadOnlyDictionary<string, NeighborhoodScoreResult>> GetAllScoresAsync(CancellationToken ct) =>
            Task.FromResult(scores);
    }
}
