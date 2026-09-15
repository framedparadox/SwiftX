using SwiftX.Models;
using Windows.UI;

namespace SwiftX.Services;

/// <summary>
/// The surface colours the shell paints under <see cref="Models.AppTheme.FrostedGlass"/>, and the
/// list of resource keys each one lands on.
/// <para>
/// A theme that is mostly a backdrop still needs its chrome to get out of the way: left on Dark's
/// near-opaque plates (<c>#D9</c>-alpha greys, see <c>Themes/Colors.xaml</c>), the acrylic
/// <see cref="FrostedGlassBackdrop"/> puts behind the window would be visible only in the margins
/// and the theme would look like Dark with a tinted edge. So every surface drops to a low-alpha
/// wash and every border becomes a thin white one — the "frosted glass" look is the desktop showing
/// through the plates, not around them.
/// </para>
/// <para>
/// Ported from windows-app-mx's <c>LogiApp/App.xaml.cs</c> (branch <c>logiApp/dotnet</c>), whose own
/// header credits the <c>Get*Brush()</c> methods of <c>Helpers/ThemeManager.cs</c> on its
/// <c>add_acrylic_frosted_theme</c> branch. Each colour is an affine function of the opacity
/// (<c>opacity * slope + offset</c>) clamped to a floor and a ceiling, copied verbatim — the base
/// RGBs and every slope, offset and bound below are that file's numbers. What is SwiftX's is the
/// mapping: Logi's brush keys do not exist here, so each formula is pointed at whichever of this
/// app's keys plays the same role, which is what <see cref="Surfaces"/> records.
/// </para>
/// <para>
/// Applied the same way Midnight's palette is — as element-local entries shadowing the theme
/// dictionary on the window root, not as a fourth <c>ThemeDictionary</c>. WinUI only ever resolves
/// <c>{ThemeResource}</c> against Light/Dark/Default, so a fifth theme riding on Dark's
/// <see cref="Microsoft.UI.Xaml.ElementTheme"/> has no dictionary of its own to be told apart by.
/// See <c>ShellWindow.ApplySurfaceOverrides</c>, which carries the same reasoning at length.
/// </para>
/// </summary>
public static class FrostedGlassPalette
{
    /// <summary>
    /// One surface role: the colour it resolves to at a given opacity, and every resource key that
    /// should be painted with it.
    /// </summary>
    public sealed record Surface(Func<double, Color> ColorFor, IReadOnlyList<string> Keys);

    /// <summary>
    /// Every surface the theme repaints, in the order it is applied. The key groupings are where
    /// Logi's vocabulary is translated into SwiftX's:
    /// <list type="bullet">
    /// <item>Logi's window/header/separator group returns <c>Transparent</c> — that is what lets the
    /// backdrop reach the page at all, so SwiftX's own page ground joins it.</item>
    /// <item>Logi's <c>SettingsCard*</c> pair covers this app's three card-ish surfaces, which are
    /// one role here even though Settings, the shell and the dialogs each name it differently.</item>
    /// <item>Logi's sidebar pair maps onto the nav pane and the separator beside it.</item>
    /// <item>Logi's button pair covers the medallion and pill — the two small inset plates inside a
    /// Home nav box.</item>
    /// <item>Logi's action-node pair covers the orbit canvas and the Settings canvas, the two
    /// surfaces that sit behind drawn content rather than behind text.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<Surface> Surfaces { get; } =
    [
        new Surface(_ => Color.FromArgb(0, 0, 0, 0),
            ["ShellBackgroundBrush", "SettingsSurfaceBrush", "ShellSeparatorBrush",
             "OrbitCanvasBackgroundBrush"]),

        new Surface(o => Wash(o, 120, 15, 20, 160, 14, 14, 20),
            ["SwiftXNavPaneBrush", "OrbitSidebarBackgroundBrush"]),

        new Surface(o => Edge(o, 50, 15, 15, 75),
            ["ShellDividerBrush", "OrbitSidebarBorderBrush"]),

        new Surface(o => Wash(o, 115, 15, 20, 155, 24, 24, 32),
            // Deliberately not SwiftXDialogSurfaceBrush. The add-item dialog and the tool windows
            // carry no backdrop at all — they are plain opaque surfaces by design — so a
            // translucent fill there would have nothing behind it to show but the bare desktop,
            // unblurred. A theme that cannot reach a window has to leave that window alone.
            ["ShellCardBackgroundBrush", "SwiftXCardBackgroundBrush"]),

        new Surface(o => Edge(o, 60, 20, 20, 95),
            ["ShellCardBorderBrush", "SwiftXCardStrokeBrush",
             "OrbitCanvasBorderBrush", "OrbitLabelPillBorderBrush"]),

        new Surface(o => Wash(o, 110, 15, 20, 150, 30, 30, 40),
            ["ShellPillBrush", "ShellMedallionBrush", "OrbitLabelPillBrush"]),

        new Surface(o => Wash(o, 125, 20, 25, 170, 26, 26, 36),
            ["SwiftXOrbitCanvasBrush", "SwiftXCanvasBrush",
             // The orbit's own bubbles ride this same formula, which is where Logi points its ring
             // nodes too. They already sit on a per-bubble DesktopAcrylicBackdrop of their own, so
             // this is only the tint over it — and at ~0x65 it lands within a point of Dark's 0x66,
             // differing in hue rather than weight. That is the intent: under frosted glass the
             // orbit should read as the same glass as the window, not as a second, heavier one.
             "OrbitBubbleGlassTint", "OrbitBubbleGlassTintColor",
             "OrbitNodeBackgroundBrush", "OrbitHubBrush"]),
    ];

