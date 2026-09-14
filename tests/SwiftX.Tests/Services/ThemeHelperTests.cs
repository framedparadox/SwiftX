using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// <see cref="ThemeHelper"/>'s colour table. Only the <c>ColorFor</c> overloads that take the theme
/// as a value are exercised here — the <see cref="Microsoft.UI.Xaml.FrameworkElement"/> overloads
/// need a live XAML tree, while these are pure functions of a key and a theme.
/// <para>
/// The table exists because a window whose theme disagrees with Windows' cannot read
/// <c>Themes/Colors.xaml</c> back out of the app's resources, so anything painted directly onto an
/// element has to ask here instead. That makes it a hand-maintained second copy of those values,
/// and the tests below are what stop the copy drifting.
/// </para>
/// </summary>
public class ThemeHelperTests
{
    /// <summary>
    /// Every <c>Shell*</c> and <c>Settings*</c> key the shell paints in code, against its own entry
    /// in <c>Themes/Colors.xaml</c>. Change a value there and this is what fails.
    /// </summary>
    [Theory]
    [InlineData("ShellBackgroundBrush", 0xD9, 0x2B, 0x2B, 0x2B, 0xFF, 0xF2, 0xF2, 0xF2)]
    [InlineData("ShellCardBackgroundBrush", 0xD9, 0x33, 0x33, 0x33, 0xFF, 0xFF, 0xFF, 0xFF)]
    [InlineData("ShellCardBorderBrush", 0xFF, 0x44, 0x44, 0x44, 0xFF, 0xE0, 0xE0, 0xE0)]
    [InlineData("ShellDividerBrush", 0xFF, 0x44, 0x44, 0x44, 0xFF, 0xE0, 0xE0, 0xE0)]
    [InlineData("ShellMedallionBrush", 0xD9, 0x24, 0x24, 0x24, 0xFF, 0xF4, 0xF6, 0xF9)]
    [InlineData("ShellPillBrush", 0xD9, 0x3A, 0x3A, 0x3A, 0xFF, 0xED, 0xEF, 0xF2)]
    [InlineData("ShellToolbarIconBrush", 0xFF, 0xBB, 0xBB, 0xBB, 0xFF, 0x55, 0x55, 0x55)]
    [InlineData("ShellSeparatorBrush", 0xFF, 0x50, 0x50, 0x50, 0xFF, 0xCC, 0xCC, 0xCC)]
    [InlineData("SettingsHeaderBrush", 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x1A, 0x1A, 0x1A)]
    [InlineData("SettingsTitleBrush", 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x1A, 0x1A, 0x1A)]
    [InlineData("SettingsMutedBrush", 0xFF, 0x80, 0x80, 0x80, 0xFF, 0x77, 0x77, 0x77)]
    [InlineData("SettingsSecondaryTextBrush", 0xFF, 0xAA, 0xAA, 0xAA, 0xFF, 0x55, 0x55, 0x55)]
    [InlineData("SettingsNavInactiveBrush", 0xFF, 0xA0, 0xA0, 0xA0, 0xFF, 0x44, 0x44, 0x44)]
    public void Shell_tokens_match_the_theme_dictionary(
        string key,
        byte da, byte dr, byte dg, byte db,
        byte la, byte lr, byte lg, byte lb)
    {
        var dark = ThemeHelper.ColorFor(dark: true, key);
        Assert.Equal((da, dr, dg, db), (dark.A, dark.R, dark.G, dark.B));

        var light = ThemeHelper.ColorFor(dark: false, key);
        Assert.Equal((la, lr, lg, lb), (light.A, light.R, light.G, light.B));
    }

    /// <summary>
    /// The regression this table was widened for. <c>ShellPillBrush</c>, <c>ShellMedallionBrush</c>
    /// and <c>ShellDividerBrush</c> were asked for by the Elements and Tools screens' card builders
    /// but had no case, so each fell through to the flat black/white default at the bottom of the
    /// switch — every card medallion, kind badge and rule painted pure black under Dark and pure
    /// white under Light. It read as "themed" (it did at least invert), which is why it survived.
    /// <para>
    /// Asserting "not the fallback" rather than an exact value on purpose: the point is that a key
    /// the app actually asks for must have an answer of its own, whatever that answer later becomes.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("ShellPillBrush")]
    [InlineData("ShellMedallionBrush")]
    [InlineData("ShellDividerBrush")]
    [InlineData("ShellCardBackgroundBrush")]
    [InlineData("ShellBackgroundBrush")]
    public void A_key_the_shell_paints_with_never_falls_through_to_the_default(string key)
    {
        var dark = ThemeHelper.ColorFor(dark: true, key);
        Assert.False(
            dark is { A: 0xFF, R: 0x00, G: 0x00, B: 0x00 },
            $"'{key}' fell through to the flat-black default under Dark — it needs a case of its own.");

        var light = ThemeHelper.ColorFor(dark: false, key);
        Assert.False(
            light is { A: 0xFF, R: 0xFF, G: 0xFF, B: 0xFF },
            $"'{key}' fell through to the flat-white default under Light — it needs a case of its own.");
    }

