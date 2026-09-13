using Microsoft.UI;
using Microsoft.UI.Xaml;
using SwiftX.Models;
using Windows.UI;

namespace SwiftX.Services;

/// <summary>
/// The accent colour the app is painting with right now, and everything a control needs derived
/// from it: the per-theme built-in default, the pointer-over and pressed shades, and the
/// black-or-white foreground that stays legible drawn on top of it.
/// <para>
/// Exactly one colour is stored (<see cref="AppConfig.AccentColorHex"/>), but every accented
/// control wants three fills — rest, pointer-over, pressed — plus something readable on them.
/// Deriving the rest from that one colour is what lets the picker in Settings stay a single
/// swatch instead of asking the user for a whole palette.
/// </para>
/// <para>
/// This lives in Services rather than in a window because the accent stopped being a
/// Settings-only affair: the Add-item dialog's "Add" button and its type tiles were still
/// painting themselves in the <em>Windows</em> system accent, which on a default install is blue
/// and has nothing to do with the colour the user chose.
/// </para>
/// <para>
/// <b>SettingsWindow still carries its own private copy of all of this</b> — its
/// <c>CurrentAccentColor</c>, <c>ResolveIsDark</c>, <c>TryParseHexColor</c> and <c>Blend</c>, and
/// its own <c>DefaultAccentHexDark</c>/<c>DefaultAccentHexLight</c> constants. That file was left
/// untouched deliberately, so it should be migrated onto this class; until it is, the two default
/// hex values exist in two places and have to be changed together.
/// </para>
/// </summary>
public static class AccentPalette
{
    /// <summary>
    /// The built-in accent, per theme, used whenever <see cref="AppConfig.AccentColorHex"/> is
    /// unset or unparsable. Must track <c>SettingsAccentBrush</c>/<c>SettingsAccentHoverBrush</c>
    /// in <c>Themes/Colors.xaml</c> — and, for now, SettingsWindow's own copies of these two
    /// constants (see the class remarks). Two values rather than one because the same teal cannot
    /// carry a dark surface and a light one: the bright mint reads as a highlight on black and as
    /// a smear on white, so light gets the deeper, more saturated variant.
    /// </summary>
    public const string DefaultHexDark = "#3DDCD3";

    /// <inheritdoc cref="DefaultHexDark"/>
    public const string DefaultHexLight = "#0B7C7C";

    /// <summary>
    /// How far <see cref="PointerOver"/> and <see cref="Pressed"/> move off the base accent,
    /// toward white and black respectively. 0.18 is deliberately modest: it mirrors roughly how
    /// WinUI's own <c>AccentFillColorSecondary/TertiaryBrush</c> sit either side of
    /// <c>AccentFillColorDefaultBrush</c> for the system accent, so an accented control still
    /// reads as one colour changing state rather than as three unrelated colours.
    /// </summary>
    private const double ShadeAmount = 0.18;

    /// <summary>
    /// What the accent resolves to right now: the user's own pick when they have made one,
    /// otherwise the built-in colour for the theme in play.
    /// </summary>
    /// <param name="isDark">
    /// Which built-in default applies. Passed in rather than read off an element because the
    /// caller usually knows it before the element does — see <see cref="IsDark"/>.
    /// </param>
    public static Color Resolve(AppConfig config, bool isDark)
    {
        var hex = config.AccentColorHex;
        if (!string.IsNullOrEmpty(hex) && TryParseHex(hex, out var custom))
            return custom;
        TryParseHex(isDark ? DefaultHexDark : DefaultHexLight, out var fallback);
        return fallback;
    }

    /// <summary>
    /// Whether the theme about to be applied is a dark one, read from the configured
    /// <see cref="AppTheme"/> rather than from <paramref name="element"/>'s ActualTheme.
    /// <para>
    /// The distinction is one of ordering. A window has to write its accent brushes <em>before</em>
    /// it nudges RequestedTheme (that nudge is what makes every <c>{ThemeResource}</c> under the
    /// root re-resolve and notice them), and at that moment the root's RequestedTheme has not been
    /// assigned yet, so ActualTheme can still be reporting the theme being replaced. The config is
    /// already correct at that point. Only System, which has no fixed answer of its own, has to
    /// defer to what the element actually resolved to.
    /// </para>
    /// </summary>
    public static bool IsDark(AppConfig config, FrameworkElement element) =>
        ThemeHelper.Resolve(config.Theme) switch
        {
            ElementTheme.Dark => true,
            ElementTheme.Light => false,
            _ => ThemeHelper.IsDark(element),
        };

