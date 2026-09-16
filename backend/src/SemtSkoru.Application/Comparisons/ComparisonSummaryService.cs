using System.Text.Json;
using System.Text.Json.Serialization;
using SemtSkoru.Application.Assistant;
using SemtSkoru.Application.Scoring;

namespace SemtSkoru.Application.Comparisons;

/// <summary>
/// Generates the short AI comparison sentence shown on the /karsilastir compare page (e.g.
/// "Kadıköy hava kalitesinde öne çıkarken, Beşiktaş otoparkta daha güçlü."). Reuses
/// IDistrictAssistantAiClient - the same interface/client/Gemini API key DistrictSummaryService
/// and DistrictAssistantService already share - rather than a third AI client abstraction; see
/// DistrictSummaryService's own remarks for why one client interface is enough for every AI
/// feature in this app.
///
/// The grounding discipline mirrors DistrictSummaryService's, extended one step further because a
/// COMPARISON makes an extra kind of claim a single-district summary never does - not just "this
/// dimension is notable" but "district A beats district B at it". So on top of
/// DistrictSummaryService's two guards (a cited dimension must be one of the real six, and it
/// must actually have data), this service adds a third: the model's claim of which district is
/// "stronger" in a dimension is independently VERIFIED against the real numbers this request
/// already fetched before it is trusted at all - a claim that contradicts the real scores (or
/// calls a near-tie a win either way) is dropped exactly like a hallucinated dimension key would
/// be, never surfaced as if it were solid.
/// </summary>
public sealed class ComparisonSummaryService(IDistrictAssistantAiClient aiClient) : IComparisonSummaryService
{
    // Same defensive upper bound as DistrictSummaryService - a "1-2 sentence" comparison should
    // never come anywhere close to this; guards the DB column and the UI layout against a model
    // that ignores the "en fazla 2 cümle" instruction rather than trusting it blindly.
    private const int MaxSummaryLength = 400;

    private static readonly HashSet<string> KnownDimensions = new(StringComparer.Ordinal)
    {
        "airQuality", "greenSpace", "transportation", "parking", "healthAccess", "transitAccess",
    };

    private const string SystemInstruction =
        """
        Sen SemtSkoru uygulamasının "İlçe Karşılaştırma Özeti" AI özelliğisin. SemtSkoru,
        İstanbul'un 39 ilçesini yalnızca gerçek İBB (İstanbul Büyükşehir Belediyesi) açık
        verisinden hesaplanan skorlarla karşılaştıran bir uygulamadır. Görevin, sana JSON olarak
        verilen İKİ ilçenin ("a" ve "b") GERÇEK ve GÜNCEL 6 boyut skorunu (0-100) karşılaştırıp,
        hangi ilçenin hangi boyut(lar)da öne çıktığını kısa bir Türkçe cümleyle özetlemek.

        KESİNLİKLE UYULMASI GEREKEN KURALLAR:
        1. SADECE sana verilen 6 boyuttan ("airQuality","greenSpace","transportation","parking",
           "healthAccess","transitAccess") ve onların sayısal skorlarından bahsedebilirsin. Bu iki
           ilçe hakkında kendi genel bilgini, eğitim verini veya dışarıdan bildiğin hiçbir gerçeği
           (ör. "sahil kenarındadır", "pahalı bir semttir", "tarihi bir bölgedir" gibi) ASLA ekleme
           veya iddia etme. Sadece verilen sayılardan bahset.
        2. Bir boyutun değeri herhangi bir ilçe için null ise ("veri yok" demektir), o boyutu
           KESİNLİKLE karşılaştırma - "highlights" dizisine dahil etme.
        3. "highlights" dizisindeki her öğe, İKİ ilçenin de gerçek verisi olan bir boyutu işaret
           etmeli ve "strongerDistrict" alanı o boyutta GERÇEKTEN daha yüksek skora sahip ilçeyi
           ("a" veya "b") doğru şekilde belirtmeli. Skorlar eşitse veya aradaki fark önemsizse o
           boyutu dahil etme.
        4. Güvenle karşılaştırılabilir bir boyut bulamıyorsan "highlights" dizisini boş bırak ve
           "summary" alanını boş string ("") yap.
        5. "summary" alanı en fazla 2 cümle olmalı, SADECE "highlights" dizisindeki boyutlara
           atıfta bulunmalı ve sana verilen "name" alanlarını (ilçelerin gerçek adlarını) doğru
           şekilde kullanmalı.
        6. Yanıtın SADECE aşağıdaki şemaya uyan geçerli bir JSON nesnesi olmalı. JSON dışında
           hiçbir açıklama, markdown veya kod bloğu ekleme:
           {"highlights":[{"dimension":"<6 boyuttan biri>","strongerDistrict":"a"|"b"}],
            "summary":"<en fazla 2 cümlelik Türkçe özet, veya highlights boşsa boş string>"}
        """;