    /// <summary>
    /// Midnight rides Dark's own <see cref="Microsoft.UI.Xaml.ElementTheme"/>, so it cannot be told
    /// apart by a theme dictionary and is told apart here instead. These values have to match
    /// <c>ShellWindow.BlackOverrides</c> entry for entry: that table shadows the same keys for
    /// everything bound with <c>{ThemeResource}</c>, and this overload answers for everything
    /// painted straight onto an element. Disagree, and a card built in code keeps Dark's grey on a
    /// page that has gone black.
    /// </summary>
    [Theory]
    [InlineData("ShellBackgroundBrush", 0xE6, 0x00, 0x00, 0x00)]
    [InlineData("ShellCardBackgroundBrush", 0xE6, 0x12, 0x12, 0x12)]
    [InlineData("ShellMedallionBrush", 0xE6, 0x0A, 0x0A, 0x0A)]
    [InlineData("ShellPillBrush", 0xE6, 0x1A, 0x1A, 0x1A)]
    [InlineData("ShellCardBorderBrush", 0xFF, 0x2A, 0x2A, 0x2A)]
    [InlineData("ShellDividerBrush", 0xFF, 0x2A, 0x2A, 0x2A)]
    public void Midnight_overrides_the_shell_surfaces(string key, byte a, byte r, byte g, byte b)
    {
        var midnight = ThemeHelper.ColorFor(dark: true, AppTheme.Black, key);
        Assert.Equal((a, r, g, b), (midnight.A, midnight.R, midnight.G, midnight.B));

        // ...and only under Midnight. Plain Dark must still answer with Dark's own value.
        var plainDark = ThemeHelper.ColorFor(dark: true, AppTheme.Dark, key);
        Assert.NotEqual((a, r, g, b), (plainDark.A, plainDark.R, plainDark.G, plainDark.B));
    }

    /// <summary>
    /// Foreground, border, muted and accent read the same on any dark surface, so Midnight leaves
    /// them alone — only SwiftX's own surface fills change. A regression here would mean Midnight
    /// had started diverging from Dark somewhere it should not.
    /// </summary>
    [Theory]
    [InlineData("SwiftXForegroundBrush")]
    [InlineData("SwiftXBorderBrush")]
    [InlineData("SwiftXMutedBrush")]
    [InlineData("SettingsMutedBrush")]
    public void Midnight_leaves_the_theme_invariant_tokens_alone(string key)
    {
        Assert.Equal(
            ThemeHelper.ColorFor(dark: true, AppTheme.Dark, key),
            ThemeHelper.ColorFor(dark: true, AppTheme.Black, key));
    }

    /// <summary>
    /// The orbit bubbles' glass is the one token Midnight deepens rather than replaces: Dark's tint
    /// is already pure black, so a higher alpha is the only thing that can tell the two apart on the
    /// orbit itself.
    /// </summary>
    [Fact]
    public void Midnight_deepens_the_orbit_bubble_glass()
    {
        var dark = ThemeHelper.ColorFor(dark: true, AppTheme.Dark, "OrbitBubbleGlassTint");
        var midnight = ThemeHelper.ColorFor(dark: true, AppTheme.Black, "OrbitBubbleGlassTint");

        Assert.Equal((dark.R, dark.G, dark.B), (midnight.R, midnight.G, midnight.B));
        Assert.True(midnight.A > dark.A);
    }

    /// <summary>
    /// Midnight and Frosted glass are SwiftX-only surface palettes with no OS counterpart, so both
    /// ride Dark's native ElementTheme and System can never resolve to either.
    /// </summary>
    [Fact]
    public void Resolve_maps_each_theme_to_its_element_theme()
    {
        Assert.Equal(Microsoft.UI.Xaml.ElementTheme.Light, ThemeHelper.Resolve(AppTheme.Light));
        Assert.Equal(Microsoft.UI.Xaml.ElementTheme.Dark, ThemeHelper.Resolve(AppTheme.Dark));
        Assert.Equal(Microsoft.UI.Xaml.ElementTheme.Dark, ThemeHelper.Resolve(AppTheme.Black));
        Assert.Equal(Microsoft.UI.Xaml.ElementTheme.Dark, ThemeHelper.Resolve(AppTheme.FrostedGlass));
        Assert.Equal(Microsoft.UI.Xaml.ElementTheme.Default, ThemeHelper.Resolve(AppTheme.System));
    }

