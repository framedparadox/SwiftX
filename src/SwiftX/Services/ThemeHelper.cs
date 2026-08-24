using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SwiftX.Models;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace SwiftX.Services;

/// <summary>
/// Resolves Light / Dark / System to an <see cref="ElementTheme"/> and supplies solid Phase-1
/// chrome brushes against a specific element's ActualTheme.
/// <para>
/// Do not look up themed brushes via <c>Application.Current.Resources</c> when a window can disagree
/// with the application theme (user chose Light while Windows is Dark). Application resources
/// follow the OS/app theme, so that path paints dark surfaces into a light window — black bubbles
/// with black glyphs. Resolve against the window root instead.
/// </para>
/// </summary>
public static class ThemeHelper
{
    public static ElementTheme Resolve(AppTheme theme) => theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    public static bool IsDark(FrameworkElement element) =>
        element.ActualTheme != ElementTheme.Light;

    public static bool IsDark(ElementTheme actual) => actual != ElementTheme.Light;

    /// <summary>
    /// Solid brush for a known theme key, resolved against <paramref name="element"/>'s ActualTheme.
    /// </summary>
    public static Brush Brush(FrameworkElement element, string key) =>
        new SolidColorBrush(ColorFor(IsDark(element), key));

    public static Color ColorFor(FrameworkElement element, string key) =>
        ColorFor(IsDark(element), key);

    public static Color ColorFor(bool dark, string key) => key switch
    {
        "SwiftXSurfaceBrush" or "SwiftXSurfaceColor"
            or "RingBubbleBackground" or "RingHubBackground" =>
            dark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255),

        "SwiftXForegroundBrush" or "SwiftXForegroundColor"
            or "SwiftXAccentBrush" or "SwiftXAccentColor" =>
            dark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0, 0, 0),

        "SwiftXBorderBrush" or "SwiftXBorderColor" =>
            dark ? Color.FromArgb(255, 0x33, 0x33, 0x33) : Color.FromArgb(255, 0xCC, 0xCC, 0xCC),

        "SwiftXMutedBrush" or "SwiftXMutedColor" =>
            dark ? Color.FromArgb(255, 0x88, 0x88, 0x88) : Color.FromArgb(255, 0x66, 0x66, 0x66),

        "RingWindowBackground" =>
            Color.FromArgb(0, 0, 0, 0),

        // Destructive-action hover, the one place the ring leaves the black/white palette. The
        // same soft red in both themes: it has to stay legible under white and black glyphs
        // alike, and a theme-flipped red would read as two different signals.
        "RingCloseHoverBackground" =>
            Color.FromArgb(255, 0xEF, 0x6F, 0x6F),

        "RingCloseHoverForeground" =>
            Color.FromArgb(255, 0xFF, 0xFF, 0xFF),

        _ => dark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255),
    };

    /// <summary>The user's Windows accent color — used for the launcher's edge notch.</summary>
    public static Color WindowsAccentColor() =>
        new UISettings().GetColorValue(UIColorType.Accent);
}