    public async Task<ComparisonSummaryOutcome> GenerateComparisonAsync(
        string neighborhoodNameA, NeighborhoodScoreResult scoreA,
        string neighborhoodNameB, NeighborhoodScoreResult scoreB,
        CancellationToken ct)
    {
        var valuesA = DimensionValues(scoreA);
        var valuesB = DimensionValues(scoreB);

        // Nothing honest to compare at all (e.g. one district has no ingested data yet, or the
        // two districts happen to share zero scored dimensions) - never spend a Gemini call
        // asking the model to compare numbers that don't both exist.
        if (!valuesA.Keys.Any(valuesB.ContainsKey))
        {
            return ComparisonSummaryOutcome.InsufficientData;
        }

        var userPrompt = BuildUserPrompt(neighborhoodNameA, scoreA, neighborhoodNameB, scoreB);

        string rawResponse;
        try
        {
            rawResponse = await aiClient.GenerateAsync(SystemInstruction, userPrompt, ct);
        }
        catch (AiAssistantNotConfiguredException)
        {
            return ComparisonSummaryOutcome.NotConfigured;
        }
        catch (AiAssistantRateLimitedException)
        {
            return ComparisonSummaryOutcome.RateLimited;
        }
        catch (AiAssistantUnavailableException)
        {
            return ComparisonSummaryOutcome.Unavailable;
        }

        var parsed = TryParseModelResponse(rawResponse);
        if (parsed is null)
        {
            return ComparisonSummaryOutcome.NoUsableSummary;
        }

        // The hallucination gate: nothing below this line is trusted until at least one cited
        // dimension is proven real, actually has data for BOTH districts, AND the claimed
        // stronger district is verified against the real numbers.
        var groundedDimensions = ValidateAndGround(parsed, valuesA, valuesB);
        if (groundedDimensions.Count == 0)
        {
            return ComparisonSummaryOutcome.NoUsableSummary;
        }

        var summaryText = parsed.Summary?.Trim();
        if (string.IsNullOrEmpty(summaryText) || summaryText.Length > MaxSummaryLength)
        {
            return ComparisonSummaryOutcome.NoUsableSummary;
        }

        return ComparisonSummaryOutcome.Ok(summaryText);
    }

    private static string BuildUserPrompt(
        string nameA, NeighborhoodScoreResult scoreA, string nameB, NeighborhoodScoreResult scoreB)
    {
        var payload = new ComparisonContext(ToDistrictContext(nameA, scoreA), ToDistrictContext(nameB, scoreB));
        var districtsJson = JsonSerializer.Serialize(payload, SerializerOptions);

        return $"""
            İki ilçenin verisi (JSON; bir alan null ise o ilçe için o boyutta veri yok demektir):
            {districtsJson}
            """;
    }

