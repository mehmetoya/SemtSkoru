using System.Text.Json;
using System.Text.Json.Serialization;
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

    private const string SystemInstruction =
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
           "oneriler" dizisini kısa tut veya boş bırak; "guven":"dusuk" işaretle.
        5. Yanıtın SADECE aşağıdaki şemaya uyan geçerli bir JSON nesnesi olmalı. JSON dışında
           hiçbir açıklama, markdown veya kod bloğu ekleme:
           {"oneriler":[{"id":"<ilçe listesinden bir id>","aciklama":"<verilen skorlara atıfta
           bulunan, 1-2 cümlelik Türkçe gerekçe>"}],"guven":"yuksek"|"dusuk"}
        """;

    public async Task<AssistantOutcome> GetRecommendationsAsync(string userQuery, CancellationToken ct)
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

        var userPrompt = BuildUserPrompt(trimmedQuery, names, scores);

        string rawResponse;
        try
        {
            rawResponse = await aiClient.GenerateAsync(SystemInstruction, userPrompt, ct);
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
        IReadOnlyDictionary<string, NeighborhoodScoreResult> scores)
    {
        var districts = names
            .OrderBy(kv => kv.Value, StringComparer.CurrentCultureIgnoreCase)
            .Select(kv => ToContext(kv.Key, kv.Value, scores.GetValueOrDefault(kv.Key)))
            .ToList();

        var districtsJson = JsonSerializer.Serialize(districts, SerializerOptions);

        return $"""
            Kullanıcının isteği: "{userQuery}"

            İlçe verileri (JSON dizisi; bir alan null ise o boyut için veri yok demektir):
            {districtsJson}
            """;
    }

    private static DistrictContext ToContext(string id, string name, NeighborhoodScoreResult? score) => new(
        Id: id,
        Ad: name,
        GenelSkor: score?.Overall?.Value,
        HavaKalitesi: score?.AirQuality.Value?.Value,
        YesilAlan: score?.GreenSpace.Value?.Value,
        Ulasim: score?.Transportation.Value?.Value,
        Otopark: score?.Parking.Value?.Value,
        SaglikErisimi: score?.HealthAccess.Value?.Value,
        TopluTasima: score?.TransitAccess.Value?.Value);

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

        foreach (var item in payload.Oneriler ?? [])
        {
            if (result.Count >= MaxRecommendations)
            {
                break;
            }

            var id = item.Id?.Trim();
            var reasoning = item.Aciklama?.Trim();
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

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record DistrictContext(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("ad")] string Ad,
        [property: JsonPropertyName("genelSkor")] int? GenelSkor,
        [property: JsonPropertyName("havaKalitesi")] int? HavaKalitesi,
        [property: JsonPropertyName("yesilAlan")] int? YesilAlan,
        [property: JsonPropertyName("ulasim")] int? Ulasim,
        [property: JsonPropertyName("otopark")] int? Otopark,
        [property: JsonPropertyName("saglikErisimi")] int? SaglikErisimi,
        [property: JsonPropertyName("topluTasima")] int? TopluTasima);

    private sealed record ModelResponsePayload(
        [property: JsonPropertyName("oneriler")] List<ModelRecommendationPayload>? Oneriler,
        [property: JsonPropertyName("guven")] string? Guven);

    private sealed record ModelRecommendationPayload(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("aciklama")] string? Aciklama);
}
