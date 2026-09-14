using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SwiftX.Models;
using Windows.UI;

namespace SwiftX.Services;

/// <summary>
/// The backdrop behind <see cref="Models.AppTheme.FrostedGlass"/>: a thin desktop acrylic tuned so
/// the desktop behind the window stays legible through it, rather than the near-opaque plate
/// <see cref="Microsoft.UI.Xaml.Media.MicaBackdrop"/> gives every other theme.
/// <para>
/// Ported from windows-app-mx's <c>LogiApp/Helpers/FrostedGlassBackdropV2.cs</c> (branch
/// <c>logiApp/dotnet</c>), which is itself that repo's port of the <c>FrostedGlassBackdrop</c> class
/// on its <c>add_acrylic_frosted_theme</c> branch. The tint/luminosity curves, the tint colour and
/// the fallback colour are all that file's, unchanged, so the two apps' frosted glass reads the
/// same over the same wallpaper.
/// </para>
/// <para>
/// Where this differs from the original: Logi drives <c>App.FrostedGlassV2Opacity</c> from a live
/// slider and calls <c>UpdateProperties</c> as the user drags. SwiftX offers frosted glass as one
/// theme among five, with no opacity control of its own, so <see cref="Opacity"/> is a constant at
/// Logi's own default and the controller is configured once on connect. If a slider is ever added,
/// the shape to add back is Logi's: make <see cref="Opacity"/> settable and re-run
/// <see cref="UpdateProperties"/> on each change.
/// </para>
/// </summary>
public sealed class FrostedGlassBackdrop : SystemBackdrop
{
    /// <summary>
    /// Where each of <see cref="FrostedGlassLevel"/>'s three steps sits on the source theme's own
    /// 0.10-1.0 opacity scale. Shared with <see cref="FrostedGlassPalette"/>, which derives the
    /// window's surface alphas from the same number, so the chrome and the blur behind it always
    /// agree about how much frost there is.
    /// <para>
    /// <see cref="FrostedGlassLevel.Light"/> is Logi's own default
    /// (<c>App.FrostedGlassV2Opacity</c>) unchanged, which is what the theme shipped as. The two
    /// above it are this app's: enough of a step between them to be worth having as separate
    /// choices, and short of 1.0 at the top because a fully opaque frosted glass is just a dark
    /// theme with extra GPU cost.
    /// </para>
    /// </summary>
    public static double OpacityFor(FrostedGlassLevel level) => level switch
    {
        FrostedGlassLevel.Medium => 0.80,
        FrostedGlassLevel.Dark => 0.95,
        _ => 0.65,
    };

    /// <summary>
    /// The level every frosted surface in the app is currently computed at.
    /// <para>
    /// Static, and set by <see cref="OrbitManager"/> at startup and on every change, for the same
    /// reason <see cref="Models.OrbitMetrics"/> holds the orbit's geometry the same way: the value
    /// is read from static colour lookups (<see cref="ThemeHelper"/>) that have no instance to
    /// carry a config reference on, and threading one through every one of those call sites to
    /// express a single app-wide choice would be a worse trade than one piece of well-named static
    /// state.
    /// </para>
    /// </summary>
    public static FrostedGlassLevel Level { get; private set; } = FrostedGlassLevel.Light;

    /// <summary>The opacity <see cref="Level"/> currently resolves to.</summary>
    public static double Opacity => OpacityFor(Level);

    /// <summary>
    /// Points every frosted surface at a new level. Returns true when the value actually changed,
    /// so a caller can skip the repaint — and, in ShellWindow's case, the backdrop teardown — that
    /// would otherwise follow a no-op.
    /// </summary>
    public static bool SetLevel(FrostedGlassLevel level)
    {
        if (Level == level) return false;
        Level = level;
        return true;
    }

    private DesktopAcrylicController? _controller;

    protected override void OnTargetConnected(
        ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        // Acrylic is unavailable on some machines (no composition support, or the user has turned
        // transparency effects off in Windows' own accessibility settings). Attaching nothing there
        // is deliberate: the window keeps its own surface brushes and simply reads as a solid dark
        // theme, which is what FallbackColor below would have produced anyway.
        if (!DesktopAcrylicController.IsSupported())
            return;

        _controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
        UpdateProperties();

        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(
            GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop connectedTarget)
    {
        base.OnTargetDisconnected(connectedTarget);

        if (_controller is null) return;

        _controller.RemoveSystemBackdropTarget(connectedTarget);
        _controller.Dispose();
        _controller = null;
    }

    /// <summary>
    /// Writes the current <see cref="Opacity"/> onto the controller through the source theme's own
    /// curves. Public because a level change has to reach an already-connected controller, and
    /// tearing the backdrop down and rebuilding it to move one float would flicker the window.
    /// <para>
    /// Both are affine in the opacity and then clamped, which is what keeps the two extremes usable:
    /// at 0.10 the tint is 0.08 and the desktop shows through almost unfiltered; at 1.0 it is 0.71
    /// and the glass reads as milky rather than opaque. Luminosity is the weaker of the two so the
    /// colour behind the window keeps some of its saturation instead of washing to grey.
    /// </para>
    /// </summary>
    public void UpdateProperties()
    {
        if (_controller is null || _controller.IsClosed) return;

        _controller.TintOpacity = (float)Math.Clamp(Opacity * 0.70 + 0.01, 0.05, 0.85);
        _controller.LuminosityOpacity = (float)Math.Clamp(Opacity * 0.60 + 0.10, 0.10, 0.85);
        _controller.TintColor = Color.FromArgb(255, 16, 16, 22);
        _controller.FallbackColor = Color.FromArgb(255, 24, 24, 30);
    }
}
