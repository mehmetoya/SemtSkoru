using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SemtSkoru.Application.Localization;
using SemtSkoru.Application.Scoring;

namespace SemtSkoru.Application.Assistant;

/// <summary>
/// Orchestrates the AI Semt Asistanı feature: builds a prompt out of the REAL, already-computed
/// score data (never recomputes or fakes it - see INeighborhoodScoringService), asks the
/// configured LLM to pick 2-3 districts, and - this is the part SPEC.md's "never fabricate a
/// score" principle actually depends on - validates every single thing the model says before any
/// of it reaches a caller: an id it invents is silently dropped (not surfaced, not crashed on),
/// and an unusable response comes back as an honest "couldn't recommend confidently" outcome
/// rather than a guess.
/// </summary>
public sealed class DistrictAssistantService(
    IDistrictAssistantAiClient aiClient,
    INeighborhoodDirectory directory,
    INeighborhoodScoringService scoringService) : IDistrictAssistantService
{
    // Bounds prompt/token size and blocks a trivial abuse vector (pasting megabytes of text)
    // independently of the request-rate limiting in RateLimiting/RateLimitingExtensions.cs -
    // a few sentences of preferences is the whole point of this feature.
    private const int MaxUserQueryLength = 600;

    private const int MaxRecommendations = 3;

    private const string BaseSystemInstruction =
        """
        Sen SemtSkoru uygulamasının "AI Semt Asistanı" özelliğisin. SemtSkoru, İstanbul'un 39
        ilçesini yalnızca gerçek İBB (İstanbul Büyükşehir Belediyesi) açık verisinden hesaplanan
        skorlarla karşılaştıran bir uygulamadır. Görevin, kullanıcının serbest metinle yazdığı
        tercihleri, sana JSON olarak verilen GERÇEK ve GÜNCEL ilçe skorlarıyla eşleştirip en fazla
        3 ilçe önermek.

        KESİNLİKLE UYULMASI GEREKEN KURALLAR:
        1. SADECE sana verilen ilçe listesindeki "id" değerlerini kullanabilirsin. Listede
           bulunmayan bir id'yi ASLA üretme veya tahmin etme.
        2. Gerekçeni SADECE sana verilen sayısal skorlara dayandır. İstanbul ilçeleri hakkında
           kendi genel bilgini, eğitim verini veya dışarıdan bildiğin hiçbir gerçeği (ör. "sahil
           kenarındadır", "pahalı bir semttir", "tarihi bir bölgedir" gibi) ASLA ekleme veya
           iddia etme. Sadece verilen sayılardan bahset.
        3. Bir ilçenin bir boyutunun değeri null ise ("veri yok" demektir), o boyut hakkında
           hiçbir şey iddia etme veya varsayma.
        4. Kullanıcının isteğine güvenle uyacak yeterli veri yoksa, ilçe sayısını zorlamak yerine
           "recommendations" dizisini kısa tut veya boş bırak; "confidence":"low" işaretle.
        5. Yanıtın SADECE aşağıdaki şemaya uyan geçerli bir JSON nesnesi olmalı. JSON dışında
           hiçbir açıklama, markdown veya kod bloğu ekleme:
           {"recommendations":[{"id":"<ilçe listesinden bir id>","reasoning":"<verilen skorlara
           atıfta bulunan, 1-2 cümlelik gerekçe>"}],"confidence":"high"|"low"}
        """;

    // Only the free-text "reasoning" sentence changes with locale - every JSON key and every
    // enum-like value ("id", "confidence": "high"/"low") must come back byte-for-byte as given,
    // because ValidateAndGround below checks them with literal string equality against REAL data.
    // If the model ever translated an id or "confidence" value, every response would silently
    // fail that check and get dropped as unverifiable - see this class's own remarks. Appended as
    // its own rule (not folded into the Turkish rule text above) so it's unmistakable and easy to
    // audit independently of the (deliberately still-Turkish, developer-authored) rules above it.
    //
    // Unlike the other three AI features, this one never asks the model to echo a dimension key
    // back in a structured field - only "id" is validated - so the scores in the user prompt are
    // keyed by their natural display name in the visitor's locale instead of by camelCase key
    // (see BuildUserPrompt). Rule 6's last sentence just tells the model to keep using those
    // words; there is no raw key in the prompt for it to copy any more.
    private static string BuildSystemInstruction(string locale) =>
        $$"""
        {{BaseSystemInstruction}}
        6. "reasoning" alanındaki serbest metni {{AiLocale.ToLanguageName(locale)}} dilinde yaz.
           Bunun dışındaki TÜM JSON anahtarları ve değerleri ("id", "confidence" gibi) verildiği
           gibi, DEĞİŞTİRMEDEN kalmalı - bunlar birer tanımlayıcı/sabit değerdir, çeviri konusu
           değildir. İlçe verilerindeki skorlar, her boyutun {{AiLocale.ToLanguageName(locale)}}
           doğal adıyla verilmiştir; "reasoning" metninde bir skordan bahsederken o adı aynen
           kullan (ör. "84 {{AiLocale.DimensionDisplayName("greenSpace", locale)}} skoru").
        7. {{AiProse.DistrictNameSpacingRule}}
        """;

    public async Task<AssistantOutcome> GetRecommendationsAsync(string userQuery, string locale, CancellationToken ct)
    {
        var trimmedQuery = userQuery?.Trim() ?? "";
        if (trimmedQuery.Length == 0)
        {
            return AssistantOutcome.InvalidRequest;
        }

        if (trimmedQuery.Length > MaxUserQueryLength)
        {
            trimmedQuery = trimmedQuery[..MaxUserQueryLength];
        }

        var names = await directory.GetAllNamesAsync(ct);
        var scores = await scoringService.GetAllScoresAsync(ct);

        var normalizedLocale = AiLocale.NormalizeOrDefault(locale);
        var userPrompt = BuildUserPrompt(trimmedQuery, names, scores, normalizedLocale);
        var systemInstruction = BuildSystemInstruction(normalizedLocale);

        string rawResponse;
        try
        {
            rawResponse = await aiClient.GenerateAsync(systemInstruction, userPrompt, ct);
        }
        catch (AiAssistantNotConfiguredException)
        {
            return AssistantOutcome.NotConfigured;
        }
        catch (AiAssistantRateLimitedException)
        {
            return AssistantOutcome.RateLimited;
        }
        catch (AiAssistantUnavailableException)
        {
            return AssistantOutcome.Unavailable;
        }

        var parsed = TryParseModelResponse(rawResponse);
        if (parsed is null)
        {
            return AssistantOutcome.NoUsableRecommendations;
        }

        var validated = ValidateAndGround(parsed, names, scores);
        return validated.Count == 0 ? AssistantOutcome.NoUsableRecommendations : AssistantOutcome.Ok(validated);
    }

    private static string BuildUserPrompt(
        string userQuery,
        IReadOnlyDictionary<string, string> names,
        IReadOnlyDictionary<string, NeighborhoodScoreResult> scores,
        string locale)
    {
        var districts = new JsonArray(names
            .OrderBy(kv => kv.Value, StringComparer.CurrentCultureIgnoreCase)
            .Select(kv => (JsonNode?)ToContext(kv.Key, kv.Value, scores.GetValueOrDefault(kv.Key), locale))
            .ToArray());

        var districtsJson = districts.ToJsonString(SerializerOptions);

        return $"""
            Kullanıcının isteği: "{userQuery}"

            İlçe verileri (JSON dizisi; bir alan null ise o boyut için veri yok demektir):
            {districtsJson}
            """;
    }

    // One district's prompt entry, with every score keyed by its natural display name in the
    // visitor's locale ("yeşil alan": 84 / "green space": 84) rather than by camelCase key
    // ("greenSpace": 84). This is the fix for a live-observed bug (2026-11-10): with camelCase
    // keys, the model explained a Turkish recommendation as "Fatih, 84 greenSpace skoru ile ...
    // ve 100 healthAccess skoru ile ..." - it cited the score by the only name it had been given
    // for it. The DistrictSummary/Trend/Comparison prompts cope with the same problem by
    // instruction alone (AiLocale.DimensionDisplayNamesLine), because their model output has to
    // echo the camelCase key back in a validated "dimension" field; this feature validates only
    // "id", so it can do better and never show the model a camelCase key at all.
    //
    // A JsonObject (not a record with [JsonPropertyName]) because the key names are
    // locale-dependent; it also serializes its properties in exactly the order they are added.
    // Values are the same real numbers as before, null still meaning "veri yok".
    private static JsonObject ToContext(string id, string name, NeighborhoodScoreResult? score, string locale)
    {
        var context = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            [AiLocale.OverallScoreDisplayName(locale)] = score?.Overall?.Value,
        };

        (string Key, int? Value)[] dimensions =
        [
            ("airQuality", score?.AirQuality.Value?.Value),
            ("greenSpace", score?.GreenSpace.Value?.Value),
            ("transportation", score?.Transportation.Value?.Value),
            ("parking", score?.Parking.Value?.Value),
            ("healthAccess", score?.HealthAccess.Value?.Value),
            ("transitAccess", score?.TransitAccess.Value?.Value),
        ];

        foreach (var (key, value) in dimensions)
        {
            context[AiLocale.DimensionDisplayName(key, locale)] = value;
        }

        return context;
    }

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

    // The one function in this whole feature SPEC.md's "never fabricate a score" principle
    // actually hinges on: every recommended id is checked against the REAL id set this same
    // request already fetched, not against anything the model claims. A hallucinated id (one
    // Gemini invents that isn't one of the real 39 districts) is silently dropped here - it
    // never reaches a caller, and it never crashes the request.
    private static List<AssistantRecommendation> ValidateAndGround(
        ModelResponsePayload payload,
        IReadOnlyDictionary<string, string> names,
        IReadOnlyDictionary<string, NeighborhoodScoreResult> scores)
    {
        var result = new List<AssistantRecommendation>();
        var seenIds = new HashSet<string>();

        foreach (var item in payload.Recommendations ?? [])
        {
            if (result.Count >= MaxRecommendations)
            {
                break;
            }

            var id = item.Id?.Trim();
            var reasoning = item.Reasoning?.Trim();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(reasoning))
            {
                continue;
            }

            // Hallucination guard: id must be one of the real ids THIS request fetched, not
            // merely "look plausible". Anything else - a made-up slug, a real Istanbul place
            // name that isn't one of the 39 scored districts, a typo - is dropped, not guessed at.
            if (!names.TryGetValue(id, out var name) || !scores.TryGetValue(id, out var score))
            {
                continue;
            }

            if (!seenIds.Add(id))
            {
                continue; // duplicate recommendation for the same district - keep the first only.
            }

            result.Add(new AssistantRecommendation(id, name, reasoning, score));
        }

        return result;
    }

    // PromptJsonEncoder keeps "Kadıköy"/"yeşil alan" as real letters in the prompt instead of
    // "\u0131"-style escapes - see AiProse.PromptJsonEncoder. Has no effect on parsing the reply.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = AiProse.PromptJsonEncoder,
    };

    private sealed record ModelResponsePayload(
        [property: JsonPropertyName("recommendations")] List<ModelRecommendationPayload>? Recommendations,
        [property: JsonPropertyName("confidence")] string? Confidence);

    private sealed record ModelRecommendationPayload(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("reasoning")] string? Reasoning);
}