    private static DistrictContext ToDistrictContext(string name, NeighborhoodScoreResult score) => new(
        Name: name,
        AirQuality: score.AirQuality.Value?.Value,
        GreenSpace: score.GreenSpace.Value?.Value,
        Transportation: score.Transportation.Value?.Value,
        Parking: score.Parking.Value?.Value,
        HealthAccess: score.HealthAccess.Value?.Value,
        TransitAccess: score.TransitAccess.Value?.Value);

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
    // Mirrors DistrictAssistantService's/DistrictSummaryService's own fence-strippers (kept
    // independent, not shared, so this file stays self-contained per the house style).
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

    // Real dimension -> real score value, but ONLY for dimensions that actually have data -
    // deliberately built from the SAME NeighborhoodScoreResult this request already fetched, so
    // "does this district have data for X" and "what IS the real value of X" never depend on
    // anything the model claims.
    private static Dictionary<string, int> DimensionValues(NeighborhoodScoreResult score)
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        void AddIfPresent(string key, DimensionScore dimension)
        {
            if (dimension.HasData)
            {
                values[key] = dimension.Value!.Value.Value;
            }
        }

        AddIfPresent("airQuality", score.AirQuality);
        AddIfPresent("greenSpace", score.GreenSpace);
        AddIfPresent("transportation", score.Transportation);
        AddIfPresent("parking", score.Parking);
        AddIfPresent("healthAccess", score.HealthAccess);
        AddIfPresent("transitAccess", score.TransitAccess);
        return values;
    }

    // The function this whole feature's "never claim district A beats district B unless it
    // really does" requirement hinges on. Three independent checks, all against REAL data this
    // request already fetched, never against anything the model claims: (1) the dimension key
    // must be one of the real six, (2) BOTH districts must actually have data for it, and (3) the
    // claimed "stronger" side must match which one truly has the higher real value - a tie or a
    // wrong call is dropped exactly like a hallucinated dimension would be.
    private static List<string> ValidateAndGround(
        ModelResponsePayload payload, IReadOnlyDictionary<string, int> valuesA, IReadOnlyDictionary<string, int> valuesB)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in payload.Highlights ?? [])
        {
            var dimension = item.Dimension?.Trim();
            var strongerDistrict = item.StrongerDistrict?.Trim();

            if (string.IsNullOrEmpty(dimension) || !KnownDimensions.Contains(dimension))
            {
                continue; // hallucinated/unknown dimension key - dropped, never reaches a caller.
            }

            if (strongerDistrict is not ("a" or "b"))
            {
                continue; // malformed strongerDistrict value - not trusted enough to count as grounded.
            }

            if (!valuesA.TryGetValue(dimension, out var valueA) || !valuesB.TryGetValue(dimension, out var valueB))
            {
                continue; // one or both districts have "Veri yok" for this dimension - unsupported claim.
            }

            var trueStrongerDistrict = valueA == valueB ? null : valueA > valueB ? "a" : "b";
            if (trueStrongerDistrict is null || trueStrongerDistrict != strongerDistrict)
            {
                continue; // the model's claim doesn't match reality (or called a tie) - dropped.
            }

            if (seen.Add(dimension))
            {
                result.Add(dimension);
            }
        }

        return result;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record ComparisonContext(
        [property: JsonPropertyName("a")] DistrictContext A,
        [property: JsonPropertyName("b")] DistrictContext B);

    private sealed record DistrictContext(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("airQuality")] int? AirQuality,
        [property: JsonPropertyName("greenSpace")] int? GreenSpace,
        [property: JsonPropertyName("transportation")] int? Transportation,
        [property: JsonPropertyName("parking")] int? Parking,
        [property: JsonPropertyName("healthAccess")] int? HealthAccess,
        [property: JsonPropertyName("transitAccess")] int? TransitAccess);

    private sealed record ModelResponsePayload(
        [property: JsonPropertyName("highlights")] List<HighlightPayload>? Highlights,
        [property: JsonPropertyName("summary")] string? Summary);

    private sealed record HighlightPayload(
        [property: JsonPropertyName("dimension")] string? Dimension,
        [property: JsonPropertyName("strongerDistrict")] string? StrongerDistrict);
}
