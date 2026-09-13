using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SwiftX.Models;
using Windows.UI;

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
        // Black is a SwiftX-only surface palette, not a native one — it rides on Dark's own
        // ElementTheme (native chrome/title bar/controls stay dark-themed) and is told apart at
        // the brush level instead, via the isBlack overloads below. There is no OS-level "Black"
        // for System to ever resolve to.
        AppTheme.Dark or AppTheme.Black => ElementTheme.Dark,
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

    /// <summary>
    /// Black-aware overload for callers that can tell Black apart from plain Dark directly (i.e.
    /// they hold the live <see cref="AppTheme"/>, not just an ActualTheme that can't express it —
    /// see the class doc's own warning about that gap). Used by Settings' own orbit/quadrant preview
    /// bubbles so they match the real orbit's glass under Black the same way they already do under
    /// Dark.
    /// </summary>
    public static Brush Brush(FrameworkElement element, bool isBlack, string key) =>
        new SolidColorBrush(ColorFor(IsDark(element), isBlack, key));

    public static Color ColorFor(FrameworkElement element, string key) =>
        ColorFor(IsDark(element), key);

    /// <summary>
    /// Black-aware color lookup — see the <see cref="Brush(FrameworkElement, bool, string)"/>
    /// overload's doc for why <paramref name="isBlack"/> has to be passed in rather than inferred.
    /// Every key but the orbit bubbles' own glass tint behaves exactly like
    /// <see cref="ColorFor(bool, string)"/> regardless of <paramref name="isBlack"/> — Black only
    /// ever changes SwiftX's own surface fills (this key, and Settings' resource overrides in
    /// ShellWindow.ApplyBlackOverrides), never foreground/border/muted/accent, which read the
    /// same on any dark surface — with one exception since the app shell landed,
    /// <c>ShellCardBorderBrush</c>, whose Midnight value is a step off Dark's.
    /// </summary>
    public static Color ColorFor(bool dark, bool isBlack, string key) => key switch
    {
        // Black's orbit bubbles read as a deeper, more opaque black glass than Dark's lighter 40%
        // tint — Dark's own tint is already pure black (there's no "grey orbit" to darken further),
        // so a higher alpha is what actually tells the two apart on the orbit itself.
        "OrbitBubbleGlassTint" or "OrbitBubbleGlassTintColor" when isBlack =>
            Color.FromArgb(0xCC, 0, 0, 0),

        // Midnight's card edge, matching ShellWindow.BlackOverrides' own value for the same key.
        // The shell asks for this one in code (the theme cards' unselected border, written
        // directly onto the element and so unreachable by a dictionary), which is why it needs a
        // Black-aware answer here rather than falling through to Dark's #444444.
        "ShellCardBorderBrush" when isBlack =>
            Color.FromArgb(255, 0x2A, 0x2A, 0x2A),

        _ => ColorFor(dark, key),
    };

    public static Color ColorFor(bool dark, string key) => key switch
    {
        "SwiftXSurfaceBrush" or "SwiftXSurfaceColor"
            or "OrbitBubbleBackground" or "OrbitHubBackground" =>
            dark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255),

        "SwiftXForegroundBrush" or "SwiftXForegroundColor"
            or "SwiftXAccentBrush" or "SwiftXAccentColor" =>
            dark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0, 0, 0),

        // The orbit bubbles' glass tint (see OrbitWindow's CreateBubble/ApplyBubbleFill) — sits over
        // a real DesktopAcrylicBackdrop, so it only needs to carry SwiftX's own dark/light identity
        // at partial opacity; the backdrop's own recipe already handles blur-vs-legibility contrast.
        "OrbitBubbleGlassTint" or "OrbitBubbleGlassTintColor" =>
            dark ? Color.FromArgb(0x66, 0, 0, 0) : Color.FromArgb(0x66, 255, 255, 255),

        "SwiftXBorderBrush" or "SwiftXBorderColor" =>
            dark ? Color.FromArgb(255, 0x33, 0x33, 0x33) : Color.FromArgb(255, 0xCC, 0xCC, 0xCC),

        "SwiftXMutedBrush" or "SwiftXMutedColor" =>
            dark ? Color.FromArgb(255, 0x88, 0x88, 0x88) : Color.FromArgb(255, 0x66, 0x66, 0x66),

        "OrbitWindowBackground" =>
            Color.FromArgb(0, 0, 0, 0),

        // Destructive-action hover, the one place the orbit leaves the black/white palette. The
        // same soft red in both themes: it has to stay legible under white and black glyphs
        // alike, and a theme-flipped red would read as two different signals.
        "OrbitCloseHoverBackground" =>
            Color.FromArgb(255, 0xEF, 0x6F, 0x6F),

        "OrbitCloseHoverForeground" =>
            Color.FromArgb(255, 0xFF, 0xFF, 0xFF),

        // Two of the shell's own tokens, duplicated here for the same reason every key above is:
        // a caller that has to write a brush directly onto an element (ShellWindow's theme cards
        // and its Settings rail, whose selected/unselected state is an assignment, not a
        // {ThemeResource} binding a dictionary swap could reach) has no element-scoped way to read
        // Themes/Colors.xaml back. Keep these two in step with that file's Dark/Light values.
        "ShellCardBorderBrush" =>
            dark ? Color.FromArgb(255, 0x44, 0x44, 0x44) : Color.FromArgb(255, 0xE0, 0xE0, 0xE0),

        "SettingsMutedBrush" =>
            dark ? Color.FromArgb(255, 0x80, 0x80, 0x80) : Color.FromArgb(255, 0x77, 0x77, 0x77),

        _ => dark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255),
    };
}
