using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
        // Every one of these means the model never gave an answer, so there is nothing to be
        // honest ABOUT yet - the keyword table below gets a turn before the failure is passed on.
        // Contrast with NoUsableCriteria further down, which is NOT routed here: there the model
        // did answer and judged the query unrelated to all 6 dimensions, and second-guessing a
        // real answer with a blunter one would make the feature less honest, not more available.
        catch (AiAssistantNotConfiguredException)
        {
            return await KeywordFallbackAsync(trimmedQuery, DistrictSearchOutcome.NotConfigured, ct);
        }
        catch (AiAssistantRateLimitedException)
        {
            return await KeywordFallbackAsync(trimmedQuery, DistrictSearchOutcome.RateLimited, ct);
        }
        catch (AiAssistantUnavailableException)
        {
            return await KeywordFallbackAsync(trimmedQuery, DistrictSearchOutcome.Unavailable, ct);
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

    /// <summary>
    /// Last resort when Gemini cannot be reached at all. The model's only job in this feature is
    /// to decide which of 6 fixed dimensions a query concerns - a small, closed classification,
    /// not open-ended writing - so when it is unavailable the same decision can be approximated
    /// in plain code instead of taking the whole search box down with it. Everything downstream
    /// is untouched: the same RankDistricts pass over the same real scores produces the matches,
    /// so this path can never invent a district or a number, only be blunter about which criteria
    /// it recognized. When even the keywords find nothing, the original AI failure is returned
    /// unchanged rather than an empty result that would imply we understood the query and found
    /// no districts.
    /// </summary>
    private async Task<DistrictSearchOutcome> KeywordFallbackAsync(
        string query, DistrictSearchOutcome aiFailureOutcome, CancellationToken ct)
    {
        var dimensions = ExtractDimensionsByKeyword(query);
        if (dimensions.Count == 0)
        {
            return aiFailureOutcome;
        }

        var scores = await scoringService.GetAllScoresAsync(ct);
        return DistrictSearchOutcome.Ok(
            dimensions, RankDistricts(scores, dimensions), DistrictSearchMatchSource.KeywordFallback);
    }

    // Turkish and English cues for each of the 6 real dimensions, stored already folded (see Fold)
    // so "yeşil", "yesil" and "YEŞİL" are all the same entry. Order matches KnownDimensions.
    // Deliberately short and literal: this is a fallback meant to catch the obvious phrasings a
    // visitor actually types, not a second natural-language system competing with the model.
    private static readonly (string Dimension, string[] Keywords)[] KeywordTable =
    [
        ("airQuality", ["hava", "temiz hava", "kirlilik", "kirli", "nefes", "air", "pollution", "smog"]),
        ("greenSpace", ["yesil", "park", "agac", "orman", "doga", "bahce", "green", "tree", "nature", "forest"]),
        ("transportation", ["trafik", "ulasim", "yol", "sikisik", "traffic", "congestion", "commute"]),
        ("parking", ["otopark", "park yeri", "arac", "araba", "parking"]),
        ("healthAccess", ["saglik", "hastane", "doktor", "eczane", "health", "hospital", "clinic", "pharmacy"]),
        ("transitAccess", ["metro", "otobus", "toplu tasima", "durak", "metrobus", "tramvay", "vapur", "transit", "bus", "subway", "public transport"]),
    ];

    // Word-START matching, not "contains": Turkish is agglutinative, so "parklar"/"parkları" must
    // match "park" - but "otopark" must NOT, or every parking query would also claim to be about
    // green space. A leading \b plus no trailing boundary is exactly that rule, and it is why the
    // table can stay this small without the suffix explosion a whole-word match would need.
    private static readonly Regex[] KeywordPatterns = KeywordTable
        .Select(entry => new Regex(
            string.Join("|", entry.Keywords.SelectMany(WithConsonantMutation).Select(k => @"\b" + Regex.Escape(k))),
            RegexOptions.Compiled | RegexOptions.CultureInvariant))
        .ToArray();

    // Turkish softens a final "k" to "ğ" before a vowel suffix - "trafik" becomes "trafiği",
    // "sağlık" becomes "sağlığı", "durak" becomes "durağı" - and Fold turns that "ğ" into a "g".
    // A word-start match on the bare stem would therefore miss every inflected form, which is the
    // form people actually type. Rather than listing both spellings for each affected keyword by
    // hand (and forgetting one), the "g" variant is derived here from the rule itself.
    private static IEnumerable<string> WithConsonantMutation(string keyword) =>
        keyword.EndsWith('k') ? [keyword, string.Concat(keyword.AsSpan(0, keyword.Length - 1), "g")] : [keyword];

    private static List<string> ExtractDimensionsByKeyword(string query)
    {
        var folded = Fold(query);
        var result = new List<string>();
        for (var i = 0; i < KeywordTable.Length; i++)
        {
            if (KeywordPatterns[i].IsMatch(folded))
            {
                result.Add(KeywordTable[i].Dimension);
            }
        }

        return result;
    }

    // Lowercases and strips Turkish diacritics so a visitor typing "yesil alan" from a keyboard
    // layout without them matches the same entry as "yeşil alan". The dotted/dotless i pair is
    // mapped explicitly BEFORE the invariant lowercase, because invariant casing does not know
    // that "İ" folds to "i" in Turkish - getting that wrong would silently break every keyword
    // starting with one.
    private static string Fold(string value)
    {
        var mapped = value
            .Replace('İ', 'i').Replace('I', 'i').Replace('ı', 'i')
            .Replace('Ş', 's').Replace('ş', 's')
            .Replace('Ğ', 'g').Replace('ğ', 'g')
            .Replace('Ü', 'u').Replace('ü', 'u')
            .Replace('Ö', 'o').Replace('ö', 'o')
            .Replace('Ç', 'c').Replace('ç', 'c')
            .ToLowerInvariant();

        // Catches any remaining accented forms (e.g. "â" in older spellings) without a second table.
        var decomposed = mapped.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
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
