using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

public class LanguageCatalogTests
{
    [Fact]
    public void Options_covers_every_AppLanguage_value_exactly_once()
    {
        var values = Enum.GetValues<AppLanguage>();
        var optionLanguages = LanguageCatalog.Options.Select(o => o.Language).ToList();

        Assert.Equal(values.Length, optionLanguages.Count);
        foreach (var value in values)
            Assert.Single(optionLanguages, l => l == value);
    }

    [Fact]
    public void Only_System_carries_an_empty_culture_tag()
    {
        foreach (var option in LanguageCatalog.Options)
        {
            if (option.Language == AppLanguage.System)
                Assert.Equal("", option.CultureTag);
            else
                Assert.False(string.IsNullOrWhiteSpace(option.CultureTag));
        }
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
    public void Find_returns_the_matching_option_for_each_language()
    {
        foreach (var language in Enum.GetValues<AppLanguage>())
            Assert.Equal(language, LanguageCatalog.Find(language).Language);
    }
}
