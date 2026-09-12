using System.ComponentModel;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SwiftX.Interop;
using SwiftX.Models;
using SwiftX.Services;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Storyboard = Microsoft.UI.Xaml.Media.Animation.Storyboard;

namespace SwiftX;

public sealed partial class RingWindow : Window
{
    private const float SpringDamping = 0.65f;
    private const int SpringPeriodMs = 220;
    private const int StaggerMs = 28;

    // Flyout ("water drop") motion. Looser damping than the main ring's entrance so the children
    // visibly overshoot and settle, and a slower period so the travel out to the outer ring reads
    // as one liquid gesture rather than a snap. The same constants drive the reverse (collapse)
    // motion, so opening and closing always share one physical language.
    private const float FlyoutDamping = 0.58f;
    private const int FlyoutPeriodMs = 320;
    private const int FlyoutScaleMs = 380;
    private const int FlyoutStaggerMs = 26;

    /// <summary>
    /// Dwell before hovering a group opens it. Long enough that sweeping the pointer across a
    /// group on the way to its neighbour doesn't fire the flyout, short enough to feel automatic.
    /// </summary>
    private const int FlyoutHoverDelayMs = 220;

    /// <summary>Resting scale of the group bubble whose flyout is open, so it reads as active.</summary>
    private const float ExpandedParentScale = 1.08f;

    /// <summary>
    /// The one duration every hover-driven change runs for — the bubble's scale, its fill, its
    /// glyph, and the acrylic under it — so a bubble always takes exactly as long to light up as
    /// it does to grow, whichever of the three paths (pointer in, pointer out, a group's flyout
    /// opening or collapsing under it) is driving it. Those paths used to disagree: 130 ms from
    /// hover, 180 ms from the flyout, and an untimed snap for the fill.
    /// </summary>
    private const int HoverMs = 160;

    /// <summary>
    /// How far the acrylic disc is held inside the bubble's painted edge, per side. Sub-pixel at
    /// 100% scaling, and only ever hidden further by DPI rounding up — enough that the disc's own
    /// hard-clipped rim can never land outside the antialiased circle drawn over it, and small
    /// enough to take nothing visible off the glass. See CreateBubble.
    /// </summary>
    private const double BackdropInset = 0.5;

    /// <summary>How long after a dismiss a summon is still read as part of that same gesture.
    /// Comfortably longer than a click's own down-to-up, and shorter than any deliberate
    /// close-then-reopen.</summary>
    private const int ClosingGraceMs = 500;

    private readonly RingManager _manager;
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private readonly List<BubbleView> _bubbles = new();
    private readonly List<BubbleView> _flyout = new();

    // Cached rather than rebuilt per render so their resolved icon and PropertyChanged wiring
    // survive a re-render, exactly like the user's own items do.
    private readonly RingItem _settingsItem = SystemRingItems.CreateSettings();
    private readonly RingItem _closeItem = SystemRingItems.CreateClose();

    private BubbleView? _flyoutParent;
    private Border? _hub;
    private double _hubSize;

    /// <summary>The app mark inside <see cref="_hub"/> — both the full ring and a quadrant's
    /// corner hold one now. Kept so the summon, the dismiss, and a hub hover can each play its
    /// own petal motion alongside the ring's.</summary>
    private AppLogo.Mark? _hubMark;

    /// <summary>Which corner (or null for the full ring) <see cref="_hub"/> was last built for —
    /// tracked separately from <see cref="_hubSize"/> because two different corners can want the
    /// exact same box size but sit at a different <see cref="PositionHub"/> position.</summary>
    private HotCorner? _hubQuadrant;
    private Border? _pill;
    private TextBlock? _pillText;
    private bool _pillShown;
    private double _pillLeft;
    private double _pillTop;
    private double _cx;
    private double _cy;

    /// <summary>
    /// The corner this summon is anchored to when the ring came up as a quarter circle, or null
    /// when it is the whole ring. Set by <see cref="Summon"/> and read by everything that has to
    /// know where the items are: layout, hover, and the hit region.
    /// </summary>
    private HotCorner? _quadrant;

    // Radii of the current summon's bands. The quarter circle sits further out than the full
    // ring (it has to clear two screen edges), so these are resolved per summon from
    // RingMetrics' *For(radius) helpers rather than read straight off RingMetrics.
    private double _itemRadius = RingMetrics.Radius;
    private double _flyoutRadius = RingMetrics.FlyoutRadius;
    private double _flyoutBandStart = RingMetrics.FlyoutBandStart;
    private double _flyoutStepDegrees = RingMetrics.FlyoutStepDegrees;
    private double _contentRadius = RingMetrics.ContentRadius;
    private bool _isOpen;
    private bool _isAnimating;

    /// <summary>
    /// Polls the mouse buttons while the ring is up so a press anywhere off it closes it — see
    /// <see cref="OutsideClick_Tick"/>. Deliberately not a global mouse hook: this is the same
    /// trade the hot corner already makes (see <see cref="Services.HotCornerWatcher"/>), and a
    /// short-lived 40 ms timer that only runs while the ring is on screen costs nothing.
    /// </summary>
    private readonly DispatcherTimer _outsideClick = new()
    {
        Interval = TimeSpan.FromMilliseconds(40),
    };

    /// <summary>Whether a mouse button was already down at the previous <see cref="_outsideClick"/>
    /// tick, so only the press itself — not the whole time a button is held — is acted on.</summary>
    private bool _buttonWasDown;
    private int _renderGeneration;
    private BubbleView? _hovered;
    private int _hoverGeneration;

    /// <summary><c>Environment.TickCount64</c> of the most recent <see cref="Dismiss"/>, or zero
    /// when the ring has not been dismissed yet. Read by <see cref="IsClosing"/>.</summary>
    private long _dismissedAt;

    public bool IsOpen => _isOpen;

    /// <summary>
    /// Whether the ring is on its way out: the closing animation is still playing, or a dismiss
    /// was asked for a moment ago. The toggles that summon it (tray icon, hotkey) read this
    /// alongside <see cref="IsOpen"/> so that a gesture which has <em>already</em> closed the ring
    /// through another route doesn't then read as "it's closed, open it".
    /// <para>
    /// The tray icon is the case that needs it: it toggles on the mouse button coming <em>up</em>,
    /// while <see cref="OutsideClick_Tick"/> has already closed the ring on the way <em>down</em>,
    /// so without this a click on the tray would shut the ring and immediately re-summon it.
    /// </para>
    /// </summary>
    public bool IsClosing =>
        _isAnimating ||
        (_dismissedAt != 0 && Environment.TickCount64 - _dismissedAt < ClosingGraceMs);

    public RingWindow(RingManager manager)
    {
        _manager = manager;
        InitializeComponent();

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        WindowChrome.MakeBorderlessToolWindow(_appWindow, _hwnd);
        WindowChrome.StripFrame(_hwnd);
        WindowChrome.MakeTransparentOverlay(this, _hwnd);
        ApplyTheme();
        _appWindow.Hide();

        // A click outside the ring's own hit-test disc never reaches this window at all — it
        // passes straight through to whatever is beneath (see LayoutAtCursor) — so it can't be
        // caught as a pointer event here. What it *does* do is hand foreground activation to
        // whatever the user just clicked, and that deactivation is something this window is
        // told about regardless of where on screen the click landed.
        Activated += RingWindow_Activated;

        // The belt to that suspenders: deactivation alone can't be relied on (see
        // OutsideClick_Tick).
        _outsideClick.Tick += OutsideClick_Tick;
    }

    public void ApplyTheme()
    {
        Root.RequestedTheme = ThemeHelper.Resolve(_manager.Config.Theme);
        // Re-paint bubbles with theme-correct brushes when already open (Light/Dark switch).
        if (_isOpen)
            RenderCurrent();
    }

    public void ReloadItems()
    {
        if (!_isOpen) return;
        UpdateGeometry();
        RecenterForMetrics();
        RenderCurrent();
    }

    /// <summary>
    /// Re-clamps the ring's center after a radius / item-size change so a ring that just grew
    /// still fits on screen. The window itself spans the whole work area and never resizes, so —
    /// unlike the old ring-sized window — there is nothing here that can leave the outer bubbles
    /// hanging outside the HWND.
    /// </summary>
    private void RecenterForMetrics()
    {
        // A quarter circle is anchored on its corner and never recentered — clamping it inward
        // would pull the fan off the very point the pointer is sitting on.
        if (_quadrant is not null)
            return;
        double r = _contentRadius;
        _cx = ClampCenter(_cx, RingCanvas.Width, r);
        _cy = ClampCenter(_cy, RingCanvas.Height, r);
    }

    private static double ClampCenter(double value, double extent, double radius) =>
        extent >= radius * 2 ? Math.Clamp(value, radius, extent - radius) : extent / 2;

    /// <summary>
    /// Resolves the band radii for the summon that is about to happen. Everything downstream —
    /// item placement, the flyout ring, hover bands, the hit region — reads these rather than
    /// RingMetrics directly, which is what lets one set of drawing code serve both the full ring
    /// and the quarter circle.
    /// </summary>
    private void UpdateGeometry()
    {
        _itemRadius = _quadrant is null ? RingMetrics.Radius : RingMetrics.QuadrantRadius;
        _flyoutRadius = RingMetrics.FlyoutRadiusFor(_itemRadius);
        _flyoutBandStart = RingMetrics.FlyoutBandStartFor(_itemRadius);
        _flyoutStepDegrees = RingMetrics.FlyoutStepDegreesFor(_flyoutRadius);
        _contentRadius = RingMetrics.ContentRadiusFor(_itemRadius);
    }

    /// <summary>
    /// Brings the ring up under the pointer, or — when <paramref name="quadrant"/> names a corner
    /// — as a quarter circle anchored on that corner of the screen the pointer is on. The
    /// quadrant form is what a hot corner set to <see cref="HotCornerLayout.Quadrant"/> summons.
    /// </summary>
    public void Summon(HotCorner? quadrant = null)
    {
        // Cancel any in-flight dismiss hide.
        _renderGeneration++;
        _quadrant = quadrant;
        UpdateGeometry();
        WindowChrome.MakeTransparentOverlay(this, _hwnd);
        ApplyTheme();
        if (quadrant is { } corner)
            LayoutAtCorner(corner);
        else
            LayoutAtCursor();
        _appWindow.Show();
        Activate();
        // Not a plain SetForegroundWindow: a hot-corner summon fires from a WM_TIMER, which
        // Windows does not treat as a real input event, so the plain call can silently fail to
        // make the ring truly active — see WindowChrome.ForceForeground for why that specifically
        // breaks "click anywhere to dismiss".
        WindowChrome.ForceForeground(_hwnd);
        _isOpen = true;
        // Seeded from the buttons' current state so the release of the very click that opened the
        // ring (a tray click, say) doesn't read as a fresh press against it.
        _buttonWasDown = IsAnyMouseButtonDown();
        _outsideClick.Start();
        RenderCurrent(animate: true);
    }

