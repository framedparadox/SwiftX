using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// <see cref="Loc"/> holds its active language as static state, so every test here restores it to
/// <see cref="AppLanguage.EnglishUS"/> in a <c>finally</c> — leaving it pointed at whatever a test
/// last selected would leak into whichever test (in this class or another) runs next.
/// </summary>
public class LocTests
{
    [Fact]
    public void Get_of_an_unknown_key_returns_the_key_itself()
    {
        Assert.Equal("No.Such.Key", Loc.Get("No.Such.Key"));
    }

    [Fact]
    public void Initialize_switches_the_active_table()
    {
        try
        {
            Loc.Initialize(AppLanguage.French);
            Assert.Equal(AppLanguage.French, Loc.CurrentLanguage);
            Assert.Equal("Paramètres", Loc.Get("Tray.Settings"));

            Loc.Initialize(AppLanguage.German);
            Assert.Equal(AppLanguage.German, Loc.CurrentLanguage);
            Assert.Equal("Einstellungen", Loc.Get("Tray.Settings"));
        }
        finally
        {
            Loc.Initialize(AppLanguage.EnglishUS);
        }
    }

    [Fact]
    public void Initialize_resolves_System_to_a_concrete_language()
    {
        try
        {
            Loc.Initialize(AppLanguage.System);
            Assert.NotEqual(AppLanguage.System, Loc.CurrentLanguage);
        }
        finally
        {
            Loc.Initialize(AppLanguage.EnglishUS);
        }
    }

    [Fact]
    public void Format_substitutes_arguments_in_the_active_language()
    {
        try
        {
            Loc.Initialize(AppLanguage.EnglishUS);
            Assert.Equal("2 of 5", Loc.Format("Formatter.SearchMatchCount", 2, 5));

            Loc.Initialize(AppLanguage.French);
            Assert.Equal("2 sur 5", Loc.Format("Formatter.SearchMatchCount", 2, 5));
        }
        finally
        {
            Loc.Initialize(AppLanguage.EnglishUS);
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
    public void Every_shipped_language_answers_a_well_known_key(AppLanguage language)
    {
        try
        {
            Loc.Initialize(language);
            // Not equal to the raw key: every language table carries this key (see the parity
            // check baked into how the per-language dictionaries were authored), so Get must
            // never fall through to returning "Tray.Quit" itself.
            Assert.NotEqual("Tray.Quit", Loc.Get("Tray.Quit"));
        }
        finally
        {
            Loc.Initialize(AppLanguage.EnglishUS);
        }
    }
}
