using Microsoft.UI.Xaml.Media;
using SwiftX.Models;
using Windows.UI;

namespace SwiftX.Services;

/// <summary>
/// The colour an item's glyph is drawn in (<see cref="OrbitItem.CustomIconColor"/>), and the
/// swatches the icon picker offers for it. Kept apart from the windows so every place that draws a
/// glyph asks the same question the same way, and so the stored form can be tested.
/// </summary>
public static class IconColors
{
    public readonly record struct Swatch(string Name, string Hex);

    /// <summary>
    /// A small, general-purpose set, bright enough to read on both the dark glass and the light one.
    /// Like <see cref="IconChoices"/>, the names are labels on a swatch grid rather than sentences,
    /// so they are plain English literals.
    /// </summary>
    public static readonly IReadOnlyList<Swatch> Presets = new[]
    {
        new Swatch("Red", "#E5484D"),
        new Swatch("Orange", "#F76B15"),
        new Swatch("Amber", "#E9A30C"),
        new Swatch("Green", "#30A46C"),
        new Swatch("Teal", "#12A594"),
        new Swatch("Blue", "#0090FF"),
        new Swatch("Indigo", "#3E63DD"),
        new Swatch("Purple", "#8E4EC6"),
        new Swatch("Pink", "#E93D82"),
        new Swatch("Grey", "#8B8D98"),
    };

    /// <summary>
    /// The item's glyph colour, or false when it has none (or one that is not a "#RRGGBB" — a
    /// hand-edited config falls back to the theme colour rather than failing a render).
    /// </summary>
    public static bool TryGet(OrbitItem? item, out Color color)
    {
        color = default;
        return item is not null && AccentPalette.TryParseHex(item.CustomIconColor, out color);
    }

    /// <summary>
    /// <paramref name="fallback"/> unless the item carries a glyph colour of its own. A fresh brush
    /// each time: the callers hand it to one element, and a brush shared between elements would
    /// recolour them all the first time one of them animates it.
    /// </summary>
    public static Brush BrushFor(OrbitItem? item, Brush fallback) =>
        TryGet(item, out var color) ? new SolidColorBrush(color) : fallback;

    /// <summary>The "#RRGGBB" form stored on the item.</summary>
    public static string ToStored(Color color) => AccentPalette.ToHex(color);
}
