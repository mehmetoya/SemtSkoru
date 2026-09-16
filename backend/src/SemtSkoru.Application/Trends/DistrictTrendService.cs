using System.Text.Json;
using System.Text.Json.Serialization;
using SemtSkoru.Application.Assistant;

namespace SemtSkoru.Application.Trends;

/// <summary>
/// Generates the "what changed" trend summary shown on one district's page (see
/// DistrictTrendBadge on the frontend). Deliberately reuses IDistrictAssistantAiClient - the same
/// GeminiClient/Gemini API key the AI Semt Asistanı and DistrictSummaryService already share (see
/// DistrictSummaryService's remarks for why one client abstraction is enough for every AI feature
/// in this app) - rather than a third AI client abstraction.
///
/// AI content in this app is generated in Turkish only, never per-locale (see
/// DistrictSummaryService/DistrictAssistantService - neither takes a locale parameter either);
/// this service follows that exact precedent rather than inventing bilingual generation for just
/// this one feature. That is a deliberate quota decision, not an oversight: this app's entire AI
/// surface shares ONE Gemini free-tier budget (150 requests/day - see
/// RateLimiting/RateLimitingExtensions.cs and ScoreSnapshotJob's remarks), and generating this
/// text twice per district (once per locale) would double this feature's real spend for no
/// benefit other than completeness - the English site already reads Turkish AI prose for the
/// existing "standout traits" summary today, and this feature stays consistent with that rather
/// than being the one AI surface that behaves differently.
///
/// The grounding mechanism mirrors DistrictSummaryService's most important property, adapted from
/// "cite a real dimension with real data" to "cite a real, already-computed, meaningfully-sized
/// delta": the caller (ScoreSnapshotJob) has ALREADY determined, with zero AI involvement, exactly
/// which dimensions changed enough to be worth mentioning (see DistrictTrendDeltas) and by
/// precisely how much. The model is asked for a structured "changes" array whose "dimension"
/// field must name one of the real deltas it was given, and whose "direction" must match that
/// delta's REAL sign - never the model's own claim. A change naming a dimension outside the given
/// list, or claiming the wrong direction for a real one, is a hallucination exactly like an
/// invented district fact and is dropped the same way. If NO change survives that check, the
/// whole summary is rejected (NoUsableSummary) - the model's free-text "summary" sentence is only
/// ever returned once at least one thing it cited about this district's change is real.
///
/// Unlike DistrictSummaryService, this service is never told (and the model is never asked to
/// guess) *why* a score moved - the underlying data is a difference between two numbers, nothing
/// more, and SPEC.md's "never fabricate" rule applies just as much to an invented cause as to an
/// invented number.
/// </summary>
public sealed class DistrictTrendService(IDistrictAssistantAiClient aiClient) : IDistrictTrendService
{
    // Same defensive upper bound as DistrictSummaryService.MaxSummaryLength, for the same reason:
    // guards the DB column and UI layout against a model that ignores the "en fazla 2 cümle"
    // instruction, without trusting that instruction blindly.
    private const int MaxSummaryLength = 400;

    private const string SystemInstruction =
        """
        Sen SemtSkoru uygulamasının bir ilçe sayfasında gösterilen "Zaman İçindeki Değişim" AI
        özetini üretiyorsun. SemtSkoru, İstanbul'un 39 ilçesini yalnızca gerçek İBB (İstanbul
        Büyükşehir Belediyesi) açık verisinden hesaplanan skorlarla karşılaştıran bir uygulamadır.
        Görevin, sana JSON olarak verilen, bir ilçenin önceki bir ölçümden şimdiye kadar GERÇEKTEN
        değişmiş skorlarını kısa bir Türkçe özetle anlatmak.

        KESİNLİKLE UYULMASI GEREKEN KURALLAR:
        1. SADECE sana "changes" listesinde verilen boyutlardan ve onların GERÇEK eski/yeni skor
           değerlerinden bahsedebilirsin. Listede olmayan hiçbir boyuttan, veya bu ilçe hakkında
           kendi genel bilgini/tahminini ASLA ekleme.
        2. Değişikliğin NEDENİNİ asla açıklama, tahmin etme veya ima etme (ör. "yeni bir park
           açıldığı için", "nüfus arttığı için" gibi) - sana verilen veri yalnızca SAYISAL bir
           farkı gösteriyor, bir sebep söylemiyor. Sadece "arttı"/"azaldı" ve gerçek sayılardan
           bahset.
        3. Her "changes" öğesi için döndürdüğün "direction" alanı, o öğenin gerçek
           previousScore/currentScore farkının yönüyle birebir eşleşmeli ("increased" sadece
           currentScore > previousScore ise, "decreased" sadece currentScore < previousScore
           ise) - ters yönü ASLA iddia etme.
        4. "summary" alanı en fazla 2 cümle olmalı ve SADECE "changes" listesindeki boyutlara ve
           verilen sayılara atıfta bulunmalı.
        5. Yanıtın SADECE aşağıdaki şemaya uyan geçerli bir JSON nesnesi olmalı. JSON dışında
           hiçbir açıklama, markdown veya kod bloğu ekleme:
           {"changes":[{"dimension":"<verilen boyutlardan biri>","direction":"increased"|"decreased"}],
            "summary":"<en fazla 2 cümlelik Türkçe özet>"}
        """;

