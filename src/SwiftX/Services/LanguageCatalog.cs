using System.Globalization;
using SwiftX.Models;

namespace SwiftX.Services;

/// <summary>One selectable entry in the Settings › General language picker.</summary>
/// <param name="Language">The enum value stored on <see cref="AppConfig"/>.</param>
/// <param name="NativeName">Shown in the picker, in that language's own script — the way a person
/// looking for their language actually scans a list, rather than in English.</param>
/// <param name="CultureTag">BCP-47 tag used to match <see cref="CultureInfo.CurrentUICulture"/>
/// when <see cref="AppLanguage.System"/> needs to resolve to a real table.</param>
/// <param name="IsRightToLeft">Whether this language's script runs right-to-left (Arabic, Hebrew),
/// so the UI needs <c>FlowDirection.RightToLeft</c> — see <see cref="LanguageCatalog.IsRightToLeft"/>.</param>
public sealed record LanguageOption(
    AppLanguage Language, string NativeName, string CultureTag, bool IsRightToLeft = false);

/// <summary>
/// The list of languages SwiftX ships a string table for, plus resolution of
/// <see cref="AppLanguage.System"/> to whichever of them best matches the OS's own UI language.
/// </summary>
public static class LanguageCatalog
{
    /// <summary>
    /// Display order for the Settings language picker: Latin-script languages alphabetized by
    /// their own native name, then the non-Latin-script ones — those don't share a single
    /// alphabetical order with the Latin names, so they are grouped afterward (Cyrillic and Greek,
    /// then the right-to-left scripts, then Devanagari, then Thai, then CJK).
    /// </summary>
    public static readonly IReadOnlyList<LanguageOption> Options = new[]
    {
        // Latin script, alphabetical by native name.
        new LanguageOption(AppLanguage.Indonesian, "Bahasa Indonesia", "id-ID"),
        new LanguageOption(AppLanguage.Czech, "Čeština", "cs-CZ"),
        new LanguageOption(AppLanguage.Danish, "Dansk", "da-DK"),
        new LanguageOption(AppLanguage.German, "Deutsch", "de-DE"),
        new LanguageOption(AppLanguage.EnglishUK, "English (UK)", "en-GB"),
        new LanguageOption(AppLanguage.EnglishUS, "English (US)", "en-US"),
        new LanguageOption(AppLanguage.Spanish, "Español", "es-ES"),
        new LanguageOption(AppLanguage.French, "Français", "fr-FR"),
        new LanguageOption(AppLanguage.Italian, "Italiano", "it-IT"),
        new LanguageOption(AppLanguage.Dutch, "Nederlands", "nl-NL"),
        new LanguageOption(AppLanguage.Norwegian, "Norsk", "nb-NO"),
        new LanguageOption(AppLanguage.Polish, "Polski", "pl-PL"),
        new LanguageOption(AppLanguage.PortugueseBR, "Português (Brasil)", "pt-BR"),
        new LanguageOption(AppLanguage.Romanian, "Română", "ro-RO"),
        new LanguageOption(AppLanguage.Swedish, "Svenska", "sv-SE"),
        new LanguageOption(AppLanguage.Vietnamese, "Tiếng Việt", "vi-VN"),
        new LanguageOption(AppLanguage.Turkish, "Türkçe", "tr-TR"),

        // Other scripts.
        new LanguageOption(AppLanguage.Russian, "Русский", "ru-RU"),
        new LanguageOption(AppLanguage.Ukrainian, "Українська", "uk-UA"),
        new LanguageOption(AppLanguage.Greek, "Ελληνικά", "el-GR"),
        new LanguageOption(AppLanguage.Arabic, "العربية", "ar-SA", IsRightToLeft: true),
        new LanguageOption(AppLanguage.Hebrew, "עברית", "he-IL", IsRightToLeft: true),
        new LanguageOption(AppLanguage.Hindi, "हिन्दी", "hi-IN"),
        new LanguageOption(AppLanguage.Thai, "ไทย", "th-TH"),
        new LanguageOption(AppLanguage.Korean, "한국어", "ko-KR"),
        new LanguageOption(AppLanguage.Japanese, "日本語", "ja-JP"),
        new LanguageOption(AppLanguage.ChineseSimplified, "中文（简体）", "zh-Hans"),
    };

