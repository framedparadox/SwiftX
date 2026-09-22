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
        // Black and FrostedGlass are SwiftX-only surface palettes, not native ones — both ride on
        // Dark's own ElementTheme (native chrome/title bar/controls stay dark-themed) and are told
        // apart at the brush level instead, via the AppTheme overloads below. Neither has an
        // OS-level counterpart for System to ever resolve to.
        AppTheme.Dark or AppTheme.Black or AppTheme.FrostedGlass => ElementTheme.Dark,
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
    /// Theme-aware overload for callers that hold the live <see cref="AppTheme"/> rather than only
    /// an ActualTheme, which cannot express Midnight or Frosted glass — both ride Dark's own
    /// ElementTheme (see <see cref="Resolve"/>), so an element that has resolved to "dark" has no
    /// way of saying which of the three it is. Used by Settings' own orbit/quadrant preview bubbles
    /// so they match the real orbit's glass under every theme, and by everything that paints one of
    /// the shell's surfaces straight onto an element.
    /// </summary>
    public static Brush Brush(FrameworkElement element, AppTheme theme, string key) =>
        new SolidColorBrush(ColorFor(IsDark(element), theme, key));

    public static Color ColorFor(FrameworkElement element, string key) =>
        ColorFor(IsDark(element), key);

    /// <summary>
    /// Theme-aware colour lookup — see the <see cref="Brush(FrameworkElement, AppTheme, string)"/>
    /// overload's doc for why <paramref name="theme"/> has to be passed in rather than inferred.
    /// <para>
    /// Both of the dark-riding palettes change only SwiftX's own surface fills, never
    /// foreground/border/muted/accent, which read the same on any dark surface. So most keys behave
    /// exactly like <see cref="ColorFor(bool, string)"/> whatever <paramref name="theme"/> is, and
    /// only the surfaces below diverge.
    /// </para>
    /// </summary>
    public static Color ColorFor(bool dark, AppTheme theme, string key)
    {
        // Frosted glass first, and from one table rather than a switch of its own: its surfaces are
        // computed from the opacity by FrostedGlassPalette, which is also what ShellWindow writes
        // into the window's resources, so asking it here is what keeps a card painted in code the
        // same colour as the card beside it painted from a {ThemeResource}. A key it has no opinion
        // about falls through to Dark's, which is the whole design — see that class's own remarks.
        if (theme == AppTheme.FrostedGlass && FrostedGlassPalette.TryColorFor(key, out var frosted))
            return frosted;

        return ColorForMidnight(dark, theme == AppTheme.Black, key);
    }

    /// <summary>
    /// The Midnight half of <see cref="ColorFor(bool, AppTheme, string)"/>, split out only so the
    /// pattern-matching switch below stays a single expression.
    /// </summary>
    private static Color ColorForMidnight(bool dark, bool isBlack, string key) => key switch
    {
        // Black's orbit bubbles read as a deeper, more opaque black glass than Dark's lighter 40%
        // tint — Dark's own tint is already pure black (there's no "grey orbit" to darken further),
        // so a higher alpha is what actually tells the two apart on the orbit itself.
        "OrbitBubbleGlassTint" or "OrbitBubbleGlassTintColor" when isBlack =>
            Color.FromArgb(0xCC, 0, 0, 0),

        // Everything below is Midnight's own value for a surface the window paints in code, matching
        // ShellWindow.BlackOverrides entry for entry — an element-local brush assignment cannot be
        // reached by that dictionary override, so the two have to agree by hand. Leave one out and
        // the card it paints stays on Dark's grey while the page behind it goes black.
        //
        // First the older Settings-canvas keys, which Midnight flattens to a single black rather
        // than giving them the narrow hierarchy the shell's four get further down.
        "SettingsSurfaceBrush" or "SwiftXCanvasBrush" or "SwiftXNavPaneBrush"
            or "SwiftXCardBackgroundBrush" or "SwiftXOrbitCanvasBrush" when isBlack =>
            Color.FromArgb(0xE6, 0x00, 0x00, 0x00),

        // The Orbit canvas under Midnight. It keeps its hierarchy rather than flattening the way
        // the Settings canvas does — a ring of nodes over a hub over a ground reads as nothing at
        // all when all three are the same black, which is the same lesson the shell's four
        // surfaces already carry above.
        "OrbitCanvasBackgroundBrush" or "OrbitSidebarBackgroundBrush" when isBlack =>
            Color.FromArgb(255, 0x00, 0x00, 0x00),
        "OrbitNodeBackgroundBrush" when isBlack => Color.FromArgb(255, 0x0A, 0x0A, 0x0A),
        "OrbitLabelPillBrush" when isBlack => Color.FromArgb(255, 0x10, 0x10, 0x10),
        "OrbitHubBrush" when isBlack => Color.FromArgb(255, 0x1A, 0x1A, 0x1A),
        "OrbitCanvasBorderBrush" when isBlack => Color.FromArgb(255, 0x2A, 0x2A, 0x2A),
        "OrbitLabelPillBorderBrush" when isBlack => Color.FromArgb(255, 0x24, 0x24, 0x24),
        "OrbitSidebarBorderBrush" when isBlack => Color.FromArgb(255, 0x16, 0x16, 0x16),

        "ShellBackgroundBrush" when isBlack => Color.FromArgb(0xE6, 0x00, 0x00, 0x00),
        "ShellCardBackgroundBrush" when isBlack => Color.FromArgb(0xE6, 0x12, 0x12, 0x12),
        "ShellMedallionBrush" when isBlack => Color.FromArgb(0xE6, 0x0A, 0x0A, 0x0A),
        "ShellPillBrush" when isBlack => Color.FromArgb(0xE6, 0x1A, 0x1A, 0x1A),
        "ShellCardBorderBrush" or "ShellDividerBrush" when isBlack =>
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

        // The shell's own tokens, duplicated here for the same reason every key above is: a caller
        // that has to write a brush directly onto an element — the theme cards and the Settings
        // rail, whose selected/unselected state is an assignment rather than a {ThemeResource}
        // binding a dictionary swap could reach, and every card the Elements and Tools screens
        // build in code — has no element-scoped way to read Themes/Colors.xaml back. Keep every
        // value below in step with that file's own Dark/Light entries.
        //
        // The set is deliberately wider than what is asked for today. A key with no case here does
        // not fail loudly: it falls through to the black/white default at the bottom, which is a
        // plausible-looking answer and so survives review. That is exactly how ShellPillBrush,
        // ShellMedallionBrush and ShellDividerBrush came to paint the Elements and Tools cards'
        // medallions, badges and rules in flat black under Dark and flat white under Light, for
        // every one of those three surfaces at once, while reading as "themed" because they did at
        // least invert. Covering the whole Shell*/Settings* family is what stops the next such
        // caller from landing in the same trap.
        "ShellBackgroundBrush" =>
            dark ? Color.FromArgb(0xD9, 0x2B, 0x2B, 0x2B) : Color.FromArgb(255, 0xF2, 0xF2, 0xF2),

        "ShellCardBackgroundBrush" =>
            dark ? Color.FromArgb(0xD9, 0x33, 0x33, 0x33) : Color.FromArgb(255, 0xFF, 0xFF, 0xFF),

        "ShellCardBorderBrush" or "ShellDividerBrush" =>
            dark ? Color.FromArgb(255, 0x44, 0x44, 0x44) : Color.FromArgb(255, 0xE0, 0xE0, 0xE0),

        "ShellMedallionBrush" =>
            dark ? Color.FromArgb(0xD9, 0x24, 0x24, 0x24) : Color.FromArgb(255, 0xF4, 0xF6, 0xF9),

        "ShellPillBrush" =>
            dark ? Color.FromArgb(0xD9, 0x3A, 0x3A, 0x3A) : Color.FromArgb(255, 0xED, 0xEF, 0xF2),

        "ShellToolbarIconBrush" =>
            dark ? Color.FromArgb(255, 0xBB, 0xBB, 0xBB) : Color.FromArgb(255, 0x55, 0x55, 0x55),

        "ShellSeparatorBrush" =>
            dark ? Color.FromArgb(255, 0x50, 0x50, 0x50) : Color.FromArgb(255, 0xCC, 0xCC, 0xCC),

        "SettingsHeaderBrush" or "SettingsTitleBrush" =>
            dark ? Color.FromArgb(255, 0xFF, 0xFF, 0xFF) : Color.FromArgb(255, 0x1A, 0x1A, 0x1A),

        "SettingsMutedBrush" =>
            dark ? Color.FromArgb(255, 0x80, 0x80, 0x80) : Color.FromArgb(255, 0x77, 0x77, 0x77),

        "SettingsSecondaryTextBrush" =>
            dark ? Color.FromArgb(255, 0xAA, 0xAA, 0xAA) : Color.FromArgb(255, 0x55, 0x55, 0x55),

        "SettingsNavInactiveBrush" =>
            dark ? Color.FromArgb(255, 0xA0, 0xA0, 0xA0) : Color.FromArgb(255, 0x44, 0x44, 0x44),

        // The older Settings-canvas family. Nothing asks this class for them today — they are read
        // from XAML as {ThemeResource} — but they are in both surface-override tables, so the
        // moment one of them is painted in code the question gets asked, and the answer had better
        // not be the default at the bottom. Same reasoning as the Shell* block above, applied before
        // rather than after the fact.
        "SwiftXCanvasBrush" or "SettingsSurfaceBrush" =>
            dark ? Color.FromArgb(0xD9, 0x22, 0x22, 0x22) : Color.FromArgb(255, 0xFF, 0xFF, 0xFF),

        "SwiftXNavPaneBrush" =>
            dark ? Color.FromArgb(0xD9, 0x1A, 0x1A, 0x1A) : Color.FromArgb(255, 0xF0, 0xF4, 0xF8),

        "SwiftXCardBackgroundBrush" =>
            dark ? Color.FromArgb(0xD9, 0x2C, 0x2C, 0x2C) : Color.FromArgb(255, 0xF0, 0xF4, 0xF8),

        "SwiftXCardStrokeBrush" =>
            dark ? Color.FromArgb(255, 0x3A, 0x3A, 0x3A) : Color.FromArgb(255, 0xDD, 0xE4, 0xEC),

        "SwiftXOrbitCanvasBrush" =>
            dark ? Color.FromArgb(0xD9, 0x20, 0x20, 0x20) : Color.FromArgb(255, 0xE7, 0xED, 0xF3),

        "SwiftXDialogSurfaceBrush" =>
            dark ? Color.FromArgb(255, 0x22, 0x22, 0x22) : Color.FromArgb(255, 0xFF, 0xFF, 0xFF),

        // The orbit canvas ground is the app background (ShellBackgroundBrush) in both themes.
        // The sidebar stays the card. Other Orbit* keys keep the ring palette.
        "OrbitCanvasBackgroundBrush" =>
            dark ? Color.FromArgb(0xD9, 0x2B, 0x2B, 0x2B) : Color.FromArgb(255, 0xF2, 0xF2, 0xF2),

        "OrbitCanvasBorderBrush" =>
            dark ? Color.FromArgb(255, 0x33, 0x33, 0x33) : Color.FromArgb(255, 0xD8, 0xDE, 0xE6),

        "OrbitNodeBackgroundBrush" =>
            dark ? Color.FromArgb(255, 0x1A, 0x1A, 0x1A) : Color.FromArgb(255, 0xFF, 0xFF, 0xFF),

        "OrbitHubBrush" =>
            dark ? Color.FromArgb(255, 0x28, 0x28, 0x28) : Color.FromArgb(255, 0xE4, 0xE9, 0xEF),

        "OrbitLabelPillBrush" =>
            dark ? Color.FromArgb(255, 0x18, 0x18, 0x18) : Color.FromArgb(255, 0xFF, 0xFF, 0xFF),

        "OrbitLabelPillBorderBrush" =>
            dark ? Color.FromArgb(255, 0x2E, 0x2E, 0x2E) : Color.FromArgb(255, 0xDD, 0xE4, 0xEC),

        "OrbitSidebarBackgroundBrush" =>
            dark ? Color.FromArgb(0xD9, 0x33, 0x33, 0x33) : Color.FromArgb(255, 0xFF, 0xFF, 0xFF),

        "OrbitSidebarBorderBrush" =>
            dark ? Color.FromArgb(255, 0x1F, 0x1F, 0x1F) : Color.FromArgb(255, 0xE4, 0xE9, 0xEF),

        // Logi's ring-canvas teal, which is not the app chrome's violet and not the user's accent
        // either — docs/full-app-mockups-logi-base.md explains why Logi keeps the two apart and why
        // this screen gets this one. Darkened under Light, where #00DDB3 falls under 3:1 on white.
        "OrbitCanvasAccentBrush" =>
            dark ? Color.FromArgb(255, 0x00, 0xDD, 0xB3) : Color.FromArgb(255, 0x00, 0xA8, 0x8A),

        // The two semantic colours the tool pages paint in code — a certificate's valid/invalid
        // badge, the privacy shield, a parse-error chip. These used to be read from the framework's
        // own SystemFillColorSuccess/CriticalBrush via Application.Current.Resources, which resolves
        // against the app-ambient theme rather than the window this control is actually in — the
        // exact bug this class's own doc comment warns about, and the one live example of it in the
        // whole app (a SwiftX window can run Light while Windows itself runs Dark). Fixed the same
        // way every other code-painted brush here is: a value tied to this element's own ActualTheme.
        // Critical's pair is ToolStatusBar's own — already tuned there for exactly this problem (see
        // that class's remarks on the hard-coded #C42B1C that used to be the only shade available);
        // Success is Fluent's usual green pair, at the same brightness step.
        "ToolSuccessBrush" =>
            dark ? Color.FromArgb(255, 0x6C, 0xCB, 0x5F) : Color.FromArgb(255, 0x0F, 0x7B, 0x0F),

        "ToolCriticalBrush" =>
            dark ? Color.FromArgb(255, 0xFF, 0x99, 0xA4) : Color.FromArgb(255, 0xC4, 0x2B, 0x1C),

        // The pale wash a badge sits on behind its success/critical icon — the same colour at low
        // alpha rather than a separate hue, so the badge and the mark inside it read as one signal
        // at two strengths instead of two different colours that happen to be nearby.
        "ToolSuccessBackgroundBrush" =>
            dark ? Color.FromArgb(0x33, 0x6C, 0xCB, 0x5F) : Color.FromArgb(0x22, 0x0F, 0x7B, 0x0F),

        "ToolCriticalBackgroundBrush" =>
            dark ? Color.FromArgb(0x33, 0xFF, 0x99, 0xA4) : Color.FromArgb(0x22, 0xC4, 0x2B, 0x1C),

        // Copy's fixed communication blue (Themes/Colors.xaml) — has to read as *blue* whatever the
        // app's accent is set to, the same way Delete/Clear's ToolCriticalBrush never tracks it
        // either. Was reached via Application.Current.Resources before this pass (which is how the
        // gap survived unnoticed: that path resolves the XAML dictionary directly, so it never fell
        // through to this switch's own default at all), so unlike SwiftXMutedBrush and the Tool*
        // pair above, this key has no other caller yet to have already forced it in here — see
        // EditorToolPage.HoverAccentBrush, its first.
        "SwiftXCopyHoverBrush" =>
            dark ? Color.FromArgb(255, 0x60, 0xCD, 0xFF) : Color.FromArgb(255, 0x00, 0x78, 0xD4),

        // Deliberately last, and deliberately dull: an unrecognised key is a missing case above,
        // not a colour anyone chose. Black-on-dark / white-on-light is the least likely of the
        // wrong answers to be mistaken for a right one.
        _ => dark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255),
    };
}
