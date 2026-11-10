using SemtSkoru.Application.Assistant;
using SemtSkoru.Application.Scoring;
using SemtSkoru.Application.Search;
using SemtSkoru.Domain;

namespace SemtSkoru.Application.Tests.Search;

// This is the SAFEST of this app's 4 (now 5) AI features by design - the model is only ever asked
// which of the 6 real dimensions a free-text query concerns, never to name, pick, or rank a
// district. These tests prove three things: (1) the hallucination guard on dimension keys works
// exactly like every other AI feature's own (a made-up/unknown key is dropped, not trusted), (2)
// the actual district ranking is pure application code against REAL, already-computed scores that
// never treats missing data as a zero, and (3) the model is never even given district-identifying
// data to begin with, so it has no way to influence which real districts end up in the result
// beyond the dimensions it names.
public class DistrictSearchServiceTests
{
    private static DimensionScore Scored(int value) =>
        new(new Score(value), DataFreshnessStatus.Fresh, "Test Source", DateTimeOffset.UtcNow);

    private static DistrictSearchService CreateService(FakeAiClient aiClient, FakeScoringService scoringService) =>
        new(aiClient, scoringService);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_returns_InvalidRequest_without_calling_the_ai_for_empty_input(string query)
    {
        var aiClient = new FakeAiClient { Response = "irrelevant" };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>());

        var outcome = await CreateService(aiClient, scoringService).SearchAsync(query, CancellationToken.None);

