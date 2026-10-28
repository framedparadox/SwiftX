using System.Runtime.InteropServices;
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
    private static readonly HashSet<nint> FrostedHwnds = new();

    /// <summary>Sizes a window's client area given a size in device-independent pixels.</summary>
    public static void SetClientSizeDip(AppWindow appWindow, nint hwnd, double dipW, double dipH)
    {
        double scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        appWindow.Resize(new SizeInt32((int)Math.Ceiling(dipW * scale), (int)Math.Ceiling(dipH * scale)));
    }

    /// <summary>
    /// Makes <paramref name="hwnd"/> the true foreground window, working around the Windows
    /// foreground-lock that <c>SetForegroundWindow</c> alone is subject to when the caller cannot
    /// prove the request rides on a real input event (see the doc comment on
    /// <see cref="NativeMethods.AttachThreadInput"/>).
    /// <para>
    /// This matters for the orbit specifically because its own "click anywhere to dismiss" only
    /// fires when it loses activation it genuinely had — a summon that silently failed to become
    /// foreground can never later fire that transition, leaving the orbit stuck on screen through a
    /// click elsewhere. A hotkey press or a tray click already carry real input and would likely
    /// succeed on their own, but routing every summon through here costs nothing and removes the
    /// one summon path — the hot corner's <c>WM_TIMER</c> poll — that provably would not.
    /// </para>
    /// </summary>
    public static void ForceForeground(nint hwnd)
    {
        nint current = NativeMethods.GetForegroundWindow();
        if (current == hwnd)
            return;

        uint currentThread = NativeMethods.GetCurrentThreadId();
        uint foregroundThread = current == nint.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(current, out _);

        bool attached = foregroundThread != 0 && foregroundThread != currentThread &&
            NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
        }
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
        var titleBar = appWindow.TitleBar;
        titleBar.PreferredTheme = dark ? TitleBarTheme.Dark : TitleBarTheme.Light;

        // ExtendsContentIntoTitleBar leaves the OS caption plate in place unless these are
        // cleared. Unset, that plate is opaque white (or the inactive-caption grey) and reads as
        // a hard line along the top of a frosted / acrylic window.
        var clear = Microsoft.UI.Colors.Transparent;
        titleBar.BackgroundColor = clear;
        titleBar.InactiveBackgroundColor = clear;
        titleBar.ButtonBackgroundColor = clear;
        titleBar.ButtonInactiveBackgroundColor = clear;
        titleBar.ButtonHoverBackgroundColor = clear;
        titleBar.ButtonPressedBackgroundColor = clear;
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

    /// <summary>
    /// Cloaks or uncloaks the window via DWM. While cloaked the window is composed but never drawn
    /// to screen, so it can be shown, activated, sized and moved entirely invisibly and then revealed
    /// in a single step once its XAML content has actually painted.
    /// <para>
    /// This is what removes the blank frame a WinUI window flashes on a cold open: the OS makes the
    /// HWND visible the instant <c>Window.Activate()</c> is called, but the composition engine does
    /// not present the window's first real frame until a beat later, and the gap shows as the window
    /// class's own background (white) behind the rounded rim. Cloaking from construction until the
    /// first screen is up means the user only ever sees the painted window.
    /// </para>
    /// </summary>
    public static void SetCloaked(nint hwnd, bool cloaked)
    {
        int value = cloaked ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAK, ref value, sizeof(int));
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
    /// Makes the DWM window rim invisible — for opaque themes by painting it the window's own
    /// surface color, for Frosted Glass by painting the backdrop's own tint. The
    /// immersive-dark-mode flag is tracked to the theme too (see <see cref="SetFrameTheme"/>) so
    /// DWM renders the window edge / corner anti-aliasing for the right background.
    /// <para>
    /// Used by every SwiftX window. On the orbit it hides a rim that would otherwise read as a
    /// rectangle drawn around the rounded glass strip. On the Mica dialogs it hides the one piece
    /// of rim those windows still show: they extend their content into the title bar, so the top
    /// edge is all that is left of the frame, and anything that doesn't match the surface reads as
    /// a stray line above the title bar.
    /// </para>
    /// <para>
    /// Opaque themes (Black / Dark / Light) must paint a solid COLORREF that matches the surface.
    /// A rim of zero width is not something DWM offers — <c>DWMWA_VISIBLE_FRAME_BORDER_THICKNESS</c>
    /// is retrieve-only and rejects a set with <c>E_INVALIDARG</c>. On those windows,
    /// <see cref="NativeMethods.DWMWA_COLOR_NONE"/> is the wrong tool: with content extended into
    /// the title bar it renders the top edge as flat white or flat black, following the
    /// immersive-dark-mode flag. That is worse than the default rim, and it is what put a hard
    /// white line above a light window and a hard black one above a dark window — the "residual
    /// border" surface-matching replaces. Measured, not inferred: with the sentinel the top row of
    /// the frame comes back <c>#FFFFFF</c> in light and <c>#000000</c> in dark, against Mica
    /// surfaces of roughly <c>#F9F0F4</c> and <c>#271C22</c>.
    /// </para>
    /// <para>
    /// Frosted Glass is the exception. Its acrylic / frosted surface is translucent, so a pure
    /// black COLORREF on the rim reads as a hard black frame. Pass the backdrop's own tint
    /// (<c>#101016</c> as COLORREF <c>0x00161010</c>) instead — close enough to the glass that
    /// the 1px top edge disappears into it, which
    /// <see cref="NativeMethods.DWMWA_COLOR_NONE"/> does not: that sentinel, with content
    /// extended into the title bar, paints the caption as flat white.
    /// </para>
    /// <para>
    /// An explicit color is also the only one of the two that survives a theme switch on opaque
    /// windows: DWM re-composes the frame for it immediately, with no move, resize or re-activation
    /// needed and nothing left over from the previous theme.
    /// </para>
    /// <para>
    /// A flat COLORREF is all the attribute accepts for opaque themes, while Mica is a blur of the
    /// wallpaper and so varies across the width of the window. The match is therefore very close
    /// rather than exact — single digits per channel, against the tens-to-hundreds that made the
    /// old rim obvious.
    /// </para>
    /// </summary>
    /// <param name="surfaceOverride">
    /// A COLORREF (0x00BBGGRR) to paint the rim instead of the theme's flat <see cref="SurfaceDark"/>/
    /// <see cref="SurfaceLight"/>, for a window whose surface is not that flat color. Frosted Glass
    /// passes the acrylic tint (<c>0x00161010</c>). Settings in Dark is the one opaque such window:
    /// its root now resolves to a lighter, design-colours.md-anchored surface (#202020, see
    /// SettingsSurfaceBrush in Themes/Colors.xaml) rather than the shared #000000 every other
    /// window still uses, so painting the rim pure black would draw a hard edge around the window
    /// instead of disappearing into it.
    /// </param>
    public static void HideWindowBorder(nint hwnd, bool dark, int? surfaceOverride = null)
    {
        SetFrameTheme(hwnd, dark);

        int surface = surfaceOverride ?? (dark ? SurfaceDark : SurfaceLight);
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref surface, sizeof(int));
        // Caption is a separate DWM colour from the rim. Leaving it at the system default is what
        // drew a white strip along the top of an extended-into-title-bar window, even when the
        // border itself matched the surface.
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_CAPTION_COLOR, ref surface, sizeof(int));
    }

    /// <summary>
    /// Frosted Glass has no DWM frame: no 1px white/black rim, no caption plate. Opaque themes
    /// keep the usual surface-matched rim.
    /// <para>
    /// <see cref="NativeMethods.DWMWA_COLOR_NONE"/> paints a white caption. A solid COLORREF on
    /// acrylic paints a black (or tinted) one. The combination that actually removes the line is
    /// to drop WinUI's border, zero the non-client metrics so DWM has nothing to stroke, skip NC
    /// paint, and reset any leftover border/caption colour to the DWM default.
    /// </para>
    /// </summary>
    public static void ApplyShellFrame(
        AppWindow appWindow, nint hwnd, bool dark, bool frosted, int rim, bool resizable = true)
    {
        SetTitleBarTheme(appWindow, dark);
        SetRoundedCorners(hwnd);

        try
        {
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(hasBorder: !frosted, hasTitleBar: true);
                // Written only when it differs, and each window passes its own answer. This runs from
                // Window.Activated, which fires inside the first Activate(); flipping IsResizable
                // there (the app window is built fixed-size, this used to force it back to true)
                // hides the window Activate() is in the middle of showing - it opened as a blank
                // frame for ~30 ms and then vanished, on every first open of a new window.
                if (presenter.IsResizable != resizable)
                    presenter.IsResizable = resizable;
            }
        }
        catch
        {
            // Some presenters reject a borderless title bar; the DWM calls below still run.
        }

        SetFrostedNonClient(hwnd, frosted);

        if (frosted)
        {
            var glass = new NativeMethods.MARGINS
            {
                cxLeftWidth = -1,
                cxRightWidth = -1,
                cyTopHeight = -1,
                cyBottomHeight = -1,
            };
            NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref glass);

            // COLOR_NONE is the white top edge. A tint COLORREF is the black one. Default, on a
            // window whose NC metrics are already zero, leaves nothing to stroke.
            int reset = NativeMethods.DWMWA_COLOR_DEFAULT;
            NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref reset, sizeof(int));
            NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_CAPTION_COLOR, ref reset, sizeof(int));

            int ncOff = NativeMethods.DWMNCRP_DISABLED;
            NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_NCRENDERING_POLICY, ref ncOff, sizeof(int));

            StripEdgeStyles(hwnd);
            SetFrameTheme(hwnd, dark);
            SuppressClientErase(hwnd);
            NotifyFrameChanged(hwnd);
            return;
        }

        int ncOn = NativeMethods.DWMNCRP_USEWINDOWSTYLE;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_NCRENDERING_POLICY, ref ncOn, sizeof(int));

        var zero = new NativeMethods.MARGINS();
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref zero);
        HideWindowBorder(hwnd, dark, rim);
        NotifyFrameChanged(hwnd);
    }

    private static void SetFrostedNonClient(nint hwnd, bool frosted)
    {
        if (frosted)
            FrostedHwnds.Add(hwnd);
        else
            FrostedHwnds.Remove(hwnd);
    }

    private static void StripEdgeStyles(nint hwnd)
    {
        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        long stripped = ex & ~(NativeMethods.WS_EX_CLIENTEDGE | NativeMethods.WS_EX_WINDOWEDGE |
                               NativeMethods.WS_EX_DLGMODALFRAME | NativeMethods.WS_EX_STATICEDGE);
        if (stripped != ex)
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (nint)stripped);
    }

    private static void NotifyFrameChanged(nint hwnd)
    {
        NativeMethods.SetWindowPos(hwnd, 0, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_FRAMECHANGED);
    }

    /// <summary>
    /// Stops Win32 from filling the client with the class background brush. On a frosted/acrylic
    /// window that fill is opaque black, and it shows up as a rim around the rounded glass.
    /// Idempotent — the subclass is installed once.
    /// </summary>
    public static void SuppressClientErase(nint hwnd) => InstallNoEraseSubclass(hwnd);

    /// <summary>
    /// Removes the maximize box from the window's native style outright, rather than relying on
    /// <c>OverlappedPresenter.IsMaximizable = false</c> alone. The presenter flag disables the
    /// Snap-Layouts-aware caption button WinAppSDK draws for a resizable window, but ShellWindow
    /// is both non-resizable and non-maximizable from construction — belt and braces against the
    /// button lingering, greyed out, in the caption instead of disappearing from it.
    /// </summary>
    public static void RemoveMaximizeBox(nint hwnd)
    {
        long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);
        long stripped = style & ~NativeMethods.WS_MAXIMIZEBOX;
        if (stripped != style)
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, (nint)stripped);
        NotifyFrameChanged(hwnd);
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
    /// and make no difference — the orbit renders pixel-identical with and without them.
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
        if (FrostedHwnds.Contains(hwnd))
        {
            // Zero NC metrics so DWM has no 1px rim to stroke, and skip painting that rim.
            if (msg == NativeMethods.WM_NCCALCSIZE || msg == NativeMethods.WM_NCPAINT)
                return 0;
        }

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

        // SetWindowRgn takes ownership of the region handle — but only when it succeeds. On
        // failure the caller still owns it, and this runs on every render and every hover pill,
        // so a handle dropped here would leak GDI objects for as long as the orbit is used. The
        // process-wide GDI limit is 10 000.
        if (NativeMethods.SetWindowRgn(hwnd, combined, true) == 0)
        {
            Diag.Log($"WindowChrome: SetWindowRgn failed ({Marshal.GetLastWin32Error()})");
            NativeMethods.DeleteObject(combined);
        }
    }
}
