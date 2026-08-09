using SwiftX.Interop;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace SwiftX.Services;

/// <summary>Native window chrome tweaks that AppWindow/WinUI don't expose directly.</summary>
public static class WindowChrome
{
    private const nuint NoEraseSubclassId = 0x53774658; // 'SwfX'
    private static readonly Dictionary<nint, NativeMethods.SubclassProc> NoEraseSubclassCallbacks = new();

    /// <summary>Sizes a window's client area given a size in device-independent pixels.</summary>
    public static void SetClientSizeDip(AppWindow appWindow, nint hwnd, double dipW, double dipH)
    {
        double scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        appWindow.Resize(new SizeInt32((int)Math.Ceiling(dipW * scale), (int)Math.Ceiling(dipH * scale)));
    }

    /// <summary>Centers a window on whichever monitor currently hosts the cursor.</summary>
    public static void CenterOnCursor(AppWindow appWindow, WindowId windowId)
    {
        RectInt32 work;
        if (NativeMethods.GetCursorPos(out var p))
            work = DisplayArea.GetFromPoint(new PointInt32(p.X, p.Y), DisplayAreaFallback.Nearest).WorkArea;
        else
            work = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary).WorkArea;

        var size = appWindow.Size;
        int x = work.X + (work.Width - size.Width) / 2;
        int y = work.Y + (work.Height - size.Height) / 2;
        appWindow.Move(new PointInt32(x, y));
    }

    /// <summary>
    /// Makes the window a borderless, always-on-top tool window: no title bar or frame,
    /// absent from the taskbar and Alt-Tab, and non-resizable — i.e. dock-like.
    /// </summary>
    public static void MakeBorderlessToolWindow(AppWindow appWindow, nint hwnd)
    {
        if (appWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false);
            p.IsAlwaysOnTop = true;
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
        }

        appWindow.IsShownInSwitchers = false;

        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (nint)ex);
    }

    /// <summary>
    /// Sets the theme of the system caption buttons (minimize / close) so they render with the
    /// right glyph color from the first frame. Without this the buttons inherit the theme late
    /// (only after activation / a pointer pass over them), so a light-themed window briefly shows
    /// hard-to-see light glyphs.
    /// </summary>
    public static void SetTitleBarTheme(AppWindow appWindow, bool dark)
    {
        appWindow.TitleBar.PreferredTheme = dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
    }

    /// <summary>
    /// Tells DWM which theme to draw the window's non-client frame for.
    /// <para>
    /// This is a separate thing from <see cref="SetTitleBarTheme"/>, which only colors the caption
    /// buttons. Note it does <b>not</b> govern the 1px window rim — that is
    /// <see cref="HideWindowBorder"/>.
    /// </para>
    /// </summary>
    public static void SetFrameTheme(nint hwnd, bool dark)
    {
        int immersiveDark = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref immersiveDark, sizeof(int));
    }

    /// <summary>Applies the Windows 11 rounded-corner treatment to the window.</summary>
    public static void SetRoundedCorners(nint hwnd, bool small = false)
    {
        int pref = small ? NativeMethods.DWMWCP_ROUNDSMALL : NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
    }

    // Phase 1 solid surfaces: pure white / pure black (COLORREF 0x00BBGGRR).
    private const int SurfaceLight = 0x00FFFFFF;
    private const int SurfaceDark = 0x00000000;

    /// <summary>
    /// Makes the DWM window rim invisible by painting it the window's own surface color for the
    /// current theme. The immersive-dark-mode flag is tracked to the theme too (see
    /// <see cref="SetFrameTheme"/>) so DWM renders the window edge / corner anti-aliasing for the
    /// right background.
    /// <para>
    /// Used by every Anchor window. On the dock it hides a rim that would otherwise read as a
    /// rectangle drawn around the rounded glass strip. On the Mica dialogs it hides the one piece
    /// of rim those windows still show: they extend their content into the title bar, so the top
    /// edge is all that is left of the frame, and anything that doesn't match the surface reads as
    /// a stray line above the title bar.
    /// </para>
    /// <para>
    /// Painting it is the only option — a rim of zero width is not something DWM offers.
    /// <c>DWMWA_VISIBLE_FRAME_BORDER_THICKNESS</c> is retrieve-only and rejects a set with
    /// <c>E_INVALIDARG</c>, and the <c>DWMWA_COLOR_NONE</c> sentinel does not mean "draw nothing"
    /// here: on a window whose content is extended into the title bar it renders the top edge as
    /// flat white or flat black, following the immersive-dark-mode flag. That is worse than the
    /// default rim, and it is what put a hard white line above a light window and a hard black one
    /// above a dark window — the "residual border" this replaces. Measured, not inferred: with the
    /// sentinel the top row of the frame comes back <c>#FFFFFF</c> in light and <c>#000000</c> in
    /// dark, against Mica surfaces of roughly <c>#F9F0F4</c> and <c>#271C22</c>.
    /// </para>
    /// <para>
    /// An explicit color is also the only one of the two that survives a theme switch: DWM
    /// re-composes the frame for it immediately, with no move, resize or re-activation needed and
    /// nothing left over from the previous theme.
    /// </para>
    /// <para>
    /// A flat COLORREF is all the attribute accepts, while Mica is a blur of the wallpaper and so
    /// varies across the width of the window. The match is therefore very close rather than exact
    /// — single digits per channel, against the tens-to-hundreds that made the old rim obvious.
    /// </para>
    /// </summary>
    public static void HideWindowBorder(nint hwnd, bool dark)
    {
        SetFrameTheme(hwnd, dark);

        int surface = dark ? SurfaceDark : SurfaceLight;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref surface, sizeof(int));
    }

    /// <summary>
    /// Strips the non-client window frame (caption / resize border) so the client area — and
    /// our glass — extends all the way to the window edge. This removes the ~3px frame whose
    /// inner highlight shows up as a white line around a rounded backdrop window.
    /// </summary>
    public static void StripFrame(nint hwnd)
    {
        long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);
        style &= ~(NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME |
                   NativeMethods.WS_BORDER | NativeMethods.WS_DLGFRAME);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, (nint)style);
        NativeMethods.SetWindowPos(hwnd, 0, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_FRAMECHANGED);
    }

    /// <summary>
    /// Makes a floating overlay window per-pixel transparent: only what the XAML tree actually
    /// paints — the bubbles and the hub — is visible, and the desktop shows through everywhere
    /// else, including the gaps between bubbles.
    /// <para>
    /// It takes exactly two things, and both are about <em>not</em> painting. A WinUI 3 window's
    /// surface is already transparent wherever XAML doesn't paint; the black plate came from the
    /// window painting over that on its own account, from two directions:
    /// </para>
    /// <list type="number">
    /// <item>Win32 erases the client area. <c>WM_ERASEBKGND</c> is answered "handled" without a
    /// fill, so the class background brush never gets to lay down its opaque rectangle.</item>
    /// <item>WinUI clears the window when it has no backdrop. A <see cref="TransparentBackdrop"/>
    /// — a backdrop that paints nothing — is attached so it doesn't.</item>
    /// </list>
    /// <para>
    /// Measured, not inferred, and both halves are load-bearing: filling the erase with magenta
    /// turns the plate magenta, and re-nulling the backdrop with the erase already suppressed
    /// brings the black straight back. That the plate tracks the fill color rather than the theme
    /// is what made the old "clear to black and let DWM drop it" trick paint the very black it
    /// was meant to remove — DWM never dropped it.
    /// </para>
    /// <para>
    /// Nothing else contributes, so nothing else is done. Empty-region
    /// <c>DwmEnableBlurBehindWindow</c> and <c>DwmExtendFrameIntoClientArea</c> were both tried
    /// and make no difference — the ring renders pixel-identical with and without them.
    /// </para>
    /// </summary>
    public static void MakeTransparentOverlay(Window window, nint hwnd)
    {
        // A backdrop that paints nothing. Not the same as no backdrop at all — see
        // TransparentBackdrop. Idempotent because Summon() re-applies this on every invocation.
        if (window.SystemBackdrop is not TransparentBackdrop)
            window.SystemBackdrop = new TransparentBackdrop();

        // Square corners — Win11's rounded clip otherwise leaves a soft rectangular shadow.
        int pref = NativeMethods.DWMWCP_DONOTROUND;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));

        InstallNoEraseSubclass(hwnd);
    }

    /// <summary>
    /// Subclasses the window so <c>WM_ERASEBKGND</c> is swallowed rather than reaching
    /// <c>DefWindowProc</c>. See <see cref="MakeTransparentOverlay"/> for why this is half of the
    /// transparency.
    /// </summary>
    private static void InstallNoEraseSubclass(nint hwnd)
    {
        if (NoEraseSubclassCallbacks.ContainsKey(hwnd))
            return;

        NativeMethods.SubclassProc callback = NoEraseSubclassProc;
        // Keep the delegate rooted for the lifetime of the subclass.
        NoEraseSubclassCallbacks[hwnd] = callback;
        NativeMethods.SetWindowSubclass(hwnd, callback, NoEraseSubclassId, 0);
    }

    private static nint NoEraseSubclassProc(
        nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nint data)
    {
        // Non-zero says "background erased" — with nothing painted, which is the point.
        if (msg == NativeMethods.WM_ERASEBKGND)
            return 1;

        return NativeMethods.DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Restricts the HWND hit-test region to a union of ellipses (DIP centers/radii).
    /// Empty chrome then click-throughs to whatever is underneath. Pass an empty list to clear.
    /// </summary>
    public static void SetEllipticHitRegions(
        nint hwnd, double dpiScale, IReadOnlyList<(double cx, double cy, double radius)> ellipsesDip)
    {
        if (ellipsesDip.Count == 0)
        {
            NativeMethods.SetWindowRgn(hwnd, 0, true);
            return;
        }

        nint combined = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        foreach (var (cx, cy, radius) in ellipsesDip)
        {
            int left = (int)Math.Floor((cx - radius) * dpiScale);
            int top = (int)Math.Floor((cy - radius) * dpiScale);
            int right = (int)Math.Ceiling((cx + radius) * dpiScale);
            int bottom = (int)Math.Ceiling((cy + radius) * dpiScale);
            nint ellipse = NativeMethods.CreateEllipticRgn(left, top, right, bottom);
            NativeMethods.CombineRgn(combined, combined, ellipse, NativeMethods.RGN_OR);
            NativeMethods.DeleteObject(ellipse);
        }

        // SetWindowRgn takes ownership of the region handle.
        NativeMethods.SetWindowRgn(hwnd, combined, true);
    }
}