        Assert.Equal(DistrictSearchOutcomeKind.InvalidRequest, outcome.Kind);
        Assert.Empty(outcome.Dimensions);
        Assert.Empty(outcome.Matches);
        Assert.False(aiClient.WasCalled);
    }

    [Fact]
    public async Task SearchAsync_never_sends_any_district_identifying_data_to_the_model()
    {
        // Unlike DistrictAssistantService/ComparisonSummaryService, this feature's whole prompt
        // must be just the user's own words - no district ids, names, or scores anywhere in it,
        // since the model must be structurally unable to name or influence which district wins.
        var aiClient = new FakeAiClient { Response = """{"dimensions":["airQuality"]}""" };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>
        {
            ["kadikoy"] = new("kadikoy", Scored(90), Scored(90), Scored(90), Scored(90), Scored(90), Scored(90), new Score(90)),
        });

        await CreateService(aiClient, scoringService).SearchAsync("hava kalitesi iyi ilçeler", CancellationToken.None);

        Assert.NotNull(aiClient.CapturedUserPrompt);
        Assert.DoesNotContain("kadikoy", aiClient.CapturedUserPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("90", aiClient.CapturedUserPrompt);
        Assert.Contains("hava kalitesi iyi ilçeler", aiClient.CapturedUserPrompt);
    }

    [Fact]
    public async Task SearchAsync_drops_a_hallucinated_dimension_key_but_keeps_a_real_one()
    {
        var aiClient = new FakeAiClient
        {
            Response = """{"dimensions":["airQuality","fiyat"]}""",
        };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>
        {
            ["kadikoy"] = new("kadikoy", Scored(90), DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, new Score(90)),
        });

        var outcome = await CreateService(aiClient, scoringService).SearchAsync("hava kalitesi ve fiyatı iyi ilçeler", CancellationToken.None);

        Assert.Equal(DistrictSearchOutcomeKind.Ok, outcome.Kind);
        Assert.Equal(["airQuality"], outcome.Dimensions);
    }

    [Fact]
    public async Task SearchAsync_returns_NoUsableCriteria_when_every_dimension_is_hallucinated()
    {
        var aiClient = new FakeAiClient { Response = """{"dimensions":["fiyat","guvenlik"]}""" };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>());

        var outcome = await CreateService(aiClient, scoringService).SearchAsync("en ucuz ve güvenli ilçeler", CancellationToken.None);

        Assert.Equal(DistrictSearchOutcomeKind.NoUsableCriteria, outcome.Kind);
        Assert.Empty(outcome.Dimensions);
        Assert.Empty(outcome.Matches);
    }

    [Fact]
    public async Task SearchAsync_returns_NoUsableCriteria_when_the_model_honestly_reports_no_relevant_dimension()
    {
        // An empty "dimensions" array is a legitimate, well-formed model response for a genuinely
        // off-topic query (see the SystemInstruction's rule 3) - not a parse failure, but it must
        // be treated exactly the same as one: an honest "couldn't understand", never "show
        // everything".
        var aiClient = new FakeAiClient { Response = """{"dimensions":[]}""" };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>());

        var outcome = await CreateService(aiClient, scoringService).SearchAsync("gece hayatı canlı ilçeler", CancellationToken.None);

        Assert.Equal(DistrictSearchOutcomeKind.NoUsableCriteria, outcome.Kind);
    }

    [Fact]
    public async Task SearchAsync_returns_NoUsableCriteria_when_the_response_is_not_valid_json()
    {
        var aiClient = new FakeAiClient { Response = "Bu bir JSON değil, düz metin." };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>());

        var outcome = await CreateService(aiClient, scoringService).SearchAsync("bir sorgu", CancellationToken.None);

        Assert.Equal(DistrictSearchOutcomeKind.NoUsableCriteria, outcome.Kind);
    }

    [Fact]
    public async Task SearchAsync_tolerates_a_markdown_code_fence_around_the_json()
    {
        var aiClient = new FakeAiClient { Response = "```json\n{\"dimensions\":[\"airQuality\"]}\n```" };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>
        {
            ["kadikoy"] = new("kadikoy", Scored(90), DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, new Score(90)),
        });

        var outcome = await CreateService(aiClient, scoringService).SearchAsync("hava kalitesi iyi ilçeler", CancellationToken.None);

        Assert.Equal(DistrictSearchOutcomeKind.Ok, outcome.Kind);
        Assert.Equal(["airQuality"], outcome.Dimensions);
    }

    [Fact]
    public async Task SearchAsync_excludes_a_district_with_no_data_for_any_requested_dimension_rather_than_scoring_it_zero()
    {
        var aiClient = new FakeAiClient { Response = """{"dimensions":["airQuality"]}""" };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>
        {
            ["kadikoy"] = new("kadikoy", Scored(90), DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, new Score(90)),
            ["sile"] = new("sile", DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, null),
        });

        var outcome = await CreateService(aiClient, scoringService).SearchAsync("hava kalitesi iyi ilçeler", CancellationToken.None);

        Assert.Equal(DistrictSearchOutcomeKind.Ok, outcome.Kind);
        var match = Assert.Single(outcome.Matches);
        Assert.Equal("kadikoy", match.NeighborhoodId);
    }

    [Fact]
    public async Task SearchAsync_excludes_a_district_whose_average_score_is_below_the_good_threshold()
    {
        var aiClient = new FakeAiClient { Response = """{"dimensions":["airQuality"]}""" };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>
        {
            ["kadikoy"] = new("kadikoy", Scored(90), DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, new Score(90)),
            // 40 is a real reading, just a mediocre one (below the "good" cutoff of 70 that
            // web/lib/score-band.ts's getScoreBand() already uses) - recognized but not a match.
            ["uskudar"] = new("uskudar", Scored(40), DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, new Score(40)),
        });

        var outcome = await CreateService(aiClient, scoringService).SearchAsync("hava kalitesi iyi ilçeler", CancellationToken.None);

        Assert.Equal(DistrictSearchOutcomeKind.Ok, outcome.Kind);
        var match = Assert.Single(outcome.Matches);
        Assert.Equal("kadikoy", match.NeighborhoodId);
    }

    [Fact]
    public async Task SearchAsync_returns_Ok_with_empty_matches_when_a_real_dimension_is_recognized_but_nothing_currently_qualifies()
    {
        // Different, more honest outcome than NoUsableCriteria: the query WAS understood (a real
        // dimension was recognized), there just isn't a district good enough on it right now.
        var aiClient = new FakeAiClient { Response = """{"dimensions":["airQuality"]}""" };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>
        {
            ["uskudar"] = new("uskudar", Scored(40), DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, new Score(40)),
        });

        var outcome = await CreateService(aiClient, scoringService).SearchAsync("hava kalitesi iyi ilçeler", CancellationToken.None);

        Assert.Equal(DistrictSearchOutcomeKind.Ok, outcome.Kind);
        Assert.Equal(["airQuality"], outcome.Dimensions);
        Assert.Empty(outcome.Matches);
    }

    [Fact]
    public async Task SearchAsync_averages_only_the_dimensions_a_district_actually_has_data_for_not_treating_missing_ones_as_zero()
    {
        var aiClient = new FakeAiClient { Response = """{"dimensions":["airQuality","greenSpace"]}""" };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>
        {
            // airQuality=90, greenSpace missing - if missing were scored as 0, the average would
            // be 45 (below the 70 threshold) instead of the correct 90 (only the real reading).
            ["kadikoy"] = new("kadikoy", Scored(90), DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, new Score(90)),
        });

        var outcome = await CreateService(aiClient, scoringService).SearchAsync("hava kalitesi ve yeşil alanı iyi ilçeler", CancellationToken.None);

        var match = Assert.Single(outcome.Matches);
        Assert.Equal(90, match.MatchScore);
    }

    [Fact]
    public async Task SearchAsync_ranks_matches_descending_by_score()
    {
        var aiClient = new FakeAiClient { Response = """{"dimensions":["airQuality"]}""" };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>
        {
            ["kadikoy"] = new("kadikoy", Scored(80), DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, new Score(80)),
            ["besiktas"] = new("besiktas", Scored(100), DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, new Score(100)),
            ["uskudar"] = new("uskudar", Scored(90), DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, new Score(90)),
        });

        var outcome = await CreateService(aiClient, scoringService).SearchAsync("hava kalitesi iyi ilçeler", CancellationToken.None);

        Assert.Equal(["besiktas", "uskudar", "kadikoy"], outcome.Matches.Select(m => m.NeighborhoodId));
    }

    [Fact]
    public async Task SearchAsync_returns_NotConfigured_when_the_ai_client_has_no_api_key()
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantNotConfiguredException() };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>());

        var outcome = await CreateService(aiClient, scoringService).SearchAsync("bir sorgu", CancellationToken.None);

        Assert.Equal(DistrictSearchOutcomeKind.NotConfigured, outcome.Kind);
    }

    [Fact]
    public async Task SearchAsync_returns_RateLimited_when_the_ai_provider_rate_limits_the_call()
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantRateLimitedException() };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>());

        var outcome = await CreateService(aiClient, scoringService).SearchAsync("bir sorgu", CancellationToken.None);

        Assert.Equal(DistrictSearchOutcomeKind.RateLimited, outcome.Kind);
    }

    [Fact]
    public async Task SearchAsync_returns_Unavailable_on_a_generic_upstream_failure()
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantUnavailableException("network down") };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>());

        var outcome = await CreateService(aiClient, scoringService).SearchAsync("bir sorgu", CancellationToken.None);

        Assert.Equal(DistrictSearchOutcomeKind.Unavailable, outcome.Kind);
    }

    [Fact]
    public async Task SearchAsync_truncates_an_excessively_long_query()
    {
        var aiClient = new FakeAiClient { Response = """{"dimensions":[]}""" };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>());
        var longQuery = new string('a', 650) + "UNIQUE_TAIL_MARKER" + new string('b', 500);

        await CreateService(aiClient, scoringService).SearchAsync(longQuery, CancellationToken.None);

        Assert.DoesNotContain("UNIQUE_TAIL_MARKER", aiClient.CapturedUserPrompt);
    }


    // --- Keyword fallback -------------------------------------------------
    // Added after the 2026-09-22 incident, where Gemini's shared free-tier model answered 503 for
    // over a day and took the whole search box down with it. The model's only job here is to pick
    // from 6 fixed dimensions, so when it cannot be reached that one small decision is made in
    // plain code rather than losing the feature. Everything downstream is deliberately unchanged -
    // these tests exist to prove the fallback stays as honest as the normal path, not just that it
    // returns something.
    public static TheoryData<string, string> KeywordQueries => new()
    {
        { "temiz hava istiyorum", "airQuality" },
        { "yeşil alanı bol olsun", "greenSpace" },
        { "parkları çok olan ilçe", "greenSpace" },
        // Inflected forms carrying the Turkish k -> ğ softening, which is what people actually
        // type: "trafik"/"sağlık"/"durak" never appear in their bare form in a real sentence.
        { "trafiği az bir yer", "transportation" },
        { "sağlığı önemseyen biri için", "healthAccess" },
        { "durağa yürüme mesafesi", "transitAccess" },
        { "otopark bulmak kolay olsun", "parking" },
        { "hastaneye yakın olsun", "healthAccess" },
        { "metroya yakın olsun", "transitAccess" },
        { "good air quality please", "airQuality" },
        { "close to a hospital", "healthAccess" },
    };

    [Theory]
    [MemberData(nameof(KeywordQueries))]
    public async Task SearchAsync_falls_back_to_keyword_matching_when_the_model_is_unavailable(
        string query, string expectedDimension)
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantUnavailableException("down") };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>
        {
            ["kadikoy"] = new("kadikoy", Scored(90), Scored(90), Scored(90), Scored(90), Scored(90), Scored(90), new Score(90)),
        });

        var outcome = await CreateService(aiClient, scoringService).SearchAsync(query, CancellationToken.None);

        Assert.Equal(DistrictSearchOutcomeKind.Ok, outcome.Kind);
        Assert.Contains(expectedDimension, outcome.Dimensions);
        Assert.Equal(DistrictSearchMatchSource.KeywordFallback, outcome.Source);
        Assert.Equal("kadikoy", Assert.Single(outcome.Matches).NeighborhoodId);
    }

    // "otopark" contains "park", so a naive substring table would report every parking query as
    // also being about green space. The matcher anchors on word starts precisely to avoid that,
    // while still letting Turkish suffixes through ("parkları" above still matches greenSpace).
    [Fact]
    public async Task SearchAsync_keyword_fallback_does_not_confuse_otopark_with_a_green_space_park()
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantUnavailableException("down") };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>());

        var outcome = await CreateService(aiClient, scoringService).SearchAsync(
            "otopark sorunu olmayan ilçe", CancellationToken.None);

        Assert.Equal(["parking"], outcome.Dimensions);
    }

    // Typing without Turkish diacritics is completely normal on a non-Turkish keyboard layout.
    [Fact]
    public async Task SearchAsync_keyword_fallback_matches_queries_typed_without_turkish_diacritics()
    {
        var aiClient = new FakeAiClient { ExceptionToThrow = new AiAssistantUnavailableException("down") };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>());

        var outcome = await CreateService(aiClient, scoringService).SearchAsync(
            "YESIL ALAN ve SAGLIK", CancellationToken.None);

        Assert.Equal(["greenSpace", "healthAccess"], outcome.Dimensions);
    }

    // The fallback must never paper over the outage with a result it did not actually derive:
    // no keyword hit means the real AI failure is what the caller gets, unchanged.
    [Theory]
    [InlineData("NotConfigured")]
    [InlineData("RateLimited")]
    [InlineData("Unavailable")]
    public async Task SearchAsync_surfaces_the_real_ai_failure_when_no_keyword_matches(string failure)
    {
        Exception exception = failure switch
        {
            "NotConfigured" => new AiAssistantNotConfiguredException(),
            "RateLimited" => new AiAssistantRateLimitedException(),
            _ => new AiAssistantUnavailableException("down"),
        };
        var aiClient = new FakeAiClient { ExceptionToThrow = exception };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>());

        var outcome = await CreateService(aiClient, scoringService).SearchAsync(
            "burada anahtar kelime yok", CancellationToken.None);

        Assert.Equal(Enum.Parse<DistrictSearchOutcomeKind>(failure), outcome.Kind);
        Assert.Empty(outcome.Dimensions);
        Assert.Empty(outcome.Matches);
    }

    // The fallback is for a model that never answered. A model that DID answer and judged the
    // query unrelated to all 6 dimensions is a real answer, and overriding it with a blunter guess
    // would make the feature less honest rather than more available.
    [Fact]
    public async Task SearchAsync_does_not_use_the_keyword_fallback_when_the_model_answered_with_no_dimensions()
    {
        var aiClient = new FakeAiClient { Response = """{"dimensions":[]}""" };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>());

        // Contains "hava", which the keyword table would otherwise have matched.
        var outcome = await CreateService(aiClient, scoringService).SearchAsync(
            "hava durumu nasıl", CancellationToken.None);

        Assert.Equal(DistrictSearchOutcomeKind.NoUsableCriteria, outcome.Kind);
        Assert.Empty(outcome.Dimensions);
    }

    // A successful model call must stay marked as such - the transparency flag is only meaningful
    // if it actually distinguishes the two paths.
    [Fact]
    public async Task SearchAsync_marks_a_normal_model_result_as_coming_from_the_model()
    {
        var aiClient = new FakeAiClient { Response = """{"dimensions":["airQuality"]}""" };
        var scoringService = new FakeScoringService(new Dictionary<string, NeighborhoodScoreResult>
        {
            ["kadikoy"] = new("kadikoy", Scored(90), DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, DimensionScore.NoData, new Score(90)),
        });

        var outcome = await CreateService(aiClient, scoringService).SearchAsync("temiz hava", CancellationToken.None);

        Assert.Equal(DistrictSearchOutcomeKind.Ok, outcome.Kind);
        Assert.Equal(DistrictSearchMatchSource.Model, outcome.Source);
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

    private sealed class FakeScoringService(IReadOnlyDictionary<string, NeighborhoodScoreResult> scores) : INeighborhoodScoringService
    {
        public Task<NeighborhoodScoreResult?> GetScoreAsync(string neighborhoodId, CancellationToken ct) =>
            Task.FromResult(scores.GetValueOrDefault(neighborhoodId));

        public Task<IReadOnlyDictionary<string, NeighborhoodScoreResult>> GetAllScoresAsync(CancellationToken ct) =>
            Task.FromResult(scores);
    }
}
