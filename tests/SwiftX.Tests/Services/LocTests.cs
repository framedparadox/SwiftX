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

    /// <summary>
    /// Iterates every concrete language (System is resolution-only, never a table) so a newly
    /// added <see cref="AppLanguage"/> that forgot to register or populate a table is caught here
    /// automatically — no per-language InlineData list to keep in sync. Checks a spread of keys
    /// across the different tool areas rather than just one, so a table that is registered but
    /// half-empty is caught too: none of these should ever fall through to returning the raw key.
    /// </summary>
    [Fact]
    public void Every_concrete_language_translates_well_known_keys()
    {
        string[] sampleKeys =
        {
            "Tray.Quit", "Tray.Settings", "Tool.Save", "Common.Cancel",
            "Base64.Encode", "Certificate.Field.Subject", "SecureValues.Add",
        };

        try
        {
            foreach (var language in Enum.GetValues<AppLanguage>())
            {
                if (language == AppLanguage.System) continue;
                Loc.Initialize(language);
                Assert.Equal(language, Loc.CurrentLanguage);
                foreach (var key in sampleKeys)
                    Assert.NotEqual(key, Loc.Get(key));
            }
        }
        finally
        {
            Loc.Initialize(AppLanguage.EnglishUS);
        }
    }

    [Fact]
    public void CurrentIsRightToLeft_tracks_the_active_language()
    {
        try
        {
            Loc.Initialize(AppLanguage.Arabic);
            Assert.True(Loc.CurrentIsRightToLeft);

            Loc.Initialize(AppLanguage.Hebrew);
            Assert.True(Loc.CurrentIsRightToLeft);

            Loc.Initialize(AppLanguage.French);
            Assert.False(Loc.CurrentIsRightToLeft);

            Loc.Initialize(AppLanguage.EnglishUS);
            Assert.False(Loc.CurrentIsRightToLeft);
        }
        finally
        {
            Loc.Initialize(AppLanguage.EnglishUS);
        }
    }
}
