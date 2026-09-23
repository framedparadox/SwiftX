using Microsoft.UI;
using SwiftX.Models;
using Windows.UI;

namespace SwiftX.Services;

/// <summary>
/// The decisions behind the orbit's hover styles that do not need a window to make: which colour
/// Tint paints, what is drawn on top of it, and which style is really in force.
/// <para>
/// Pulled out of <c>OrbitWindow</c> so they can be tested. The window is the only thing that reads
/// them at run time, but each is a small rule with a wrong answer that would only be noticed by
/// hovering the right item in the right theme with the right colour chosen.
/// </para>
/// </summary>
public static class OrbitHover
{
    /// <summary>
    /// The fill Tint paints under the pointer: the user's own pick when
    /// <see cref="AppConfig.HoverTintColorHex"/> holds a colour, otherwise black. Parsing is
    /// <see cref="AccentPalette.TryParseHex"/>, so an unset, blank or hand-edited value falls back
    /// the same way the accent does rather than failing a render.
    /// <para>
    /// Black rather than something per theme, unlike the accent: the setting exists so the user
    /// can choose, and a default that moved with the theme would make "reset" mean something
    /// different in Light than in Dark. Black also gives the glyph the strongest white to sit on.
    /// </para>
    /// </summary>
    public static Color TintColor(AppConfig config) =>
        AccentPalette.TryParseHex(config.HoverTintColorHex, out var custom) ? custom : Colors.Black;

    /// <summary>
    /// The glyph and icon colour drawn on <paramref name="tint"/>: black or white, whichever reads
    /// better, decided by <see cref="AccentPalette.OnAccent(Color)"/>'s WCAG contrast rather than by
    /// the theme. The fill is the user's choice, so the theme's own foreground says nothing about
    /// whether it is legible on it: white glyphs on a pale yellow, or black ones on navy, would be
    /// exactly the failure a free colour pick invites.
    /// </summary>
    public static Color OnTint(Color tint) => AccentPalette.OnAccent(tint);

    /// <summary>
    /// The style hover actually applies. Two things change it: under High Contrast, Tint is treated
    /// as Invert, and with animations switched off in Windows, Animate is treated as Zoom.
    /// <para>
    /// High Contrast always wins (design doc, section 13.3), and a user-chosen fill is precisely
    /// what it exists to override, so the colour is not applied there. Falling back to no visible
    /// change at all would be worse than either, though: Tint changes nothing but the fill, so
    /// without one the item would give no sign it is under the pointer. Invert is the style that
    /// reads from the theme's own paired colours alone, which is what makes it the right stand-in.
    /// </para>
    /// <para>
    /// Reduced motion is the same reasoning for a different setting. Animate repeats for as long as
    /// the pointer rests on an item, which is precisely what "no animations" is asked to prevent, and
    /// it changes nothing else, so an item would be left with no cue. Zoom is one short grow that
    /// settles, the quietest style that still says "this one".
    /// </para>
    /// </summary>
    public static OrbitHoverStyle Effective(
        OrbitHoverStyle style, bool highContrast, bool reducedMotion = false) => style switch
        {
            OrbitHoverStyle.Tint when highContrast => OrbitHoverStyle.Invert,
            OrbitHoverStyle.Animate when reducedMotion => OrbitHoverStyle.Zoom,
            _ => style,
        };
}
