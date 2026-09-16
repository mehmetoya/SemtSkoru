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
}
