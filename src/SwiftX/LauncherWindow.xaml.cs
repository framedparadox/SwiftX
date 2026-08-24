using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SwiftX.Interop;
using SwiftX.Models;
using SwiftX.Services;
using Windows.Graphics;

namespace SwiftX;

/// <summary>
/// The floating launcher: a small always-on-top puck that summons the ring with the mouse, for
/// when reaching for the hotkey isn't what you want. Three behaviours, and they compose:
/// <list type="bullet">
/// <item><b>Float.</b> It sits above everything and can be dragged anywhere on the desktop.</item>
/// <item><b>Snap.</b> Dropped near a work-area edge it docks there and slides out of the way,
/// leaving a notch — a thin sliver of itself — behind the edge. Hovering the notch slides it back
/// out; moving away hides it again.</item>
/// <item><b>Stay on its own monitor.</b> Hiding works by pushing the puck out through the edge,
/// and on an edge shared with the next monitor along, "out through the edge" is somewhere very
/// visible. The window is clipped to its own monitor's work area so the hidden part is not drawn
/// anywhere at all — see <see cref="LauncherSnap.ClipToWorkArea"/>.</item>
/// </list>
/// <para>
/// All placement arithmetic lives in <see cref="LauncherSnap"/> as pure functions over physical
/// screen pixels; this class is the part that owns an HWND — DPI, timers, pointer input and
/// persistence. Nothing here re-derives geometry it could ask for instead.
/// </para>
/// </summary>
public sealed partial class LauncherWindow : Window
{
    /// <summary>Diameter of the puck, in DIPs. Converted to pixels per monitor — see <see cref="SizePx"/>.</summary>
    private const double SizeDip = 46;

    /// <summary>Rounded-square corner radius — a bubble, not a circle.</summary>
    private const double CornerRadiusDip = 12;

    private const double LogoDip = 22;

    /// <summary>Accent-colored edge strip that reads as the dock notch when snapped to a screen edge.</summary>
    private const double NotchAccentDip = 3;

    /// <summary>
    /// Pointer travel that turns a press into a drag rather than a click. Small enough that
    /// deliberate dragging is immediate, large enough that the hand-wobble in an ordinary click
    /// doesn't nudge the launcher across the desktop.
    /// </summary>
    private const int DragThresholdPx = 4;

    /// <summary>
    /// Grace period between the pointer leaving a peeked launcher and it hiding again. Covers the
    /// pointer clipping a corner of the puck on its way somewhere else, and gives a slip of the
    /// hand on the way to clicking it a chance to come back.
    /// </summary>
    private const int HideDelayMs = 420;

    private const int SlideMs = 170;
    private const int SlideFrameMs = 16;

    /// <summary>
    /// How much of the launcher a display must be showing for that display to count as the one
    /// it lives on. Two pixels rather than a full notch: the check exists to reject a position
    /// left over from a display layout that no longer exists, not to second-guess a notch width
    /// that was rounded on a monitor whose scale factor we may no longer know.
    /// </summary>
    private const int MinVisiblePx = 2;

    private readonly RingManager _manager;
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private readonly DispatcherQueueTimer _slide;
    private readonly DispatcherQueueTimer _hideTimer;

    /// <summary>
    /// Work area of the monitor the launcher belongs to. Held rather than re-derived on demand:
    /// while docked, the window's own position no longer identifies its monitor (see
    /// <see cref="LauncherSnap.AnchorPoint"/>), so this is the authority and is refreshed only
    /// when the launcher is free to move between displays, or when the layout changes.
    /// </summary>
    private RectInt32 _workArea;

    private ScreenEdge _edge;
    private bool _peeked;
    private bool _pressed;
    private bool _dragging;
    private bool _suspended;
    private uint _dpi = 96;

    private PointInt32 _dragCursorStart;
    private PointInt32 _dragWindowStart;

    private PointInt32 _slideFrom;
    private PointInt32 _slideTo;
    private long _slideStart;

    private Brush? _restBorder;
    private Brush? _hoverBorder;
    private Brush? _accentBrush;

