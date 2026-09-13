using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SwiftX.Services;

/// <summary>
/// A <see cref="SystemBackdrop"/> that contributes no pixels of its own, leaving the window's
/// surface transparent wherever the XAML tree doesn't paint.
/// <para>
/// Attaching this — as opposed to leaving <see cref="Window.SystemBackdrop"/> null — is half of
/// what makes the orbit float; suppressing the <c>WM_ERASEBKGND</c> fill in
/// <see cref="WindowChrome.MakeTransparentOverlay"/> is the other half. Measured, not inferred:
/// with the erase already suppressed, setting this back to null brings the opaque black plate
/// straight back. Simply having <em>a</em> backdrop attached is what stops WinUI clearing the
/// window to an opaque surface, so an empty one is not a placeholder — it is the whole point.
/// </para>
/// <para>
/// Deliberately empty rather than assigning a transparent brush, because there is no transparent
/// brush to assign. Windows App SDK 1.8 projects
/// <c>ICompositionSupportsSystemBackdrop.SystemBackdrop</c> as a system
/// <c>Windows.UI.Composition</c> brush, which the WinUI compositor cannot produce and whose own
/// compositor throws <c>E_ACCESSDENIED</c> when constructed on a WinUI thread; going under the
/// projection to the ABI fails too, with <c>put_SystemBackdrop</c> rejecting a
/// <c>Microsoft.UI.Composition</c> brush as <c>E_NOINTERFACE</c>. None of which matters here —
/// no brush is the result we were after.
/// </para>
/// </summary>
public sealed class TransparentBackdrop : SystemBackdrop
{
    /// <summary>
    /// A genuine no-op — not simply "not overridden". <see cref="SystemBackdrop"/>'s own,
    /// unoverridden default implementation of this hook round-trips back through the WinRT ABI
    /// (visible in its call stack as a second, re-entrant <c>ISystemBackdropOverrides</c> call),
    /// and on this app's hidden owner <c>OrbitWindow</c> — created once at startup and kept alive
    /// with no content ever shown, purely so WinUI doesn't exit with no windows open (see
    /// <c>OrbitManager.Start</c>) — that round-trip throws <see cref="ArgumentException"/> ("The
    /// parameter is incorrect") the moment anything fires it, which a theme change does:
    /// <c>OrbitManager.ApplyTheme</c> sets <c>RequestedTheme</c> on every open window, this
    /// perpetually-hidden one included. This backdrop has no configuration to react to in the
    /// first place — see the class doc above — so the fix is to never let that buggy base call
    /// happen at all, rather than let the framework reach it.
    /// </summary>
    protected override void OnDefaultSystemBackdropConfigurationChanged(
        Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
    }
}
