using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace SemtSkoru.Application.Localization;

/// <summary>
/// What every one of SemtSkoru's 4 AI text features (AI Semt Asistanı, district summary, district
/// trend summary, comparison summary) needs in common to make the model's free-text prose READ
/// right, on top of each feature's own grounding checks that make sure it SAYS only true things.
/// Two separate live-observed defects (2026-11-10) are why this exists:
///
///  - Internal identifiers leaking into prose: the assistant explained a recommendation as
///    "Fatih, 84 greenSpace skoru ile ... ve 100 healthAccess skoru ile ..." - the camelCase JSON
///    key the score was sent under, verbatim, in a Turkish sentence a visitor reads.
///  - A district name run into the next word: a comparison summary came back as "Beşiktaşhava
///    kalitesi ve ulaşım boyutlarında öne çıkarken, ...". Nothing in this app builds or rewrites
///    that text (it goes model -> Trim -> DB -> HTTP response untouched), so the missing space is
///    the model's own. What the app DID do was hand the model every Turkish place name as a JSON
///    escape sequence ("Beşiktaş" - System.Text.Json's default encoder escapes every
///    non-ASCII character), i.e. the model never saw the word "Beşiktaş" at all and had to
///    reassemble it out of escape codes before writing it back as prose. See
///    <see cref="PromptJsonEncoder"/>.
///
/// Deliberately does NOT try to repair a bad sentence: re-inserting a "missing" space after a
/// district name can change what the sentence means in Turkish (an apostrophe-less suffix like
/// "Kadıköyde" would become "Kadıköy de" - "Kadıköy too"), and swapping an identifier for its
/// display name breaks Turkish vowel harmony on whatever suffix the model attached to it. So a
/// defect found here only ever REJECTS the text - the same honest "show nothing rather than
/// something wrong" outcome every feature already uses for an ungrounded claim - and never edits
/// it into something the model didn't write.
/// </summary>
public static class AiProse
{
    /// <summary>
    /// The JSON encoder every AI feature's prompt payload must be serialized with. The default
    /// System.Text.Json encoder only lets Basic Latin through unescaped, which turns every
    /// district name with a Turkish letter (Beşiktaş, Kadıköy, Üsküdar, Şişli, ...) into "\uXXXX"
    /// sequences in the prompt - harmless for a JSON parser, but a model reads the prompt as
    /// TEXT, and those names are exactly what it is asked to copy into its own prose. Allowing
    /// all of Unicode still escapes quotes, backslashes, control characters and the HTML-sensitive
    /// characters, so the payload stays valid JSON; it only stops mangling ordinary letters.
    /// Only affects serialization (the prompt), never how the model's response is parsed.
    /// </summary>
    public static readonly JavaScriptEncoder PromptJsonEncoder = JavaScriptEncoder.Create(UnicodeRanges.All);

    /// <summary>
    /// One prompt rule, shared by all four features so the wording can't drift between them,
    /// telling the model to write district names exactly as given and never glue them onto the
    /// next word. Written in Turkish like the rest of each feature's (developer-authored) rules;
    /// the examples are Turkish because that is where the defect was observed, and the rule
    /// itself is language-neutral (English prose needs a space after a name just the same).
    /// </summary>
    public const string DistrictNameSpacingRule =
        """
        İlçe adlarını sana verilen "name" değeriyle birebir aynı yaz ve her ilçe adından sonra
           MUTLAKA bir boşluk veya noktalama işareti bırak - bir ilçe adını ASLA sonraki kelimeye
           bitiştirme (YANLIŞ: "Beşiktaşhava kalitesi", DOĞRU: "Beşiktaş hava kalitesi"). İlçe
           adına ek getirirken kesme işareti kullan (ör. "Kadıköy'de", "Beşiktaş'ın").
        """;

    // The camelCase identifiers this app ever puts in a prompt. Matched case-SENSITIVELY on
    // purpose: "greenSpace" with a capital S never occurs in natural Turkish or English prose,
    // whereas "greenspace" is an ordinary (British) English word a correct English sentence may
    // well use. "transportation" and "parking" are left out for the same reason - they are both
    // JSON keys and the correct English display names, so their presence proves nothing.
    private static readonly string[] CamelCaseIdentifiers =
    [
        "airQuality", "greenSpace", "healthAccess", "transitAccess", "overallScore",
    ];

    /// <summary>True when <paramref name="text"/> contains one of this app's internal camelCase
    /// identifiers verbatim (e.g. "84 greenSpace skoru") - i.e. the model copied a JSON key
    /// into prose meant for a visitor instead of using its natural-language name.</summary>
    public static bool ContainsInternalIdentifier(string text) =>
        CamelCaseIdentifiers.Any(id => text.Contains(id, StringComparison.Ordinal));

    /// <summary>
    /// True when one of <paramref name="districtNames"/> appears in <paramref name="text"/> fused
    /// to an adjacent letter or digit with no space or punctuation in between - "Beşiktaşhava" -
    /// on either side. A correctly written name is always followed by a space, punctuation, or (in
    /// Turkish) an apostrophe before any suffix ("Kadıköy'de"), so a letter directly after it
    /// means the text is either missing a space or misspelling a suffix; neither is presentable.
    /// An occurrence that is really part of a LONGER name in the same list is not counted (no two
    /// of the real 39 districts' names nest like that today, but this must not depend on it).
    /// </summary>
    public static bool HasDistrictNameRunTogether(string text, IEnumerable<string> districtNames)
    {
        var names = districtNames
            .Select(n => n?.Trim() ?? "")
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var name in names)
        {
            for (var start = text.IndexOf(name, StringComparison.Ordinal);
                 start >= 0;
                 start = text.IndexOf(name, start + 1, StringComparison.Ordinal))
            {
                if (IsInsideLongerName(text, start, name, names))
                {
                    continue;
                }

                var end = start + name.Length;
                var gluedBefore = start > 0 && char.IsLetterOrDigit(text[start - 1]);
                var gluedAfter = end < text.Length && char.IsLetterOrDigit(text[end]);
                if (gluedBefore || gluedAfter)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The single check every AI feature runs on its free-text output before showing
    /// it (or, for a cached row, before serving it again): no internal identifier and no
    /// run-together district name. False means "reject", never "repair" - see this class's
    /// remarks.</summary>
    public static bool IsPresentable(string text, params IEnumerable<string> districtNames) =>
        !ContainsInternalIdentifier(text) && !HasDistrictNameRunTogether(text, districtNames);

    private static bool IsInsideLongerName(string text, int start, string name, IReadOnlyList<string> names)
    {
        foreach (var other in names)
        {
            if (other.Length <= name.Length)
            {
                continue;
            }

            for (var offset = other.IndexOf(name, StringComparison.Ordinal);
                 offset >= 0;
                 offset = other.IndexOf(name, offset + 1, StringComparison.Ordinal))
            {
                var otherStart = start - offset;
                if (otherStart >= 0
                    && otherStart + other.Length <= text.Length
                    && string.CompareOrdinal(text, otherStart, other, 0, other.Length) == 0)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