    public LauncherWindow(RingManager manager)
    {
        _manager = manager;
        InitializeComponent();

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));

        WindowChrome.MakeBorderlessToolWindow(_appWindow, _hwnd);
        WindowChrome.StripFrame(_hwnd);
        WindowChrome.MakeTransparentOverlay(this, _hwnd);

        _slide = DispatcherQueue.CreateTimer();
        _slide.Interval = TimeSpan.FromMilliseconds(SlideFrameMs);
        _slide.Tick += (_, _) => OnSlideTick();

        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(HideDelayMs);
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => OnHideTick();

        BuildPuck();
        ApplyTheme();
        RestorePlacement();

        // Activate, and only then make the window non-activating. A WinUI 3 window builds its
        // XAML content on first activation, so a launcher that is shown but never activated comes
        // up as an empty square — and WS_EX_NOACTIVATE applied any earlier is exactly what would
        // stop that activation happening. The cost is one momentary focus change, at the moment
        // the launcher appears; from then on the flag means every click on the puck leaves the
        // foreground window where it was, which is the case that actually matters.
        Activate();
        WindowChrome.MakeNonActivating(_hwnd);
        WindowChrome.RaiseToTopmost(_hwnd);
    }

    // ---- Appearance --------------------------------------------------------

    /// <summary>
    /// The puck: a rounded-square bubble with the app logo on the theme surface (solid black or
    /// white), matching the ring hover name pill's shape vocabulary. When docked, a Windows-accent
    /// strip on the screen-edge side reads as the notch.
    /// </summary>
    private void BuildPuck()
    {
        Puck.CornerRadius = new CornerRadius(CornerRadiusDip);

        var logo = AppLogo.Create(LogoDip);
        if (logo is FrameworkElement element)
        {
            element.HorizontalAlignment = HorizontalAlignment.Center;
            element.VerticalAlignment = VerticalAlignment.Center;
        }

        var content = new Grid();
        content.Children.Add(logo);
        Puck.Child = content;

        Notch.CornerRadius = new CornerRadius(1);
    }

    public void ApplyTheme()
    {
        var requested = ThemeHelper.Resolve(_manager.Config.Theme);
        Root.RequestedTheme = requested;

        bool dark = requested == ElementTheme.Default
            ? ThemeHelper.IsDark(Root)
            : requested == ElementTheme.Dark;

        _restBorder = new SolidColorBrush(ThemeHelper.ColorFor(dark, "SwiftXBorderBrush"));
        _hoverBorder = new SolidColorBrush(ThemeHelper.ColorFor(dark, "SwiftXForegroundBrush"));
        _accentBrush = new SolidColorBrush(ThemeHelper.WindowsAccentColor());

        Puck.Background = new SolidColorBrush(ThemeHelper.ColorFor(dark, "SwiftXSurfaceBrush"));
        Puck.BorderBrush = _restBorder;
        Notch.Background = _accentBrush;
        UpdateNotch();
    }

    // ---- Geometry ----------------------------------------------------------

    private double Scale => _dpi / 96.0;

    private int SizePx => (int)Math.Ceiling(SizeDip * Scale);

    private int CornerRadiusPx => Math.Max(1, (int)Math.Round(CornerRadiusDip * Scale));

    private int NotchAccentPx => Math.Max(1, (int)Math.Round(NotchAccentDip * Scale));

    private int SnapThresholdPx => (int)Math.Round(LauncherSnap.SnapThresholdDip * Scale);

    private int PeekMarginPx => (int)Math.Round(LauncherSnap.PeekMarginDip * Scale);

    private RectInt32 WindowRect
    {
        get
        {
            var position = _appWindow.Position;
            var size = _appWindow.Size;
            return new RectInt32(position.X, position.Y, size.Width, size.Height);
        }
    }

    /// <summary>
    /// Re-reads the monitor DPI and resizes the puck to match, returning whether anything changed.
    /// <para>
    /// A per-monitor-v2 process gets told about the scale factor but is not resized for it: cross
    /// from a 100% monitor to a 150% one and the window keeps its pixel size, so a 46-DIP puck
    /// becomes a 31-DIP one with its content clipped. Every path that can land the launcher on a
    /// different monitor calls this.
    /// </para>
    /// </summary>
    private bool SyncDpi()
    {
        uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
        if (dpi == 0)
            dpi = 96;
        if (dpi == _dpi && _appWindow.Size.Width == SizePx)
            return false;

        _dpi = dpi;
        _appWindow.Resize(new SizeInt32(SizePx, SizePx));
        return true;
    }

    /// <summary>
    /// Clips the HWND to the puck's rounded square, and — while docked — to the launcher's own
    /// monitor. Re-applied on every move, including every frame of a slide, which is what makes
    /// the puck appear to emerge from behind the screen edge rather than jump out from under it.
    /// </summary>
    private void ApplyRegion()
    {
        var rect = WindowRect;
        var body = new RectInt32(0, 0, rect.Width, rect.Height);

        RectInt32? clip = _edge == ScreenEdge.None
            ? null
            : LauncherSnap.ClipToWorkArea(rect, _workArea);

        WindowChrome.SetRoundRectRegion(_hwnd, body, CornerRadiusPx, clip);
    }

    /// <summary>
    /// Positions the accent notch on whichever side faces the screen edge when docked. The strip
    /// is the visible sliver users hunt for — tinted with the Windows accent rather than the glass
    /// fill of the puck itself.
    /// </summary>
    private void UpdateNotch()
    {
        if (_edge == ScreenEdge.None)
        {
            Notch.Visibility = Visibility.Collapsed;
            return;
        }

        Notch.Visibility = Visibility.Visible;
        double accent = NotchAccentDip;
        switch (_edge)
        {
            case ScreenEdge.Left:
                Notch.Width = accent;
                Notch.Height = SizeDip;
                Notch.HorizontalAlignment = HorizontalAlignment.Left;
                Notch.VerticalAlignment = VerticalAlignment.Stretch;
                Notch.Margin = new Thickness(0);
                break;
            case ScreenEdge.Right:
                Notch.Width = accent;
                Notch.Height = SizeDip;
                Notch.HorizontalAlignment = HorizontalAlignment.Right;
                Notch.VerticalAlignment = VerticalAlignment.Stretch;
                Notch.Margin = new Thickness(0);
                break;
            case ScreenEdge.Top:
                Notch.Width = SizeDip;
                Notch.Height = accent;
                Notch.HorizontalAlignment = HorizontalAlignment.Stretch;
                Notch.VerticalAlignment = VerticalAlignment.Top;
                Notch.Margin = new Thickness(0);
                break;
            case ScreenEdge.Bottom:
                Notch.Width = SizeDip;
                Notch.Height = accent;
                Notch.HorizontalAlignment = HorizontalAlignment.Stretch;
                Notch.VerticalAlignment = VerticalAlignment.Bottom;
                Notch.Margin = new Thickness(0);
                break;
        }
    }

    private int NotchPx => Math.Max(1, (int)Math.Round(LauncherSnap.NotchDip * Scale));

    /// <summary>Moves the window, keeping its size, DPI and clip region consistent with where it landed.</summary>
    private void PlaceAt(PointInt32 origin)
    {
        _appWindow.Move(origin);
        SyncDpi();
        ApplyRegion();
    }

    private DisplayArea? DisplayAt(PointInt32 point) =>
        DisplayArea.GetFromPoint(point, DisplayAreaFallback.Nearest);

    /// <summary>The work area of the monitor the launcher currently lives on. See <see cref="LauncherSnap.AnchorPoint"/>.</summary>
    private RectInt32 OwnerWorkArea()
    {
        var anchor = LauncherSnap.AnchorPoint(WindowRect, _edge);
        return DisplayAt(anchor)?.WorkArea ?? _workArea;
    }

    // ---- Dock states -------------------------------------------------------

    /// <summary>Slides the launcher behind <paramref name="edge"/>, leaving only its notch showing.</summary>
    private void Dock(ScreenEdge edge, bool animate)
    {
        _edge = edge;
        _peeked = false;
        UpdateNotch();
        SyncDpi();

        var target = LauncherSnap.HiddenOrigin(WindowRect, _workArea, edge, NotchPx);
        SlideTo(target, animate);
        Persist(target);
    }

    /// <summary>Slides a docked launcher fully back into view, flush against the edge it is docked to.</summary>
    private void Peek(bool animate)
    {
        if (_suspended || _edge == ScreenEdge.None || _peeked)
            return;

        _peeked = true;
        SlideTo(LauncherSnap.PeekOrigin(WindowRect, _workArea, _edge, PeekMarginPx), animate);
    }

    private void Undock()
    {
        _edge = ScreenEdge.None;
        _peeked = false;
        UpdateNotch();
        ApplyRegion();
    }

    // ---- Slide animation ---------------------------------------------------

    private void SlideTo(PointInt32 target, bool animate)
    {
        _slide.Stop();

        var from = _appWindow.Position;
        if (!animate || (from.X == target.X && from.Y == target.Y))
        {
            PlaceAt(target);
            return;
        }

        _slideFrom = from;
        _slideTo = target;
        _slideStart = Environment.TickCount64;
        _slide.Start();
    }

    private void OnSlideTick()
    {
        double t = Math.Clamp((Environment.TickCount64 - _slideStart) / (double)SlideMs, 0, 1);

        // Ease-out cubic: quick off the mark and settling gently, the same "arrives rather than
        // stops" feel as the ring's spring entrance, at the cost a plain window move can afford.
        double eased = 1 - Math.Pow(1 - t, 3);

        PlaceAt(new PointInt32(
            (int)Math.Round(_slideFrom.X + (_slideTo.X - _slideFrom.X) * eased),
            (int)Math.Round(_slideFrom.Y + (_slideTo.Y - _slideFrom.Y) * eased)));

        if (t >= 1)
            _slide.Stop();
    }

    // ---- Hover -------------------------------------------------------------

    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hideTimer.Stop();
        Puck.BorderBrush = _hoverBorder;
        Peek(animate: true);
    }

    private void Root_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        Puck.BorderBrush = _restBorder;
        if (_edge != ScreenEdge.None && !_pressed)
            ArmHide();
    }

    private void ArmHide()
    {
        if (_edge == ScreenEdge.None)
            return;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void OnHideTick()
    {
        // Undocked or already off screen for the ring: there is nothing to hide behind. Reachable
        // because hiding the window for the ring, and undocking mid-drag, can both land between
        // this timer being armed and it firing.
        if (_suspended || _edge == ScreenEdge.None)
            return;

        // Ask the OS where the pointer is rather than trusting the exit that armed this. The puck
        // is a circle inside a square window and it moves while it slides, so a pointer still
        // resting on it can easily have produced an exit on the way past a corner; hiding then
        // would pull the launcher out from under a pointer that is plainly still on it.
        if (_pressed || _dragging || IsPointerOverWindow())
        {
            ArmHide();
            return;
        }

        Dock(_edge, animate: true);
    }

    private bool IsPointerOverWindow()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
            return false;

        var rect = WindowRect;
        return cursor.X >= rect.X && cursor.X < rect.X + rect.Width
            && cursor.Y >= rect.Y && cursor.Y < rect.Y + rect.Height;
    }

    // ---- Drag / click ------------------------------------------------------

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(Root).Properties;
        if (properties.IsRightButtonPressed || properties.IsMiddleButtonPressed)
            return;
        if (!NativeMethods.GetCursorPos(out var cursor))
            return;

        _hideTimer.Stop();
        _slide.Stop();

        _pressed = true;
        _dragging = false;
        // Screen coordinates from the OS, not window-relative ones from the event: the window is
        // about to move under the pointer, which makes every window-relative reading a measurement
        // of the drag's own effect rather than of the pointer.
        _dragCursorStart = new PointInt32(cursor.X, cursor.Y);
        _dragWindowStart = _appWindow.Position;

        Root.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed || !NativeMethods.GetCursorPos(out var cursor))
            return;

        int dx = cursor.X - _dragCursorStart.X;
        int dy = cursor.Y - _dragCursorStart.Y;

        if (!_dragging)
        {
            if (Math.Abs(dx) < DragThresholdPx && Math.Abs(dy) < DragThresholdPx)
                return;

            // Past the threshold this is a drag, not a click. Undock before the first move: a
            // launcher being dragged has to follow the pointer freely, and it cannot while it is
            // still clipped to the monitor it was docked on.
            _dragging = true;
            Undock();
        }

        PlaceAt(new PointInt32(_dragWindowStart.X + dx, _dragWindowStart.Y + dy));

        // The monitor under the launcher is what a drop snaps against, so keep it current while
        // the launcher is still free to cross between them.
        _workArea = OwnerWorkArea();
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed)
            return;

        bool wasDrag = _dragging;
        _pressed = false;
        _dragging = false;
        Root.ReleasePointerCaptures();
        e.Handled = true;

        if (!wasDrag)
        {
            SummonRing();
            return;
        }

        _workArea = OwnerWorkArea();
        var edge = LauncherSnap.NearestEdge(WindowRect, _workArea, SnapThresholdPx);

        if (edge == ScreenEdge.None)
        {
            Undock();
            Persist(_appWindow.Position);
            return;
        }

        // Snap to the edge but stop at the peeked position, and let the ordinary hover-exit path
        // hide it. Sliding straight behind the edge here would yank the launcher out from under
        // the pointer that just placed it, which reads as the drop having gone wrong.
        _edge = edge;
        _peeked = true;
        UpdateNotch();
        var peek = LauncherSnap.PeekOrigin(WindowRect, _workArea, edge, PeekMarginPx);
        SlideTo(peek, animate: true);
        Persist(peek);
        ArmHide();
    }

    private void Root_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // Only reached when capture is taken away mid-gesture (a system drag, a display change);
        // an ordinary release has already cleared these and this is then a no-op.
        if (!_pressed)
            return;

        bool wasDrag = _dragging;
        _pressed = false;
        _dragging = false;

        // The launcher stays where the interrupted drag left it, so that is the position to keep.
        // Nothing snaps here: an interruption is not a drop, and guessing at an edge the user
        // never chose would move the launcher somewhere they did not put it.
        if (wasDrag)
            Persist(_appWindow.Position);

        ArmHide();
    }

    private void SummonRing()
    {
        // The anchor rather than the plain centre. For a free-floating launcher they are the same
        // point, but a docked one clicked before its peek has finished sliding still has its
        // centre out past the edge — and on a shared edge that centre is on the *next* monitor,
        // which is where the ring would then open. The anchor is on the launcher's own monitor by
        // construction. See LauncherSnap.AnchorPoint.
        _manager.ToggleRingAt(LauncherSnap.AnchorPoint(WindowRect, _edge));
    }

    // ---- Placement lifecycle -----------------------------------------------

    private void Persist(PointInt32 origin)
    {
        _manager.Config.LauncherX = origin.X;
        _manager.Config.LauncherY = origin.Y;
        _manager.Config.LauncherEdge = _edge;
        _manager.Save();
    }

    private void RestorePlacement()
    {
        SyncDpi();

        if (_manager.Config.LauncherX is int x && _manager.Config.LauncherY is int y)
        {
            _edge = _manager.Config.LauncherEdge;
            var stored = new RectInt32(x, y, _appWindow.Size.Width, _appWindow.Size.Height);
            var display = DisplayAt(LauncherSnap.AnchorPoint(stored, _edge));

            if (display is not null && LauncherSnap.IsReachable(stored, display.WorkArea, MinVisiblePx))
            {
                _workArea = display.WorkArea;
                PlaceAt(new PointInt32(x, y));
                // Re-dock rather than trusting the stored origin: the move may have landed on a
                // monitor whose scale factor resized the puck, and a docked position is only
                // right for one particular size.
                if (_edge != ScreenEdge.None)
                    Dock(_edge, animate: false);
                return;
            }
        }

        ResetToDefault();
    }

    /// <summary>
    /// Puts the launcher back at its out-of-the-box home — docked to the right edge of the monitor
    /// with the cursor. Also the recovery path when a stored position turns out to name a display
    /// that is no longer attached, and what the Settings button of the same name calls.
    /// </summary>
    public void ResetToDefault()
    {
        SyncDpi();

        NativeMethods.GetCursorPos(out var cursor);
        _workArea = (DisplayAt(new PointInt32(cursor.X, cursor.Y)) ?? DisplayArea.Primary).WorkArea;

        _edge = ScreenEdge.None;
        _peeked = false;
        var size = _appWindow.Size;
        PlaceAt(LauncherSnap.DefaultOrigin(_workArea, size.Width, size.Height));
        Dock(ScreenEdge.Right, animate: false);
    }

    /// <summary>
    /// Re-checks the launcher against the current display layout, after a monitor is attached,
    /// detached or re-arranged. A docked position is only meaningful relative to a particular work
    /// area, so one that no longer resolves to a display showing any of the launcher is discarded
    /// for the default rather than left pointing off the desktop.
    /// </summary>
    public void RevalidatePlacement()
    {
        var rect = WindowRect;
        var display = DisplayAt(LauncherSnap.AnchorPoint(rect, _edge));

        if (display is null || !LauncherSnap.IsReachable(rect, display.WorkArea, MinVisiblePx))
        {
            ResetToDefault();
            return;
        }

        _workArea = display.WorkArea;
        if (_edge != ScreenEdge.None)
            Dock(_edge, animate: false);
        else
            ApplyRegion();
    }

    // ---- Ring coordination -------------------------------------------------

    /// <summary>
    /// Takes the launcher off screen while the ring is open. The ring spreads a transparent window
    /// over the whole work area and paints its bubbles wherever it likes within it, so a launcher
    /// left showing would sit in the middle of the open ring — and, since it was very likely the
    /// thing that opened it, sit right under the ring's own hub.
    /// </summary>
    public void SuspendForRing()
    {
        if (_suspended)
            return;

        _suspended = true;
        _hideTimer.Stop();
        _slide.Stop();
        _appWindow.Hide();
    }

    /// <summary>Brings the launcher back once the ring has closed, hidden behind its edge again if it was docked.</summary>
    public void ResumeAfterRing()
    {
        if (!_suspended)
            return;

        _suspended = false;
        _appWindow.Show(false);
        WindowChrome.RaiseToTopmost(_hwnd);

        if (_edge != ScreenEdge.None)
            Dock(_edge, animate: false);
        else
            ApplyRegion();
    }

    /// <summary>Closes the launcher for good — the setting was turned off, or the app is quitting.</summary>
    public void Retire()
    {
        _hideTimer.Stop();
        _slide.Stop();
        Close();
    }
}
