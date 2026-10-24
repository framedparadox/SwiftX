using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

public class LanguageCatalogTests
{
    [Fact]
    public void Options_covers_every_concrete_AppLanguage_exactly_once()
    {
        // System is a stored value that resolves to one of these, not something the picker offers.
        var values = Enum.GetValues<AppLanguage>().Where(l => l != AppLanguage.System).ToList();
        var optionLanguages = LanguageCatalog.Options.Select(o => o.Language).ToList();

        Assert.Equal(values.Count, optionLanguages.Count);
        foreach (var value in values)
            Assert.Single(optionLanguages, l => l == value);
    }

    [Fact]
    public void The_picker_never_offers_System()
    {
        Assert.DoesNotContain(LanguageCatalog.Options, o => o.Language == AppLanguage.System);
    }

    [Fact]
    public void Every_option_carries_a_culture_tag()
    {
        foreach (var option in LanguageCatalog.Options)
            Assert.False(string.IsNullOrWhiteSpace(option.CultureTag));
    }

    [Theory]
    [InlineData(AppLanguage.EnglishUS)]
    [InlineData(AppLanguage.EnglishUK)]
    [InlineData(AppLanguage.French)]
    [InlineData(AppLanguage.German)]
    [InlineData(AppLanguage.Spanish)]
    [InlineData(AppLanguage.Italian)]
    [InlineData(AppLanguage.PortugueseBR)]
    [InlineData(AppLanguage.Japanese)]
    [InlineData(AppLanguage.ChineseSimplified)]
    [InlineData(AppLanguage.Korean)]
    public void ResolveEffective_passes_concrete_languages_through_unchanged(AppLanguage language)
    {
        Assert.Equal(language, LanguageCatalog.ResolveEffective(language));
    }

    [Fact]
    public void ResolveEffective_of_System_never_returns_System()
    {
        // Whatever the test host's own UI culture happens to be, System must resolve to one of
        // the concrete languages SwiftX actually ships a table for — see Loc.EnglishUS's doc
        // comment on why English (US) is always a safe fallback.
        Assert.NotEqual(AppLanguage.System, LanguageCatalog.ResolveEffective(AppLanguage.System));
    }

    [Fact]
    public void Find_returns_the_matching_option_for_each_concrete_language()
    {
        foreach (var language in Enum.GetValues<AppLanguage>().Where(l => l != AppLanguage.System))
            Assert.Equal(language, LanguageCatalog.Find(language).Language);
    }

    [Fact]
    public void Find_of_System_returns_the_language_System_resolves_to()
    {
        // Not Options[0]: with no System entry left, that fall-through would hand a stored System
        // (every config written before the picker dropped it) Bahasa Indonesia.
        Assert.Equal(
            LanguageCatalog.ResolveEffective(AppLanguage.System),
            LanguageCatalog.Find(AppLanguage.System).Language);
    }

    [Theory]
    [InlineData(AppLanguage.Arabic)]
    [InlineData(AppLanguage.Hebrew)]
    public void Right_to_left_languages_are_flagged(AppLanguage language)
    {
        Assert.True(LanguageCatalog.IsRightToLeft(language));
        Assert.True(LanguageCatalog.Find(language).IsRightToLeft);
    }

    [Theory]
    [InlineData(AppLanguage.EnglishUS)]
    [InlineData(AppLanguage.French)]
    [InlineData(AppLanguage.Japanese)]
    [InlineData(AppLanguage.Russian)]
    [InlineData(AppLanguage.Hindi)]
    public void Left_to_right_languages_are_not_flagged(AppLanguage language)
    {
        Assert.False(LanguageCatalog.IsRightToLeft(language));
        Assert.False(LanguageCatalog.Find(language).IsRightToLeft);
    }

    [Fact]
    public void Every_option_has_a_non_empty_native_name()
    {
        foreach (var option in LanguageCatalog.Options)
            Assert.False(string.IsNullOrWhiteSpace(option.NativeName));
    }

    [Fact]
    public void Culture_tags_are_unique_among_concrete_languages()
    {
        // A duplicated culture tag would make System resolution ambiguous — the first match in
        // Options order would silently win. Every shipped language must own a distinct tag.
        var tags = LanguageCatalog.Options
            .Select(o => o.CultureTag.ToLowerInvariant())
            .ToList();
        Assert.Equal(tags.Count, tags.Distinct().Count());
    }
}