    /// <summary>
    /// Every key this palette repaints, resolved for each of the three levels.
    /// <para>
    /// Built eagerly from <see cref="Surfaces"/> rather than kept as a second hand-written list, so
    /// the table and the lookup can never disagree about which keys the theme owns — the way
    /// <c>ThemeHelper.ColorFor</c>'s Midnight cases and <c>ShellWindow.BlackOverrides</c> can, and
    /// have. All three are computed up front rather than on demand for the level in play: there are
    /// three of them, each a few dozen colours, and a table that is never rebuilt is a table with
    /// no invalidation to get wrong when the level changes.
    /// </para>
    /// </summary>
    private static readonly Dictionary<FrostedGlassLevel, Dictionary<string, Color>> Resolved =
        Enum.GetValues<FrostedGlassLevel>().ToDictionary(
            level => level,
            level => Surfaces
                .SelectMany(s => s.Keys.Select(
                    k => (Key: k, Color: s.ColorFor(FrostedGlassBackdrop.OpacityFor(level)))))
                .ToDictionary(e => e.Key, e => e.Color, StringComparer.Ordinal));

    /// <summary>
    /// The colour this theme paints <paramref name="key"/> at the level currently in play, or false
    /// if the theme has no opinion about it — foreground, muted, border and accent tokens all keep
    /// Dark's values, exactly as the source theme leaves them.
    /// </summary>
    public static bool TryColorFor(string key, out Color color) =>
        TryColorFor(FrostedGlassBackdrop.Level, key, out color);

    /// <summary>
    /// The same lookup against an explicit level, for the one caller that needs to paint a level it
    /// is not currently wearing: the Settings picker's own three preview swatches.
    /// </summary>
    public static bool TryColorFor(FrostedGlassLevel level, string key, out Color color) =>
        Resolved[level].TryGetValue(key, out color);

    /// <summary>
    /// A tinted plate: the source theme's own near-black base colour at an alpha that rises with the
    /// opacity. The bases differ by a couple of points per channel between roles (14/14/20 for the
    /// nav pane, 30/30/40 for the pills) — small, but it is what keeps the plates distinguishable
    /// from each other once they are all this translucent.
    /// </summary>
    private static Color Wash(
        double opacity, double slope, double offset, int min, int max, byte r, byte g, byte b) =>
        Color.FromArgb(Alpha(opacity, slope, offset, min, max), r, g, b);

    /// <summary>
    /// A border: white at a low alpha rather than a colour of its own, so an edge reads as a
    /// highlight catching the glass rather than as a drawn line. The same choice the source theme
    /// makes for every one of its borders.
    /// </summary>
    private static Color Edge(double opacity, double slope, double offset, int min, int max) =>
        Color.FromArgb(Alpha(opacity, slope, offset, min, max), 255, 255, 255);

    /// <summary>
    /// The shared curve. Truncating to int before clamping — rather than rounding — is deliberate:
    /// it is what the source does, and matching it is what keeps the two apps' glass identical
    /// rather than a point apart at most opacities.
    /// </summary>
    private static byte Alpha(double opacity, double slope, double offset, int min, int max) =>
        (byte)Math.Clamp((int)(opacity * slope + offset), min, max);
}