    public async Task<DistrictTrendOutcome> GenerateTrendAsync(
        string neighborhoodName, IReadOnlyList<DimensionDelta> deltas, CancellationToken ct)
    {
        if (deltas.Count == 0)
        {
            // Defensive - see DistrictTrendOutcomeKind.NoMeaningfulChange's remarks. ScoreSnapshotJob
            // already checks this before ever reaching this call, but this service never assumes
            // that guarantee holds; asking the AI to describe zero real changes would be a wasted
            // call at best and an invitation to fabricate at worst.
            return DistrictTrendOutcome.NoMeaningfulChange;
        }

        var userPrompt = BuildUserPrompt(neighborhoodName, deltas);

        string rawResponse;
        try
        {
            rawResponse = await aiClient.GenerateAsync(SystemInstruction, userPrompt, ct);
        }
        catch (AiAssistantNotConfiguredException)
        {
            return DistrictTrendOutcome.NotConfigured;
        }
        catch (AiAssistantRateLimitedException)
        {
            return DistrictTrendOutcome.RateLimited;
        }
        catch (AiAssistantUnavailableException)
        {
            return DistrictTrendOutcome.Unavailable;
        }

        var parsed = TryParseModelResponse(rawResponse);
        if (parsed is null)
        {
            return DistrictTrendOutcome.NoUsableSummary;
        }

        // The hallucination gate: nothing below this line is trusted until at least one cited
        // change is proven real AND its claimed direction matches the real sign of the delta.
        var groundedChanges = ValidateAndGround(parsed, deltas);
        if (groundedChanges.Count == 0)
        {
            return DistrictTrendOutcome.NoUsableSummary;
        }

        var summaryText = parsed.Summary?.Trim();
        if (string.IsNullOrEmpty(summaryText) || summaryText.Length > MaxSummaryLength)
        {
            return DistrictTrendOutcome.NoUsableSummary;
        }

        return DistrictTrendOutcome.Ok(summaryText);
    }

    private static string BuildUserPrompt(string neighborhoodName, IReadOnlyList<DimensionDelta> deltas)
    {
        var context = new TrendContext(
            Name: neighborhoodName,
            Changes: deltas
                .Select(d => new ChangeContext(d.Dimension, d.PreviousScore, d.CurrentScore, d.Delta))
                .ToList());

        var contextJson = JsonSerializer.Serialize(context, SerializerOptions);

        return $"""
            İlçe skor değişikliği verisi (JSON; her "changes" öğesi gerçek bir önceki/şimdiki skor
            farkını temsil eder):
            {contextJson}
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
    // tolerate that the same way DistrictSummaryService does (kept independent, not shared, so
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

    // The one function this whole feature's "never claim a change beyond the real, already-
    // computed delta list" requirement hinges on: every cited change is checked against the REAL
    // delta list this request was given AND the REAL sign of that delta - never against anything
    // the model claims. A dimension that isn't in the given list (hallucinated, or real but not
    // meaningfully changed for this district) OR a direction that contradicts the real delta's
    // sign is silently dropped here - it never reaches a caller, and it never crashes the request.
    private static List<string> ValidateAndGround(ModelResponsePayload payload, IReadOnlyList<DimensionDelta> deltas)
    {
        var byDimension = deltas.ToDictionary(d => d.Dimension, StringComparer.Ordinal);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in payload.Changes ?? [])
        {
            var dimension = item.Dimension?.Trim();
            var direction = item.Direction?.Trim();

            if (string.IsNullOrEmpty(dimension) || !byDimension.TryGetValue(dimension, out var delta))
            {
                continue; // hallucinated/unknown dimension, or real but not a meaningful change - dropped.
            }

            var expectedDirection = delta.Delta > 0 ? "increased" : "decreased";
            if (direction != expectedDirection)
            {
                continue; // claimed the wrong direction for a real delta - not trusted.
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

    private sealed record TrendContext(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("changes")] List<ChangeContext> Changes);

    private sealed record ChangeContext(
        [property: JsonPropertyName("dimension")] string Dimension,
        [property: JsonPropertyName("previousScore")] int PreviousScore,
        [property: JsonPropertyName("currentScore")] int CurrentScore,
        [property: JsonPropertyName("delta")] int Delta);

    private sealed record ModelResponsePayload(
        [property: JsonPropertyName("changes")] List<ChangePayload>? Changes,
        [property: JsonPropertyName("summary")] string? Summary);

    private sealed record ChangePayload(
        [property: JsonPropertyName("dimension")] string? Dimension,
        [property: JsonPropertyName("direction")] string? Direction);
}