    /// <summary>The pointer-over shade of <paramref name="accent"/> — lightened, so hover reads as
    /// the control coming toward the pointer.</summary>
    public static Color PointerOver(Color accent) => Blend(accent, Colors.White, ShadeAmount);

    /// <summary>The pressed shade of <paramref name="accent"/> — darkened, so a press reads as the
    /// control being pushed in.</summary>
    public static Color Pressed(Color accent) => Blend(accent, Colors.Black, ShadeAmount);

    /// <summary>
    /// Parses "#RRGGBB" (the only form <see cref="AppConfig.AccentColorHex"/> is ever written in).
    /// Never throws — a stored value that has been hand-edited into nonsense simply falls back the
    /// same way an unset one does, rather than taking a whole theme pass down with it.
    /// </summary>
    public static bool TryParseHex(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrEmpty(hex))
            return false;

        var s = hex.TrimStart('#');
        if (s.Length != 6)
            return false;

        try
        {
            byte r = Convert.ToByte(s.Substring(0, 2), 16);
            byte g = Convert.ToByte(s.Substring(2, 2), 16);
            byte b = Convert.ToByte(s.Substring(4, 2), 16);
            color = Color.FromArgb(255, r, g, b);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The inverse of <see cref="TryParseHex"/>: the "#RRGGBB" form the config stores.
    /// Alpha is dropped because the config has never carried one.</summary>
    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>
    /// Blends <paramref name="color"/> toward <paramref name="target"/> by <paramref name="amount"/>
    /// (0-1). A straight per-channel lerp in sRGB: not colour-accurate, but it is what produces the
    /// familiar "same hue, one step lighter/darker" a hover state is supposed to read as.
    /// </summary>
    public static Color Blend(Color color, Color target, double amount)
    {
        byte L(byte a, byte b) => (byte)Math.Clamp(a + (b - a) * amount, 0, 255);
        return Color.FromArgb(255, L(color.R, target.R), L(color.G, target.G), L(color.B, target.B));
    }

    /// <summary>
    /// Black or white — whichever is actually readable drawn on <paramref name="background"/>.
    /// <para>
    /// Needed because the accent is the user's to choose. Hard-coding white (which is what WinUI's
    /// own <c>TextOnAccentFillColorPrimaryBrush</c> effectively is) is only safe while the accent
    /// is guaranteed dark; pick a pale mint or a lemon yellow and the label on the button
    /// disappears into it.
    /// </para>
    /// </summary>
    public static Color OnAccent(Color background) => OnAccent(new[] { background });

    /// <summary>
    /// The same choice made once for a whole set of related fills — a control's rest,
    /// pointer-over and pressed shades — by taking whichever of black or white has the better
    /// <em>worst</em> contrast across all of them.
    /// <para>
    /// Deciding per state instead would let the foreground flip from white to black as the pointer
    /// crosses the control, for any accent sitting near the crossover. One choice for the set never
    /// flickers, and because the shades are only <c>ShadeAmount</c> apart it costs almost nothing
    /// in contrast to make it.
    /// </para>
    /// </summary>
    public static Color OnAccent(params Color[] backgrounds)
    {
        if (backgrounds.Length == 0)
            return Colors.White;

        double worstOnBlack = double.MaxValue;
        double worstOnWhite = double.MaxValue;
        foreach (var background in backgrounds)
        {
            worstOnBlack = Math.Min(worstOnBlack, ContrastRatio(Colors.Black, background));
            worstOnWhite = Math.Min(worstOnWhite, ContrastRatio(Colors.White, background));
        }

        return worstOnBlack > worstOnWhite ? Colors.Black : Colors.White;
    }

    /// <summary>
    /// The WCAG contrast ratio between two opaque colours, 1:1 (identical) to 21:1 (black on
    /// white). WCAG AA asks 4.5:1 for body text and 3:1 for large text and UI boundaries.
    /// </summary>
    public static double ContrastRatio(Color a, Color b)
    {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// WCAG relative luminance: each sRGB channel linearised out of the display gamma curve, then
    /// weighted by how much of the perceived brightness it actually carries.
    /// <para>
    /// Deliberately not the naive <c>(R + G + B) / 3</c>. Those weights are not decoration: pure
    /// green #00FF00 and pure blue #0000FF share the identical naive average of 85, yet green's
    /// luminance is 0.715 and blue's is 0.072 — green needs black text on it and blue needs white.
    /// An averaging shortcut gets that exact case backwards.
    /// </para>
    /// </summary>
    public static double RelativeLuminance(Color color)
    {
        static double Linear(byte channel)
        {
            double v = channel / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linear(color.R))
             + (0.7152 * Linear(color.G))
             + (0.0722 * Linear(color.B));
    }
}