    /// <summary>
    /// Turns <see cref="AppLanguage.System"/> into whichever concrete language SwiftX should
    /// actually render in, matching <see cref="CultureInfo.CurrentUICulture"/> in two passes.
    /// First, an exact culture-tag match walking up the OS's own culture hierarchy
    /// (<see cref="CultureInfo.Parent"/>) — this is what picks English (UK) for "en-GB" directly,
    /// and what picks Chinese (Simplified) for a specific locale like "zh-CN" once it steps up to
    /// its own neutral parent "zh-Hans". Second, for anything that still doesn't match (a regional
    /// variant SwiftX carries no tag for at all, e.g. "fr-CA" or "de-AT"), a plain two-letter
    /// language fallback. Any other <paramref name="language"/> passes through unchanged, and a
    /// System that matches nothing in either pass falls back to English (US) — the neutral table
    /// every other language's lookup already falls back to (see <see cref="Loc.Get"/>).
    /// </summary>
    public static AppLanguage ResolveEffective(AppLanguage language)
    {
        if (language != AppLanguage.System)
            return language;

        for (var culture = CultureInfo.CurrentUICulture; !string.IsNullOrEmpty(culture.Name);
             culture = culture.Parent)
        {
            foreach (var option in Options)
            {
                if (string.Equals(option.CultureTag, culture.Name, StringComparison.OrdinalIgnoreCase))
                    return option.Language;
            }
        }

        return TwoLetterFallback(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    }

    private static AppLanguage TwoLetterFallback(string twoLetter) => twoLetter switch
    {
        "de" => AppLanguage.German,
        "es" => AppLanguage.Spanish,
        "fr" => AppLanguage.French,
        "it" => AppLanguage.Italian,
        "nl" => AppLanguage.Dutch,
        "pl" => AppLanguage.Polish,
        // Portuguese with no region: Brazil is the larger variant, so it's the safer default.
        "pt" => AppLanguage.PortugueseBR,
        "tr" => AppLanguage.Turkish,
        "ru" => AppLanguage.Russian,
        "ar" => AppLanguage.Arabic,
        "he" => AppLanguage.Hebrew,
        // "iw" is the legacy ISO code for Hebrew that some systems still report.
        "iw" => AppLanguage.Hebrew,
        "hi" => AppLanguage.Hindi,
        "ko" => AppLanguage.Korean,
        "ja" => AppLanguage.Japanese,
        // Chinese with no script: Simplified is the larger variant, so it's the safer default.
        "zh" => AppLanguage.ChineseSimplified,
        "uk" => AppLanguage.Ukrainian,
        "vi" => AppLanguage.Vietnamese,
        "id" => AppLanguage.Indonesian,
        "th" => AppLanguage.Thai,
        "sv" => AppLanguage.Swedish,
        "da" => AppLanguage.Danish,
        "nb" => AppLanguage.Norwegian,
        // "no" is the generic Norwegian code; real-world systems report both "nb" and plain "no".
        "no" => AppLanguage.Norwegian,
        "cs" => AppLanguage.Czech,
        "el" => AppLanguage.Greek,
        "ro" => AppLanguage.Romanian,
        _ => AppLanguage.EnglishUS,
    };

    /// <summary>
    /// The option for <paramref name="language"/>. <see cref="AppLanguage.System"/> has no entry of its
    /// own any more (the picker offers real languages only; a stored System is still honoured), so it
    /// is resolved first rather than falling through to whichever language happens to be listed first.
    /// </summary>
    public static LanguageOption Find(AppLanguage language)
    {
        language = ResolveEffective(language);
        foreach (var option in Options)
            if (option.Language == language)
                return option;
        return Options[0];
    }

    /// <summary>
    /// Whether <paramref name="language"/>'s script runs right-to-left, resolving
    /// <see cref="AppLanguage.System"/> first so callers get the effective direction. Drives
    /// <c>FlowDirection</c> on the windows that render localized content — see the tool windows and
    /// Settings. A right-to-left flag lives on the concrete language rather than being sniffed from
    /// the culture at the call site so it stays correct even for the System case.
    /// </summary>
    public static bool IsRightToLeft(AppLanguage language) =>
        Find(ResolveEffective(language)).IsRightToLeft;
}
