using System.Text.Json;
using System.Text.Json.Serialization;
using SemtSkoru.Application.Assistant;
using SemtSkoru.Application.Scoring;

namespace SemtSkoru.Application.Summaries;

/// <summary>
/// Generates the "standout traits" summary shown on one district's page. Deliberately reuses
/// IDistrictAssistantAiClient (and, via DI, the same GeminiClient/Gemini API key as the AI Semt
/// Asistanı - see Program.cs) rather than a second AI client abstraction: that interface's shape
/// (a system instruction plus a user prompt, returning text) has nothing assistant-specific about
/// it, and this feature has exactly the same "never fabricate, degrade to nothing on any failure"
/// requirement DistrictAssistantService already implements - building a parallel client/interface
/// pair here would just be the same 100 lines twice.
///
/// The grounding mechanism mirrors DistrictAssistantService's most important property, adapted
/// from "many districts, pick some real ids" to "one district, cite some real dimensions": the
/// model is asked for a structured "highlights" array whose "dimension" field must name one of
/// the six real dimension keys, and every single one is checked against the REAL score this
/// request already fetched before the model's prose is trusted at all. A highlight naming a
/// dimension key that doesn't exist is a hallucination exactly like an invented district id; a
/// highlight naming a real dimension that simply has no data for this district (HasData == false,
/// i.e. "Veri yok") is just as much an unsupported claim and is dropped the same way. If NO
/// highlight survives that check, the whole summary is rejected (NoUsableSummary) - the model's
/// free-text "summary" sentence is only ever returned once at least one thing it cited about this
/// district is real and present.
/// </summary>
public sealed class DistrictSummaryService(IDistrictAssistantAiClient aiClient) : IDistrictSummaryService
{
    // Defensive upper bound on what gets persisted/served - a "1-2 sentence" summary should
    // never come anywhere close to this; guards the DB column and the UI layout against a model
    // that ignores the "en fazla 2 cümle" instruction rather than trusting it blindly.
    private const int MaxSummaryLength = 400;

    private static readonly HashSet<string> KnownDimensions = new(StringComparer.Ordinal)
    {
        "airQuality", "greenSpace", "transportation", "parking", "healthAccess", "transitAccess",
    };

    private const string SystemInstruction =
        """
        Sen SemtSkoru uygulamasının bir ilçe sayfasında gösterilen "Öne Çıkan Özellikler" AI
        özetini üretiyorsun. SemtSkoru, İstanbul'un 39 ilçesini yalnızca gerçek İBB (İstanbul
        Büyükşehir Belediyesi) açık verisinden hesaplanan skorlarla karşılaştıran bir uygulamadır.
        Görevin, sana JSON olarak verilen TEK bir ilçenin GERÇEK ve GÜNCEL 6 boyut skorunu (0-100)
        inceleyip, o ilçenin öne çıkan güçlü ve zayıf yönlerini kısa bir Türkçe özetle anlatmak.

        KESİNLİKLE UYULMASI GEREKEN KURALLAR:
        1. SADECE sana verilen 6 boyuttan ("airQuality","greenSpace","transportation","parking",
           "healthAccess","transitAccess") ve onların sayısal skorlarından bahsedebilirsin. Bu
           ilçe hakkında kendi genel bilgini, eğitim verini veya dışarıdan bildiğin hiçbir gerçeği
           (ör. "sahil kenarındadır", "pahalı bir semttir", "tarihi bir bölgedir", nüfusu,
           konumu gibi) ASLA ekleme veya iddia etme. Sadece verilen sayılardan bahset.
        2. Bir boyutun değeri null ise ("veri yok" demektir), o boyut hakkında hiçbir şey iddia
           etme veya varsayma; o boyutu "highlights" dizisine KESİNLİKLE dahil etme.
        3. "highlights" dizisindeki her öğe SADECE değeri null OLMAYAN bir boyutu işaret etmeli ve
           o boyutun skoru gerçekten öne çıkan (belirgin şekilde yüksek veya belirgin şekilde
           düşük) olmalı - vasat/ortalama bir skoru öne çıkan gibi gösterme.
        4. Güvenle öne çıkan bir yön bulamıyorsan "highlights" dizisini boş bırak ve "summary"
           alanını boş string ("") yap.
        5. "summary" alanı en fazla 2 cümle olmalı ve SADECE "highlights" dizisindeki boyutlara ve
           verilen sayılara atıfta bulunmalı.
        6. Yanıtın SADECE aşağıdaki şemaya uyan geçerli bir JSON nesnesi olmalı. JSON dışında
           hiçbir açıklama, markdown veya kod bloğu ekleme:
           {"highlights":[{"dimension":"<6 boyuttan biri>","strength":"strong"|"weak"}],
            "summary":"<en fazla 2 cümlelik Türkçe özet, veya highlights boşsa boş string>"}
        """;