    /// <summary>
    /// Spreads the window over the whole work area of the monitor under the cursor and places the
    /// ring's center at the cursor inside it.
    /// <para>
    /// The window used to be a ring-sized square moved to the cursor, which forced two
    /// compromises: near a screen edge the square got clamped into the work area and dragged the
    /// ring's center away from the cursor with it, and the window had to grow whenever the ring
    /// did. A work-area-sized window has neither problem — only the <em>center point</em> is
    /// clamped, by exactly enough for the ring to fit, and an expanding flyout needs no resize at
    /// all. Nothing is visible outside what XAML paints (see
    /// <see cref="WindowChrome.MakeTransparentOverlay"/>) and nothing outside the ring's own disc
    /// takes input (see <see cref="ApplyHitRegions"/>), so the extra extent costs nothing.
    /// </para>
    /// </summary>
    private void LayoutAtCursor()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var area = DisplayArea.GetFromPoint(
            new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest).WorkArea;
        _appWindow.MoveAndResize(area);

        // Read the DPI only after the move, so a summon onto a differently-scaled monitor sizes
        // the canvas for the monitor the window actually landed on.
        double scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        RingCanvas.Width = area.Width / scale;
        RingCanvas.Height = area.Height / scale;

        double r = _contentRadius;
        _cx = ClampCenter((cursor.X - area.X) / scale, RingCanvas.Width, r);
        _cy = ClampCenter((cursor.Y - area.Y) / scale, RingCanvas.Height, r);
    }

    /// <summary>
    /// Spreads the window over the whole of the monitor the pointer is on — outer bounds, not the
    /// work area — and puts the ring's center on <paramref name="corner"/> itself. The taskbar's
    /// strip has to be inside the window for a bottom corner's fan to be drawn around the point
    /// the pointer actually stopped at; nothing outside the painted items takes input anyway (see
    /// <see cref="ApplyHitRegions"/>), so covering it costs nothing.
    /// </summary>
    private void LayoutAtCorner(HotCorner corner)
    {
        NativeMethods.GetCursorPos(out var cursor);
        var area = DisplayArea.GetFromPoint(
            new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest).OuterBounds;
        _appWindow.MoveAndResize(area);

        double scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        RingCanvas.Width = area.Width / scale;
        RingCanvas.Height = area.Height / scale;

        (_cx, _cy) = QuadrantLayout.Center(corner, RingCanvas.Width, RingCanvas.Height);
    }

    public void Dismiss()
    {
        if (!_isOpen && !_isAnimating)
            return;

        _outsideClick.Stop();
        _dismissedAt = Environment.TickCount64;
        _isOpen = false;
        _ = DismissAnimatedAsync();
    }

    /// <summary>Clicking anywhere else on screen — another window, the desktop, the taskbar —
    /// hands that window foreground activation and deactivates this one; treat it the same as
    /// clicking the ring's own dead zone.
    /// <para>
    /// Suppressed only while Settings or Add-new is the window that just took activation (see
    /// <see cref="RingManager.IsExemptWindowForeground"/>) — not merely while either is open
    /// somewhere. Opening either from the ring's own flyout already dismisses the ring on selection
    /// (see <see cref="OnSelectAsync"/>), but both can also be reached from the tray icon while the
    /// ring is sitting open, or the ring can be re-summoned while one already has focus — either
    /// way, every click into that window would otherwise deactivate this one and immediately
    /// dismiss the ring out from under the user. Checking by foreground rather than "is it open at
    /// all" is what keeps that exemption to the one moment it's for: a Settings window left open in
    /// the background for the rest of the session must not permanently disable click-anywhere on
    /// every later summon.
    /// </para>
    /// </summary>
    private void RingWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
            return;
        if (_manager.IsExemptWindowForeground())
            return;
        Dismiss();
    }

    /// <summary>
    /// Closes the ring on a mouse press that lands anywhere outside it — another window, the
    /// desktop, the taskbar, or the empty screen past the fan — which is the whole of "click
    /// anywhere to dismiss" that <see cref="RingWindow_Activated"/> cannot cover on its own.
    /// <para>
    /// Deactivation is not enough by itself, and this is measured rather than assumed: a summon
    /// that doesn't win the foreground — the hot corner fires from a <c>WM_TIMER</c>, which
    /// Windows won't treat as real input, so <see cref="Services.WindowChrome.ForceForeground"/>
    /// can still lose the race — has no activation to lose later, and the ring then sits there
    /// through click after click. Presses land on this window only inside its own hit region (see
    /// <see cref="ApplyHitRegions"/>); everything outside passes straight through to whatever is
    /// beneath, so polling the buttons is what makes those presses visible here at all.
    /// </para>
    /// <para>
    /// A press <em>inside</em> the region is left alone: it belongs to
    /// <see cref="RingCanvas_PointerPressed"/> and a bubble's own Tapped handler, which pick an
    /// item when one is under the pointer and dismiss when none is.
    /// </para>
    /// </summary>
    private void OutsideClick_Tick(object? sender, object e)
    {
        if (!_isOpen)
        {
            _outsideClick.Stop();
            return;
        }

        bool down = IsAnyMouseButtonDown();
        bool pressed = down && !_buttonWasDown;
        _buttonWasDown = down;
        if (!pressed)
            return;

        // Same exemption the deactivation path makes, and by the same foreground check: a press
        // that actually landed on Settings or Add-new must not close the ring, but either window
        // merely existing elsewhere must not block every other click from closing it either.
        if (_manager.IsExemptWindowForeground())
            return;

        if (!NativeMethods.GetCursorPos(out var cursor) || IsOverRing(cursor))
            return;

        Dismiss();
    }

    private static bool IsAnyMouseButtonDown() =>
        IsButtonDown(NativeMethods.VK_LBUTTON) ||
        IsButtonDown(NativeMethods.VK_RBUTTON) ||
        IsButtonDown(NativeMethods.VK_MBUTTON);

    private static bool IsButtonDown(int virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    /// <summary>
    /// Whether a screen point falls on the ring's own disc — the same circle
    /// <see cref="ApplyHitRegions"/> hands the window, so this agrees by construction with which
    /// presses the window receives for itself. The hover pill's extra region is deliberately left
    /// out: it takes no input (it isn't hit-testable), so a press out on a label means the same
    /// thing as a press on bare desktop.
    /// </summary>
    private bool IsOverRing(NativeMethods.POINT screenPoint)
    {
        double scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        var origin = _appWindow.Position;
        double dx = (screenPoint.X - origin.X) / scale - _cx;
        double dy = (screenPoint.Y - origin.Y) / scale - _cy;
        return dx * dx + dy * dy <= _contentRadius * _contentRadius;
    }

    private async Task DismissAnimatedAsync()
    {
        int gen = ++_renderGeneration;
        _isAnimating = true;
        try
        {
            HidePill();
            FadeOutHub();
            // Inner ring and any open flyout children reverse their own entrances, at once — the
            // exact opposite of Summon's AnimateBurstIn / ExpandFlyout's AnimateDropletOut.
            await Task.WhenAll(AnimateBurstOutAsync(_bubbles), AnimateFlyoutBackAsync(_flyout));
            if (gen != _renderGeneration) return;
        }
        finally
        {
            _isAnimating = false;
        }

        ClearRing();
        WindowChrome.SetEllipticHitRegions(_hwnd, 1, Array.Empty<(double, double, double)>());
        _appWindow.Hide();
    }

    private void ClearRing()
    {
        DetachAll();
        RingCanvas.Children.Clear();
        _bubbles.Clear();
        _flyout.Clear();
        _flyoutParent = null;
        _hub = null;
        _hubMark = null;
        _pill = null;
        _pillText = null;
        _hovered = null;

        // Back to the full ring: the next summon says for itself whether it is a quadrant, and
        // anything that reads this between now and then (a metrics change, say) should see the
        // ring's own geometry rather than the last corner's.
        _quadrant = null;
        UpdateGeometry();
    }

    /// <summary>
    /// Drops the per-item PropertyChanged subscriptions a render installed. Items outlive the
    /// bubbles that show them (they are the user's config objects), so without this every summon
    /// would leave another handler on them pointing at discarded UI.
    /// </summary>
    private void DetachAll()
    {
        foreach (var bubble in _bubbles.Concat(_flyout))
            bubble.Detach?.Invoke();
    }

    /// <summary>
    /// Keeps the Settings bubble's "Add new" child (see <see cref="SystemRingItems.CreateAddNew"/>)
    /// in sync with whether there is anywhere left to add into. The full ring never needs this
    /// fallback — <see cref="CurrentItems"/> already puts an "Add new" placeholder directly on
    /// every empty user slot, which is reachable without opening Settings at all — so this only
    /// ever fires for the quadrant, whose four places (Settings among them) have no such per-slot
    /// placeholder of their own. Recomputed on every render since which shape is showing — ring or
    /// quadrant — is itself part of what "room to add" means.
    /// </summary>
    private void UpdateSettingsAddNew()
    {
        _settingsItem.Children.Clear();
        if (_quadrant is not { } corner)
            return;

        bool full = HotCornerItems.Resolve(_manager.Config, _settingsItem).Count >= QuadrantLayout.ItemCount;
        if (!full)
            _settingsItem.Children.Add(SystemRingItems.CreateAddNew());
    }

    private IReadOnlyList<(RingItem Item, int Slot)> CurrentItems()
    {
        var slots = new List<(RingItem, int)>();
        int slot = 0;
        foreach (var item in _manager.Config.Primary.Items.Where(i => !i.Hidden))
        {
            if (slot >= RingMetrics.UserSlotCount) break;
            slots.Add((item, slot++));
        }

        // Fewer than UserSlotCount items leaves the rest of the ring empty — fill each remaining
        // user slot with its own "Add new" placeholder rather than leaving a bare gap, so the ring
        // always reads as a full circle and every empty slot is itself an obvious way to fill it.
        while (slot < RingMetrics.UserSlotCount)
            slots.Add((SystemRingItems.CreateAddNew(), slot++));

        // Always last, always present, always in the same place — see SystemRingItems.
        slots.Add((_settingsItem, RingMetrics.SlotCount - 2));
        slots.Add((_closeItem, RingMetrics.SlotCount - 1));
        return slots;
    }

    /// <summary>
    /// What to draw and where, for whichever shape this summon took: the eight-slot ring, or the
    /// quarter circle's fan. Index is the bubble's position in its own layout — a ring slot, or a
    /// place along the arc — and the angle is already in canvas terms.
    /// </summary>
    private IReadOnlyList<(RingItem Item, int Index, double AngleRad)> CurrentLayout()
    {
        if (_quadrant is not { } corner)
        {
            return CurrentItems()
                .Select(entry => (
                    Item: entry.Item,
                    Index: entry.Slot,
                    AngleRad: RingMetrics.SlotAngleRad(entry.Slot)))
                .ToList();
        }

        var items = HotCornerItems.Resolve(_manager.Config, _settingsItem);
        var layout = new List<(RingItem Item, int Index, double AngleRad)>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            double local = QuadrantLayout.LocalAngleRad(
                i, items.Count, RingMetrics.Bubble * RingMetrics.HoverScale, _itemRadius);
            layout.Add((items[i], i, QuadrantLayout.ScreenAngleRad(corner, local)));
        }
        return layout;
    }

    private void RenderCurrent(bool animate = false)
    {
        _renderGeneration++;
        DetachAll();
        RingCanvas.Children.Clear();
        _bubbles.Clear();
        _flyout.Clear();
        _flyoutParent = null;
        _hovered = null;
        UpdateSettingsAddNew();

        // The hub is the ring's center mark. A quarter circle's center is the screen corner, so
        // three quarters of a full-sized one would be off the screen — AttachHub draws a smaller
        // mark there instead, and the corner itself is what clips it down to the sliver that
        // actually shows.
        AttachHub();

        foreach (var (item, slot, angle) in CurrentLayout())
        {
            var bubble = CreateBubble(
                item, slot, angle,
                radius: _itemRadius,
                diameter: RingMetrics.Bubble,
                iconSize: RingMetrics.Icon,
                glyphSize: RingMetrics.Glyph,
                origin: Vector2.Zero,
                isFlyout: false);
            _bubbles.Add(bubble);
            RingCanvas.Children.Add(bubble.Host);
            _ = ResolveIconAsync(item);
        }

        BuildPill();

        // Composition Offset/Scale must start after the element is in the visual tree.
        foreach (var bubble in _bubbles)
        {
            if (animate)
                AnimateBurstIn(bubble);
            else
                ResetBubbleVisual(bubble, rested: true);
        }

        ApplyHitRegions();
    }

    /// <summary>
    /// One circle covering everything the ring can paint — hub, bubbles, a hover-grown bubble, an
    /// expanded flyout ring, and the hover pill beyond it (<see cref="RingMetrics.ContentRadius"/>)
    /// — so the whole disk receives pointer input the way a real action ring does, and the rest of
    /// the work-area-sized window stays click-through.
    /// <para>
    /// The radius has to bound the <em>painted</em> content, not just the resting bubbles:
    /// <see cref="WindowChrome.SetEllipticHitRegions"/> genuinely clips DWM's composited pixels,
    /// so a region drawn tight to the ring is what used to shave the outer edge off every bubble
    /// the moment hover scaled it up.
    /// </para>
    /// </summary>
    private void ApplyHitRegions(double pillCx = 0, double pillCy = 0, double pillRadius = 0)
    {
        double scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        var regions = new List<(double, double, double)>
        {
            (_cx, _cy, _contentRadius),
        };

        // The region clips painting, so a pill that reaches past the ring disc needs its own
        // circle unioned in or its far end is simply cut off. Adding it per-pill, instead of
        // permanently padding ContentRadius by the longest name a user might have, keeps the
        // ring's own footprint tight enough to still open under the cursor near a screen edge.
        if (pillRadius > 0)
            regions.Add((pillCx, pillCy, pillRadius));

        WindowChrome.SetEllipticHitRegions(_hwnd, scale, regions);
    }

    /// <summary>
    /// Gives a bubble an accessible name and hides the decoration inside it from Narrator.
    /// <para>
    /// The ring is drawn as bare <see cref="Border"/> elements on a <see cref="Canvas"/> with
    /// pointer handlers, which is why it needs this: a Border is not a control, so nothing here
    /// carried a name, and a screen reader saw an unnamed canvas where the whole launcher is.
    /// Naming each bubble after the item it launches is what lets Narrator's scan and touch modes
    /// report what is on the ring. It does not make the ring keyboard-operable — that needs real
    /// invokable peers and arrow navigation, which is a feature rather than a fix, and is recorded
    /// as such in the review — but a surface that cannot be read at all is the worse of the two
    /// problems and this is the one that is safe to fix without touching input handling.
    /// </para>
    /// <para>
    /// The children are pushed to the raw view deliberately. The glass, the tint and the group arc
    /// are decoration, and the icon and glyph restate the name — a <c>FontIcon</c> in particular
    /// carries a private-use codepoint, which is not something to read out.
    /// </para>
    /// </summary>
    private static void NameForNarrator(Border host, RingItem item)
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            host, string.IsNullOrWhiteSpace(item.DisplayName) ? item.Kind.ToString() : item.DisplayName);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(
            host, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content);

        if (host.Child is Panel panel)
        {
            foreach (var child in panel.Children)
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(
                    child, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        }
    }

    /// <summary>
    /// Resolves an item's icon, unless the picture it already has was resolved from exactly the
    /// same inputs.
    /// <para>
    /// The skip is what makes a summon cheap. <see cref="RenderCurrent"/> fires one of these per
    /// item, and it runs on every summon, every theme change and every settings save — while
    /// resolving is not cheap at all: an app or file goes through the shell's jumbo image list,
    /// <c>GetDIBits</c>, a PNG encode and a XAML decode, and a web link re-reads and re-decodes
    /// its cached favicon. That is ten of those on every appearance of a window whose entire
    /// value is appearing instantly, to arrive back at the picture already on screen.
    /// <see cref="RingItem"/> instances are the user's own config objects and outlive any render,
    /// so the previous answer is still there to reuse.
    /// </para>
    /// <para>
    /// A failed resolution is not cached — <see cref="RingItem.IconImage"/> stays null and the
    /// next render tries again, which is what lets a favicon that missed on the first summon
    /// (no connectivity yet) appear on a later one.
    /// </para>
    /// <para>
    /// The key doubles as the staleness guard the previous <c>Target</c> comparison provided, and
    /// covers more than it did: editing <see cref="RingItem.CustomIconPath"/> or
    /// <see cref="RingItem.CustomGlyph"/> also invalidates, so a slow resolution that lands after
    /// an edit is discarded rather than painted over the newer one. Assigning a null result on a
    /// changed key is deliberate — it clears a bitmap the item is no longer entitled to, which is
    /// what lets a glyph chosen for an item that previously showed a shell icon actually appear.
    /// </para>
    /// </summary>
    private static async Task ResolveIconAsync(RingItem ringItem)
    {
        string key = ringItem.IconKey;
        if (ringItem.IconImage is not null && ringItem.IconSignature == key)
            return;

        var src = await IconService.LoadIconAsync(ringItem);

        if (ringItem.IconKey != key)
            return; // edited mid-flight; the resolution for the newer key owns the result

        ringItem.IconSignature = key;
        ringItem.IconImage = src;
    }

    /// <summary>
    /// Adds the hub to the canvas for the current render — reusing the existing instance (and
    /// leaving it visually untouched) whenever one is already alive and still the right size, so
    /// a re-render doesn't replay its entrance animation. <see cref="RenderCurrent"/> clears every
    /// canvas child on every call, so even a reused hub still needs re-adding here.
    /// </summary>
    private void AttachHub()
    {
        double hub = HubSize;
        if (_hub is null || _hubSize != hub || _hubQuadrant != _quadrant)
        {
            BuildHub(hub);
            return;
        }

        PositionHub(hub);
        RingCanvas.Children.Add(_hub);

        // A dismiss that a new summon interrupted leaves FadeOutHub's shrink still applied to
        // this very visual — ClearRing, which would have discarded it, never ran. Put it back
        // before reusing it, or the ring comes up with an invisible centre.
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(_hub);
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Scale");
            visual.Opacity = 1;
            visual.Scale = Vector3.One;
            _hubMark?.Reset();
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// Full-ring hub size — a fixed small mark that no item-size setting scales — or, on a
    /// quarter circle, the size of the corner's own mark. Scales with
    /// <see cref="RingMetrics.Bubble"/> so it grows and shrinks along with the corner dock's own
    /// items rather than sitting at one fixed size regardless of theirs.
    /// </summary>
    private double HubSize => _quadrant is null ? RingMetrics.Hub : RingMetrics.Bubble;

    /// <summary>
    /// The hub is the app's own mark and nothing else: no plate, no border, no fill — the logo
    /// sits directly on the desktop, lifted off it by a drop shadow of its own
    /// (<see cref="AppLogo.CreateFloating"/>) rather than by anything drawn under it, and always
    /// at the same fixed <see cref="RingMetrics.HubLogo"/> size no item-size setting scales — a
    /// quadrant's corner shows the very same mark, at the very same size, that the full ring's
    /// center does, rather than the small quarter-disc wedge this used to draw there or a mark
    /// stretched to fill its own (larger, item-scaled) box. Positioned by <see cref="PositionHub"/>,
    /// which is what actually clears the two screen edges a quadrant's corner is made of, since the
    /// box the mark sits in is still <see cref="RingMetrics.Bubble"/>-sized so it doesn't crowd the
    /// items fanned around it.
    /// <para>
    /// Item names pop out beside the item they belong to (<see cref="ShowPill"/>), which is what
    /// freed the center up for the mark alone.
    /// </para>
    /// </summary>
    private void BuildHub(double hub)
    {
        _hubSize = hub;
        _hubQuadrant = _quadrant;
        _hubMark = AppLogo.CreateFloating(RingMetrics.HubLogo);
        _hub = new Border
        {
            Width = hub,
            Height = hub,
            // Transparent rather than null: a null Background makes a Border invisible to hit
            // testing entirely, which is what the hover-replay below needs (the mark itself stays
            // IsHitTestVisible = false — see its own doc comment — so this outer box is what
            // actually receives the pointer).
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Child = _hubMark,
            IsHitTestVisible = true,
        };
        // Hovering the hub after it has already settled plays PlayHubEntrance again — the exact
        // same motion as the real invoke, never a lesser or different-looking substitute (see that
        // method's own doc comment for why both spring layers have to replay together).
        _hub.PointerEntered += (_, _) => PlayHubEntrance();
        PositionHub(hub);
        RingCanvas.Children.Add(_hub);
        PlayHubEntrance();
    }

    /// <summary>
    /// Plays the hub's whole invoke motion: the outer box's own scale/opacity spring together with
    /// the mark's own petal choreography (<see cref="AppLogo.Mark.PlayEntrance"/>), as the one
    /// animation they always are together. Called once when the hub is first built for a summon,
    /// and replayed in full — both layers, not just the mark's — every time the pointer hovers it
    /// afterward (<see cref="BuildHub"/>'s <c>PointerEntered</c> handler), so a hover is never a
    /// partial or different-looking version of the real invoke: it always is the real invoke,
    /// exactly, snap back to the starting frame and all.
    /// </summary>
    private void PlayHubEntrance()
    {
        if (_hub is null) return;

        // Same spring family as the bubble burst-in (AnimateBurstIn), not a flat linear fade, so
        // the hub reads as part of one physical motion instead of popping in on its own timeline.
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(_hub);
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Scale");
            visual.CenterPoint = new Vector3((float)(_hubSize / 2), (float)(_hubSize / 2), 0);
            visual.Opacity = 0;
            visual.Scale = new Vector3(0.7f, 0.7f, 1);
            var compositor = visual.Compositor;

            var scale = compositor.CreateSpringVector3Animation();
            scale.Target = "Scale";
            scale.FinalValue = Vector3.One;
            scale.DampingRatio = SpringDamping;
            scale.Period = TimeSpan.FromMilliseconds(SpringPeriodMs);
            visual.StartAnimation("Scale", scale);

            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(1, 1);
            opacity.Duration = TimeSpan.FromMilliseconds(SpringPeriodMs);
            visual.StartAnimation("Opacity", opacity);
        }
        catch
        {
            // ignore
        }

        // The mark's own motion — a quarter turn in, petals unfurling clockwise behind it — rides
        // on top of the spring above rather than replacing it, so the centre reads as one thing
        // arriving. Outside the try above on purpose: it guards its own composition calls, and a
        // failure there must not cost the hub its entrance.
        _hubMark?.PlayEntrance();
    }

    /// <summary>
    /// Where the hub box's top-left corner belongs. The full ring centers it on <c>(_cx, _cy)</c>
    /// — the point the ring was summoned at. A quadrant's <c>(_cx, _cy)</c> is instead the screen
    /// corner itself (see <see cref="QuadrantLayout.Center"/>), so centering a hub-sized box there
    /// the same way would leave three quarters of it — and the whole logo inside it — hanging off
    /// the edge of the screen. Instead the box is pushed diagonally into the screen, clearing each
    /// of the corner's two edges by <see cref="QuadrantLayout.EdgeMargin"/> — the same clearance
    /// the fanned-out items themselves keep off these same two edges — so the entire mark lands on
    /// screen rather than being bisected by either one.
    /// </summary>
    private void PositionHub(double hub)
    {
        if (_hub is null)
            return;

        if (_quadrant is not { } corner)
        {
            Canvas.SetLeft(_hub, _cx - hub / 2);
            Canvas.SetTop(_hub, _cy - hub / 2);
            return;
        }

        double margin = QuadrantLayout.EdgeMargin;
        bool nearLeft = corner is HotCorner.TopLeft or HotCorner.BottomLeft;
        bool nearTop = corner is HotCorner.TopLeft or HotCorner.TopRight;
        Canvas.SetLeft(_hub, nearLeft ? _cx + margin : _cx - margin - hub);
        Canvas.SetTop(_hub, nearTop ? _cy + margin : _cy - margin - hub);
    }

    private BubbleView CreateBubble(
        RingItem item, int index, double angle, double radius, double diameter,
        double iconSize, double glyphSize, Vector2 origin, bool isFlyout)
    {
        float restX = (float)(radius * Math.Cos(angle));
        float restY = (float)(radius * Math.Sin(angle));

        var icon = new Image
        {
            Width = iconSize,
            Height = iconSize,
            Stretch = Stretch.Uniform,
            Source = item.IconImage,
            Visibility = item.ImageVisibility,
        };

        // Shared with the group arc below, and animated alongside the tint by ApplyBubbleFill —
        // see tintBrush there for why the fill is one brush per bubble rather than a new one each
        // time the state changes.
        var glyphBrush = new SolidColorBrush(ThemeColor("SwiftXForegroundBrush"));
        var glyph = new FontIcon
        {
            Glyph = item.Glyph,
            FontSize = glyphSize,
            Foreground = glyphBrush,
            Visibility = item.GlyphVisibility,
        };
        if (GlyphFonts.IsTextGlyph(item.Glyph))
            glyph.FontFamily = new FontFamily("Segoe UI");

        PropertyChangedEventHandler onItemChanged = (_, e) =>
        {
            if (e.PropertyName is nameof(RingItem.IconImage) or nameof(RingItem.ImageVisibility) or nameof(RingItem.GlyphVisibility))
            {
                icon.Source = item.IconImage;
                icon.Visibility = item.ImageVisibility;
                glyph.Visibility = item.GlyphVisibility;
            }
        };
        item.PropertyChanged += onItemChanged;

        // Live frosted glass, bottom-most so it samples the real desktop behind the bubble.
        // Lives inside the same Border every entrance/hover/flyout animation already targets as
        // one Composition visual (bubble.Host below), so it travels with those animations for
        // free — nothing in the motion code needs to know it's there.
        //
        // Drawn a hair smaller than the tint above it (BackdropInset). SystemBackdropElement can
        // only round itself with a composition rounded-rect clip, whose edge is a hard, unfeathered
        // stair-step; the tint's own rounded Background is drawn by XAML and is properly
        // antialiased. At exactly equal sizes the aliased disc therefore peeks out from behind the
        // feathered one by a fraction of a pixel all the way round — invisible while the tint is
        // translucent, but the literal light rim that showed around an inverted bubble under Light
        // once the tint went opaque. Insetting puts the backdrop's own edge strictly inside the
        // tint's, so the outermost thing painted is always the antialiased circle.
        double backdropSize = Math.Max(1, diameter - BackdropInset * 2);
        var backdrop = new SystemBackdropElement
        {
            Width = backdropSize,
            Height = backdropSize,
            CornerRadius = new CornerRadius(backdropSize / 2),
            SystemBackdrop = new DesktopAcrylicBackdrop(),
        };

        // The theme tint over the blur — what ApplyBubbleFill actually swaps for hover/close
        // states, not border.Background below (left unset, or it would paint over the blur and
        // hide it). Semi-transparent at rest so the glass shows through; hover/close still swap
        // it to a fully opaque color for contrast, same as the old solid-fill behavior.
        //
        // One long-lived brush per bubble whose Color is animated, rather than a fresh brush
        // assigned on every state change: the fill then travels on the same curve and over the
        // same duration as the hover scale instead of snapping the instant the pointer arrives.
        var tintBrush = new SolidColorBrush(ThemeColor("RingBubbleGlassTint"));
        var tint = new Border
        {
            Width = diameter,
            Height = diameter,
            CornerRadius = new CornerRadius(diameter / 2),
            Background = tintBrush,
        };

        var content = new Grid();
        content.Children.Add(backdrop);
        content.Children.Add(tint);
        content.Children.Add(icon);
        content.Children.Add(glyph);

        // A group wears a short arc traced on its own outward edge — before you hover it, that
        // arc is the only thing distinguishing an item that opens a ring of its own from one that
        // launches. It rides on the circle's own boundary, centered on the direction the item
        // already sits from the hub, so it never needs its own transform and scales with the item
        // because it lives in the same content.
        //
        // Only drawn when the group actually has something to expand into — the same
        // Where(!Hidden) filter ExpandFlyout itself uses, since that is what decides whether
        // hovering this bubble opens a flyout at all. Settings is the item this matters for: it is
        // always Kind.Group, but its one child — "Add new" — only exists on the quadrant, where it
        // drops out once that's full too (see UpdateSettingsAddNew); on the full ring it never has
        // one at all, since every empty user slot already carries its own "Add new" placeholder.
        // Either way, a Settings bubble wearing an arc that promises a flyout it can no longer open
        // would be a lie the moment you hovered it.
        if (item.IsGroup && !isFlyout && item.Children.Any(c => !c.Hidden))
            content.Children.Add(BuildGroupArc(diameter, angle, glyphBrush));

        // No CornerRadius on the host: it paints nothing of its own, and a rounded Border clips
        // its child with the same unfeathered composition rounded rect the backdrop uses — which
        // would cut the tint's antialiased edge back into a stair-step and undo the smoothing
        // above. The circle is drawn by the tint and clipped by nothing.
        var border = new Border
        {
            Width = diameter,
            Height = diameter,
            Child = content,
            Tag = item,
        };
        Canvas.SetLeft(border, _cx + restX - diameter / 2);
        Canvas.SetTop(border, _cy + restY - diameter / 2);

        NameForNarrator(border, item);

        var bubble = new BubbleView
        {
            Host = border,
            Item = item,
            Index = index,
            AngleRad = angle,
            RestX = restX,
            RestY = restY,
            OriginX = origin.X,
            OriginY = origin.Y,
            Diameter = diameter,
            IsFlyout = isFlyout,
            StaggerIndex = index,
            Backdrop = backdrop,
            TintBrush = tintBrush,
            GlyphBrush = glyphBrush,
            Detach = () => item.PropertyChanged -= onItemChanged,
        };

        // Precise hover: the pointer is directly over this bubble's own shape. This and the
        // angle-driven hover in RingCanvas_PointerMoved both funnel through SetHovered so the
        // visual treatment (OnHover) never runs twice or gets out of sync between the two paths.
        // Enter only. A matching PointerExited here used to clear the hover the moment the pointer
        // left this bubble's own circle — but the ring hovers by angle from anywhere past the dead
        // zone (RingCanvas_PointerMoved), so stepping into the gap beside a bubble fired an
        // unhover that the very next move immediately undid. That round trip is what made hover
        // look inconsistent: the bubble flinched, the name pill blinked out and back, and, because
        // every hover change bumps _hoverGeneration, a group's flyout kept being re-armed from
        // scratch instead of opening. Leaving is now owned by whatever the pointer moves on to —
        // another bubble, an angle with nothing in it, the dead zone, or the canvas edge
        // (RingCanvas_PointerExited).
        border.PointerEntered += (_, _) => SetHovered(bubble);
        border.Tapped += async (_, e) =>
        {
            e.Handled = true;
            await OnSelectAsync(bubble);
        };

        return bubble;
    }

    /// <summary>
    /// A 30° stroke traced along the bubble's own circular edge, centered on <paramref
    /// name="angleRad"/> — the same outward direction the bubble already rests on, so the arc
    /// reads as part of that edge instead of a separate ornament. Drawn as an unfilled
    /// <see cref="ArcSegment"/> rather than the old filled dot so it hugs the boundary at any
    /// bubble size instead of floating as a disconnected speck once items get small.
    /// </summary>
    private static Path BuildGroupArc(double diameter, double angleRad, Brush stroke)
    {
        const double SweepDegrees = 30;
        double sweep = SweepDegrees * Math.PI / 180.0;
        double thickness = Math.Max(2, diameter * 0.08);
        double radius = diameter / 2 - thickness / 2;
        double center = diameter / 2;
        double startAngle = angleRad - sweep / 2;
        double endAngle = angleRad + sweep / 2;

        var figure = new PathFigure
        {
            StartPoint = new Point(
                center + radius * Math.Cos(startAngle),
                center + radius * Math.Sin(startAngle)),
            IsClosed = false,
        };
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(
                center + radius * Math.Cos(endAngle),
                center + radius * Math.Sin(endAngle)),
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = false,
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        return new Path
        {
            Width = diameter,
            Height = diameter,
            Data = geometry,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
    }

    /// <summary>
    /// Shared with Settings/chrome via ThemeHelper — never Application.Current.Resources alone,
    /// or Light mode under a Dark OS paints dark surfaces with dark glyphs.
    /// Keys: RingBubbleBackground, RingHubBackground, SwiftX* brushes in Themes/Colors.xaml.
    /// <para>
    /// An explicit Light/Dark choice is read from the config rather than from
    /// <c>Root.ActualTheme</c>. Summon assigns <c>RequestedTheme</c> and paints the whole ring in
    /// the same pass, and ActualTheme does not always reflect the assignment that early — when it
    /// hadn't, the ring came up inverted (white bubbles under a Dark config) until the next
    /// re-render. Only "System" still has to ask the element, which is the one case where the
    /// element is the authority.
    /// </para>
    /// </summary>
    private Brush ThemeBrush(string key) => new SolidColorBrush(ThemeColor(key));

    /// <summary>
    /// <see cref="ThemeBrush"/>'s color, for the callers that hold their own long-lived brush and
    /// animate its <c>Color</c> rather than swapping the brush itself — see
    /// <see cref="AnimateBrushTo"/>.
    /// </summary>
    private Color ThemeColor(string key)
    {
        var requested = ThemeHelper.Resolve(_manager.Config.Theme);
        bool dark = requested == ElementTheme.Default
            ? ThemeHelper.IsDark(Root)
            : requested == ElementTheme.Dark;
        bool isBlack = _manager.Config.Theme == AppTheme.Black;
        return ThemeHelper.ColorFor(dark, isBlack, key);
    }

    // ---- Hover name pill -----------------------------------------------------------------

    /// <summary>
    /// The hover label. One reused element rather than one per bubble, kept at the top of the
    /// canvas z-order so it always sits over the ring — including over an expanded flyout, whose
    /// bubbles are added to the canvas after it.
    /// </summary>
    private void BuildPill()
    {
        _pillText = new TextBlock
        {
            FontSize = RingMetrics.PillFontSize,
            TextWrapping = TextWrapping.WrapWholeWords,
            TextAlignment = TextAlignment.Center,
            MaxWidth = RingMetrics.PillMaxWidth,
            Foreground = ThemeBrush("SwiftXForegroundBrush"),
        };
        _pill = new Border
        {
            Background = ThemeBrush("RingHubBackground"),
            BorderBrush = ThemeBrush("SwiftXBorderBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 4, 9, 4),
            Child = _pillText,
            IsHitTestVisible = false,
        };
        Canvas.SetZIndex(_pill, 100);
        RingCanvas.Children.Add(_pill);
        _pillShown = false;

        // Every motion this pill makes rides on Translation, never on the element visual's own
        // Offset — see GlidePill. Enabling it here, once, is what makes that property exist.
        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(_pill, true);
        }
        catch
        {
            // ignore
        }

        // Hidden via the composition visual, not Border.Opacity: XAML owns that property and
        // re-asserts it on the same visual the fade animates, which left the pill permanently
        // invisible. Nothing else touches Opacity on this element, so the animation is unopposed.
        try
        {
            ElementCompositionPreview.GetElementVisual(_pill).Opacity = 0;
        }
        catch
        {
            _pill.Opacity = 0;
        }
    }

    private void ShowPill(BubbleView bubble)
    {
        if (_pill is null || _pillText is null) return;
        if (string.IsNullOrWhiteSpace(bubble.Item.DisplayName))
        {
            HidePill();
            return;
        }

        _pillText.Text = bubble.Item.DisplayName;
        // Measure explicitly: the position depends on the pill's own size, and the layout pass
        // that would fill DesiredSize in hasn't run yet for the text we just assigned.
        _pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _pill.DesiredSize;
        _pill.CornerRadius = new CornerRadius(size.Height / 2);

        // Always sit wholly outside the item, along the same radial it sits on — never anywhere
        // else — so the label reads as belonging to that item and never covers it, and a hovered
        // item's name is drawn in exactly one place for the life of the hover instead of jumping
        // to a second spot (e.g. a "heading" above the ring) the moment its flyout opens. The
        // offset uses the pill's own extent measured along that radial — the projection of its box
        // onto the radial axis, which is its width when the item is due east and its height when
        // the item is due north — rather than one constant that would tuck a wide pill under a
        // sideways item.
        double ux = Math.Cos(bubble.AngleRad);
        double uy = Math.Sin(bubble.AngleRad);
        double reach = (Math.Abs(ux) * size.Width + Math.Abs(uy) * size.Height) / 2;

        double restRadius = Math.Sqrt(bubble.RestX * bubble.RestX + bubble.RestY * bubble.RestY);
        double clearance = restRadius + bubble.Diameter / 2 * HoverScaleFor(bubble);

        // An item whose label reaches into an open flyout's arc has to clear those children as
        // well as its own circle — including the group itself, whose children fan out around its
        // own angle.
        if (!bubble.IsFlyout && _flyout.Count > 0 && PointsIntoFlyoutArc(bubble.AngleRad))
        {
            clearance = Math.Max(
                clearance,
                _flyoutRadius + RingMetrics.FlyoutBubble / 2 * RingMetrics.HoverScale);
        }

        double distance = clearance + RingMetrics.PillGap + reach;
        double px = distance * ux;
        double py = distance * uy;

        // Only the window edge may move it, and only sideways — the clearance above is never
        // traded away, because a label sitting on top of its own item is worse than one that runs
        // close to the screen edge.
        double left = Math.Clamp(
            _cx + px - size.Width / 2, 0, Math.Max(0, RingCanvas.Width - size.Width));
        double top = Math.Clamp(
            _cy + py - size.Height / 2, 0, Math.Max(0, RingCanvas.Height - size.Height));

        bool moving = _pillShown;
        double previousLeft = _pillLeft;
        double previousTop = _pillTop;

        Canvas.SetLeft(_pill, left);
        Canvas.SetTop(_pill, top);
        _pillLeft = left;
        _pillTop = top;
        _pillShown = true;

        double halfDiagonal = Math.Sqrt(size.Width * size.Width + size.Height * size.Height) / 2;
        ApplyHitRegions(left + size.Width / 2, top + size.Height / 2, halfDiagonal + 2);

        if (moving)
            GlidePill(previousLeft - left, previousTop - top);
        else
            FadePill(1, new Vector3(1, 1, 1), size);
    }

    /// <summary>
    /// Whether <paramref name="angleRad"/> aims into the arc the open flyout occupies. The flyout
    /// is an arc rather than a full circle, so an item on the far side of the ring keeps its
    /// label close in — only the ones actually pointing at the children get pushed past them.
    /// </summary>
    private bool PointsIntoFlyoutArc(double angleRad)
    {
        double tolerance = (_flyoutStepDegrees * 0.5 + 12) * Math.PI / 180.0;
        return _flyout.Any(b => Math.Abs(NormalizeAngle(angleRad - b.AngleRad)) <= tolerance);
    }

    /// <summary>
    /// Slides an already-visible pill from where it was to where <see cref="ShowPill"/> has just
    /// put it, instead of letting it blink out and back in: the animation runs from the old spot's
    /// delta back to zero, so it is purely a decoration on top of the Canvas position.
    /// <para>
    /// It moves <c>Translation</c>, never the element visual's <c>Offset</c>. Offset is how XAML
    /// itself carries this element's <c>Canvas.Left</c>/<c>Top</c>, so writing it hands the pill's
    /// position to composition and leaves it stranded wherever the animation ended — zero, i.e.
    /// the canvas origin, which on a corner dock is the screen corner the ring is centered on.
    /// That is the second "label" that used to appear in the corner on hover. Translation is
    /// applied on top of the layout position instead, so the pill can only ever be beside its own
    /// item (see <see cref="BuildPill"/>, which enables the property).
    /// </para>
    /// </summary>
    private void GlidePill(double fromDx, double fromDy)
    {
        if (_pill is null) return;
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(_pill);
            visual.Properties.InsertVector3(
                "Translation", new Vector3((float)fromDx, (float)fromDy, 0));
            var compositor = visual.Compositor;
            var glide = compositor.CreateVector3KeyFrameAnimation();
            glide.InsertKeyFrame(1, Vector3.Zero, compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.2f, 0.9f), new Vector2(0.2f, 1f)));
            glide.Duration = TimeSpan.FromMilliseconds(200);
            visual.StartAnimation("Translation", glide);
        }
        catch
        {
            // ignore
        }
    }

    private void HidePill()
    {
        if (_pill is null) return;
        _pillShown = false;
        FadePill(0, new Vector3(0.92f, 0.92f, 1), _pill.DesiredSize);
    }

    private void FadePill(float opacity, Vector3 scale, Size size)
    {
        if (_pill is null) return;
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(_pill);
            visual.CenterPoint = new Vector3((float)(size.Width / 2), (float)(size.Height / 2), 0);
            // Land on the Canvas position exactly, cancelling any glide still in flight — and,
            // like GlidePill, through Translation so XAML keeps ownership of where the pill is.
            visual.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            var compositor = visual.Compositor;

            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1, opacity);
            fade.Duration = TimeSpan.FromMilliseconds(120);
            visual.StartAnimation("Opacity", fade);

            var grow = compositor.CreateVector3KeyFrameAnimation();
            grow.InsertKeyFrame(1, scale);
            grow.Duration = TimeSpan.FromMilliseconds(120);
            visual.StartAnimation("Scale", grow);
        }
        catch
        {
            _pill.Opacity = opacity;
        }
    }

    // ---- Motion --------------------------------------------------------------------------

    private static void ResetBubbleVisual(BubbleView bubble, bool rested)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(bubble.Host);
            visual.CenterPoint = new Vector3(
                (float)(bubble.Diameter / 2),
                (float)(bubble.Diameter / 2),
                0);
            if (rested)
            {
                visual.Offset = Vector3.Zero;
                visual.Scale = new Vector3(bubble.RestScale, bubble.RestScale, 1);
                visual.Opacity = 1;
            }
            else
            {
                // Layout puts the bubble at its resting place, so "emerging from the hub" (or,
                // for a flyout child, from its parent bubble) is an offset back to that origin.
                visual.Offset = bubble.EntryOffset;
                visual.Scale = new Vector3(0.15f, 0.15f, 1);
                visual.Opacity = 0;
            }
        }
        catch
        {
            // Composition may be unavailable in some hosts.
        }
    }

    private void AnimateBurstIn(BubbleView bubble)
    {
        try
        {
            ResetBubbleVisual(bubble, rested: false);
            var visual = ElementCompositionPreview.GetElementVisual(bubble.Host);
            var compositor = visual.Compositor;
            var delay = TimeSpan.FromMilliseconds(bubble.StaggerIndex * StaggerMs);

            var offset = compositor.CreateSpringVector3Animation();
            offset.Target = "Offset";
            offset.FinalValue = Vector3.Zero;
            offset.DampingRatio = SpringDamping;
            offset.Period = TimeSpan.FromMilliseconds(SpringPeriodMs);
            offset.DelayTime = delay;
            visual.StartAnimation("Offset", offset);

            var scale = compositor.CreateSpringVector3Animation();
            scale.Target = "Scale";
            scale.FinalValue = Vector3.One;
            scale.DampingRatio = SpringDamping;
            scale.Period = TimeSpan.FromMilliseconds(SpringPeriodMs);
            scale.DelayTime = delay;
            visual.StartAnimation("Scale", scale);

            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(1, 1);
            opacity.Duration = TimeSpan.FromMilliseconds(SpringPeriodMs);
            opacity.DelayTime = delay;
            visual.StartAnimation("Opacity", opacity);
        }
        catch
        {
            // skip animation
        }
    }

    /// <summary>
    /// Closing companion to <see cref="AnimateBurstIn"/>, but deliberately not its mirror image:
    /// the entrance travels from the hub out to each bubble's resting spot, so reversing that
    /// motion pulls every bubble back along its own radial — which, for a ring whose populated
    /// items lean to one side, reads as the whole dock sliding sideways rather than fading.
    /// Closing instead leaves each bubble exactly where it rests and only fades/shrinks it in
    /// place, so the dock always dissolves symmetrically no matter how the items are arranged.
    /// Stagger is still reversed — whichever bubble was the last to arrive is the first to leave.
    /// </summary>
    private void AnimateBurstOut(BubbleView bubble, int staggerIndex)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(bubble.Host);
            visual.CenterPoint = new Vector3(
                (float)(bubble.Diameter / 2), (float)(bubble.Diameter / 2), 0);
            var compositor = visual.Compositor;
            var delay = TimeSpan.FromMilliseconds(staggerIndex * StaggerMs);
            var ease = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.3f, 0f), new Vector2(0.2f, 1f));

            var scale = compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(1, new Vector3(0.85f, 0.85f, 1), ease);
            scale.Duration = TimeSpan.FromMilliseconds(SpringPeriodMs);
            scale.DelayTime = delay;
            visual.StartAnimation("Scale", scale);

            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(1, 0, ease);
            opacity.Duration = TimeSpan.FromMilliseconds(SpringPeriodMs);
            opacity.DelayTime = delay;
            visual.StartAnimation("Opacity", opacity);
        }
        catch
        {
            // skip animation
        }
    }

    /// <summary>Runs <see cref="AnimateBurstOut"/> across the whole inner ring and waits for it to settle.</summary>
    private async Task AnimateBurstOutAsync(IReadOnlyList<BubbleView> bubbles)
    {
        if (bubbles.Count == 0)
            return;

        int maxStagger = bubbles.Max(b => b.StaggerIndex);
        foreach (var bubble in bubbles)
            AnimateBurstOut(bubble, maxStagger - bubble.StaggerIndex);

        await Task.Delay(SpringPeriodMs + maxStagger * StaggerMs + 30);
    }

    /// <summary>
    /// The exact reverse of <see cref="BuildHub"/>'s entrance spring — and, with it, of the mark's
    /// own petal motion.
    /// </summary>
    private void FadeOutHub()
    {
        if (_hub is null) return;
        _hubMark?.PlayExit();
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(_hub);
            visual.CenterPoint = new Vector3((float)(_hubSize / 2), (float)(_hubSize / 2), 0);
            var compositor = visual.Compositor;

            var scale = compositor.CreateSpringVector3Animation();
            scale.Target = "Scale";
            scale.FinalValue = new Vector3(0.7f, 0.7f, 1);
            scale.DampingRatio = SpringDamping;
            scale.Period = TimeSpan.FromMilliseconds(SpringPeriodMs);
            visual.StartAnimation("Scale", scale);

            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(1, 0);
            opacity.Duration = TimeSpan.FromMilliseconds(SpringPeriodMs);
            visual.StartAnimation("Opacity", opacity);
        }
        catch
        {
            // ignore
        }
    }

    // ---- Flyout (outer ring) ---------------------------------------------------------------

    /// <summary>
    /// Canvas angles for an expanding group's children: a fan of
    /// <see cref="_flyoutStepDegrees"/> centered on the group itself.
    /// <para>
    /// On a quarter circle that fan also has to stay inside the quarter — a group sitting near
    /// one of the two screen edges would otherwise throw half its children off the screen — so
    /// there the whole fan is slid back inside the span its own radius leaves, keeping its
    /// spacing. On the full ring there is no such constraint and the fan is simply centered.
    /// </para>
    /// </summary>
    private IReadOnlyList<double> FlyoutAngles(BubbleView parent, int count)
    {
        double step = _flyoutStepDegrees * Math.PI / 180.0;
        double spread = step * (count - 1);

        if (_quadrant is not { } corner)
        {
            double start = parent.AngleRad - spread / 2;
            return Enumerable.Range(0, count).Select(i => start + i * step).ToList();
        }

        double inset = QuadrantLayout.EdgeInsetRad(
            RingMetrics.FlyoutBubble * RingMetrics.HoverScale, _flyoutRadius);
        double lowest = inset;
        double highest = Math.Max(inset, Math.PI / 2 - inset - spread);
        double localStart = Math.Clamp(
            QuadrantLayout.LocalAngleFromScreenRad(corner, parent.AngleRad) - spread / 2,
            lowest, highest);

        return Enumerable.Range(0, count)
            .Select(i => QuadrantLayout.ScreenAngleRad(corner, localStart + i * step))
            .ToList();
    }

    /// <summary>
    /// Pops a group's children out into a concentric ring <em>around</em> the ring that is already
    /// on screen, rather than replacing it. The children are born at the parent bubble's own
    /// center and travel outward on a loose spring while their scale runs a stretch-then-settle
    /// curve, so the group reads as shedding droplets that swell into place — the inner ring never
    /// moves, so the user keeps their bearings and can pick a sibling group without going "back".
    /// </summary>
    private void ExpandFlyout(BubbleView parent)
    {
        var children = parent.Item.Children
            .Where(c => !c.Hidden)
            .Take(RingMetrics.FlyoutSlotCount)
            .ToList();
        if (children.Count == 0)
            return;

        _flyoutParent = parent;
        parent.RestScale = ExpandedParentScale;
        ApplyBubbleFill(parent, active: true);
        AnimateScale(parent, ExpandedParentScale, HoverMs);

        var angles = FlyoutAngles(parent, children.Count);
        var origin = new Vector2(parent.RestX, parent.RestY);

        for (int i = 0; i < children.Count; i++)
        {
            var bubble = CreateBubble(
                children[i], i, angles[i],
                radius: _flyoutRadius,
                diameter: RingMetrics.FlyoutBubble,
                iconSize: RingMetrics.FlyoutIcon,
                glyphSize: RingMetrics.FlyoutGlyph,
                origin: origin,
                isFlyout: true);
            _flyout.Add(bubble);
            RingCanvas.Children.Add(bubble.Host);
            _ = ResolveIconAsync(children[i]);
        }

        // Fan out from the middle of the arc rather than from one end, so the group looks like it
        // splits open symmetrically instead of unrolling.
        double middle = (children.Count - 1) / 2.0;
        foreach (var bubble in _flyout)
        {
            bubble.StaggerIndex = (int)Math.Round(Math.Abs(bubble.Index - middle));
            AnimateDropletOut(bubble, bubble.StaggerIndex);
        }

        // Hover placed the pill before these children existed — and, since hover is what opens a
        // group, that is the normal order of events, not an edge case. Re-place it now that there
        // is an outer ring for it to clear.
        if (_hovered is not null)
            ShowPill(_hovered);
    }

    private void AnimateDropletOut(BubbleView bubble, int staggerIndex)
    {
        try
        {
            ResetBubbleVisual(bubble, rested: false);
            var visual = ElementCompositionPreview.GetElementVisual(bubble.Host);
            var compositor = visual.Compositor;
            var delay = TimeSpan.FromMilliseconds(staggerIndex * FlyoutStaggerMs);

            var offset = compositor.CreateSpringVector3Animation();
            offset.Target = "Offset";
            offset.FinalValue = Vector3.Zero;
            offset.DampingRatio = FlyoutDamping;
            offset.Period = TimeSpan.FromMilliseconds(FlyoutPeriodMs);
            offset.DelayTime = delay;
            visual.StartAnimation("Offset", offset);

            // Swell past full size on the way out and dip back under it on arrival — the surface
            // tension half of the droplet, and the part a plain spring can't express.
            var scale = compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(0f, new Vector3(0.18f, 0.18f, 1));
            scale.InsertKeyFrame(0.55f, new Vector3(1.22f, 1.22f, 1));
            scale.InsertKeyFrame(0.78f, new Vector3(0.93f, 0.93f, 1));
            scale.InsertKeyFrame(1f, Vector3.One);
            scale.Duration = TimeSpan.FromMilliseconds(FlyoutScaleMs);
            scale.DelayTime = delay;
            visual.StartAnimation("Scale", scale);

            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(1, 1);
            opacity.Duration = TimeSpan.FromMilliseconds(130);
            opacity.DelayTime = delay;
            visual.StartAnimation("Opacity", opacity);
        }
        catch
        {
            ResetBubbleVisual(bubble, rested: true);
        }
    }

    /// <summary>
    /// Closing companion to <see cref="AnimateDropletOut"/>, but — like <see cref="AnimateBurstOut"/>
    /// versus its own entrance — deliberately not its mirror image. The entrance travels from the
    /// parent bubble out to each child's resting spot; reversing that motion would pull every
    /// child back toward the parent along its own radial, which for an arc of children reads as
    /// the whole flyout sliding sideways rather than fading. Hiding instead leaves each child
    /// exactly where it rests and only fades/shrinks it in place.
    /// </summary>
    private void AnimateDropletBack(BubbleView bubble, int staggerIndex)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(bubble.Host);
            visual.CenterPoint = new Vector3(
                (float)(bubble.Diameter / 2), (float)(bubble.Diameter / 2), 0);
            var compositor = visual.Compositor;
            var delay = TimeSpan.FromMilliseconds(staggerIndex * FlyoutStaggerMs);
            var ease = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.3f, 0f), new Vector2(0.2f, 1f));

            var scale = compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(1, new Vector3(0.85f, 0.85f, 1), ease);
            scale.Duration = TimeSpan.FromMilliseconds(FlyoutScaleMs);
            scale.DelayTime = delay;
            visual.StartAnimation("Scale", scale);

            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(1, 0, ease);
            opacity.Duration = TimeSpan.FromMilliseconds(FlyoutScaleMs);
            opacity.DelayTime = delay;
            visual.StartAnimation("Opacity", opacity);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Runs <see cref="AnimateDropletBack"/> across a flyout's children and waits for it to settle.</summary>
    private async Task AnimateFlyoutBackAsync(IReadOnlyList<BubbleView> children)
    {
        if (children.Count == 0)
            return;

        int maxStagger = children.Max(c => c.StaggerIndex);
        foreach (var bubble in children)
            AnimateDropletBack(bubble, maxStagger - bubble.StaggerIndex);

        await Task.Delay(FlyoutScaleMs + maxStagger * FlyoutStaggerMs + 30);
    }

    private async Task CollapseFlyoutAsync()
    {
        if (_flyoutParent is null && _flyout.Count == 0)
            return;

        var parent = _flyoutParent;
        var children = _flyout.ToList();
        _flyoutParent = null;
        _flyout.Clear();

        if (parent is not null)
        {
            parent.RestScale = 1f;
            bool hovered = ReferenceEquals(_hovered, parent);
            ApplyBubbleFill(parent, active: hovered);
            AnimateScale(parent, hovered ? HoverScaleFor(parent) : 1f, HoverMs);
        }

        if (_hovered is not null && _hovered.IsFlyout)
            SetHovered(null);

        int gen = _renderGeneration;
        // Children shed back into the parent item they emerged from, reversing AnimateDropletOut.
        await AnimateFlyoutBackAsync(children);

        if (gen != _renderGeneration)
            return;

        foreach (var child in children)
        {
            child.Detach?.Invoke();
            RingCanvas.Children.Remove(child.Host);
        }

        // Nothing to clear out there any more, so the label can come back in beside its item.
        if (_hovered is not null)
            ShowPill(_hovered);
    }

    // ---- Hover / selection -------------------------------------------------------------------

    /// <summary>
    /// Single entry point for "which bubble is hovered right now" — fed both by a bubble's own
    /// PointerEntered/Exited (precise hover) and by the angle-nearest calculation in
    /// RingCanvas_PointerMoved (hover from anywhere in the ring, including the gaps between
    /// bubbles). Diffs against <see cref="_hovered"/> so the enter/exit treatment only runs on the
    /// bubbles that actually changed state.
    /// </summary>
    private void SetHovered(BubbleView? bubble)
    {
        if (ReferenceEquals(_hovered, bubble))
            return;

        if (_hovered is not null)
            OnHover(_hovered, false);

        _hovered = bubble;

        if (bubble is not null)
        {
            OnHover(bubble, true);
            ShowPill(bubble);
        }
        else
        {
            HidePill();
        }

        ScheduleFlyoutForHover(bubble);
    }

    /// <summary>
    /// Dwelling on a group opens its flyout without a click, and dwelling on a plain item closes
    /// whichever flyout is open — so the outer ring follows the pointer instead of having to be
    /// clicked open and Esc'd shut.
    /// <para>
    /// Both halves are on a delay and both are cancelled by the next hover change (via
    /// <see cref="_hoverGeneration"/>), so sweeping the pointer across a group on the way
    /// somewhere else doesn't fire anything. Moving onto the flyout's own children is explicitly
    /// inert: those sit past the group that spawned them, and collapsing the ring out from under
    /// the pointer on the way to them would make them unreachable.
    /// </para>
    /// </summary>
    private async void ScheduleFlyoutForHover(BubbleView? bubble)
    {
        int hoverGen = ++_hoverGeneration;
        int renderGen = _renderGeneration;

        if (bubble is null || bubble.IsFlyout)
            return;

        bool wantsOpen = bubble.Item.IsGroup && !ReferenceEquals(_flyoutParent, bubble);
        bool wantsClose = !bubble.Item.IsGroup && _flyoutParent is not null;
        if (!wantsOpen && !wantsClose)
            return;

        await Task.Delay(FlyoutHoverDelayMs);
        if (hoverGen != _hoverGeneration || renderGen != _renderGeneration || !_isOpen)
            return;

        await CollapseFlyoutAsync();
        if (hoverGen != _hoverGeneration || renderGen != _renderGeneration || !_isOpen)
            return;

        if (wantsOpen)
            ExpandFlyout(bubble);
    }

    private void OnHover(BubbleView bubble, bool entered)
    {
        AnimateScale(bubble, entered ? HoverScaleFor(bubble) : bubble.RestScale, HoverMs);

        // An expanded group stays lit while its flyout is open, so exiting it must not reset the
        // fill the way exiting an ordinary bubble does.
        ApplyBubbleFill(bubble, entered || ReferenceEquals(bubble, _flyoutParent));
    }

    /// <summary>
    /// Active state under <see cref="RingHoverStyle.Invert"/> reads as an inverted fill (no border
    /// in either theme) rather than an outline — the same two colors CreateBubble already applies
    /// at rest, just reversed. Under <see cref="RingHoverStyle.Zoom"/> the fill never changes at
    /// all; <see cref="HoverScaleFor"/> shrinking the bubble is the only hover cue. Close is the
    /// exception to both: it goes red under the pointer regardless of the setting, because it is
    /// the one item on the ring whose effect can't be undone by clicking again.
    /// <para>
    /// Both colors are animated over <see cref="HoverMs"/>, on the same curve as the scale the
    /// same hover started, rather than assigned outright — a fill that snapped while the bubble
    /// was still growing was half of what made hover read as two unrelated effects.
    /// </para>
    /// <para>
    /// An opaque fill also takes the acrylic disc under it out with it. Nothing of the glass shows
    /// through an opaque circle anyway, and leaving it lit is what put a pale rim around an
    /// inverted bubble under Light — the disc is clipped to a hard-edged rounded rect while the
    /// fill over it is antialiased, so at the boundary the disc always won by a sliver. See
    /// <see cref="BackdropInset"/>, which closes the same gap geometrically for the rest states.
    /// </para>
    /// </summary>
    private void ApplyBubbleFill(BubbleView bubble, bool active)
    {
        bool destructive = active && IsCloseItem(bubble.Item);
        bool invert = active && !destructive && _manager.Config.HoverStyle == RingHoverStyle.Invert;
        bool opaque = destructive || invert;

        Color background = destructive ? ThemeColor("RingCloseHoverBackground")
            : invert ? ThemeColor("SwiftXForegroundBrush")
            : ThemeColor("RingBubbleGlassTint");

        Color foreground = destructive ? ThemeColor("RingCloseHoverForeground")
            : invert ? ThemeColor("RingBubbleBackground")
            : ThemeColor("SwiftXForegroundBrush");

        bubble.TintFade = AnimateBrushTo(bubble.TintFade, bubble.TintBrush, background, HoverMs);
        bubble.GlyphFade = AnimateBrushTo(bubble.GlyphFade, bubble.GlyphBrush, foreground, HoverMs);
        AnimateOpacity(bubble.Backdrop, opaque ? 0f : 1f, HoverMs);
    }

    private static bool IsCloseItem(RingItem item) =>
        string.Equals(item.Target, SystemRingItems.CloseTarget, StringComparison.OrdinalIgnoreCase);

    private float HoverScaleFor(BubbleView bubble)
    {
        if (IsCloseItem(bubble.Item))
            return (float)RingMetrics.CloseHoverScale;
        return (float)(_manager.Config.HoverStyle == RingHoverStyle.Zoom
            ? RingMetrics.ZoomHoverScale
            : RingMetrics.HoverScale);
    }

    private static void AnimateScale(BubbleView bubble, float target, int durationMs)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(bubble.Host);
            visual.CenterPoint = new Vector3(
                (float)(bubble.Diameter / 2),
                (float)(bubble.Diameter / 2),
                0);
            var compositor = visual.Compositor;
            var scale = compositor.CreateVector3KeyFrameAnimation();
            // Explicitly eased rather than left on the compositor's default: every other motion in
            // this file states its own curve, and an unstated one is how the hover grow ended up
            // reading differently from the collapse it is supposed to mirror.
            scale.InsertKeyFrame(1, new Vector3(target, target, 1), HoverEase(compositor));
            scale.Duration = TimeSpan.FromMilliseconds(durationMs);
            visual.StartAnimation("Scale", scale);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// The single curve every hover transition rides — a decelerate, the same one the hover pill's
    /// own glide uses (<see cref="GlidePill"/>), so the bubble, its label, and its fill all settle
    /// the same way.
    /// </summary>
    private static CompositionEasingFunction HoverEase(Compositor compositor) =>
        compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.9f), new Vector2(0.2f, 1f));

    /// <summary>
    /// Runs one bubble layer's opacity to <paramref name="target"/> on the hover curve. Falls back
    /// to setting it outright: which layers are lit matters, the transition between them does not.
    /// </summary>
    private static void AnimateOpacity(UIElement element, float target, int durationMs)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1, target, HoverEase(compositor));
            fade.Duration = TimeSpan.FromMilliseconds(durationMs);
            visual.StartAnimation("Opacity", fade);
        }
        catch
        {
            element.Opacity = target;
        }
    }

    /// <summary>
    /// Glides a fill from whatever color it is showing to <paramref name="color"/>, on the same
    /// curve and over the same duration as the hover scale running beside it, and returns the
    /// storyboard driving it so the next call can hand off from it (pass it back as
    /// <paramref name="running"/>).
    /// <para>
    /// A brush color is a dependent animation — it is not a Composition property — so this is a
    /// Storyboard against the bubble's own long-lived brush rather than a Composition animation,
    /// which is exactly why <see cref="CreateBubble"/> keeps one brush per bubble instead of
    /// assigning a fresh one per state. A spline keyframe carries the same control points
    /// <see cref="HoverEase"/> hands Composition, so the two curves are the one curve.
    /// </para>
    /// <para>
    /// Two details keep a filling storyboard from being left behind on the brush, which
    /// <c>HoldEnd</c> would otherwise do on every single hover for the life of the process: the
    /// previous one is stopped here, and each one stops itself on Completed after writing its own
    /// end color as the brush's plain value. Stopping drops a storyboard's hold and the property
    /// falls back to that plain value, so the color the pointer is mid-way through is captured
    /// first and written back as the new starting point — the handoff is continuous rather than a
    /// jump back to where the last animation began.
    /// </para>
    /// </summary>
    private static Storyboard? AnimateBrushTo(
        Storyboard? running, SolidColorBrush brush, Color color, int durationMs)
    {
        try
        {
            // Read before stopping: a DependencyProperty reports the running animation's current
            // value, which is what is actually on screen right now.
            var from = brush.Color;
            running?.Stop();
            brush.Color = from;
            if (from.Equals(color))
                return null;

            var animation = new Microsoft.UI.Xaml.Media.Animation.ColorAnimationUsingKeyFrames
            {
                EnableDependentAnimation = true,
            };
            animation.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.LinearColorKeyFrame
            {
                Value = from,
                KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero),
            });
            animation.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.SplineColorKeyFrame
            {
                Value = color,
                KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(
                    TimeSpan.FromMilliseconds(durationMs)),
                KeySpline = new Microsoft.UI.Xaml.Media.Animation.KeySpline
                {
                    ControlPoint1 = new Point(0.2, 0.9),
                    ControlPoint2 = new Point(0.2, 1),
                },
            });
            Storyboard.SetTarget(animation, brush);
            Storyboard.SetTargetProperty(animation, "Color");

            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            storyboard.Completed += (_, _) =>
            {
                storyboard.Stop();
                brush.Color = color;
            };
            storyboard.Begin();
            return storyboard;
        }
        catch
        {
            brush.Color = color;
            return null;
        }
    }

    private async Task OnSelectAsync(BubbleView bubble)
    {
        var item = bubble.Item;

        if (IsCloseItem(item))
        {
            Dismiss();
            return;
        }

        // Settings is a group now too (it owns the Add new quick action below), but unlike a
        // user-created group it still has a job of its own to do on click — open Settings
        // — so it deliberately skips the generic group-click branch, which only ever toggles a
        // flyout and never activates anything. Hovering it still opens that flyout regardless (see
        // ScheduleFlyoutForHover, which checks Item.IsGroup independently of this method).
        if (item.IsGroup && !IsSettingsItem(item))
        {
            // Nested groups inside a flyout would need a third ring to land in; there isn't one,
            // so they simply don't expand.
            if (bubble.IsFlyout)
                return;

            // Hover has usually opened this one already, so a click must not toggle it shut —
            // that would make a group flicker open and closed under a normal point-and-click.
            // Esc, the hub, and hovering away are the ways back out.
            if (!ReferenceEquals(_flyoutParent, bubble))
            {
                await CollapseFlyoutAsync();
                ExpandFlyout(bubble);
            }
            return;
        }

        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(bubble.Host);
            visual.CenterPoint = new Vector3(
                (float)(bubble.Diameter / 2),
                (float)(bubble.Diameter / 2),
                0);
            var pulse = visual.Compositor.CreateVector3KeyFrameAnimation();
            pulse.InsertKeyFrame(0.5f, new Vector3(1.25f, 1.25f, 1));
            pulse.InsertKeyFrame(1, Vector3.One);
            pulse.Duration = TimeSpan.FromMilliseconds(150);
            visual.StartAnimation("Scale", pulse);
        }
        catch
        {
            // ignore
        }

        await Task.Delay(150);
        _manager.HandleItemActivation(item);

        // Every other selection closes the ring on activation — Settings and Add-new are the
        // exceptions: each opens its own window and should stay on screen for as long as that
        // window is open (see RingWindow_Activated's matching IsExemptWindowForeground check,
        // which keeps the deactivation that follows here from dismissing it too). Selecting either
        // a second time while its window is already open just re-activates that window (see
        // RingManager.OpenSettings/OpenAddItem) — still no dismiss.
        bool staysOpen = IsSettingsItem(item) ||
            string.Equals(item.Target, SystemRingItems.AddNewTarget, StringComparison.OrdinalIgnoreCase);
        if (!staysOpen)
            Dismiss();
    }

    private static bool IsSettingsItem(RingItem item) =>
        string.Equals(item.Target, SystemRingItems.SettingsTarget, StringComparison.OrdinalIgnoreCase);

    // ---- Input ---------------------------------------------------------------------------

    private async void RingCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(RingCanvas).Position;
        double dx = pos.X - _cx;
        double dy = pos.Y - _cy;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < RingMetrics.DeadZone)
        {
            // Nothing lives in the dead zone to select, so a press here always means "close this",
            // whether or not a flyout happens to be open — see the matching gap case below.
            Dismiss();
            return;
        }

        // A press landing directly on a bubble is handled by that bubble's own Tapped handler
        // (e.OriginalSource is then the Border or one of its children, not the Canvas). This
        // covers presses in the gaps between bubbles: with the hit-region covering the whole
        // disk, a press out here should activate whichever item angular-hover currently targets,
        // the same as clicking the bubble itself would.
        if (e.OriginalSource is Canvas)
        {
            if (_hovered is { } bubble)
            {
                e.Handled = true;
                await OnSelectAsync(bubble);
            }
            else
            {
                // Nothing is hovered/selected — a gap with no bubble in it, or empty space past an
                // open flyout's arc — so the click means "dismiss the dock", not "step back one
                // level". Dismiss() already reverses an open flyout alongside the inner ring.
                Dismiss();
            }
        }
    }

    private void RingCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(RingCanvas).Position;
        double dx = pos.X - _cx;
        double dy = pos.Y - _cy;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < RingMetrics.DeadZone || (_bubbles.Count == 0 && _flyout.Count == 0))
        {
            // Back in the dead zone (or nothing to hover): don't leave a stale bubble highlighted.
            SetHovered(null);
            return;
        }

        // Past the flyout band the outer ring owns the pointer; inside it, the inner ring does.
        if (_flyout.Count > 0 && dist >= _flyoutBandStart)
        {
            SetHovered(NearestOnArc(_flyout, dx, dy, _flyoutStepDegrees * 0.6 * Math.PI / 180.0));
            return;
        }

        // A quarter circle is an arc too, so it is hovered the same way its flyout is rather than
        // through the eight-slot lookup below, which only knows about the full ring's geometry.
        if (_quadrant is not null)
        {
            SetHovered(NearestOnArc(_bubbles, dx, dy, QuadrantHoverToleranceRad()));
            return;
        }

        // Angularly-nearest slot from ANY point in the ring beyond the dead zone — not just a
        // precise hover over a bubble's own small circle — so the gaps between bubbles feel like
        // part of the ring instead of dead space. A gap that points at a slot with no bubble in
        // it simply clears the hover.
        int index = RingMetrics.NearestSlotIndex(dx, dy);
        SetHovered(_bubbles.FirstOrDefault(b => b.Index == index));
    }

    /// <summary>
    /// Half the gap between two neighbours on the quarter circle, with a little slack: far enough
    /// that the space between items still points at one of them, tight enough that the screen
    /// beyond either end of the fan points at nothing.
    /// </summary>
    private double QuadrantHoverToleranceRad()
    {
        if (_bubbles.Count < 2)
            return Math.PI / 4;
        double step = Math.Abs(NormalizeAngle(_bubbles[1].AngleRad - _bubbles[0].AngleRad));
        return step * 0.6;
    }

    /// <summary>
    /// An arc occupies part of the circle, not all of it — unlike the full ring's slot lookup — so
    /// a pointer out in the band still has to be near the arc to hover anything on it.
    /// </summary>
    private static BubbleView? NearestOnArc(
        IReadOnlyList<BubbleView> bubbles, double dx, double dy, double toleranceRad)
    {
        double pointer = Math.Atan2(dy, dx);

        BubbleView? best = null;
        double bestDelta = double.MaxValue;
        foreach (var bubble in bubbles)
        {
            double delta = Math.Abs(NormalizeAngle(pointer - bubble.AngleRad));
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = bubble;
            }
        }

        return bestDelta <= toleranceRad ? best : null;
    }

    private static double NormalizeAngle(double radians)
    {
        double twoPi = Math.PI * 2;
        radians %= twoPi;
        if (radians > Math.PI) radians -= twoPi;
        if (radians < -Math.PI) radians += twoPi;
        return radians;
    }

    /// <summary>
    /// Clears the hover when the pointer really has left the ring — and only then.
    /// <para>
    /// PointerExited bubbles, so this handler also receives the exit a bubble raises when the
    /// pointer merely steps off it into the gap beside it, where the ring still hovers it by
    /// angle (<see cref="RingCanvas_PointerMoved"/>). Acting on those was half of what made hover
    /// look erratic: the item under the pointer kept being un-hovered and re-hovered a frame
    /// later, so it flinched, its label blinked, and a group's flyout never survived long enough
    /// to open. The pointer's own distance from the ring center says which kind of exit this is,
    /// with nothing to infer from the event's source.
    /// </para>
    /// </summary>
    private void RingCanvas_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        // An exit the canvas itself raised is always real — moving onto one of its own children
        // never exits the parent — so only the bubbled kind is worth second-guessing.
        if (e.OriginalSource is not Canvas)
        {
            var pos = e.GetCurrentPoint(RingCanvas).Position;
            double dx = pos.X - _cx;
            double dy = pos.Y - _cy;
            if (dx * dx + dy * dy <= _contentRadius * _contentRadius)
                return;
        }

        SetHovered(null);
    }

    private async void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
            return;

        e.Handled = true;
        if (_flyoutParent is not null)
            await CollapseFlyoutAsync();
        else
            Dismiss();
    }

    private sealed class BubbleView
    {
        public required Border Host { get; init; }
        public required RingItem Item { get; init; }

        /// <summary>Inner slot index, or position along the flyout arc.</summary>
        public required int Index { get; init; }

        /// <summary>
        /// Position in the entrance stagger, which is not always <see cref="Index"/>: the flyout
        /// fans out from the middle of its arc, so collapsing it in mirror image needs the
        /// distance-from-middle, not the arc position.
        /// </summary>
        public int StaggerIndex { get; set; }

        public required double AngleRad { get; init; }

        /// <summary>Resting position relative to the ring center.</summary>
        public required float RestX { get; init; }

        public required float RestY { get; init; }

        /// <summary>Where the bubble is born and dies — the hub, or its parent group bubble.</summary>
        public required float OriginX { get; init; }

        public required float OriginY { get; init; }

        public required double Diameter { get; init; }
        public required bool IsFlyout { get; init; }

        /// <summary>
        /// The live-acrylic disc under the tint. ApplyBubbleFill fades it out whenever the fill
        /// above it goes fully opaque: there is nothing left to see through it then, and an
        /// acrylic disc under an opaque one is only a chance for the two edges to disagree.
        /// </summary>
        public required UIElement Backdrop { get; init; }

        /// <summary>The glass tint layer's own brush, animated by ApplyBubbleFill for hover/close
        /// states — see CreateBubble's comment on why this, and not Host.Background, carries the
        /// fill, and why it is one brush for the life of the bubble.</summary>
        public required SolidColorBrush TintBrush { get; init; }

        /// <summary>Counterpart of <see cref="TintBrush"/> for the glyph and the group arc, which
        /// invert together with the fill behind them.</summary>
        public required SolidColorBrush GlyphBrush { get; init; }

        /// <summary>The color animation currently running on <see cref="TintBrush"/>, so the next
        /// state change can hand off from it instead of stacking a second one on the same
        /// property — see <see cref="AnimateBrushTo"/>. Null once it has settled.</summary>
        public Storyboard? TintFade { get; set; }

        /// <summary><see cref="TintFade"/>'s counterpart for <see cref="GlyphBrush"/>.</summary>
        public Storyboard? GlyphFade { get; set; }

        /// <summary>Scale to return to when hover ends — not 1 for an expanded group bubble.</summary>
        public float RestScale { get; set; } = 1f;

        public Action? Detach { get; set; }

        /// <summary>Composition offset that puts the bubble back at its origin.</summary>
        public Vector3 EntryOffset => new(OriginX - RestX, OriginY - RestY, 0);
    }
}