    /// <summary>
    /// Frosted glass answers for the surfaces and nothing else. Every one of them has to be
    /// translucent — an opaque plate would hide the acrylic the theme exists for, leaving a window
    /// that is only tinted at its margins.
    /// </summary>
    [Theory]
    [InlineData("SwiftXNavPaneBrush")]
    [InlineData("ShellCardBackgroundBrush")]
    [InlineData("ShellPillBrush")]
    [InlineData("ShellMedallionBrush")]
    [InlineData("SwiftXOrbitCanvasBrush")]
    [InlineData("SwiftXCanvasBrush")]
    public void Frosted_glass_surfaces_are_translucent(string key)
    {
        var frosted = ThemeHelper.ColorFor(dark: true, AppTheme.FrostedGlass, key);
        Assert.True(frosted.A < 0xFF, $"'{key}' is opaque under Frosted glass ({frosted.A:X2}).");
        Assert.True(frosted.A > 0x00, $"'{key}' is fully transparent under Frosted glass.");
        Assert.NotEqual(ThemeHelper.ColorFor(dark: true, AppTheme.Dark, key), frosted);
    }

    /// <summary>
    /// The page itself, the Settings canvas and the separator go all the way to transparent: they
    /// are what the backdrop has to reach for anything else to read as glass.
    /// </summary>
    [Theory]
    [InlineData("ShellBackgroundBrush")]
    [InlineData("SettingsSurfaceBrush")]
    [InlineData("ShellSeparatorBrush")]
    public void Frosted_glass_clears_the_page_entirely(string key)
    {
        Assert.Equal(0x00, ThemeHelper.ColorFor(dark: true, AppTheme.FrostedGlass, key).A);
    }

    /// <summary>
    /// Borders become a white highlight catching the glass rather than a drawn line — the source
    /// theme's own choice, and the thing that keeps a card's edge visible once its fill is a wash.
    /// </summary>
    [Theory]
    [InlineData("ShellCardBorderBrush")]
    [InlineData("ShellDividerBrush")]
    public void Frosted_glass_borders_are_a_low_alpha_white(string key)
    {
        var border = ThemeHelper.ColorFor(dark: true, AppTheme.FrostedGlass, key);
        Assert.Equal((0xFF, 0xFF, 0xFF), (border.R, border.G, border.B));
        Assert.InRange(border.A, 0x01, 0x7F);
    }

    /// <summary>
    /// Like Midnight, Frosted glass changes only surfaces — text and the rest keep Dark's values, so
    /// contrast is still reasoned about once rather than per theme.
    /// </summary>
    [Theory]
    [InlineData("SwiftXForegroundBrush")]
    [InlineData("SwiftXMutedBrush")]
    [InlineData("SettingsMutedBrush")]
    [InlineData("SettingsTitleBrush")]
    public void Frosted_glass_leaves_the_text_tokens_alone(string key)
    {
        Assert.Equal(
            ThemeHelper.ColorFor(dark: true, AppTheme.Dark, key),
            ThemeHelper.ColorFor(dark: true, AppTheme.FrostedGlass, key));
    }

    /// <summary>
    /// ShellWindow writes the palette into the window's resources while ThemeHelper answers for
    /// anything painted straight onto an element. Both read FrostedGlassPalette, and this is what
    /// says so: ask the two by their two routes and get the same colour.
    /// </summary>
    [Theory]
    [InlineData("ShellCardBackgroundBrush")]
    [InlineData("ShellPillBrush")]
    [InlineData("SwiftXNavPaneBrush")]
    [InlineData("ShellCardBorderBrush")]
    public void The_palette_and_the_helper_agree(string key)
    {
        Assert.True(FrostedGlassPalette.TryColorFor(key, out var fromPalette));
        Assert.Equal(fromPalette, ThemeHelper.ColorFor(dark: true, AppTheme.FrostedGlass, key));
    }

    /// <summary>
    /// A key the palette has no opinion about must fall through rather than resolve to some default
    /// of its own — that fall-through is what lets the theme restate only the surfaces.
    /// </summary>
    [Fact]
    public void The_palette_declines_keys_it_does_not_own()
    {
        Assert.False(FrostedGlassPalette.TryColorFor("SwiftXForegroundBrush", out _));
        Assert.False(FrostedGlassPalette.TryColorFor("No.Such.Key", out _));
    }
}