    public async Task<DistrictSummaryOutcome> GenerateSummaryAsync(
        string neighborhoodName, NeighborhoodScoreResult score, CancellationToken ct)
    {
        if (!score.HasAnyData)
        {
            return DistrictSummaryOutcome.InsufficientData;
        }

        var userPrompt = BuildUserPrompt(neighborhoodName, score);

        string rawResponse;
        try
        {
            rawResponse = await aiClient.GenerateAsync(SystemInstruction, userPrompt, ct);
        }
        catch (AiAssistantNotConfiguredException)
        {
            return DistrictSummaryOutcome.NotConfigured;
        }
        catch (AiAssistantRateLimitedException)
        {
            return DistrictSummaryOutcome.RateLimited;
        }
        catch (AiAssistantUnavailableException)
        {
            return DistrictSummaryOutcome.Unavailable;
        }

        var parsed = TryParseModelResponse(rawResponse);
        if (parsed is null)
        {
            return DistrictSummaryOutcome.NoUsableSummary;
        }

        // The hallucination gate: nothing below this line is trusted until at least one cited
        // dimension is proven real AND actually has data for this district.
        var groundedDimensions = ValidateAndGround(parsed, score);
        if (groundedDimensions.Count == 0)
        {
            return DistrictSummaryOutcome.NoUsableSummary;
        }

        var summaryText = parsed.Summary?.Trim();
        if (string.IsNullOrEmpty(summaryText) || summaryText.Length > MaxSummaryLength)
        {
            return DistrictSummaryOutcome.NoUsableSummary;
        }

        return DistrictSummaryOutcome.Ok(summaryText);
    }

    private static string BuildUserPrompt(string neighborhoodName, NeighborhoodScoreResult score)
    {
        var context = new DistrictContext(
            Name: neighborhoodName,
            AirQuality: score.AirQuality.Value?.Value,
            GreenSpace: score.GreenSpace.Value?.Value,
            Transportation: score.Transportation.Value?.Value,
            Parking: score.Parking.Value?.Value,
            HealthAccess: score.HealthAccess.Value?.Value,
            TransitAccess: score.TransitAccess.Value?.Value);

        var districtJson = JsonSerializer.Serialize(context, SerializerOptions);

        return $"""
            İlçe verisi (JSON; bir alan null ise o boyut için veri yok demektir):
            {districtJson}
            """;
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
    // Mirrors DistrictAssistantService's own fence-stripper (kept independent, not shared, so
    // this file stays self-contained and low-risk to change without touching that already-shipped
    // feature's code).
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

    // The one function this whole feature's "never claim anything about the district beyond what
    // its own 6 dimension scores show" requirement hinges on: every cited dimension is checked
    // against the REAL known dimension-key set AND the REAL score this request already fetched -
    // never against anything the model claims. A dimension key that isn't one of the real six
    // (hallucinated) OR that IS real but has no data for this district (an unsupported claim,
    // same "Veri yok" boundary as everywhere else in this app) is silently dropped here - it
    // never reaches a caller, and it never crashes the request.
    private static List<string> ValidateAndGround(ModelResponsePayload payload, NeighborhoodScoreResult score)
    {
        var hasData = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["airQuality"] = score.AirQuality.HasData,
            ["greenSpace"] = score.GreenSpace.HasData,
            ["transportation"] = score.Transportation.HasData,
            ["parking"] = score.Parking.HasData,
            ["healthAccess"] = score.HealthAccess.HasData,
            ["transitAccess"] = score.TransitAccess.HasData,
        };

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in payload.Highlights ?? [])
        {
            var dimension = item.Dimension?.Trim();
            var strength = item.Strength?.Trim();

            if (string.IsNullOrEmpty(dimension) || !KnownDimensions.Contains(dimension))
            {
                continue; // hallucinated/unknown dimension key - dropped, never reaches a caller.
            }

            if (strength is not ("strong" or "weak"))
            {
                continue; // malformed strength value - not trusted enough to count as grounded.
            }

            if (!hasData[dimension])
            {
                continue; // "Veri yok" for this dimension - a claim about it is unsupported.
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
        [property: JsonPropertyName("strength")] string? Strength);
}
