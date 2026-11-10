using System.Text.Json;
using SemtSkoru.Application.Localization;

namespace SemtSkoru.Application.Tests.Localization;

// AiProse is the one place that decides whether an AI-written sentence is fit to show a visitor
// at all (on top of each feature's own grounding checks). The two "live" cases below are the
// exact sentences production returned on 2026-11-10 - if either ever passes again, the bug is
// back. The rest pin down that the check rejects only what is genuinely broken: a correctly
// written Turkish suffix ("Kadıköy'de"), English possessive, or the ordinary English word
// "greenspace" must never be mistaken for a defect, because a false positive here silently
// hides a perfectly good summary.
public class AiProseTests
{
    private const string LiveAssistantReasoning =
        "Fatih, 84 greenSpace skoru ile yeşil alan bakımından ve 100 healthAccess skoru ile sağlık hizmetlerine erişimde yüksek değerler sunmaktadır.";

    private const string LiveComparisonSummary =
        "Beşiktaşhava kalitesi ve ulaşım boyutlarında öne çıkarken, Kadıköy otopark, sağlık erişimi ve toplu taşıma erişimi alanlarında daha yüksek skorlara sahiptir.";

    [Fact]
    public void ContainsInternalIdentifier_flags_the_live_assistant_reasoning()
    {
        Assert.True(AiProse.ContainsInternalIdentifier(LiveAssistantReasoning));
    }

    [Theory]
    [InlineData("Fatih, 84 yeşil alan skoru ve 100 sağlık erişimi skoru ile öne çıkıyor.")]
    [InlineData("Fatih stands out with a green space score of 84 and a healthcare access score of 100.")]
    [InlineData("Üsküdar has plenty of greenspace.")] // ordinary (British) English word, not the key.
    [InlineData("Parking and transportation scores are high.")] // both are real words as well as keys.
    public void ContainsInternalIdentifier_accepts_natural_prose(string text)
    {
        Assert.False(AiProse.ContainsInternalIdentifier(text));
    }

    [Theory]
    [InlineData("airQuality")]
    [InlineData("greenSpace")]
    [InlineData("healthAccess")]
    [InlineData("transitAccess")]
    [InlineData("overallScore")]
    public void ContainsInternalIdentifier_flags_every_camelCase_key_the_app_puts_in_a_prompt(string key)
    {
        Assert.True(AiProse.ContainsInternalIdentifier($"Kadıköy {key} alanında öne çıkıyor."));
    }

    [Fact]
    public void HasDistrictNameRunTogether_flags_the_live_comparison_summary()
    {
        Assert.True(AiProse.HasDistrictNameRunTogether(LiveComparisonSummary, ["Beşiktaş", "Kadıköy"]));
    }

    [Theory]
    [InlineData("Beşiktaş hava kalitesi ve ulaşım boyutlarında öne çıkarken, Kadıköy otoparkta daha güçlü.")]
    [InlineData("Kadıköy'de hava kalitesi, Beşiktaş'ın skorundan yüksek.")] // apostrophe before a suffix.
    [InlineData("Kadıköy’de hava kalitesi daha yüksek.")] // typographic apostrophe.
    [InlineData("Beşiktaş's air quality is higher than Kadıköy's.")]
    [InlineData("Beşiktaş, Kadıköy.")]
    [InlineData("Kadıköy")]
    [InlineData("Bu ilçe hava kalitesinde güçlü.")] // name not mentioned at all.
    public void HasDistrictNameRunTogether_accepts_correctly_separated_names(string text)
    {
        Assert.False(AiProse.HasDistrictNameRunTogether(text, ["Beşiktaş", "Kadıköy"]));
    }

    [Theory]
    [InlineData("Kadıköyde hava kalitesi yüksek.")] // suffix without the apostrophe Turkish requires.
    [InlineData("Hava kalitesi veBeşiktaş otoparkta öne çıkıyor.")] // run together on the left.
    [InlineData("Beşiktaş84 skorla öne çıkıyor.")]
    public void HasDistrictNameRunTogether_flags_a_name_fused_to_a_neighbouring_letter_or_digit(string text)
    {
        Assert.True(AiProse.HasDistrictNameRunTogether(text, ["Beşiktaş", "Kadıköy"]));
    }

    // No two real district names nest today, but the check must not depend on that: "Esen" inside
    // a correctly written "Esenler" is the longer name, not "Esen" run into "ler".
    [Fact]
    public void HasDistrictNameRunTogether_does_not_mistake_a_longer_given_name_for_a_shorter_one_run_together()
    {
        Assert.False(AiProse.HasDistrictNameRunTogether("Esenler hava kalitesinde, Esen otoparkta öne çıkıyor.", ["Esen", "Esenler"]));
        Assert.True(AiProse.HasDistrictNameRunTogether("Esenlerhava kalitesinde öne çıkıyor.", ["Esen", "Esenler"]));
    }

    [Fact]
    public void IsPresentable_rejects_both_live_defects_and_accepts_their_corrected_forms()
    {
        Assert.False(AiProse.IsPresentable(LiveAssistantReasoning, "Fatih"));
        Assert.False(AiProse.IsPresentable(LiveComparisonSummary, "Beşiktaş", "Kadıköy"));

        Assert.True(AiProse.IsPresentable("Fatih, 84 yeşil alan skoru ile öne çıkıyor.", "Fatih"));
        Assert.True(AiProse.IsPresentable(
            "Beşiktaş hava kalitesinde öne çıkarken, Kadıköy otoparkta daha yüksek skora sahip.", "Beşiktaş", "Kadıköy"));
    }

    // The prompt-side half of the run-together fix: the model must see the district name as the
    // real word it is asked to copy, not as "ş" escape codes - while the payload stays valid,
    // properly escaped JSON for anything that actually needs escaping.
    [Fact]
    public void PromptJsonEncoder_keeps_turkish_letters_literal_but_still_escapes_json_specials()
    {
        var options = new JsonSerializerOptions { Encoder = AiProse.PromptJsonEncoder };

        var names = JsonSerializer.Serialize(new { name = "Beşiktaş", other = "Kadıköy Üsküdar Şişli Ağva" }, options);

        Assert.Equal("""{"name":"Beşiktaş","other":"Kadıköy Üsküdar Şişli Ağva"}""", names);

        // Quotes and backslashes are still escaped (the quote as ", an HTML-sensitive
        // character), so whatever a value contains, the payload parses back to exactly it.
        var specials = JsonSerializer.Serialize(new { q = "a\"b\\c<d>" }, options);
        Assert.Equal("a\"b\\c<d>", JsonDocument.Parse(specials).RootElement.GetProperty("q").GetString());
    }
}
