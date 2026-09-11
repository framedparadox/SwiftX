using System.Globalization;
using SwiftX.Models;

namespace SwiftX.Services;

/// <summary>One selectable entry in the Settings › General language picker.</summary>
/// <param name="Language">The enum value stored on <see cref="AppConfig"/>.</param>
/// <param name="NativeName">Shown in the picker, in that language's own script — the way a person
/// looking for their language actually scans a list, rather than in English.</param>
/// <param name="CultureTag">BCP-47 tag used to match <see cref="CultureInfo.CurrentUICulture"/>
/// when <see cref="AppLanguage.System"/> needs to resolve to a real table.</param>
public sealed record LanguageOption(AppLanguage Language, string NativeName, string CultureTag);

/// <summary>
/// The list of languages SwiftX ships a string table for, plus resolution of
/// <see cref="AppLanguage.System"/> to whichever of them best matches the OS's own UI language.
/// </summary>
public static class LanguageCatalog
{
    /// <summary>Display order for the Settings language picker — System first (it's the default),
    /// then every concrete language ordered by its own native name: the Latin-script names sort
    /// alphabetically, followed by Korean, Japanese, and Chinese, whose own scripts don't share a
    /// single alphabetical order with the Latin ones.</summary>
    public static readonly IReadOnlyList<LanguageOption> Options = new[]
    {
        new LanguageOption(AppLanguage.System, "System", ""),
        new LanguageOption(AppLanguage.German, "Deutsch", "de-DE"),
        new LanguageOption(AppLanguage.EnglishUS, "English (US)", "en-US"),
        new LanguageOption(AppLanguage.EnglishUK, "English (UK)", "en-GB"),
        new LanguageOption(AppLanguage.Spanish, "Español", "es-ES"),
        new LanguageOption(AppLanguage.French, "Français", "fr-FR"),
        new LanguageOption(AppLanguage.Italian, "Italiano", "it-IT"),
        new LanguageOption(AppLanguage.PortugueseBR, "Português (Brasil)", "pt-BR"),
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
                if (option.Language == AppLanguage.System) continue;
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
        "pt" => AppLanguage.PortugueseBR,
        "ko" => AppLanguage.Korean,
        "ja" => AppLanguage.Japanese,
        "zh" => AppLanguage.ChineseSimplified,
        _ => AppLanguage.EnglishUS,
    };

    public static LanguageOption Find(AppLanguage language)
    {
        foreach (var option in Options)
            if (option.Language == language)
                return option;
        return Options[0];
    }
}
