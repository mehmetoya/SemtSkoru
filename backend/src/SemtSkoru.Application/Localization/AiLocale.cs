namespace SemtSkoru.Application.Localization;

/// <summary>
/// Single source of truth for which locales SemtSkoru's 4 AI text features (AI Semt Asistanı,
/// district summary, district trend summary, comparison summary) support - mirrors
/// web/i18n/routing.ts's `locales: ["tr", "en"]`, the ONLY two locales this app has anywhere.
/// Every call site that accepts a locale from an HTTP request (query string, JSON body) funnels
/// through <see cref="NormalizeOrDefault"/> instead of each repeating its own `{"tr","en"}`
/// literal check, so this is the one place that set ever needs to change if a third locale is
/// ever added.
///
/// An unrecognized or missing value silently falls back to <see cref="Default"/> rather than
/// rejecting the request - matches this app's existing "never break the rest of the app over
/// something optional" style (e.g. a missing district summary/trend just renders nothing rather
/// than erroring - see DistrictSummaryBadge.tsx/DistrictTrendBadge.tsx). A locale is presentation
/// preference, not something worth 400ing a whole request over.
/// </summary>
public static class AiLocale
{
    /// <summary>Turkish - this app's original, default locale (see routing.ts's
    /// `defaultLocale: "tr"`), and what every AI feature generated exclusively before locale
    /// awareness was added.</summary>
    public const string Default = "tr";

    private const string English = "en";

    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase) { Default, English };

    /// <summary>Every locale a batch job (DistrictSummaryGenerationJob, ScoreSnapshotJob) must
    /// generate one row per district for - i.e. every value NormalizeOrDefault can ever return.
    /// Default first, so a partial/interrupted run (see each job's own per-district SaveChangesAsync)
    /// leaves the original, already-shipped Turkish behavior generated before the additive English
    /// pass.</summary>
    public static readonly IReadOnlyList<string> All = [Default, English];

    /// <summary>Normalizes a raw locale value (e.g. from a query string or request body) to
    /// exactly "tr" or "en" - never anything else, and never throws. Case-insensitive on the way
    /// in, but the returned value is always lowercase.</summary>
    public static string NormalizeOrDefault(string? locale) =>
        locale is not null && Supported.Contains(locale) ? locale.ToLowerInvariant() : Default;

    /// <summary>The human-readable language name for a (normalized-or-defaulted) locale, meant to
    /// be embedded directly in an AI system instruction - Gemini follows a plain language name
    /// ("Türkçe"/"English") far more reliably than a raw locale code ("tr"/"en").</summary>
    public static string ToLanguageName(string? locale) =>
        NormalizeOrDefault(locale) == English ? "English" : "Türkçe";

    /// <summary>The six dimension keys' natural-language display name in the given locale - the
    /// EXACT same strings web/messages/{tr,en}.json's "Dimensions" namespace shows in the UI
    /// (lowercased for natural mid-sentence prose), so an AI-generated sentence names a dimension
    /// the same way the rest of the page around it does.
    ///
    /// Exists because of a real, live-verified bug: telling the model "leave dimension KEYS
    /// unchanged" (so the structured "dimension"/"highlights[].dimension" JSON field keeps
    /// matching KnownDimensions's literal camelCase strings) was ambiguous in English specifically
    /// - camelCase keys like "greenSpace" already LOOK like English words, so the model applied
    /// "leave it unchanged" to the free-text prose too and produced sentences like "Üsküdar stands
    /// out in greenSpace, transportation, and transitAccess" instead of natural English. Turkish
    /// never had this problem (a Turkish reader obviously can't mistake "greenSpace" for a Turkish
    /// word, so the model already paraphrased it as "yeşil alan" on its own) - this mapping makes
    /// the instruction unambiguous in EITHER language instead of relying on the model inferring
    /// the right behavior only where the key happens not to look native.</summary>
    public static string DimensionDisplayName(string dimensionKey, string? locale) =>
        (dimensionKey, NormalizeOrDefault(locale) == English) switch
        {
            ("airQuality", true) => "air quality",
            ("airQuality", false) => "hava kalitesi",
            ("greenSpace", true) => "green space",
            ("greenSpace", false) => "yeşil alan",
            ("transportation", true) => "transportation",
            ("transportation", false) => "ulaşım",
            ("parking", true) => "parking",
            ("parking", false) => "otopark",
            ("healthAccess", true) => "healthcare access",
            ("healthAccess", false) => "sağlık erişimi",
            ("transitAccess", true) => "public transit access",
            ("transitAccess", false) => "toplu taşıma erişimi",
            _ => dimensionKey,
        };

    /// <summary>A single prompt-ready line listing every dimension's natural-language display name
    /// for the given locale, in the fixed canonical order used everywhere else in this app (see
    /// e.g. DistrictSummarySignature.Compute) - appended to a SystemInstruction right after the
    /// "don't translate JSON keys" rule so the model has an explicit, unambiguous word to reach
    /// for in prose instead of the literal key. See <see cref="DimensionDisplayName"/>.</summary>
    public static string DimensionDisplayNamesLine(string? locale)
    {
        string[] keys = ["airQuality", "greenSpace", "transportation", "parking", "healthAccess", "transitAccess"];
        var pairs = keys.Select(k => $"{k}=\"{DimensionDisplayName(k, locale)}\"");
        return string.Join(", ", pairs);
    }
}
