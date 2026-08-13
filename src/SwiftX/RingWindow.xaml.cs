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
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using Ellipse = Microsoft.UI.Xaml.Shapes.Ellipse;

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

    /// <summary>Period of the "waiting for a drop" pulse on the Drag &amp; drop quick action.</summary>
    private const int DragDropPulseMs = 900;

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
    private Border? _pill;
    private TextBlock? _pillText;
    private bool _pillShown;
    private double _pillLeft;
    private double _pillTop;
    private double _hubSize;
    private double _cx;
    private double _cy;
    private bool _isOpen;
    private bool _isAnimating;
    private int _renderGeneration;
    private BubbleView? _hovered;
    private int _hoverGeneration;

    // Drag & drop quick action state. Armed by a click on that flyout child (see OnSelectAsync's
    // IsDragDropItem branch) so the user can go find something external to drag in without a
    // window-activation change dismissing the ring first — see RingWindow_Activated. The bubble
    // reference is what StopDragDropPulse needs to halt its own composition animation; it does not
    // survive a re-render, so every place that rebuilds or tears down the ring resets both fields.
    private bool _dragDropArmed;
    private BubbleView? _dragDropBubble;

    public bool IsOpen => _isOpen;

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
        double r = RingMetrics.ContentRadius;
        _cx = ClampCenter(_cx, RingCanvas.Width, r);
        _cy = ClampCenter(_cy, RingCanvas.Height, r);
    }

    private static double ClampCenter(double value, double extent, double radius) =>
        extent >= radius * 2 ? Math.Clamp(value, radius, extent - radius) : extent / 2;

    public void Summon()
    {
        // Cancel any in-flight dismiss hide.
        _renderGeneration++;
        WindowChrome.MakeTransparentOverlay(this, _hwnd);
        ApplyTheme();
        LayoutAtCursor();
        _appWindow.Show();
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);
        _isOpen = true;
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

        double r = RingMetrics.ContentRadius;
        _cx = ClampCenter((cursor.X - area.X) / scale, RingCanvas.Width, r);
        _cy = ClampCenter((cursor.Y - area.Y) / scale, RingCanvas.Height, r);
    }

    public void Dismiss()
    {
        if (!_isOpen && !_isAnimating)
            return;

        _isOpen = false;
        _ = DismissAnimatedAsync();
    }

    /// <summary>Clicking anywhere else on screen — another window, the desktop, the taskbar —
    /// hands that window foreground activation and deactivates this one; treat it the same as
    /// clicking the ring's own dead zone.
    /// <para>
    /// Suppressed while Settings or Add-new is open: opening either from the ring's own flyout
    /// already dismisses the ring on selection (see <see cref="OnSelectAsync"/>), but both can also
    /// be reached from the tray icon while the ring is sitting open, or the ring can be re-summoned
    /// while one already has focus — either way, every click into that window would otherwise
    /// deactivate this one and immediately dismiss the ring out from under the user. Once the
    /// window closes, the matching <c>RingManager</c> flag goes false again and the very next
    /// deactivation resumes the normal click-anywhere-to-dismiss behavior.
    /// </para>
    /// <para>
    /// Also suppressed while drag &amp; drop mode is armed (<see cref="_dragDropArmed"/>) — the
    /// whole point of arming it is to survive the activation change that clicking an external drag
    /// source (an icon on the desktop, a link in a browser) causes.
    /// </para>
    /// </summary>
    private void RingWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
            return;
        if (_manager.IsSettingsOpen || _manager.IsAddItemOpen || _dragDropArmed)
            return;
        Dismiss();
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
        _pill = null;
        _pillText = null;
        _hovered = null;
        // Not a DisarmDragDrop() call: the bubble (and its composition Visual) is being discarded
        // right here along with everything else, so there is nothing left to stop an animation on.
        _dragDropArmed = false;
        _dragDropBubble = null;
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

    private IReadOnlyList<(RingItem Item, int Slot)> CurrentItems()
    {
        var slots = new List<(RingItem, int)>();
        int slot = 0;
        foreach (var item in _manager.Config.Primary.Items.Where(i => !i.Hidden))
        {
            if (slot >= RingMetrics.UserSlotCount) break;
            slots.Add((item, slot++));
        }

        // Always last, always present, always in the same place — see SystemRingItems.
        slots.Add((_settingsItem, RingMetrics.SlotCount - 2));
        slots.Add((_closeItem, RingMetrics.SlotCount - 1));
        return slots;
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
        // A successful drop reaches here too (NotifyItemsChanged -> ReloadItems -> RenderCurrent),
        // which is exactly the moment drag & drop mode should end — one reset covers both "the
        // whole ring reloaded" and "the item that was just dropped needs the armed pulse to stop."
        _dragDropArmed = false;
        _dragDropBubble = null;

        AttachHub();

        foreach (var (item, slot) in CurrentItems())
        {
            double angle = RingMetrics.SlotAngleRad(slot);
            var bubble = CreateBubble(
                item, slot, angle,
                radius: RingMetrics.Radius,
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
            (_cx, _cy, RingMetrics.ContentRadius),
        };

        // The region clips painting, so a pill that reaches past the ring disc needs its own
        // circle unioned in or its far end is simply cut off. Adding it per-pill, instead of
        // permanently padding ContentRadius by the longest name a user might have, keeps the
        // ring's own footprint tight enough to still open under the cursor near a screen edge.
        if (pillRadius > 0)
            regions.Add((pillCx, pillCy, pillRadius));

        WindowChrome.SetEllipticHitRegions(_hwnd, scale, regions);
    }

    private static async Task ResolveIconAsync(RingItem ringItem)
    {
        // Every render fires one of these per item, unconditionally and with no de-dup — if the
        // item's Target is edited while a slow resolution (e.g. a favicon fetch) for the old
        // Target is still in flight, that stale result must not overwrite whatever the new
        // Target already resolved to.
        string targetAtStart = ringItem.Target;
        var src = await IconService.LoadIconAsync(ringItem);
        if (src is not null && ringItem.Target == targetAtStart)
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
        double hub = RingMetrics.Hub;
        if (_hub is null || _hubSize != hub)
        {
            BuildHub(hub);
            return;
        }

        Canvas.SetLeft(_hub, _cx - hub / 2);
        Canvas.SetTop(_hub, _cy - hub / 2);
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
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// The hub is the app's own mark and nothing else: no plate, no border, no fill — the logo
    /// sits directly on the desktop. Its size is fixed (<see cref="RingMetrics.HubLogo"/>) and no
    /// setting scales it; item size governs the ring, not the center.
    /// <para>
    /// Item names pop out beside the item they belong to (<see cref="ShowPill"/>), which is what
    /// freed the center up.
    /// </para>
    /// </summary>
    private void BuildHub(double hub)
    {
        _hubSize = hub;
        _hub = new Border
        {
            Width = hub,
            Height = hub,
            Background = null,
            BorderThickness = new Thickness(0),
            Child = AppLogo.Create(RingMetrics.HubLogo),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(_hub, _cx - hub / 2);
        Canvas.SetTop(_hub, _cy - hub / 2);
        RingCanvas.Children.Add(_hub);

        // Same spring family as the bubble burst-in (AnimateBurstIn), not a flat linear fade, so
        // the hub reads as part of one physical motion instead of popping in on its own timeline.
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(_hub);
            visual.CenterPoint = new Vector3((float)(hub / 2), (float)(hub / 2), 0);
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

        bool isDragDropItem = IsDragDropItem(item);

        var glyph = new FontIcon
        {
            Glyph = item.Glyph,
            FontSize = glyphSize,
            Foreground = ThemeBrush("SwiftXForegroundBrush"),
            // The Drag & drop quick action draws a dot-grip instead (see BuildDragHandleDots
            // below) — there's no Fluent glyph for the concept, so item.Glyph here is only ever
            // the unstyled fallback from RingItem.Glyph's Kind switch, never something meant to
            // actually be shown.
            Visibility = isDragDropItem ? Visibility.Collapsed : item.GlyphVisibility,
        };
        if (GlyphFonts.IsTextGlyph(item.Glyph))
            glyph.FontFamily = new FontFamily("Segoe UI");

        PropertyChangedEventHandler onItemChanged = (_, e) =>
        {
            if (e.PropertyName is nameof(RingItem.IconImage) or nameof(RingItem.ImageVisibility) or nameof(RingItem.GlyphVisibility))
            {
                icon.Source = item.IconImage;
                icon.Visibility = item.ImageVisibility;
                glyph.Visibility = isDragDropItem ? Visibility.Collapsed : item.GlyphVisibility;
            }
        };
        item.PropertyChanged += onItemChanged;

        var content = new Grid();
        content.Children.Add(icon);
        content.Children.Add(glyph);
        if (isDragDropItem)
            content.Children.Add(BuildDragHandleDots(glyphSize, ThemeBrush("SwiftXForegroundBrush")));

        // A group wears a small nub on its outward edge — before you hover it, that dot is the
        // only thing distinguishing an item that opens a ring of its own from one that launches.
        // It rides inside the circle rather than protruding, so the Border's corner-radius clip
        // can never crop it, and it scales with the item because it lives in the same content.
        Ellipse? bump = null;
        if (item.IsGroup && !isFlyout)
        {
            double dot = RingMetrics.Bump;
            bump = new Ellipse
            {
                Width = dot,
                Height = dot,
                Fill = ThemeBrush("SwiftXForegroundBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                RenderTransform = new TranslateTransform
                {
                    X = Math.Cos(angle) * (diameter / 2 - dot),
                    Y = Math.Sin(angle) * (diameter / 2 - dot),
                },
            };
            content.Children.Add(bump);
        }

        var border = new Border
        {
            Width = diameter,
            Height = diameter,
            CornerRadius = new CornerRadius(diameter / 2),
            Background = ThemeBrush("RingBubbleBackground"),
            Child = content,
            Tag = item,
        };
        Canvas.SetLeft(border, _cx + restX - diameter / 2);
        Canvas.SetTop(border, _cy + restY - diameter / 2);

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
            Glyph = glyph,
            Bump = bump,
            Detach = () => item.PropertyChanged -= onItemChanged,
        };

        // Precise hover: the pointer is directly over this bubble's own shape. This and the
        // angle-driven hover in RingCanvas_PointerMoved both funnel through SetHovered so the
        // visual treatment (OnHover) never runs twice or gets out of sync between the two paths.
        border.PointerEntered += (_, _) => SetHovered(bubble);
        border.PointerExited += (_, _) =>
        {
            if (ReferenceEquals(_hovered, bubble))
                SetHovered(null);
        };
        border.Tapped += async (_, e) =>
        {
            e.Handled = true;
            await OnSelectAsync(bubble);
        };

        if (isDragDropItem)
            WireDropTarget(border);

        return bubble;
    }

    /// <summary>
    /// Makes the Drag &amp; drop quick action's bubble an OS drop target — ordinary WinUI
    /// <c>AllowDrop</c>/drag events, the same mechanism a ListView's own reorder/file-drop support
    /// is built on, and (like any other pointer input this ring receives) it works here because the
    /// bubble sits inside the window's own elliptical hit-region clip
    /// (<see cref="WindowChrome.SetEllipticHitRegions"/>), not despite it. Actually accepting the
    /// drop still requires drag &amp; drop mode to have been armed first by a click (see
    /// <see cref="ToggleDragDropArmed"/>) — hovering the bubble with something already mid-drag
    /// does not arm it on its own, since that would arm on a graze the user never meant as a click.
    /// </summary>
    private void WireDropTarget(Border border)
    {
        border.AllowDrop = true;
        border.DragOver += (_, e) =>
        {
            e.AcceptedOperation = _dragDropArmed ? DataPackageOperation.Copy : DataPackageOperation.None;
            if (_dragDropArmed)
                e.DragUIOverride.Caption = "Add to ring";
        };
        border.Drop += async (_, e) =>
        {
            if (!_dragDropArmed)
                return;
            var deferral = e.GetDeferral();
            try
            {
                await AddRingItemFromDropAsync(e.DataView);
            }
            finally
            {
                // AddRingItemFromDropAsync (on success) goes through NotifyItemsChanged ->
                // ReloadItems -> RenderCurrent, which already resets _dragDropArmed/_dragDropBubble
                // and rebuilds this exact bubble from scratch — but a drop that yielded nothing
                // usable (an unrecognized format) never reaches that path, so disarm explicitly
                // here too rather than leaving the pulse running for a drop that silently did
                // nothing.
                if (_dragDropArmed)
                    DisarmDragDrop();
                deferral.Complete();
            }
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
    private Brush ThemeBrush(string key)
    {
        var requested = ThemeHelper.Resolve(_manager.Config.Theme);
        bool dark = requested == ElementTheme.Default
            ? ThemeHelper.IsDark(Root)
            : requested == ElementTheme.Dark;
        return new SolidColorBrush(ThemeHelper.ColorFor(dark, key));
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

        double px, py;
        if (!bubble.IsFlyout && _flyout.Count > 0 && ReferenceEquals(_flyoutParent, bubble))
        {
            // Once a group is open, its name is the heading of the ring it opened rather than a
            // label on the item — so it sits directly above the first child in the arc, a fixed
            // point a user's eye already lands on, instead of centered over the whole arc, which
            // would drift sideways depending on how many children the group has.
            var first = _flyout[0];
            px = first.RestX;
            py = first.RestY
                 - (RingMetrics.FlyoutBubble / 2 * RingMetrics.HoverScale
                    + RingMetrics.PillGap + size.Height / 2);
        }
        else
        {
            // Sit wholly outside the item, along the same radial it sits on, so the label reads
            // as belonging to that item and never covers it. The offset uses the pill's own
            // extent measured along that radial — the projection of its box onto the radial axis,
            // which is its width when the item is due east and its height when the item is due
            // north — rather than one constant that would tuck a wide pill under a sideways item.
            double ux = Math.Cos(bubble.AngleRad);
            double uy = Math.Sin(bubble.AngleRad);
            double reach = (Math.Abs(ux) * size.Width + Math.Abs(uy) * size.Height) / 2;

            double restRadius = Math.Sqrt(bubble.RestX * bubble.RestX + bubble.RestY * bubble.RestY);
            double clearance = restRadius + bubble.Diameter / 2 * HoverScaleFor(bubble);

            // Any other item whose label reaches into an open flyout's arc has to clear those
            // children as well as its own circle.
            if (!bubble.IsFlyout && _flyout.Count > 0 && PointsIntoFlyoutArc(bubble.AngleRad))
            {
                clearance = Math.Max(
                    clearance,
                    RingMetrics.FlyoutRadius + RingMetrics.FlyoutBubble / 2 * RingMetrics.HoverScale);
            }

            double distance = clearance + RingMetrics.PillGap + reach;
            px = distance * ux;
            py = distance * uy;
        }

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
        double tolerance = (RingMetrics.FlyoutStepDegrees * 0.5 + 12) * Math.PI / 180.0;
        return _flyout.Any(b => Math.Abs(NormalizeAngle(angleRad - b.AngleRad)) <= tolerance);
    }

    /// <summary>
    /// Slides an already-visible pill to a new spot instead of letting it blink out and back in.
    /// Opening a group moves its label from beside the item up to the top of the outer ring, and
    /// that jump is the most visible motion on screen if it isn't animated.
    /// </summary>
    private void GlidePill(double fromDx, double fromDy)
    {
        if (_pill is null) return;
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(_pill);
            visual.Offset = new Vector3((float)fromDx, (float)fromDy, 0);
            var compositor = visual.Compositor;
            var glide = compositor.CreateVector3KeyFrameAnimation();
            glide.InsertKeyFrame(1, Vector3.Zero, compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.2f, 0.9f), new Vector2(0.2f, 1f)));
            glide.Duration = TimeSpan.FromMilliseconds(200);
            visual.StartAnimation("Offset", glide);
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
            visual.Offset = Vector3.Zero;
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

    /// <summary>The exact reverse of <see cref="BuildHub"/>'s entrance spring.</summary>
    private void FadeOutHub()
    {
        if (_hub is null) return;
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
        AnimateScale(parent, ExpandedParentScale, 180);

        double step = RingMetrics.FlyoutStepDegrees;
        double centerDeg = parent.AngleRad * 180.0 / Math.PI;
        double startDeg = centerDeg - step * (children.Count - 1) / 2.0;
        var origin = new Vector2(parent.RestX, parent.RestY);

        for (int i = 0; i < children.Count; i++)
        {
            double angle = (startDeg + i * step) * Math.PI / 180.0;
            var bubble = CreateBubble(
                children[i], i, angle,
                radius: RingMetrics.FlyoutRadius,
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
            AnimateScale(parent, hovered ? HoverScaleFor(parent) : 1f, 180);
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
        AnimateScale(bubble, entered ? HoverScaleFor(bubble) : bubble.RestScale, 130);

        // An expanded group stays lit while its flyout is open, so exiting it must not reset the
        // fill the way exiting an ordinary bubble does.
        ApplyBubbleFill(bubble, entered || ReferenceEquals(bubble, _flyoutParent));
    }

    /// <summary>
    /// Active state reads as an inverted fill (no border in either theme) rather than an outline —
    /// swaps the same two brushes CreateBubble already applies at rest, just reversed. Close is
    /// the exception: it goes red under the pointer, because it is the one item on the ring whose
    /// effect can't be undone by clicking again.
    /// </summary>
    private void ApplyBubbleFill(BubbleView bubble, bool active)
    {
        bool destructive = active && IsCloseItem(bubble.Item);

        Brush background = destructive ? ThemeBrush("RingCloseHoverBackground")
            : active ? ThemeBrush("SwiftXForegroundBrush")
            : ThemeBrush("RingBubbleBackground");

        Brush foreground = destructive ? ThemeBrush("RingCloseHoverForeground")
            : active ? ThemeBrush("RingBubbleBackground")
            : ThemeBrush("SwiftXForegroundBrush");

        bubble.Host.Background = background;
        bubble.Glyph.Foreground = foreground;
        if (bubble.Bump is not null)
            bubble.Bump.Fill = foreground;
    }

    private static bool IsCloseItem(RingItem item) =>
        string.Equals(item.Target, SystemRingItems.CloseTarget, StringComparison.OrdinalIgnoreCase);

    private static float HoverScaleFor(BubbleView bubble) => (float)(IsCloseItem(bubble.Item)
        ? RingMetrics.CloseHoverScale
        : RingMetrics.HoverScale);

    private static void AnimateScale(BubbleView bubble, float target, int durationMs)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(bubble.Host);
            visual.CenterPoint = new Vector3(
                (float)(bubble.Diameter / 2),
                (float)(bubble.Diameter / 2),
                0);
            var scale = visual.Compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(1, new Vector3(target, target, 1));
            scale.Duration = TimeSpan.FromMilliseconds(durationMs);
            visual.StartAnimation("Scale", scale);
        }
        catch
        {
            // ignore
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

        if (IsDragDropItem(item))
        {
            // Arms/disarms drop-target mode instead of "activating" anything — see
            // ToggleDragDropArmed and the matching check in RingWindow_Activated.
            ToggleDragDropArmed(bubble);
            return;
        }

        // Selecting anything else ends drag & drop mode if it was armed — the user's attention has
        // moved on to something unrelated, so the pulse shouldn't keep running.
        if (_dragDropArmed)
            DisarmDragDrop();

        // Settings is a group now too (it owns the Drag & drop / Add new quick actions below), but
        // unlike a user-created group it still has a job of its own to do on click — open Settings
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
        // window is open (see RingWindow_Activated's matching IsSettingsOpen/IsAddItemOpen checks,
        // which keep the deactivation that follows here from dismissing it too). Selecting either
        // a second time while its window is already open just re-activates that window (see
        // RingManager.OpenSettings/OpenAddItem) — still no dismiss.
        bool staysOpen = IsSettingsItem(item) ||
            string.Equals(item.Target, SystemRingItems.AddNewTarget, StringComparison.OrdinalIgnoreCase);
        if (!staysOpen)
            Dismiss();
    }

    private static bool IsSettingsItem(RingItem item) =>
        string.Equals(item.Target, SystemRingItems.SettingsTarget, StringComparison.OrdinalIgnoreCase);

    private static bool IsDragDropItem(RingItem item) =>
        string.Equals(item.Target, SystemRingItems.DragDropTarget, StringComparison.OrdinalIgnoreCase);

    // ---- Drag & drop quick action ----------------------------------------------------------

    private void ToggleDragDropArmed(BubbleView bubble)
    {
        if (_dragDropArmed && ReferenceEquals(_dragDropBubble, bubble))
        {
            DisarmDragDrop();
            return;
        }
        // Defensive: only one bubble should ever be armed at a time, but if some other path left
        // a stale one set, stop its animation before starting a new one rather than leaking it.
        if (_dragDropArmed)
            DisarmDragDrop();

        _dragDropArmed = true;
        _dragDropBubble = bubble;
        StartDragDropPulse(bubble);
    }

    private void DisarmDragDrop()
    {
        if (_dragDropBubble is not null)
            StopDragDropPulse(_dragDropBubble);
        _dragDropArmed = false;
        _dragDropBubble = null;
    }

    private static void StartDragDropPulse(BubbleView bubble)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(bubble.Host);
            visual.CenterPoint = new Vector3((float)(bubble.Diameter / 2), (float)(bubble.Diameter / 2), 0);

            var pulse = visual.Compositor.CreateVector3KeyFrameAnimation();
            pulse.InsertKeyFrame(0f, Vector3.One);
            pulse.InsertKeyFrame(0.5f, new Vector3(1.18f, 1.18f, 1));
            pulse.InsertKeyFrame(1f, Vector3.One);
            pulse.Duration = TimeSpan.FromMilliseconds(DragDropPulseMs);
            pulse.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("Scale", pulse);
        }
        catch
        {
            // Composition may be unavailable in some hosts — same tolerance as every other
            // animation in this file.
        }
    }

    private static void StopDragDropPulse(BubbleView bubble)
    {
        try
        {
            ElementCompositionPreview.GetElementVisual(bubble.Host).StopAnimation("Scale");
        }
        catch
        {
        }
        // Composition animations leave their last-driven value in place once stopped rather than
        // reverting to the pre-animation value — this is what puts the bubble back at rest scale
        // instead of leaving it wherever the pulse happened to be when it was cut off.
        ResetBubbleVisual(bubble, rested: true);
    }

    /// <summary>
    /// Extracts a droppable target (a file/folder/shortcut/app path, or a URL) from an external
    /// drag's payload and adds it to the ring the same way "Add new" would. Tries storage items
    /// first (Explorer, the Start menu, a desktop icon), then a web link (a browser's own address
    /// bar or an anchor drag), then falls back to plain text in case some source only offers that —
    /// <see cref="RingItemFactory.Classify"/> re-derives the actual kind from the string either way,
    /// so nothing here has to guess whether what came off the source was a shortcut, a folder, or
    /// a URL.
    /// </summary>
    private async Task<bool> AddRingItemFromDropAsync(DataPackageView data)
    {
        string? target = null;

        if (data.Contains(StandardDataFormats.StorageItems))
        {
            var items = await data.GetStorageItemsAsync();
            target = items.FirstOrDefault()?.Path;
        }

        if (string.IsNullOrEmpty(target) && data.Contains(StandardDataFormats.WebLink))
        {
            var uri = await data.GetWebLinkAsync();
            target = uri?.ToString();
        }

        if (string.IsNullOrEmpty(target) && data.Contains(StandardDataFormats.Text))
        {
            var text = (await data.GetTextAsync())?.Trim();
            if (!string.IsNullOrEmpty(text) &&
                Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                target = text;
        }

        if (string.IsNullOrEmpty(target))
            return false;

        var item = new RingItem
        {
            Kind = RingItemFactory.Classify(target),
            Target = target,
            DisplayName = RingItemFactory.SuggestName(target),
        };
        _manager.Config.Primary.Items.Add(item);
        _manager.NotifyItemsChanged();
        return true;
    }

    /// <summary>
    /// Six dots standing in for the Drag &amp; drop quick action's icon — the same hand-drawn grip
    /// Settings › Tools already uses for its reorder handle (see SettingsWindow.BuildDragGrip): no
    /// Segoe Fluent Icons codepoint draws a drag/move glyph, and guessing an unfamiliar private-use
    /// codepoint risks silently rendering nothing at all rather than failing loudly.
    /// </summary>
    private static FrameworkElement BuildDragHandleDots(double size, Brush dotBrush)
    {
        double dot = Math.Max(2, size * 0.16);
        var grid = new Grid
        {
            Width = size,
            Height = size,
            ColumnSpacing = dot,
            RowSpacing = dot,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 2; column++)
            {
                var pip = new Ellipse
                {
                    Width = dot,
                    Height = dot,
                    Fill = dotBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(pip, row);
                Grid.SetColumn(pip, column);
                grid.Children.Add(pip);
            }
        }

        return grid;
    }

    // ---- Input ---------------------------------------------------------------------------

    private async void RingCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(RingCanvas).Position;
        double dx = pos.X - _cx;
        double dy = pos.Y - _cy;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < RingMetrics.DeadZone)
        {
            // The hub is the way back out of a flyout before it is the way out of the ring.
            if (_flyoutParent is not null)
                await CollapseFlyoutAsync();
            else
                Dismiss();
            return;
        }

        // A press landing directly on a bubble is handled by that bubble's own Tapped handler
        // (e.OriginalSource is then the Border or one of its children, not the Canvas). This
        // covers presses in the gaps between bubbles: with the hit-region covering the whole
        // disk, a press out here should activate whichever item angular-hover currently targets,
        // the same as clicking the bubble itself would — not fall through to a dismiss.
        if (e.OriginalSource is Canvas)
        {
            if (_hovered is { } bubble)
            {
                e.Handled = true;
                await OnSelectAsync(bubble);
            }
            else if (_flyoutParent is not null)
            {
                await CollapseFlyoutAsync();
            }
            else
            {
                // Genuinely empty ring space (a slot with no bubble in it): same as before.
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
        if (_flyout.Count > 0 && dist >= RingMetrics.FlyoutBandStart)
        {
            SetHovered(NearestFlyoutBubble(dx, dy));
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
    /// The flyout occupies an arc, not the whole circle, so — unlike the inner ring's slot
    /// lookup — a pointer out in the band still has to be near the arc to hover anything.
    /// </summary>
    private BubbleView? NearestFlyoutBubble(double dx, double dy)
    {
        double pointer = Math.Atan2(dy, dx);
        double tolerance = RingMetrics.FlyoutStepDegrees * 0.6 * Math.PI / 180.0;

        BubbleView? best = null;
        double bestDelta = double.MaxValue;
        foreach (var bubble in _flyout)
        {
            double delta = Math.Abs(NormalizeAngle(pointer - bubble.AngleRad));
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = bubble;
            }
        }

        return bestDelta <= tolerance ? best : null;
    }

    private static double NormalizeAngle(double radians)
    {
        double twoPi = Math.PI * 2;
        radians %= twoPi;
        if (radians > Math.PI) radians -= twoPi;
        if (radians < -Math.PI) radians += twoPi;
        return radians;
    }

    private void RingCanvas_PointerExited(object sender, PointerRoutedEventArgs e)
    {
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
        public required FontIcon Glyph { get; init; }

        /// <summary>The has-children marker, present only on inner-ring groups.</summary>
        public Ellipse? Bump { get; init; }

        /// <summary>Scale to return to when hover ends — not 1 for an expanded group bubble.</summary>
        public float RestScale { get; set; } = 1f;

        public Action? Detach { get; set; }

        /// <summary>Composition offset that puts the bubble back at its origin.</summary>
        public Vector3 EntryOffset => new(OriginX - RestX, OriginY - RestY, 0);
    }
}
