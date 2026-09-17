using System.Text.Json;
using System.Text.Json.Serialization;
using SemtSkoru.Application.Assistant;
using SemtSkoru.Application.Scoring;

namespace SemtSkoru.Application.Search;

/// <summary>
/// Powers the home page's natural-language district search/filter box. This is the one AI feature
/// in the app deliberately built to be SAFER than the other three (AI Semt Asistanı, district
/// summary, comparison summary): those all ask Gemini to write prose about, or pick/rank, real
/// districts by name. This one never does either. The model is asked ONLY which of the app's 6
/// real, hardcoded dimensions ("airQuality","greenSpace","transportation","parking",
/// "healthAccess","transitAccess") the user's free-text query concerns - it never sees a single
/// district's name or score, and it is structurally incapable of naming, ranking, or fabricating
/// one, because the prompt never gives it that information in the first place. Every dimension it
/// names is still independently checked against the same hardcoded KnownDimensions set every
/// other AI feature validates against (see e.g. ComparisonSummaryService's own remarks) - a
/// hallucinated/invented dimension name is silently dropped, never trusted.
///
/// The actual work of turning "which dimensions matter" into "which real districts match" is
/// pure application code against INeighborhoodScoringService's real, already-computed scores
/// (RankDistricts below) - the same interface DistrictAssistantService already uses, reused here
/// rather than a second scoring abstraction. Reuses IDistrictAssistantAiClient - the same
/// interface/Gemini client every other AI feature in this app shares (see
/// DistrictSummaryService's own remarks for why one client abstraction is enough for the whole
/// app) - rather than a second AI client. Because the model never writes any free text for this
/// feature (its whole output is a handful of fixed dimension keys, never prose), this service has
/// no locale-specific branch and takes no locale parameter at all - contrast with
/// DistrictAssistantService/ComparisonSummaryService/DistrictSummaryService, which all thread a
/// locale through for the free-text sentence they generate (see
/// SemtSkoru.Application.Localization.AiLocale).
/// </summary>
public sealed class DistrictSearchService(
    IDistrictAssistantAiClient aiClient,
    INeighborhoodScoringService scoringService) : IDistrictSearchService
{
    // Same defensive upper bound/rationale as DistrictAssistantService.MaxUserQueryLength - bounds
    // prompt/token size and blocks a trivial abuse vector independently of the request-rate
    // limiting in RateLimiting/RateLimitingExtensions.cs.
    private const int MaxQueryLength = 600;

    // Byte-identical to every other AI feature's own KnownDimensions set (see
    // ComparisonSummaryService, DistrictAssistantService, DistrictSummaryService) - the one set of
    // real dimension keys this whole app ever validates AI output against.
    private static readonly HashSet<string> KnownDimensions = new(StringComparer.Ordinal)
    {
        "airQuality", "greenSpace", "transportation", "parking", "healthAccess", "transitAccess",
    };

    // A district only counts as a "match" once its average score on the requested dimension(s) is
    // at least this good - not merely above the 0-100 scale's bare midpoint. Reuses, rather than
    // reinvents, the exact cutoff web/lib/score-band.ts's getScoreBand() already uses for its
    // "good" (green) band everywhere else in this app's UI (district cards, score bars) - so a
    // district this endpoint calls a match is one the rest of the app would also show as "İyi" for
    // that dimension, not a fresh, unverifiable threshold invented just for search.
    private const int GoodScoreThreshold = 70;

    private const string SystemInstruction =
        """
        Sen SemtSkoru uygulamasının "Doğal Dilde İlçe Arama" özelliğisin. SemtSkoru, İstanbul'un 39
        ilçesini SADECE gerçek İBB (İstanbul Büyükşehir Belediyesi) açık verisinden hesaplanan 6
        boyutta (hava kalitesi, yeşil alan, ulaşım, otopark, sağlık erişimi, toplu taşıma erişimi)
        karşılaştıran bir uygulamadır. Görevin, kullanıcının serbest metinle yazdığı tercihin BU 6
        boyuttan HANGİLERİYLE ilgili olduğunu belirlemek.

        KESİNLİKLE UYULMASI GEREKEN KURALLAR:
        1. SADECE şu 6 boyut anahtarını kullanabilirsin: "airQuality", "greenSpace",
           "transportation", "parking", "healthAccess", "transitAccess". Başka HİÇBİR boyut
           anahtarı ASLA üretme veya icat etme.
        2. Görevin SADECE hangi boyut(lar)ın sorguyla ilgili olduğunu belirlemektir. HİÇBİR ilçe
           adı, skor değeri, sıralama veya öneri ÜRETME - ilçeleri seçmek, adlandırmak veya
           sıralamak tamamen ayrı bir sistemin, gerçek verilerle yapacağı bir iştir; sana hiçbir
           ilçe verisi de verilmeyecek.
        3. Sorgu bu 6 boyuttan HİÇBİRİYLE açıkça ilgili değilse (ör. bu uygulamanın verisi OLMAYAN
           bir konu - fiyat, güvenlik, gece hayatı, eğitim gibi - soruluyorsa) "dimensions"
           dizisini boş bırak; en yakın boyutu zorlayarak tahmin ETME.
        4. Yanıtın SADECE aşağıdaki şemaya uyan geçerli bir JSON nesnesi olmalı. JSON dışında hiçbir
           açıklama, markdown veya kod bloğu ekleme:
           {"dimensions":["<6 boyuttan sıfır veya daha fazlası, ör. \"airQuality\">"]}
        """;

    public async Task<DistrictSearchOutcome> SearchAsync(string query, CancellationToken ct)
    {
        var trimmedQuery = query?.Trim() ?? "";
        if (trimmedQuery.Length == 0)
        {
            return DistrictSearchOutcome.InvalidRequest;
        }

        if (trimmedQuery.Length > MaxQueryLength)
        {
            trimmedQuery = trimmedQuery[..MaxQueryLength];
        }

        string rawResponse;
        try
        {
            rawResponse = await aiClient.GenerateAsync(SystemInstruction, BuildUserPrompt(trimmedQuery), ct);
        }
        catch (AiAssistantNotConfiguredException)
        {
            return DistrictSearchOutcome.NotConfigured;
        }
        catch (AiAssistantRateLimitedException)
        {
            return DistrictSearchOutcome.RateLimited;
        }
        catch (AiAssistantUnavailableException)
        {
            return DistrictSearchOutcome.Unavailable;
        }

        var parsed = TryParseModelResponse(rawResponse);
        var dimensions = ValidateDimensions(parsed);
        if (dimensions.Count == 0)
        {
            return DistrictSearchOutcome.NoUsableCriteria;
        }

        var scores = await scoringService.GetAllScoresAsync(ct);
        var matches = RankDistricts(scores, dimensions);

        return DistrictSearchOutcome.Ok(dimensions, matches);
    }

    // Deliberately just the query - no district data, no "here are the 39 real districts" JSON
    // block like DistrictAssistantService's prompt has. The model has nothing here it COULD name
    // or fabricate a district from even if it tried; it only ever sees the free text it must map
    // onto the 6 known dimension keys.
    private static string BuildUserPrompt(string query) =>
        $"""
        Kullanıcının arama sorgusu: "{query}"
        """;

    private static ModelResponsePayload? TryParseModelResponse(string rawResponse)
    {
        var cleaned = StripMarkdownCodeFence(rawResponse);
        try
        {
            return JsonSerializer.Deserialize<ModelResponsePayload>(cleaned, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Live models asked for JSON-only output still occasionally wrap it in a ```json fence -
    // tolerate that rather than treating an otherwise-perfectly-usable response as unusable.
    // Mirrors every other AI feature's own fence-stripper (kept independent, not shared, so this
    // file stays self-contained per the house style).
    private static string StripMarkdownCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0)
        {
            return trimmed;
        }

        var withoutOpeningFence = trimmed[(firstNewline + 1)..];
        var closingFenceIndex = withoutOpeningFence.LastIndexOf("```", StringComparison.Ordinal);
        return closingFenceIndex >= 0 ? withoutOpeningFence[..closingFenceIndex].Trim() : withoutOpeningFence.Trim();
    }

    // The hallucination gate: a dimension key the model invents (or spells differently, or
    // translates) is silently dropped here - it never reaches a caller, and an empty result after
    // this filtering is exactly as honest an outcome as the model explicitly returning no
    // dimensions at all (see SearchAsync's NoUsableCriteria branch).
    private static List<string> ValidateDimensions(ModelResponsePayload? payload)
    {
        var result = new List<string>();
        if (payload?.Dimensions is null)
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in payload.Dimensions)
        {
            var dimension = raw?.Trim();
            if (string.IsNullOrEmpty(dimension) || !KnownDimensions.Contains(dimension))
            {
                continue; // hallucinated/unknown dimension key - dropped, never reaches a caller.
            }

            if (seen.Add(dimension))
            {
                result.Add(dimension);
            }
        }

        return result;
    }

    // Ranks every REAL district (from the same NeighborhoodScoreResult data every other page
    // shows, never recomputed or faked) by the equal-weight average of its own real score on just
    // the requested dimensions it actually HAS data for - a district missing data for every
    // requested dimension is excluded entirely from the results, never scored as if a missing
    // reading were a 0 (the same "Veri yok isn't 0" convention NeighborhoodScoringService.Overall
    // already follows for the overall score). Only districts whose resulting average clears
    // GoodScoreThreshold are returned as an honest "match" - a real but mediocre score doesn't
    // count just because the dimension itself was recognized.
    private static List<DistrictSearchMatch> RankDistricts(
        IReadOnlyDictionary<string, NeighborhoodScoreResult> scores, IReadOnlyList<string> dimensions)
    {
        var requested = new HashSet<string>(dimensions, StringComparer.Ordinal);
        var matches = new List<DistrictSearchMatch>();

        foreach (var (id, score) in scores)
        {
            var values = SelectedDimensionValues(score, requested);
            if (values.Count == 0)
            {
                continue; // no real data for ANY requested dimension - excluded, not penalized.
            }

            var averageScore = (int)Math.Round(values.Average());
            if (averageScore >= GoodScoreThreshold)
            {
                matches.Add(new DistrictSearchMatch(id, averageScore));
            }
        }

        return matches
            .OrderByDescending(m => m.MatchScore)
            .ThenBy(m => m.NeighborhoodId, StringComparer.Ordinal)
            .ToList();
    }

    private static List<int> SelectedDimensionValues(NeighborhoodScoreResult score, IReadOnlySet<string> requested)
    {
        var values = new List<int>();
        void AddIfRequestedAndPresent(string key, DimensionScore dimension)
        {
            if (requested.Contains(key) && dimension.HasData)
            {
                values.Add(dimension.Value!.Value.Value);
            }
        }

        AddIfRequestedAndPresent("airQuality", score.AirQuality);
        AddIfRequestedAndPresent("greenSpace", score.GreenSpace);
        AddIfRequestedAndPresent("transportation", score.Transportation);
        AddIfRequestedAndPresent("parking", score.Parking);
        AddIfRequestedAndPresent("healthAccess", score.HealthAccess);
        AddIfRequestedAndPresent("transitAccess", score.TransitAccess);
        return values;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record ModelResponsePayload(
        [property: JsonPropertyName("dimensions")] List<string?>? Dimensions);
}
