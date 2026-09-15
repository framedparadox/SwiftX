using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// Frosted glass's three frost levels.
/// <para>
/// <see cref="FrostedGlassBackdrop.Level"/> is static, app-wide state — the same shape
/// <see cref="OrbitMetrics"/> holds the orbit's geometry in, and for the same reason: it is read
/// from static colour lookups that have no instance to carry a config reference on. So every test
/// here restores it in a <c>finally</c>, or it leaks into whichever test runs next — including
/// <see cref="ThemeHelperTests"/>'s own frosted assertions, which are written against Light.
/// </para>
/// </summary>
public class FrostedGlassLevelTests
{
    /// <summary>Every surface key the palette owns, at whatever level is current.</summary>
    private static readonly string[] SurfaceKeys =
    [
        "SwiftXNavPaneBrush",
        "ShellCardBackgroundBrush",
        "ShellPillBrush",
        "ShellMedallionBrush",
        "SwiftXOrbitCanvasBrush",
        "OrbitNodeBackgroundBrush",
    ];

    [Fact]
    public void Light_is_the_default_and_is_what_the_theme_shipped_as()
    {
        Assert.Equal(FrostedGlassLevel.Light, new AppConfig().FrostedGlass);
        Assert.Equal(0.65, FrostedGlassBackdrop.OpacityFor(FrostedGlassLevel.Light), 3);
    }

    /// <summary>
    /// The three have to actually step, and step the same way their names read: more frost as you
    /// go down the list. A level that came out lighter than the one above it would be a picker
    /// whose labels lie.
    /// </summary>
    [Fact]
    public void The_three_levels_increase_in_opacity()
    {
        double light = FrostedGlassBackdrop.OpacityFor(FrostedGlassLevel.Light);
        double medium = FrostedGlassBackdrop.OpacityFor(FrostedGlassLevel.Medium);
        double dark = FrostedGlassBackdrop.OpacityFor(FrostedGlassLevel.Dark);

        Assert.True(light < medium, $"Light ({light}) is not lighter than Medium ({medium}).");
        Assert.True(medium < dark, $"Medium ({medium}) is not lighter than Dark ({dark}).");
        Assert.InRange(dark, 0.10, 1.0);
    }

    /// <summary>
    /// And the surfaces have to follow the backdrop. They are computed from the same opacity by the
    /// same affine curves, so a level that moved the blur without moving the plates over it would
    /// mean the two disagreed about how much frost there is.
    /// </summary>
    [Theory]
    [InlineData("SwiftXNavPaneBrush")]
    [InlineData("ShellCardBackgroundBrush")]
    [InlineData("ShellPillBrush")]
    [InlineData("SwiftXOrbitCanvasBrush")]
    public void A_darker_level_makes_every_surface_more_opaque(string key)
    {
        Assert.True(FrostedGlassPalette.TryColorFor(FrostedGlassLevel.Light, key, out var light));
        Assert.True(FrostedGlassPalette.TryColorFor(FrostedGlassLevel.Medium, key, out var medium));
        Assert.True(FrostedGlassPalette.TryColorFor(FrostedGlassLevel.Dark, key, out var dark));

        Assert.True(light.A < medium.A, $"'{key}': Light alpha {light.A:X2} is not below Medium's {medium.A:X2}.");
        Assert.True(medium.A <= dark.A, $"'{key}': Medium alpha {medium.A:X2} is above Dark's {dark.A:X2}.");
    }

    /// <summary>
    /// The page stays fully clear at every level. It is what the backdrop has to reach for anything
    /// else to read as glass, so a level that started tinting it would be a level that turned the
    /// theme off.
    /// </summary>
    [Theory]
    [InlineData(FrostedGlassLevel.Light)]
    [InlineData(FrostedGlassLevel.Medium)]
    [InlineData(FrostedGlassLevel.Dark)]
    public void The_page_is_transparent_at_every_level(FrostedGlassLevel level)
    {
        Assert.True(FrostedGlassPalette.TryColorFor(level, "ShellBackgroundBrush", out var page));
        Assert.Equal(0x00, page.A);
    }

    /// <summary>
    /// No level may reach full opacity on a surface: at 0xFF the acrylic behind it is invisible and
    /// the theme is a dark theme paying for a blur nobody sees.
    /// </summary>
    [Theory]
    [InlineData(FrostedGlassLevel.Light)]
    [InlineData(FrostedGlassLevel.Medium)]
    [InlineData(FrostedGlassLevel.Dark)]
    public void No_level_makes_a_surface_fully_opaque(FrostedGlassLevel level)
    {
        foreach (var key in SurfaceKeys)
        {
            Assert.True(FrostedGlassPalette.TryColorFor(level, key, out var colour));
            Assert.True(colour.A < 0xFF, $"'{key}' is fully opaque at {level}.");
        }
    }

    /// <summary>
    /// The no-argument lookup is the one ThemeHelper uses, and it has to answer for whichever level
    /// is currently set rather than a fixed one.
    /// </summary>
    [Fact]
    public void The_current_level_is_what_the_default_lookup_answers_for()
    {
        var restore = FrostedGlassBackdrop.Level;
        try
        {
            FrostedGlassBackdrop.SetLevel(FrostedGlassLevel.Dark);
            Assert.True(FrostedGlassPalette.TryColorFor("ShellCardBackgroundBrush", out var current));
            Assert.True(FrostedGlassPalette.TryColorFor(
                FrostedGlassLevel.Dark, "ShellCardBackgroundBrush", out var explicitly));
            Assert.Equal(explicitly, current);

            // ...and so does ThemeHelper, which is the path every surface painted in code takes.
            Assert.Equal(
                explicitly,
                ThemeHelper.ColorFor(dark: true, AppTheme.FrostedGlass, "ShellCardBackgroundBrush"));
        }
        finally
        {
            FrostedGlassBackdrop.SetLevel(restore);
        }
    }

    /// <summary>
    /// SetLevel reports whether anything moved, because its callers use that to skip a repaint —
    /// and, in ShellWindow's case, a backdrop update — that would otherwise follow a no-op click on
    /// the level already selected.
    /// </summary>
    [Fact]
    public void SetLevel_reports_whether_it_changed_anything()
    {
        var restore = FrostedGlassBackdrop.Level;
        try
        {
            FrostedGlassBackdrop.SetLevel(FrostedGlassLevel.Light);
            Assert.False(FrostedGlassBackdrop.SetLevel(FrostedGlassLevel.Light));
            Assert.True(FrostedGlassBackdrop.SetLevel(FrostedGlassLevel.Medium));
            Assert.Equal(FrostedGlassLevel.Medium, FrostedGlassBackdrop.Level);
        }
        finally
        {
            FrostedGlassBackdrop.SetLevel(restore);
        }
    }

    /// <summary>
    /// The level survives a backup/restore round trip. It is a property of the theme, and a restore
    /// that brought back Frosted glass at the wrong frost would be a restore that changed how the
    /// app looks.
    /// </summary>
    [Fact]
    public void CopyAppSettingsFrom_carries_the_level()
    {
        var source = new AppConfig { Theme = AppTheme.FrostedGlass, FrostedGlass = FrostedGlassLevel.Dark };
        var target = new AppConfig();
        target.CopyAppSettingsFrom(source);

        Assert.Equal(FrostedGlassLevel.Dark, target.FrostedGlass);
    }
}
