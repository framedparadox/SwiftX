using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using SwiftX.Controls;
using SwiftX.Models;
using SwiftX.Services;
using Windows.Foundation;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace SwiftX;

/// <summary>
/// The SwiftX app window: Home, Orbit, Tools, Elements and Settings as five sibling screens in one
/// Grid, switched by <see cref="ShowScreen"/>.
/// <para>
/// Built on windows-app-mx's LogiApp (branch <c>logiApp/dotnet</c>) — Home is its <c>MainPage</c>
/// header, Settings is its <c>SettingsPage</c> measured value for value (270px sidebar, two-line
/// 30/34 editorial title, 44px rows with a 4px accent indicator, 48,28,64,48 content padding
/// capped at 780), and the palette is its own <c>Settings*</c> token family carried into
/// <c>Themes/Colors.xaml</c> as <c>Shell*</c>. See <c>docs/full-app-mockups-logi-base.md</c> for
/// the mockups and <c>docs/swiftx-app-shell-plan.md</c> for the plan.
/// </para>
/// <para>
/// This is the window <c>SettingsWindow</c> became. Promoting it rather than adding a sixth window
/// class beside it is what keeps the orbit preview's slot geometry, its drag-reorder and reflow
/// animation, group expansion, the hot-corner quadrant, the theme/accent engine and every setting
/// handler exactly as they were — the redesign is a re-homing of those panels into Logi's frame,
/// not a rewrite of what they do.
/// </para>
/// <para>
/// This window, and only this window, follows the Shell/Settings-exclusive palette in
/// <c>Themes/Colors.xaml</c>; the orbit, AddItemWindow, and the tool windows keep the app's shared
/// black/white identity, which is why the shared <c>SwiftX*</c> brushes are deliberately left
/// alone here.
/// </para>
/// <para>
/// Light is flat and fully opaque (Logi's own #F2F2F2 page and #FFFFFF cards). Dark sits on a real
/// <see cref="MicaBackdrop"/> (attached below, regardless of theme — it only ever shows through
/// Dark's brushes) under the layered, alpha-carrying plates in the Dark dictionary of
/// <c>Themes/Colors.xaml</c>, so Logi's hues keep SwiftX's Mica.
/// </para>
/// </summary>
public sealed partial class ShellWindow : Window
{
    private readonly OrbitManager _manager;
    // Latched in the constructor and acted on once the tree is loaded, since the InfoBar it opens
    // and the nav item it selects don't exist yet when the constructor runs.
    private readonly bool _offerDesktopShortcut;
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private bool _loading = true;

    /// <summary>
    /// Re-reads the clock for the Home screen's greeting. Armed for the next boundary rather than
    /// ticking on a fixed interval — see <see cref="StartGreetingClock"/>.
    /// </summary>
    private DispatcherTimer? _greetingTimer;

    private Border? _dragBorder;
    private OrbitItem? _dragItem;
    private Point _dragStart;
    private bool _dragMoved;
    private int _dragSlot = -1;
    // Tentative reorder target computed live while a drag is in progress, so the other bubbles can
    // reflow to preview where they'll land — see Preview_PointerMoved/UpdateDragReflow. -1 means
    // "no drag in progress" / "not yet computed". Preview_PointerReleased reuses these directly
    // rather than recomputing from the drop point, so the committed order can never disagree with
    // whatever the live preview last showed.
    private int _dragTentativeSlot = -1;
    private int _dragTentativeQuadrantSlot = -1;

    // Preview geometry and slot occupancy, recorded by RenderPreview so the canvas-level pointer
    // handlers can work out which slot an event landed in without walking the visual tree.
    private readonly Border?[] _slotBorders = new Border?[OrbitMetrics.SlotCount];
    private Point _previewCenter;
    private double _previewRadius;
    private double _previewBubble;

    // Quadrant geometry and slot occupancy, the corner view's counterpart to _slotBorders and
    // friends above: the canvas-level pointer handlers work out which of the four places an event
    // landed in from these rather than by walking the visual tree.
    private readonly Border?[] _quadrantSlotBorders = new Border?[QuadrantLayout.ItemCount];
    private readonly double[] _quadrantSlotAngles = new double[QuadrantLayout.ItemCount];
    private int _quadrantSlotCount;
    private Point _quadrantCenter;
    private double _quadrantRadius;
    private double _quadrantBubble;

    // Whether the canvas is currently showing the hot corner's quadrant instead of the orbit.
    // Purely a preview: toggled by the corner button RenderPreview draws, and never persisted —
    // what the corner actually summons is Config.HotCornerLayout.
    private bool _previewQuadrant;

    // Selection state for the read-only inspector (InfoBox) and the group children row (GroupRow).
    // Neither swaps out which list the orbit renders anymore — a click just picks what, if anything,
    // is shown below the orbit — so plain "currently selected / currently expanded" fields replace
    // the old _editing/_editList-swapping model entirely.
    private OrbitItem? _selectedItem;
    private OrbitItem? _expandedGroup;

    // Geometry + drag state for GroupRow's horizontal children strip, mirroring _slotBorders /
    // _dragBorder etc. above but for a 1-D layout instead of the orbit's circular one.
    private const double GroupChildBubble = 44;
    private const double GroupChildGap = 14;
    private readonly List<Border> _groupSlotBorders = new();
    private Border? _groupDragBorder;
    private OrbitItem? _groupDragItem;
    private Point _groupDragStart;
    private bool _groupDragMoved;
    private int _groupDragIndex = -1;

    /// <param name="offerDesktopShortcut">
    /// Opens this window on Settings › General with the desktop-shortcut prompt showing. Set only
    /// by the first run (see <see cref="OrbitManager.Start"/>), because a Store-packaged SwiftX
    /// cannot create that shortcut at install time and so has to ask for it once, from inside the
    /// app.
    /// </param>
    public ShellWindow(OrbitManager manager, bool offerDesktopShortcut = false)
    {
        _manager = manager;
        _offerDesktopShortcut = offerDesktopShortcut;
        InitializeComponent();

        // No backdrop is attached here any more: ApplyTheme below attaches whichever one the theme
        // calls for (see ApplyBackdrop). Mica is still what four of the five get, and still
        // regardless of which — Light's own brushes stay fully opaque, so it never has anything
        // translucent to show through there. Attaching it here as well would only have built one
        // for Frosted glass to throw away a few lines later.

        _toolCards.CollectionChanged += ToolCards_CollectionChanged;

        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));
        Title = "SwiftX app";

        var titleBar = AppTitleBar.WithAppMark("SwiftX app");
        Grid.SetRow(titleBar, 0);
        Root.Children.Insert(0, titleBar);

        // Extend content under the caption so the title bar strip is part of the same surface as
        // the rest of the window (AppTitleBar paints nothing of its own, so the window's own
        // background reaches all the way up). Must precede HideWindowBorder or
        // ExtendsContentIntoTitleBar clobbers the rim color.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);

        // Built once here rather than in the Loaded handler that fills in the rest of the About
        // card: Loaded can fire again if the tree is ever re-parented, and the mark has nothing to
        // wait for — unlike the version string and the toggles, it reads no state.
        AboutMark.Content = AppLogo.Create(72);

        PreviewCanvas.PointerPressed += Preview_PointerPressed;
        PreviewCanvas.PointerMoved += Preview_PointerMoved;
        PreviewCanvas.PointerReleased += Preview_PointerReleased;
        PreviewCanvas.PointerCaptureLost += (_, _) => EndPreviewDrag();
        PreviewCanvas.RightTapped += Preview_RightTapped;

        GroupChildrenCanvas.PointerPressed += GroupRow_PointerPressed;
        GroupChildrenCanvas.PointerMoved += GroupRow_PointerMoved;
        GroupChildrenCanvas.PointerReleased += GroupRow_PointerReleased;
        GroupChildrenCanvas.PointerCaptureLost += (_, _) => EndGroupDrag();

        // See OnRatingValueChanged for why this is a DP callback and not the ValueChanged event.
        ExperienceRating.RegisterPropertyChangedCallback(
            RatingControl.ValueProperty, (_, _) => OnRatingValueChanged());

        ApplyTheme();
        Root.ActualThemeChanged += (_, _) =>
        {
            // Fires on an OS theme flip (relevant to AppTheme.System, which never resolves to
            // Black — see ThemeHelper.Resolve) — reapplying here is a no-op whenever Black isn't
            // selected, and keeps this in sync with ApplyTheme's own ordering if it ever isn't.
            ApplySurfaceOverrides();
            // System follows Windows, so an OS flip changes which built-in accent is the right one.
            ApplyAccentColorOverride();
            ApplyChromeTheme();
            // ApplyAccentColorOverride already re-runs the rail and the theme cards, but only ever
            // with the accent it just computed. Every local brush in this window is a theme colour
            // as well as, or instead of, an accent one — and this is the one event that changes
            // those without changing the accent. See RepaintThemedContent.
            RepaintThemedContent();
        };
        Activated += (_, _) =>
        {
            ApplyChromeTheme();
            // The timer below covers the window sitting open across a boundary, but not a machine
            // that was asleep through one, nor a clock or time zone the user moved while this
            // window was in the background. Coming back to the foreground is the cheapest moment to
            // re-read the clock, and the one where a stale greeting would actually be seen.
            RefreshGreeting();
        };

        // A DispatcherTimer keeps its tick handler — and through it this window — alive on the
        // dispatcher's own queue. OrbitManager drops its reference on Closed (see OpenSettings);
        // without this, the timer would hold the window past that and keep firing at a tree that is
        // no longer on screen.
        Closed += (_, _) => StopGreetingClock();

        if (_appWindow.Presenter is OverlappedPresenter p)
            p.IsMaximizable = false;
        _appWindow.IsShownInSwitchers = true;

        // 1040 x 700 is what the layout is measured for: the three Home nav boxes come to
        // 3x268 + 2x34 = 872 with room to breathe either side, and the Orbit screen splits the same
        // width into Logi's fixed 320px sidebar and a 720px canvas column — which is exactly the
        // split docs/images/17-app-ring.svg is drawn at, and enough for the 640px label canvas
        // inside it with a 40px margin either side.
        WindowChrome.SetClientSizeDip(_appWindow, _hwnd, 1040, 700);
        WindowChrome.SetRoundedCorners(_hwnd);
        WindowChrome.CenterOnCursor(_appWindow, _appWindow.Id);

        Root.Loaded += async (_, _) =>
        {
            _loading = true;
            // Logi's own header line, hour-for-hour — see Services/Greeting.cs.
            StartGreetingClock();
            SelectTheme(_manager.Config.Theme);
            UpdateThemeToggleIcon();
            PopulateLanguageBox();
            SelectHoverStyle(_manager.Config.HoverStyle);
            SelectItemSize(_manager.Config.ItemSize);
            RadiusSlider.Value = _manager.Config.OrbitRadius ?? OrbitMetrics.DefaultRadius;
            HotkeyEnabledToggle.IsOn = _manager.Config.HotkeyEnabled;
            HotCornerEnabledToggle.IsOn = _manager.Config.HotCornerEnabled;
            SelectHotCorner(_manager.Config.HotCorner);
            SelectHotCornerLayout(_manager.Config.HotCornerLayout);
            SyncHotCornerControls();
            if (HotkeyGesture.TryParse(_manager.Config.Hotkey, out var g))
                HotkeyCapture.Gesture = g;
            HotkeyCapture.Assigned += OnHotkeyAssigned;
            // Previously unwired: without this, pressing an unregistrable combo (e.g. a bare "A")
            // during capture left the button silently armed with no feedback.
            HotkeyCapture.NeedsModifier += () =>
                HotkeyStatus.Text = "Hold Ctrl, Alt, or Win with that key.";
            RebuildToolsList();
            ExperienceRating.Value = _manager.Config.FeedbackRating > 0
                ? _manager.Config.FeedbackRating
                : -1; // RatingControl's own "unset"; 0 would render as a real zero-star rating.
            UpdateRatingThanks();
            SyncAccentControls();
            StartupToggle.IsOn = await StartupService.IsEnabledAsync();
            SyncDesktopShortcutToggle();
            VersionText.Text = "Version " + (typeof(App).Assembly.GetName().Version?.ToString() ?? "1.0.0");
            FillSpecCard();
            // Wired here rather than in XAML because ThemeShadow.Receivers is a collection with no
            // XAML syntax of its own on a shared resource. Without a receiver the three nav boxes
            // cast no shadow at all (outside a popup a ThemeShadow draws only onto its receivers),
            // which is the whole point of lifting them with Translation. Guarded on the resource
            // lookup rather than assumed: an element that is not yet in the visual tree cannot be a
            // receiver, and Loaded is the first moment HomeShadowPlate is.
            if (Root.Resources["BoxShadow"] is ThemeShadow boxShadow &&
                !boxShadow.Receivers.Contains(HomeShadowPlate))
            {
                boxShadow.Receivers.Add(HomeShadowPlate);
            }

            _loading = false;
            RenderPreview();
            RebuildItemsList();
            UpdateHomeMeta();
            UpdateHotkeyStatus();
            // Home first, with Settings parked on its own default section so the rail is already
            // consistent the moment the gear is pressed.
            SelectSection(SectionGeneral);
            ShowScreen(ScreenHome);

            if (_offerDesktopShortcut)
            {
                // Land on Settings › General rather than Home so the offer and the switch it
                // corresponds to are both on screen at once — an InfoBar on a page the user has to
                // be told to go to is just a notification with extra steps.
                GoToGeneralPage();
                DesktopShortcutOffer.IsOpen = true;
            }
        };

        _manager.ItemsChanged += () =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                // An edit from elsewhere (e.g. a delete on the Elements screen) can remove the
                // item the Orbit screen currently has selected or expanded — drop stale state rather than
                // leave the inspector or children row pointing at something that no longer exists.
                if (_expandedGroup is not null && !ItemExists(_manager.Config.Primary.Items, _expandedGroup))
                    CollapseGroup();
                if (_selectedItem is not null && !ItemExists(_manager.Config.Primary.Items, _selectedItem))
                    HideInfo();
                RenderPreview();
                if (_expandedGroup is not null) RenderGroupRow();
                RebuildItemsList();
                // The edit sidebar's rows say whether each thing is already on the orbit, which a
                // delete or an add made anywhere else has just changed the answer to.
                if (_orbitEditBuilt) BuildOrbitEditSidebar();
                if (_currentScreen == ScreenHome) UpdateHomeMeta();
            });
        };
    }

    private static bool ItemExists(List<OrbitItem> items, OrbitItem target)
    {
        foreach (var item in items)
        {
            if (ReferenceEquals(item, target)) return true;
            if (item.Children.Count > 0 && ItemExists(item.Children, target)) return true;
        }
        return false;
    }

    public void ApplyTheme()
    {
        ApplyBackdrop();
        ApplySurfaceOverrides();
        ApplyAccentColorOverride();
        ForceThemeResourceRefresh(ThemeHelper.Resolve(_manager.Config.Theme));
        ApplyChromeTheme();
        RepaintThemedContent();
    }

    /// <summary>
    /// Re-runs every part of this window that paints a brush <em>onto an element</em> rather than
    /// binding one, so a theme change reaches it.
    /// <para>
    /// <see cref="ForceThemeResourceRefresh"/> handles everything bound with <c>{ThemeResource}</c>
    /// — which is most of the window, and is why the theme switch was never wholly dead. What it
    /// cannot reach is a local value: a brush written to <c>element.Background</c> is a local value
    /// in the dependency-property sense, and a local value beats any dictionary the framework might
    /// later re-resolve. Everything below writes those.
    /// </para>
    /// <para>
    /// Two of these were already re-run on a theme change and the rest were not, which is what
    /// "the theme toggle does nothing" actually looked like: the page, the cards' own
    /// <c>{ThemeResource}</c> chrome and the orbit preview all flipped, while the Elements screen's
    /// item cards and the whole Tools screen — every icon medallion, kind badge, divider and footer
    /// button on them, all built in code by <see cref="BuildCardShell"/> — kept the colours they
    /// were built with. Switch the theme while either screen is open and nothing on it moved. The
    /// Hover style pill and an expanded group's children row had the same gap; the doc comment on
    /// <see cref="SelectHoverStyle"/> even called its own out, and this is what closes it.
    /// </para>
    /// <para>
    /// A full rebuild of both lists is heavier than repainting in place would be, but a theme
    /// change is a thing a person does seconds apart at most, and the two <c>Rebuild*</c> methods
    /// are the only place that knows how those cards are assembled — a parallel "repaint an
    /// existing card" path would be a second copy of that knowledge, and the copy that goes stale.
    /// </para>
    /// </summary>
    private void RepaintThemedContent()
    {
        SyncAccentControls();
        RenderPreview();
        if (_expandedGroup is not null)
            RenderGroupRow();
        SelectHoverStyle(_manager.Config.HoverStyle);
        SelectSection(_currentSection);
        UpdateThemeCardSelection(_manager.Config.Theme);
        RebuildItemsList();
        RebuildToolsList();
        if (_orbitEditBuilt) BuildOrbitEditSidebar();
    }

    /// <summary>
    /// Assigns <see cref="Root"/>'s RequestedTheme, forcing every <c>{ThemeResource}</c> binding
    /// under it to re-resolve even when <paramref name="target"/> is the value it's already at.
    /// <para>
    /// WinUI only re-walks <c>{ThemeResource}</c> lookups when RequestedTheme/ActualTheme actually
    /// changes value — a plain edit to <see cref="Root"/>'s own Resources (see
    /// <see cref="ApplySurfaceOverrides"/>) is invisible to bindings that already resolved earlier.
    /// Black rides on Dark's own ElementTheme and is told apart purely through that resource
    /// override, so switching Dark &lt;-&gt; Black never changes RequestedTheme's value at all —
    /// without the nudge below, SettingsSurfaceBrush and the rest of <see cref="BlackOverrideKeys"/>
    /// kept showing whatever they'd resolved to before, until some unrelated theme flip (a real
    /// Light/Dark switch, or an OS theme change under System) came along later and finally picked
    /// the override up. That's what made switching to/from Black look like it "did nothing" at
    /// first.
    /// </para>
    /// </summary>
    private void ForceThemeResourceRefresh(ElementTheme target)
    {
        if (Root.RequestedTheme == target)
            Root.RequestedTheme = target == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        Root.RequestedTheme = target;
    }

    /// <summary>
    /// Black rides on Dark's own ElementTheme (see ThemeHelper.Resolve), so it can't be told apart
    /// through a XAML ThemeDictionary the way Light/Dark are — WinUI only ever resolves
    /// {ThemeResource} against Light/Dark/Default. Instead, when Black is selected this overrides
    /// the same keys' resolved brush with a plain, element-local entry in Root.Resources: XAML's
    /// {ThemeResource} lookup walks up the tree checking each level's own Resources before falling
    /// back to the app-level ThemeDictionaries, so a same-keyed entry here shadows the Dark
    /// dictionary's for every Card, screen and canvas Grid under Root — exactly the
    /// same "closer resource wins" behavior SecondaryCaptionStyle's own {ThemeResource} already
    /// relies on (see its comment). Removed again the moment Black is no longer selected, so
    /// switching back to Dark/Light falls straight through to the normal dictionary.
    /// </summary>
    /// <summary>
    /// What each overridden key becomes under Midnight, as (key, ARGB) pairs.
    /// <para>
    /// This used to be a bare key list painted with one flat black, on the reasoning that once the
    /// anchor colour is pure black there is no lighter tone left to graduate through. That held
    /// while every surface in the window was a plate of the same family. It stops holding on Logi's
    /// layout, where a Home nav box is a card with an inset icon medallion and a pill inside it:
    /// flattening card, medallion and pill to the identical black leaves the medallion and the pill
    /// invisible, and the box reads as a bare outline with floating text. So the shell's four
    /// surfaces keep a (very narrow) hierarchy of their own — 0A/12/1A over a 00 page — while the
    /// older Settings-canvas keys keep exactly the flat black they always had.
    /// </para>
    /// </summary>
    private static readonly (string Key, byte A, byte R, byte G, byte B)[] BlackOverrides =
    {
        ("SettingsSurfaceBrush", 0xE6, 0x00, 0x00, 0x00),
        ("SwiftXCanvasBrush", 0xE6, 0x00, 0x00, 0x00),
        ("SwiftXNavPaneBrush", 0xE6, 0x00, 0x00, 0x00),
        ("SwiftXCardBackgroundBrush", 0xE6, 0x00, 0x00, 0x00),
        ("SwiftXOrbitCanvasBrush", 0xE6, 0x00, 0x00, 0x00),

        // The shell's own four, per the remarks above.
        ("ShellBackgroundBrush", 0xE6, 0x00, 0x00, 0x00),
        ("ShellCardBackgroundBrush", 0xE6, 0x12, 0x12, 0x12),
        ("ShellMedallionBrush", 0xE6, 0x0A, 0x0A, 0x0A),
        ("ShellPillBrush", 0xE6, 0x1A, 0x1A, 0x1A),
        ("ShellCardBorderBrush", 0xFF, 0x2A, 0x2A, 0x2A),
        ("ShellDividerBrush", 0xFF, 0x2A, 0x2A, 0x2A),

        // The Orbit screen's canvas family. It keeps a hierarchy under Midnight rather than
        // flattening the way the Settings canvas above does: a ring of nodes over a hub over a
        // ground is nothing at all when all three are the same black. Must stay in step with
        // ThemeHelper.ColorForMidnight's cases for the same keys — the canvas is drawn in code and
        // the sidebar in XAML, so both routes are live on this one screen.
        ("OrbitCanvasBackgroundBrush", 0xFF, 0x00, 0x00, 0x00),
        ("OrbitSidebarBackgroundBrush", 0xFF, 0x00, 0x00, 0x00),
        ("OrbitNodeBackgroundBrush", 0xFF, 0x0A, 0x0A, 0x0A),
        ("OrbitLabelPillBrush", 0xFF, 0x10, 0x10, 0x10),
        ("OrbitHubBrush", 0xFF, 0x1A, 0x1A, 0x1A),
        ("OrbitCanvasBorderBrush", 0xFF, 0x2A, 0x2A, 0x2A),
        ("OrbitLabelPillBorderBrush", 0xFF, 0x24, 0x24, 0x24),
        ("OrbitSidebarBorderBrush", 0xFF, 0x16, 0x16, 0x16),
    };

    /// <summary>
    /// Every key either of the two dark-riding palettes can shadow, so a switch between them — or
    /// back to plain Dark — clears the one before it rather than leaving half of Midnight's black
    /// under Frosted glass's wash. Derived from the two tables rather than listed a third time.
    /// <para>
    /// Brushes only. <see cref="FrostedGlassPalette"/> also answers for the <c>…Color</c> spelling of
    /// the orbit bubble tint, because <see cref="ThemeHelper"/> is asked for colours as well as
    /// brushes; a resource dictionary entry under that key has to actually be a <c>Color</c>, and
    /// writing a brush there would be a type error waiting for the first XAML binding to it.
    /// </para>
    /// </summary>
    private static readonly string[] OverridableSurfaceKeys =
        BlackOverrides.Select(o => o.Key)
            .Concat(FrostedGlassPalette.Surfaces.SelectMany(s => s.Keys))
            .Where(k => k.EndsWith("Brush", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Installs the selected theme's surface palette, or clears both when the theme has a dictionary
    /// of its own. See the remarks above <see cref="BlackOverrides"/> for the mechanism, which is
    /// the same for Frosted glass — the only difference is where the colours come from: Midnight's
    /// are a fixed table, Frosted glass's are computed from its opacity by
    /// <see cref="FrostedGlassPalette"/>, the same source <see cref="ThemeHelper"/> answers from, so
    /// a card painted in code and a card painted from a {ThemeResource} cannot disagree.
    /// </summary>
    private void ApplySurfaceOverrides()
    {
        foreach (var key in OverridableSurfaceKeys)
            Root.Resources.Remove(key);

        switch (_manager.Config.Theme)
        {
            case AppTheme.Black:
                foreach (var (key, a, r, g, b) in BlackOverrides)
                    Root.Resources[key] = new SolidColorBrush(Color.FromArgb(a, r, g, b));
                break;

            case AppTheme.FrostedGlass:
                foreach (var surface in FrostedGlassPalette.Surfaces)
                {
                    var brush = new SolidColorBrush(surface.ColorFor(FrostedGlassBackdrop.Opacity));
                    foreach (var key in surface.Keys)
                    {
                        if (key.EndsWith("Brush", StringComparison.Ordinal))
                            Root.Resources[key] = brush;
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Swaps the window's backdrop to match the theme. Mica for everything but Frosted glass, which
    /// wants a real, thin acrylic instead — see <see cref="FrostedGlassBackdrop"/>.
    /// <para>
    /// Guarded on the type already attached rather than assigned unconditionally: rebuilding a
    /// backdrop tears down and re-creates its composition target, which flickers, and this runs on
    /// every theme apply — including the two that <see cref="ForceThemeResourceRefresh"/> triggers
    /// as it nudges RequestedTheme back and forth.
    /// </para>
    /// </summary>
    private void ApplyBackdrop()
    {
        if (_manager.Config.Theme == AppTheme.FrostedGlass)
        {
            if (SystemBackdrop is not FrostedGlassBackdrop)
                SystemBackdrop = new FrostedGlassBackdrop();
        }
        else if (SystemBackdrop is not MicaBackdrop)
        {
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        }
    }

    /// <summary>
    /// Paints every WinUI brush key, across the controls Settings actually uses, that a real
    /// "accent colour" has to reach — always, whether the colour is the user's own pick or the
    /// built-in teal default. Shadows them in <see cref="Root"/>'s own Resources, the same "closer
    /// resource wins" mechanism <see cref="ApplySurfaceOverrides"/> uses.
    /// <para>
    /// It applies the default too, rather than removing the shadow and letting the keys fall
    /// through, because falling through does not reach SwiftX's teal at all: unset, these keys
    /// resolve to WinUI's own <c>AccentFillColorDefaultBrush</c>, i.e. the Windows system accent
    /// (measured at #4CC2FF on this machine). Only the hyperlinks were ever teal by default —
    /// the toggles, radio buttons, Radius slider, rating stars, and Settings rail all
    /// stayed Windows blue, which is exactly what "the default accent colour is not visible" was.
    /// </para>
    /// <para>
    /// Key names verified against this project's pinned WinUI package (2.3.6)'s own generic.xaml:
    /// each control template reads its key directly via <c>{ThemeResource KeyName}</c>, not through
    /// the intermediate <c>AccentFillColorDefault/Secondary/TertiaryBrush</c> aliases those keys are
    /// themselves defined from — overriding those upstream aliases instead would do nothing,
    /// because a <c>StaticResource</c> alias resolves once, at the point the framework's own
    /// dictionary is parsed, not on every lookup. Assigned in (default, pointer-over, pressed)
    /// triples per control; disabled states are deliberately left to the framework.
    /// </para>
    /// </summary>
    private void ApplyAccentColorOverride()
    {
        var accent = CurrentAccentColor();
        var normal = new SolidColorBrush(accent);
        // A single stored colour still needs three states everywhere it lands (default/pointer-
        // over/pressed) — lightening for hover and darkening for pressed mirrors how
        // AccentFillColorSecondary/TertiaryBrush relate to AccentFillColorDefaultBrush for the
        // system accent, without asking the user to pick three colours themselves.
        var hover = new SolidColorBrush(AccentPalette.PointerOver(accent));
        var pressed = new SolidColorBrush(AccentPalette.Pressed(accent));

        Root.Resources["SettingsAccentBrush"] = normal;
        Root.Resources["SettingsAccentHoverBrush"] = hover;
        Root.Resources["HyperlinkButtonForeground"] = normal;
        Root.Resources["HyperlinkButtonForegroundPointerOver"] = hover;
        Root.Resources["HyperlinkButtonForegroundPressed"] = hover;
        Root.Resources["ToggleSwitchFillOn"] = normal;
        Root.Resources["ToggleSwitchFillOnPointerOver"] = hover;
        Root.Resources["ToggleSwitchFillOnPressed"] = pressed;
        Root.Resources["ToggleSwitchStrokeOn"] = normal;
        Root.Resources["ToggleSwitchStrokeOnPointerOver"] = hover;
        Root.Resources["ToggleSwitchStrokeOnPressed"] = pressed;
        Root.Resources["RadioButtonOuterEllipseCheckedFill"] = normal;
        Root.Resources["RadioButtonOuterEllipseCheckedFillPointerOver"] = hover;
        Root.Resources["RadioButtonOuterEllipseCheckedFillPressed"] = pressed;
        Root.Resources["RadioButtonOuterEllipseCheckedStroke"] = normal;
        Root.Resources["RadioButtonOuterEllipseCheckedStrokePointerOver"] = hover;
        Root.Resources["RadioButtonOuterEllipseCheckedStrokePressed"] = pressed;
        Root.Resources["SliderTrackValueFill"] = normal;
        Root.Resources["SliderTrackValueFillPointerOver"] = hover;
        Root.Resources["SliderTrackValueFillPressed"] = pressed;
        Root.Resources["SliderThumbBackground"] = normal;
        Root.Resources["SliderThumbBackgroundPointerOver"] = hover;
        Root.Resources["SliderThumbBackgroundPressed"] = pressed;
        Root.Resources["RatingControlSelectedForeground"] = normal;
        Root.Resources["RatingControlPointerOverSelectedForeground"] = hover;

        // Logi's three accent-derived Settings tokens. They are StaticResource aliases inside each
        // theme dictionary (Themes/Colors.xaml), and a StaticResource alias resolves once, at the
        // point that dictionary is parsed — so shadowing SettingsAccentBrush above does not reach
        // them and they would have stayed on the built-in violet while every other accented control
        // moved to the user's colour. Shadowing them here is the other half.
        Root.Resources["SettingsNavActiveBrush"] = normal;
        Root.Resources["SettingsNavIndicatorBrush"] = normal;
        Root.Resources["SettingsCardSelectedBorderBrush"] = normal;

        // Two things a resource override cannot reach on its own. ReadPrivacyLink is an inline
        // Hyperlink — not a FrameworkElement, so it has no place in the tree to walk up from (see
        // its comment in ShellWindow.xaml). The rail and the theme cards carry brush assignments
        // written directly onto the element by SelectSection / UpdateThemeCardSelection, which a
        // dictionary change can never reach either, so both are re-run.
        ReadPrivacyLink.Foreground = normal;
        SelectSection(_currentSection);
        UpdateThemeCardSelection(_manager.Config.Theme);
    }

    /// <summary>
    /// What the accent colour actually resolves to right now: the user's own pick when they have
    /// made one, otherwise the built-in teal for the theme in play. Drives both the brushes
    /// <see cref="ApplyAccentColorOverride"/> paints and the swatch/picker that report them.
    /// <para>
    /// The per-theme defaults, the parsing and the hover/pressed derivation all live in
    /// <see cref="AccentPalette"/> now, shared with <see cref="AddItemWindow"/> — this window is no
    /// longer the only one that honours the accent, and two private copies of the default teal
    /// would have been two places to forget when it changes.
    /// </para>
    /// <para>
    /// Note which "is it dark" this passes: the one read from the configured <see cref="AppTheme"/>
    /// rather than from <c>Root.ActualTheme</c>, because <see cref="ApplyTheme"/> has to write the
    /// accent brushes <em>before</em> <see cref="ForceThemeResourceRefresh"/> — that refresh is what
    /// makes every <c>{ThemeResource}</c> under Root re-resolve and pick the new brushes up, but at
    /// that moment Root.RequestedTheme has not been assigned yet, so ActualTheme can still be
    /// reporting the previous theme. Config is already correct at that point; only System, which has
    /// no fixed answer of its own, falls back to what the element actually resolved to. See
    /// <see cref="AccentPalette.IsDark"/>, which carries exactly that rule.
    /// </para>
    /// </summary>
    private Color CurrentAccentColor() => AccentPalette.Resolve(
        _manager.Config, AccentPalette.IsDark(_manager.Config, Root));

    /// <summary>
    /// Brings the swatch and the picker in line with <see cref="CurrentAccentColor"/>. Seeding the
    /// picker re-enters <see cref="AccentColorPicker_ColorChanged"/>, which would otherwise save
    /// the value it was just handed as though the user had chosen it — hence the loading
    /// guard, saved and restored rather than cleared, so this stays safe to call from inside the
    /// window's own load pass where the flag is already up.
    /// </summary>
    private void SyncAccentControls()
    {
        var accent = CurrentAccentColor();
        bool wasLoading = _loading;
        _loading = true;
        AccentColorPicker.Color = accent;
        _loading = wasLoading;
        AccentColorSwatch.Background = new SolidColorBrush(accent);
    }

    // TryParseHexColor / ToHexColor / Blend used to live here. They are AccentPalette.TryParseHex,
    // AccentPalette.ToHex and AccentPalette.Blend now — see CurrentAccentColor's own remarks for
    // why they moved.

    private void AccentColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_loading) return;
        _manager.Config.AccentColorHex = AccentPalette.ToHex(args.NewColor);
        _manager.Save();
        ApplyAccentColorOverride();
        // The Add-item dialog honours the accent too and can be open beside this picker.
        _manager.ApplyAccentColour();
        AccentColorSwatch.Background = new SolidColorBrush(args.NewColor);
    }

    private void ResetAccentColor_Click(object sender, RoutedEventArgs e)
    {
        _manager.Config.AccentColorHex = null;
        _manager.Save();
        ApplyAccentColorOverride();
        _manager.ApplyAccentColour();
        SyncAccentControls();
        AccentColorSwatch.Flyout?.Hide();
    }

    private void ApplyChromeTheme()
    {
        bool dark = ThemeHelper.IsDark(Root);
        WindowChrome.SetTitleBarTheme(_appWindow, dark);
        int rim = _manager.Config.Theme switch
        {
            AppTheme.Black => ShellRimColorBlack,
            AppTheme.FrostedGlass => ShellRimColorFrosted,
            _ => dark ? ShellRimColorDark : ShellRimColorLight,
        };
        WindowChrome.HideWindowBorder(_hwnd, dark, rim);
    }

    /// <summary>
    /// Rim colors for the shell, as COLORREFs (0x00BBGGRR) — these must track
    /// <c>ShellBackgroundBrush</c> in <c>Themes/Colors.xaml</c> (Logi's own #2B2B2B and #F2F2F2),
    /// not the shared black/white every other window's rim still uses. Leave one stale after a
    /// palette change and the window grows a visible hairline where the rim stops matching the
    /// surface behind it. All three are neutral greys, so channel order is moot.
    /// <see cref="ShellRimColorBlack"/> matches <see cref="BlackOverrides"/>' own page value rather
    /// than Dark's. <see cref="ShellRimColorFrosted"/> is the one that is not a neutral grey, so it
    /// is the one where the BGR order matters: it is <see cref="FrostedGlassBackdrop"/>'s own tint
    /// colour #101016, byte-reversed. The rim cannot be translucent the way that theme's surfaces
    /// are — it is a solid COLORREF the compositor draws — so it takes the colour the glass settles
    /// on rather than trying to disappear.
    /// </summary>
    private const int ShellRimColorDark = 0x002B2B2B;
    private const int ShellRimColorLight = 0x00F2F2F2;
    private const int ShellRimColorBlack = 0x00000000;
    private const int ShellRimColorFrosted = 0x00161010;

    private Style SecondaryCaptionStyle => (Style)Root.Resources["SecondaryCaption"];

    // ================= Screen router =================
    // Logi navigates a Frame between MainPage / ActionRingPage / SettingsPage. The five screens are
    // siblings in one Grid here and switch by Visibility instead — Logi's own ActionRingPage does
    // exactly this to swap its Layout view for its Explore AI view, and it is what lets the orbit
    // screen keep its live slot geometry, drag state and group expansion across a navigation
    // instead of rebuilding them every time.

    private const string ScreenHome = "home";
    private const string ScreenOrbit = "orbit";
    private const string ScreenTools = "tools";
    private const string ScreenElements = "elements";
    private const string ScreenSettings = "settings";

    private const string SectionGeneral = "general";
    private const string SectionFeedback = "feedback";
    private const string SectionPrivacy = "privacy";
    private const string SectionAbout = "about";

    private string _currentScreen = ScreenHome;
    private string _currentSection = SectionGeneral;

    /// <summary>
    /// Shows one of the five screens and hides the other four, re-running whatever that screen
    /// builds from live config. The per-screen side effects are the ones the old NavigationView's
    /// SelectionChanged used to carry, unchanged: the orbit canvas, the elements grid and the tool
    /// list are all built imperatively and would otherwise show whatever they last drew.
    /// </summary>
    private void ShowScreen(string screen)
    {
        _currentScreen = screen;
        HomeScreen.Visibility = screen == ScreenHome ? Visibility.Visible : Visibility.Collapsed;
        OrbitScreen.Visibility = screen == ScreenOrbit ? Visibility.Visible : Visibility.Collapsed;
        ToolsScreen.Visibility = screen == ScreenTools ? Visibility.Visible : Visibility.Collapsed;
        ElementsScreen.Visibility = screen == ScreenElements ? Visibility.Visible : Visibility.Collapsed;
        SettingsScreen.Visibility = screen == ScreenSettings ? Visibility.Visible : Visibility.Collapsed;

        if (screen == ScreenHome) UpdateHomeMeta();
        if (screen == ScreenOrbit)
        {
            // Editing is a mode you are in while arranging the orbit, not a preference — arriving
            // at the screen should always show what it is set to, not what you were last doing.
            SetOrbitEditing(false);
            RenderPreview();
        }
        if (screen == ScreenElements) RebuildItemsList();
        if (screen == ScreenTools) RebuildToolsList();
    }

    /// <summary>
    /// Shows one of Settings' four sections. Logi's own <c>SelectSection</c> writes twelve
    /// properties per call — section visibility, indicator visibility and label colour for each of
    /// the four — and so does this, for the same reason: selection is expressed by the 4px rail
    /// indicator <em>and</em> the label colour together, never by a background fill.
    /// <para>
    /// The active brush is <see cref="CurrentAccentColor"/>, so the rail follows a user's own
    /// accent pick rather than the built-in one; the inactive brush is Logi's
    /// <c>SettingsNavInactiveBrush</c> pair, written out here because a window that can be Light
    /// while Windows is Dark has no element-scoped way to read a theme dictionary back (the same
    /// reason <see cref="ThemeHelper"/> carries a second copy of the palette). Writing them onto
    /// the TextBlock is also why <see cref="ApplyAccentColorOverride"/> and the
    /// <c>ActualThemeChanged</c> handler both re-run this: a local brush is invisible to a later
    /// dictionary change.
    /// </para>
    /// </summary>
    private void SelectSection(string section)
    {
        _currentSection = section;

        GeneralSection.Visibility = section == SectionGeneral ? Visibility.Visible : Visibility.Collapsed;
        FeedbackSection.Visibility = section == SectionFeedback ? Visibility.Visible : Visibility.Collapsed;
        PrivacySection.Visibility = section == SectionPrivacy ? Visibility.Visible : Visibility.Collapsed;
        AboutSection.Visibility = section == SectionAbout ? Visibility.Visible : Visibility.Collapsed;

        NavGeneralIndicator.Visibility = section == SectionGeneral ? Visibility.Visible : Visibility.Collapsed;
        NavFeedbackIndicator.Visibility = section == SectionFeedback ? Visibility.Visible : Visibility.Collapsed;
        NavPrivacyIndicator.Visibility = section == SectionPrivacy ? Visibility.Visible : Visibility.Collapsed;
        NavAboutIndicator.Visibility = section == SectionAbout ? Visibility.Visible : Visibility.Collapsed;

        var active = new SolidColorBrush(CurrentAccentColor());
        var inactive = new SolidColorBrush(ThemeHelper.IsDark(Root)
            ? Color.FromArgb(255, 0xA0, 0xA0, 0xA0)
            : Color.FromArgb(255, 0x44, 0x44, 0x44));

        NavGeneralText.Foreground = section == SectionGeneral ? active : inactive;
        NavFeedbackText.Foreground = section == SectionFeedback ? active : inactive;
        NavPrivacyText.Foreground = section == SectionPrivacy ? active : inactive;
        NavAboutText.Foreground = section == SectionAbout ? active : inactive;

        // Each section is its own StackPanel in one Grid, so the ScrollViewer's offset is shared
        // between them — land on the top of the section that was just picked rather than partway
        // down it because the previous one happened to be scrolled.
        SettingsScroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    /// <summary>
    /// The counts on the Home nav boxes' meta pills, read from live state rather than written into
    /// the XAML — a hard-coded "4 tools" is exactly the kind of label that goes quietly stale the
    /// next time the catalog changes (the old Tools panel carried a "six fixed built-in tools"
    /// comment against a catalog of five).
    /// </summary>
    private void UpdateHomeMeta()
    {
        int used = _manager.Config.Primary.Items.Count(i => !i.Hidden);
        OrbitBoxMeta.Text = $"{OrbitMetrics.SlotCount} slots · {Math.Min(used, OrbitMetrics.UserSlotCount)} used";

        int tools = ToolCatalog.All.Count;
        ToolsBoxMeta.Text = tools == 1 ? "1 tool" : $"{tools} tools";
    }

    /// <summary>
    /// Paints the Home header's greeting for the current local time and keeps it there, so the line
    /// reads Morning / Afternoon / Evening according to the clock rather than according to whenever
    /// this window happened to be opened.
    /// <para>
    /// The timer is armed for the next boundary (<see cref="Greeting.TimeUntilNextChange"/>) and
    /// re-armed on each tick, rather than ticking on a fixed interval: the greeting can only change
    /// three times a day, so a one-minute poll would wake the dispatcher some fourteen hundred times
    /// to do nothing between each pair of them. <c>DispatcherTimer</c> is not exact — it fires on
    /// the UI queue, so a tick can land a beat late — which is why each tick re-reads the clock
    /// rather than assuming the boundary it was armed for is the one it arrived at, and why
    /// <see cref="Greeting.TimeUntilNextChange"/> floors the wait rather than trusting the interval
    /// to be positive.
    /// </para>
    /// </summary>
    private void StartGreetingClock()
    {
        StopGreetingClock();

        _greetingTimer = new DispatcherTimer();
        _greetingTimer.Tick += (_, _) =>
        {
            RefreshGreeting();
            // Re-armed from the clock as it now reads, not by leaving a fixed interval running: a
            // machine that slept through a boundary, or a user who moved the clock, lands here with
            // a different next boundary than the one this tick was armed for.
            if (_greetingTimer is not null)
                _greetingTimer.Interval = Greeting.TimeUntilNextChange(DateTime.Now);
        };
        _greetingTimer.Interval = Greeting.TimeUntilNextChange(DateTime.Now);
        _greetingTimer.Start();

        RefreshGreeting();
    }

    private void StopGreetingClock()
    {
        _greetingTimer?.Stop();
        _greetingTimer = null;
    }

    /// <summary>
    /// Writes the greeting for right now. Safe to call at any time — it reads the clock and the
    /// current string table and nothing else, so it is also what picks a new language up.
    /// </summary>
    private void RefreshGreeting()
    {
        if (GreetingText is null) return;
        var greeting = Greeting.Current();
        if (GreetingText.Text != greeting)
            GreetingText.Text = greeting;
    }

    private void OrbitBox_Click(object sender, RoutedEventArgs e) => ShowScreen(ScreenOrbit);

    private void ToolsBox_Click(object sender, RoutedEventArgs e) => ShowScreen(ScreenTools);

    private void ElementsBox_Click(object sender, RoutedEventArgs e) => ShowScreen(ScreenElements);

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowScreen(ScreenSettings);

    /// <summary>Every sub-screen's back arrow. There is one place to go back to — Home.</summary>
    private void Back_Click(object sender, RoutedEventArgs e) => ShowScreen(ScreenHome);

    private void NavGeneral_Click(object sender, RoutedEventArgs e) => SelectSection(SectionGeneral);

    private void NavFeedback_Click(object sender, RoutedEventArgs e) => SelectSection(SectionFeedback);

    private void NavPrivacy_Click(object sender, RoutedEventArgs e) => SelectSection(SectionPrivacy);

    private void NavAbout_Click(object sender, RoutedEventArgs e) => SelectSection(SectionAbout);

    /// <summary>
    /// Re-runs the first-run guide from Settings › General. Not gated on anything: running it again
    /// against an orbit that already exists is a supported way to start over, and the guide writes
    /// nothing unless the user presses Finish — so opening it is never destructive on its own.
    /// <para>
    /// <c>openShellAfter</c> stays false: this window is the thing the guide was opened from and is
    /// still sitting behind it. Whatever the guide commits arrives here through
    /// <see cref="OrbitManager.ItemsChanged"/> like any other edit.
    /// </para>
    /// </summary>
    private void RunOnboarding_Click(object sender, RoutedEventArgs e) => _manager.OpenOnboarding();

    /// <summary>
    /// Logi's header theme button cycles the app's themes in place rather than opening a picker.
    /// SwiftX has four where Logi has three, and the fourth — System — is the one that has to come
    /// last in the cycle: landing on it mid-cycle would make the next press's starting point depend
    /// on what Windows is set to.
    /// </summary>
    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var next = _manager.Config.Theme switch
        {
            AppTheme.Light => AppTheme.Dark,
            AppTheme.Dark => AppTheme.Black,
            AppTheme.Black => AppTheme.FrostedGlass,
            AppTheme.FrostedGlass => AppTheme.System,
            _ => AppTheme.Light,
        };
        ApplyThemeChoice(next);
    }

    /// <summary>
    /// The glyph on the header's theme button, which has to say which theme is <em>in effect</em>,
    /// not which one the next press picks. Logi's own three (sun / moon / glass) plus a settings-
    /// brightness mark for System, which is the one choice that has no fixed appearance of its own.
    /// </summary>
    private void UpdateThemeToggleIcon()
    {
        ThemeToggleIcon.Glyph = _manager.Config.Theme switch
        {
            AppTheme.Light => "\uE706",
            AppTheme.Dark => "\uE793",
            AppTheme.Black => "\uE708",
            // Segoe MDL2 has no frosted-glass mark. MapLayers is the nearest honest stand-in —
            // stacked translucent planes, which is what the theme actually is.
            AppTheme.FrostedGlass => "\uE81E",
            _ => "\uE7E8",
        };
    }

    /// <summary>
    /// The orbit preview's own canvas, square because <see cref="RenderQuadrantPreview"/> and
    /// <see cref="AddHotCornerButton"/> both place against <see cref="QuadrantLayout"/>, which
    /// works in one dimension.
    /// </summary>
    private const double PreviewCanvasSize = 470;

    /// <summary>
    /// The label canvas behind it is wider, because a pill reaches further out sideways than the
    /// ring ever does. The two are centred on each other (see ShellWindow.xaml), so converting a
    /// point from the preview's coordinates to the label canvas's is this one constant.
    /// </summary>
    private const double PillCanvasWidth = 640;
    private const double PillCanvasOffsetX = (PillCanvasWidth - PreviewCanvasSize) / 2;

    /// <summary>
    /// Logi's own label pill: 30 tall, 6px corners, 12,4 padding, 12px SemiBold. The gap out from
    /// the node's edge and the width cap are SwiftX's — Logi hand-places eight pills for eight
    /// fixed labels and can tune each one, while these are user-named items of any length placed
    /// by formula. 136 is the widest pill the mockup this screen is built to draws.
    /// </summary>
    private const double PillHeight = 30;
    private const double PillGap = 22;
    private const double PillMaxWidth = 136;

    /// <summary>
    /// The outward name pill for one filled slot, on <c>PreviewLabelCanvas</c> rather than the
    /// canvas the bubbles live on.
    /// <para>
    /// That separation is the whole reason none of the drag code had to learn about pills: the
    /// label canvas has hit-testing off and is never read by <see cref="PreviewSlotAt"/>, so a
    /// pill can overlap a node without ever becoming a drag target or swallowing a press. The
    /// cost is that pills do not follow a bubble mid-drag, which is why
    /// <see cref="Preview_PointerMoved"/> hides the whole canvas the moment a drag starts —
    /// labels pointing at bubbles that have moved out from under them would be worse than no
    /// labels at all, and the next <see cref="RenderPreview"/> brings them back.
    /// </para>
    /// <para>
    /// Placement follows Logi's own table rather than a single polar formula: the top and bottom
    /// slots take their pill above and below, centred on the node, and the other six take theirs
    /// out to the side, vertically centred on the node. A pure radial placement instead pulls the
    /// four diagonals in towards the ring, where they crowd their neighbours.
    /// </para>
    /// </summary>
    private void AddSlotLabel(
        string text, double cx, double cy, double angle, double radius, double bubble)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var pill = new Border
        {
            Height = PillHeight,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 4, 12, 4),
            Background = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitLabelPillBrush"),
            BorderBrush = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitLabelPillBorderBrush"),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush"),
            },
        };

        // A Canvas gives its children no alignment, so the pill has to be placed by a corner — and
        // the six side pills are placed by whichever corner faces the hub, which for the left-hand
        // three is the one a width has to be subtracted to reach. Measuring here rather than
        // letting it size itself is what makes that width knowable before it is in the tree, and
        // the measured value is then written back as an explicit Width so the rendered pill cannot
        // come out a pixel wider than what was placed. Trimming handles the overflow that caps.
        pill.Measure(new Size(PillMaxWidth, PillHeight));
        double width = Math.Min(PillMaxWidth, Math.Ceiling(pill.DesiredSize.Width));
        pill.Width = width;

        double ux = Math.Cos(angle), uy = Math.Sin(angle);
        double nodeCx = cx + radius * ux;
        double nodeCy = cy + radius * uy;
        double reach = bubble / 2 + PillGap;

        double px, py;
        // The two slots on the vertical axis. Compared against a small epsilon rather than zero
        // because the angles come out of a cosine, and cos(-90 degrees in radians) is not exactly 0.
        if (Math.Abs(ux) < 0.01)
        {
            px = nodeCx - width / 2;
            py = uy < 0 ? nodeCy - reach - PillHeight : nodeCy + reach;
        }
        else
        {
            py = nodeCy - PillHeight / 2;
            px = ux > 0 ? nodeCx + reach : nodeCx - reach - width;
        }

        Canvas.SetLeft(pill, Math.Clamp(px + PillCanvasOffsetX, 0, PillCanvasWidth - width));
        Canvas.SetTop(pill, Math.Clamp(py, 0, PreviewCanvasSize - PillHeight));
        PreviewLabelCanvas.Children.Add(pill);
    }

    private void RenderPreview()
    {
        if (PreviewCanvas is null) return;
        PreviewCanvas.Children.Clear();
        PreviewLabelCanvas.Children.Clear();
        // Back on for every render. Preview_PointerMoved is the only thing that takes them away,
        // and a render is what always follows the drop it was hiding them for.
        PreviewLabelCanvas.Visibility = Visibility.Visible;
        const double size = PreviewCanvasSize;
        // Dead centre now. The nudge that used to lift the ring was making room for the Add item /
        // Reset defaults row, which sat on the canvas itself; those live in the screen's own 60px
        // toolbar since the Logi layout landed, so the canvas is the ring's alone again.
        double cx = size / 2, cy = size / 2;

        if (_previewQuadrant && _manager.Config.HotCornerEnabled)
        {
            // The orbit's own slots are not on screen, so drop them: a press must not reach a
            // bubble that is no longer drawn. The two orbit-wide buttons go with them — they act on
            // the orbit, and a bottom corner's fan would run straight through where they sit.
            Array.Clear(_slotBorders);
            CanvasActions.Visibility = Visibility.Collapsed;
            RenderQuadrantPreview(size);
            AddHotCornerButton(size);
            return;
        }

        CanvasActions.Visibility = Visibility.Visible;

        // Scale the live orbit geometry into the preview canvas rather than drawing one fixed
        // picture of it, so Orbit size / Item size / Orbit radius visibly change what this shows.
        // 0.65 rather than the old 0.74 because the canvas grew: at SwiftX's default radius of 100
        // and medium bubbles this puts the ring at r=122, which is exactly the radius the mockup
        // this screen is built to draws (docs/images/17-app-ring.svg). The remaining 82px of
        // half-canvas is what the label pills reach into.
        double scale = size * 0.65 / (2 * (OrbitMetrics.Radius + OrbitMetrics.Bubble / 2));
        double radius = OrbitMetrics.Radius * scale;
        double bubble = OrbitMetrics.Bubble * scale;
        double glyphSize = Math.Max(10, OrbitMetrics.Glyph * scale);

        _previewCenter = new Point(cx, cy);
        _previewRadius = radius;
        _previewBubble = bubble;
        Array.Clear(_slotBorders);

        // Resolve against this window's theme — not Application.Current.Resources.
        var borderBrush = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitCanvasBorderBrush");
        var muted = ThemeHelper.Brush(Root, "SwiftXMutedBrush");
        var foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush");
        var canvasAccent = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitCanvasAccentBrush");

        // The guide circle the nodes sit on. Logi draws no such ring — its wheel is eight
        // hard-coded positions and the ring is purely implied — but Logi's is a fixed arrangement
        // and SwiftX's is not: radius is a setting here, and the circle is what makes a drag of the
        // Orbit radius slider legible as a circle growing rather than eight bubbles wandering.
        // Behind everything else, so a node always covers it rather than the other way round.
        var guide = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitCanvasBorderBrush"),
            StrokeThickness = 1,
        };
        Canvas.SetLeft(guide, cx - radius);
        Canvas.SetTop(guide, cy - radius);
        PreviewCanvas.Children.Add(guide);

        // Match the orbit: the center is the app mark alone, painted on nothing — floating form and
        // all, so the preview shows the same shadow the real hub has. A filled plate here read as a
        // big empty hole that does not exist on the real thing.
        double logo = OrbitMetrics.HubLogo * scale;
        var hub = AppLogo.CreateFloating(logo);
        Canvas.SetLeft(hub, cx - logo / 2);
        Canvas.SetTop(hub, cy - logo / 2);
        PreviewCanvas.Children.Add(hub);

        var items = _manager.Config.Primary.Items.Where(i => !i.Hidden).Take(OrbitMetrics.UserSlotCount).ToList();

        // A full orbit has no empty slot left to add into, so the button that would open the add
        // dialog goes away rather than sitting there offering something the orbit cannot take.
        // Reset defaults stays: it is still meaningful on a full orbit.
        AddItemButton.Visibility = items.Count >= OrbitMetrics.UserSlotCount
            ? Visibility.Collapsed
            : Visibility.Visible;

        for (int i = 0; i < OrbitMetrics.SlotCount; i++)
        {
            double angle = (-90 + i * 45) * Math.PI / 180.0;
            double x = cx + radius * Math.Cos(angle) - bubble / 2;
            double y = cy + radius * Math.Sin(angle) - bubble / 2;

            // The orbit synthesizes Settings and Close into the last two slots, so the preview does
            // the same rather than reading them out of the profile — they are not in it.
            bool pinned = i >= OrbitMetrics.UserSlotCount;
            OrbitItem? item = pinned
                ? (i == OrbitMetrics.UserSlotCount ? SystemOrbitItems.CreateSettings() : SystemOrbitItems.CreateClose())
                : i < items.Count ? items[i] : null;

            var border = new Border
            {
                Width = bubble,
                Height = bubble,
                CornerRadius = new CornerRadius(bubble / 2),
                Background = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitNodeBackgroundBrush"),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Tag = item,
            };

            if (pinned)
            {
                // Logi's own treatment for the one node it differentiates: the accent border at
                // 1.5px and an accent glyph. Here it marks the two the orbit synthesizes rather
                // than owns, which is the distinction that actually matters on this canvas — they
                // cannot be moved, replaced or deleted, and nothing else about them says so.
                border.Child = PreviewGlyph(item!.Glyph, glyphSize, canvasAccent);
                border.BorderBrush = canvasAccent;
                border.BorderThickness = new Thickness(1.5);
                ToolTipService.SetToolTip(border, $"{item.DisplayName} — always on the orbit");
            }
            else if (item is null)
            {
                border.Child = PreviewGlyph("\uE710", glyphSize, muted);
            }
            else
            {
                border.Child = PreviewGlyph(item.Glyph, glyphSize, foreground);
                ToolTipService.SetToolTip(border, $"{item.DisplayName} — right-click to replace or delete");
                _slotBorders[i] = border;
            }

            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);
            PreviewCanvas.Children.Add(border);

            // An empty slot gets no pill: "+" is its own label, and eight pills reading "Empty"
            // would be the loudest thing on the canvas.
            if (item is not null)
                AddSlotLabel(item.DisplayName, cx, cy, angle, radius, bubble);
        }

        AddHotCornerButton(size);
    }

    /// <summary>
    /// The quarter circle a hot corner set to <see cref="HotCornerLayout.Quadrant"/> summons,
    /// drawn to the same rules the real one uses (<see cref="QuadrantLayout"/>) and scaled into
    /// the canvas — centered on the canvas corner that matches the chosen screen corner, so the
    /// picture says which corner as much as what.
    /// <para>
    /// Editable, like the orbit view beside it: one more place than the corner holds is drawn (up
    /// to the four it can hold) and offers a +, any place can be dragged onto another to reorder,
    /// and right-clicking a filled one replaces or removes what is in it
    /// (<see cref="BuildQuadrantItemMenu"/>). The first such edit turns the corner's default pick
    /// into an arrangement of the user's own — see <see cref="HotCornerItems"/>.
    /// </para>
    /// <para>
    /// Nothing is drawn but the places themselves. The guide arc the items used to be strung
    /// along, and the remove badge each filled one used to wear, are both gone: the arc drew a
    /// line through a picture whose whole job is to show where the items sit, and the badges put a
    /// permanent piece of chrome on every item to carry one command that the context menu now
    /// holds alongside the other.
    /// </para>
    /// </summary>
    private void RenderQuadrantPreview(double size)
    {
        var corner = _manager.Config.HotCorner;
        var items = HotCornerItems.Resolve(_manager.Config, SystemOrbitItems.CreateSettings());

        double bubble = OrbitMetrics.Bubble;
        // The places the corner will actually use, plus one empty place to add into while there is
        // room for it. Laying out for what is really there is what keeps this a picture of the
        // corner rather than an approximation of it — a corner holding all four is drawn exactly
        // as the corner draws it.
        int slots = Math.Min(QuadrantLayout.ItemCount, items.Count + 1);
        double hoverBubble = bubble * OrbitMetrics.HoverScale;
        double radius = QuadrantLayout.Radius(slots, hoverBubble, OrbitMetrics.Radius);

        // Same scale the orbit preview uses, so switching between the two views changes the shape
        // and not the size of everything in it — only clamped smaller if a large orbit radius
        // would push the fan off the canvas.
        double scale = Math.Min(
            size * 0.74 / (2 * (OrbitMetrics.Radius + bubble / 2)),
            size * 0.96 / (radius + bubble / 2));
        double previewRadius = radius * scale;
        double previewBubble = bubble * scale;
        double glyphSize = Math.Max(10, OrbitMetrics.Glyph * scale);

        var (cx, cy) = QuadrantLayout.Center(corner, size, size);
        _quadrantCenter = new Point(cx, cy);
        _quadrantRadius = previewRadius;
        _quadrantBubble = previewBubble;
        _quadrantSlotCount = slots;
        Array.Clear(_quadrantSlotBorders);

        var borderBrush = ThemeHelper.Brush(Root, "SwiftXBorderBrush");
        var muted = ThemeHelper.Brush(Root, "SwiftXMutedBrush");
        var foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush");

        for (int i = 0; i < slots; i++)
        {
            double angle = QuadrantLayout.ScreenAngleRad(
                corner, QuadrantLayout.LocalAngleRad(i, slots, hoverBubble, radius));
            _quadrantSlotAngles[i] = angle;

            double left = cx + previewRadius * Math.Cos(angle) - previewBubble / 2;
            double top = cy + previewRadius * Math.Sin(angle) - previewBubble / 2;
            var item = i < items.Count ? items[i] : null;

            var border = new Border
            {
                Width = previewBubble,
                Height = previewBubble,
                CornerRadius = new CornerRadius(previewBubble / 2),
                Background = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitBubbleGlassTint"),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Tag = item,
                Child = PreviewGlyph(
                    item?.Glyph ?? "\uE710", glyphSize, item is null || item.IsSystem ? muted : foreground),
            };
            ToolTipService.SetToolTip(border, item is null
                ? "Add to this corner"
                : $"{item.DisplayName} — right-click to replace or remove");
            Canvas.SetLeft(border, left);
            Canvas.SetTop(border, top);
            PreviewCanvas.Children.Add(border);
            _quadrantSlotBorders[i] = border;
        }
    }

    /// <summary>
    /// The corner button: present on the canvas whenever the hot corner is on, sitting in the
    /// canvas corner that matches the chosen screen one, and switching the canvas between the
    /// orbit and what that corner would actually show.
    /// <para>
    /// The icon is the same corner-pointing triangle as the Hot corner row above (see
    /// <see cref="CornerTriangleGeometry"/>/<see cref="CornerTriangleAngle"/>), always rotated to
    /// the chosen corner regardless of which of the two states below the button is currently in —
    /// this button sits physically in that corner of the canvas, so pointing at it is what marks
    /// the button as "this corner" rather than any other one on the canvas. The two states (orbit
    /// vs. quadrant preview) differ only in tooltip and automation name now; they used to swap in
    /// a generic resize/back glyph that said nothing about which corner it was.
    /// </para>
    /// </summary>
    private void AddHotCornerButton(double size)
    {
        if (!_manager.Config.HotCornerEnabled)
            return;

        const double diameter = 34;
        const double inset = 2;
        var corner = _manager.Config.HotCorner;

        var button = new Button
        {
            Width = diameter,
            Height = diameter,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(diameter / 2),
            Content = new PathIcon
            {
                Data = CornerTriangleGeometry(),
                Width = 14,
                Height = 14,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform { Angle = CornerTriangleAngle(corner) },
            },
        };
        // No resting chrome: the triangle marks the corner on its own, and a filled plate around
        // it read as one more bubble sitting in the orbit rather than as the canvas's own corner.
        // Shadowing the two keys the Button style resolves at rest (its Style sets Background and
        // BorderBrush from ButtonBackground/ButtonBorderBrush) rather than assigning
        // button.Background directly, so the change lands at the same layer the framework sets it
        // and touches nothing else: the pointer-over and pressed states drive
        // ContentPresenter.Background from ButtonBackgroundPointerOver/Pressed, which are left
        // alone here, so the full 34px disc still lights up under the pointer and keeps reading as
        // a button. Only the resting fill goes away.
        var transparent = new SolidColorBrush(Colors.Transparent);
        button.Resources["ButtonBackground"] = transparent;
        button.Resources["ButtonBorderBrush"] = transparent;

        ToolTipService.SetToolTip(button, _previewQuadrant
            ? "Back to the full orbit"
            : "Show what this corner opens");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Hot corner preview");
        button.Click += (_, _) =>
        {
            _previewQuadrant = !_previewQuadrant;
            RenderPreview();
        };

        var (cx, cy) = QuadrantLayout.Center(corner, size, size);
        Canvas.SetLeft(button, Math.Clamp(cx - diameter / 2, inset, size - diameter - inset));
        Canvas.SetTop(button, Math.Clamp(cy - diameter / 2, inset, size - diameter - inset));
        PreviewCanvas.Children.Add(button);
    }

    /// <summary>
    /// Preview press/drag/release, handled once on the canvas instead of per bubble.
    /// <para>
    /// Every bubble used to capture the pointer itself and carry its own Tapped handler. That is
    /// why repositioning never worked: the per-item capture and the tap gesture competed on the
    /// same element, and a drop never reached <see cref="ReorderItem"/>. One capture on the
    /// canvas, with the slot derived from geometry, settles tap and drag in the same place.
    /// </para>
    /// </summary>
    private void Preview_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var current = e.GetCurrentPoint(PreviewCanvas);

        // Left button only — which a touch contact and a pen tip both report as. A right press
        // belongs to the context menu (see Preview_RightTapped); picking a bubble up on it would
        // leave the canvas holding a drag, and the release that ends it would read as a plain
        // click and open the inspector behind the menu.
        if (!current.Properties.IsLeftButtonPressed)
        {
            EndPreviewDrag();
            return;
        }

        var point = current.Position;
        _dragSlot = _previewQuadrant ? QuadrantSlotAt(point) : PreviewSlotAt(point);
        _dragBorder = _dragSlot >= 0
            ? (_previewQuadrant ? _quadrantSlotBorders[_dragSlot] : _slotBorders[_dragSlot])
            : null;
        _dragItem = _dragBorder?.Tag as OrbitItem;
        _dragStart = point;
        _dragMoved = false;
        // Starts equal to the origin slot, not -1: until the pointer actually crosses into a
        // different target, "tentative" is just "still where it started", so a drop with no real
        // movement resolves as a no-op the same way it always did.
        _dragTentativeSlot = _dragSlot;
        _dragTentativeQuadrantSlot = _dragSlot;
        if (_dragSlot >= 0)
            PreviewCanvas.CapturePointer(e.Pointer);
    }

    private void Preview_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragBorder is null) return;

        var point = e.GetCurrentPoint(PreviewCanvas).Position;
        double dx = point.X - _dragStart.X;
        double dy = point.Y - _dragStart.Y;
        if (!_dragMoved && (Math.Abs(dx) > 4 || Math.Abs(dy) > 4))
        {
            _dragMoved = true;
            // The pills live on their own canvas and do not follow a bubble (see AddSlotLabel for
            // why that separation is what keeps the drag code free of them). A label still sitting
            // where a bubble no longer is reads as a bug, so the whole layer goes for the duration;
            // RenderPreview puts it back on the drop.
            PreviewLabelCanvas.Visibility = Visibility.Collapsed;
        }
        if (_dragMoved)
        {
            _dragBorder.RenderTransform = new TranslateTransform { X = dx, Y = dy };
            UpdateDragReflow(point);
        }
    }

    /// <summary>
    /// The live "make room" preview: while a bubble is mid-drag, every other bubble shifts to
    /// where it would actually land if the pointer released right now, instead of staying frozen
    /// until the drop. Only recomputes when the tentative target slot actually changes, so a
    /// pointer held still costs nothing on every move tick.
    /// <para>
    /// Simulates the same remove-then-insert <see cref="ReorderItem"/> / <see cref="MoveQuadrantItem"/>
    /// perform, but against a throwaway copy of the order (<c>_manager.Config.Primary.Items</c>
    /// filtered/copied the same way <see cref="ReorderItem"/> does, or a fresh
    /// <see cref="HotCornerItems.Resolve"/> list for the quadrant — never <see cref="HotCornerItems.Edit"/>,
    /// which has the side effect of turning a corner that was still following the orbit into a
    /// stored arrangement the moment it's called) — nothing is persisted until the actual drop.
    /// </para>
    /// <para>
    /// <see cref="Preview_PointerReleased"/> reuses <see cref="_dragTentativeSlot"/> /
    /// <see cref="_dragTentativeQuadrantSlot"/> directly rather than recomputing a target from the
    /// release point, so the committed order can never land somewhere different from what this
    /// last showed on screen.
    /// </para>
    /// </summary>
    private void UpdateDragReflow(Point point)
    {
        if (_dragItem is null) return;

        if (_previewQuadrant)
        {
            int target = QuadrantSlotAt(point, requireHit: false);
            if (target == _dragTentativeQuadrantSlot) return;
            _dragTentativeQuadrantSlot = target;
            if (target < 0) return;

            var list = HotCornerItems.Resolve(_manager.Config, SystemOrbitItems.CreateSettings());
            int from = list.IndexOf(_dragItem);
            if (from < 0) return;
            int to = Math.Min(target, list.Count - 1);
            if (to != from)
            {
                list.RemoveAt(from);
                list.Insert(to, _dragItem);
            }
            ReflowQuadrantBorders(list);
            return;
        }

        int targetSlot = PreviewSlotAt(point, requireHit: false);
        if (targetSlot == _dragTentativeSlot) return;
        _dragTentativeSlot = targetSlot;
        if (targetSlot < 0 || targetSlot >= OrbitMetrics.UserSlotCount) return;

        var visible = _manager.Config.Primary.Items.Where(i => !i.Hidden).Take(OrbitMetrics.UserSlotCount).ToList();
        int vFrom = visible.IndexOf(_dragItem);
        if (vFrom < 0) return;
        int vTo = Math.Min(targetSlot, visible.Count - 1);
        if (vTo != vFrom)
        {
            visible.RemoveAt(vFrom);
            visible.Insert(vTo, _dragItem);
        }
        ReflowOrbitBorders(visible);
    }

    /// <summary>
    /// Glides every bubble in <paramref name="tentativeOrder"/> other than the one actually being
    /// dragged (that one follows the pointer via <c>RenderTransform</c> instead) to the orbit
    /// position its index in that order implies. Pinned slots (Settings/Close) are never part of
    /// <paramref name="tentativeOrder"/> to begin with — it is built the same way
    /// <see cref="ReorderItem"/>'s own <c>visible</c> list is — so they can never be moved here.
    /// </summary>
    private void ReflowOrbitBorders(List<OrbitItem> tentativeOrder)
    {
        for (int i = 0; i < tentativeOrder.Count; i++)
        {
            var item = tentativeOrder[i];
            if (ReferenceEquals(item, _dragItem)) continue;
            var border = FindSlotBorder(_slotBorders, item);
            if (border is null) continue;
            double angle = OrbitMetrics.SlotAngleRad(i);
            double x = _previewCenter.X + _previewRadius * Math.Cos(angle) - _previewBubble / 2;
            double y = _previewCenter.Y + _previewRadius * Math.Sin(angle) - _previewBubble / 2;
            AnimateBubbleTo(border, x, y);
        }
    }

    /// <summary>Quadrant counterpart of <see cref="ReflowOrbitBorders"/>.</summary>
    private void ReflowQuadrantBorders(List<OrbitItem> tentativeOrder)
    {
        for (int i = 0; i < tentativeOrder.Count && i < _quadrantSlotBorders.Length; i++)
        {
            var item = tentativeOrder[i];
            if (ReferenceEquals(item, _dragItem)) continue;
            var border = FindSlotBorder(_quadrantSlotBorders, item);
            if (border is null) continue;
            double angle = _quadrantSlotAngles[i];
            double x = _quadrantCenter.X + _quadrantRadius * Math.Cos(angle) - _quadrantBubble / 2;
            double y = _quadrantCenter.Y + _quadrantRadius * Math.Sin(angle) - _quadrantBubble / 2;
            AnimateBubbleTo(border, x, y);
        }
    }

    private static Border? FindSlotBorder(Border?[] borders, OrbitItem item)
    {
        foreach (var border in borders)
        {
            if (border?.Tag is OrbitItem tagged && ReferenceEquals(tagged, item))
                return border;
        }
        return null;
    }

    /// <summary>
    /// Short glide of a preview bubble to a new canvas position for the live drag reflow — distinct
    /// from the dragged bubble's own pointer-following <c>RenderTransform</c> in
    /// <see cref="Preview_PointerMoved"/>. Falls back to an instant reposition if animating the
    /// attached properties fails for any reason: landing the other bubbles in the right place is
    /// what actually matters, the glide is just polish on top of that.
    /// </summary>
    private static void AnimateBubbleTo(Border border, double x, double y)
    {
        try
        {
            var duration = TimeSpan.FromMilliseconds(160);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            var animX = new DoubleAnimation { To = x, Duration = duration, EasingFunction = ease };
            Storyboard.SetTarget(animX, border);
            Storyboard.SetTargetProperty(animX, "(Canvas.Left)");

            var animY = new DoubleAnimation { To = y, Duration = duration, EasingFunction = ease };
            Storyboard.SetTarget(animY, border);
            Storyboard.SetTargetProperty(animY, "(Canvas.Top)");

            var storyboard = new Storyboard();
            storyboard.Children.Add(animX);
            storyboard.Children.Add(animY);
            storyboard.Begin();
        }
        catch
        {
            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);
        }
    }

    private void Preview_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        int slot = _dragSlot;
        var item = _dragItem;
        bool moved = _dragMoved;
        // Captured before EndPreviewDrag resets them to -1. Reusing these instead of recomputing a
        // target from the release point is what guarantees the committed order matches whatever
        // UpdateDragReflow last showed on screen.
        int tentativeSlot = _dragTentativeSlot;
        int tentativeQuadrantSlot = _dragTentativeQuadrantSlot;

        PreviewCanvas.ReleasePointerCapture(e.Pointer);
        EndPreviewDrag();

        if (slot < 0)
            return;

        if (_previewQuadrant)
        {
            if (moved)
            {
                // A drag out of an empty place is not an add — it is a drag of nothing.
                if (item is not null)
                    MoveQuadrantItem(slot, tentativeQuadrantSlot);
            }
            else if (item is not null)
            {
                ShowItemInfo(item);
            }
            else
            {
                AddQuadrantItem();
            }
            return;
        }

        if (moved)
        {
            if (item is not null)
                ReorderItem(item, tentativeSlot);
            return;
        }

        if (item is not null)
        {
            if (item.IsGroup)
                ToggleGroup(item);
            else
                ShowItemInfo(item);
        }
        else if (slot < OrbitMetrics.UserSlotCount)
        {
            _manager.OpenAddItem(added =>
            {
                _manager.Config.Primary.Items.Add(added);
                _manager.NotifyItemsChanged();
                RenderPreview();
            }, _manager.Config.Primary.Items);
        }
    }

    /// <summary>
    /// The dock's own edit menu: right-clicking an item on either view offers to swap what is in
    /// that place or take it out of it, so the two things you can do to an item you can already
    /// see are done where you see it, rather than only on the Elements screen (the orbit) or through a
    /// badge pinned to every bubble (the corner, which no longer has one).
    /// </summary>
    private void Preview_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var point = e.GetPosition(PreviewCanvas);
        var menu = _previewQuadrant ? BuildQuadrantItemMenu(point) : BuildOrbitItemMenu(point);
        if (menu is null)
            return;

        e.Handled = true;
        menu.ShowAt(PreviewCanvas, new FlyoutShowOptions { Position = point });
    }

    /// <summary>
    /// The menu for an orbit slot, or null when the point is not on an item the user owns — an
    /// empty slot is filled by clicking it, and Settings/Close are the orbit's own and cannot be
    /// replaced or removed at all.
    /// </summary>
    private MenuFlyout? BuildOrbitItemMenu(Point point)
    {
        int slot = PreviewSlotAt(point);
        if (slot < 0 || slot >= OrbitMetrics.UserSlotCount)
            return null;

        // Only a filled user slot has a border recorded here — RenderPreview leaves the pinned and
        // empty ones out — so this doubles as the "is this actually editable" check.
        if (_slotBorders[slot]?.Tag is not OrbitItem item)
            return null;

        // The built-in tools are the Tools screen's, exactly as on the Elements screen, which leaves
        // them out of its own list for the same reason: deleting the Tools group here would only
        // have it rebuilt on the next start (ToolCatalog.EnsureToolsGroup), and a tool is taken off
        // the orbit by switching it off there, not by deleting it.
        if (IsToolsOwned(item))
            return null;

        return BuildItemMenu(
            item.DisplayName,
            replace: () => ReplaceOrbitItem(item),
            remove: () => RemoveOrbitItem(item),
            removeLabel: "Delete",
            canRemove: true);
    }

    /// <summary>
    /// Quadrant counterpart of <see cref="BuildOrbitItemMenu"/>. The Tools group is editable here
    /// where it is not on the orbit: taking it off the corner is a change to the corner's own
    /// arrangement, and leaves the orbit's copy exactly where it was.
    /// </summary>
    private MenuFlyout? BuildQuadrantItemMenu(Point point)
    {
        int slot = QuadrantSlotAt(point);
        if (slot < 0 || _quadrantSlotBorders[slot]?.Tag is not OrbitItem item)
            return null;

        // The last item stays: a corner with nothing on it would summon an empty screen. Shown
        // greyed rather than left out, so the menu is the same shape whichever item it opens on.
        int count = HotCornerItems.Resolve(_manager.Config, SystemOrbitItems.CreateSettings()).Count;

        return BuildItemMenu(
            item.DisplayName,
            replace: () => ReplaceQuadrantItem(slot),
            remove: () => RemoveQuadrantItem(slot),
            removeLabel: "Remove from corner",
            canRemove: count > 1);
    }

    private static MenuFlyout BuildItemMenu(
        string name, Action replace, Action remove, string removeLabel, bool canRemove)
    {
        var menu = new MenuFlyout();

        var replaceItem = new MenuFlyoutItem
        {
            Text = "Replace…",
            Icon = new FontIcon { Glyph = char.ConvertFromUtf32(0xE70F) },
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(replaceItem, "Replace " + name);
        replaceItem.Click += (_, _) => replace();
        menu.Items.Add(replaceItem);

        var removeItem = new MenuFlyoutItem
        {
            Text = removeLabel,
            Icon = new FontIcon { Glyph = char.ConvertFromUtf32(0xE74D) },
            IsEnabled = canRemove,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(removeItem, removeLabel + " " + name);
        removeItem.Click += (_, _) => remove();
        menu.Items.Add(removeItem);

        return menu;
    }

    /// <summary>Whether an item on the orbit belongs to the Tools screen rather than to the user —
    /// the same two cases <see cref="RebuildItemsList"/> leaves out of the Elements list.</summary>
    private static bool IsToolsOwned(OrbitItem item) =>
        item.IsTool ||
        (item.IsGroup && string.Equals(
            item.Target, ToolCatalog.ToolsGroupTarget, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Swaps one orbit item for a newly added one in the same place, rather than dropping the old
    /// one and appending the new one at the end — replacing is a change of what is in a slot, not
    /// a rearrangement of the orbit. The position is re-read inside the callback because the dialog
    /// is modeless: the list can have moved on by the time it comes back.
    /// <para>
    /// Replacing a group takes its children with it, the same way deleting one does on the
    /// Elements screen — they exist only inside it.
    /// </para>
    /// </summary>
    private void ReplaceOrbitItem(OrbitItem item)
    {
        var items = _manager.Config.Primary.Items;
        _manager.OpenAddItem(
            added =>
            {
                int at = items.IndexOf(item);
                if (at < 0)
                    items.Add(added);
                else
                    items[at] = added;

                if (ReferenceEquals(_expandedGroup, item)) CollapseGroup();
                if (ReferenceEquals(_selectedItem, item)) HideInfo();
                _manager.NotifyItemsChanged();
                RenderPreview();
                RebuildItemsList();
            },
            items,
            heading: "Replace " + item.DisplayName,
            confirmLabel: "Replace");
    }

    /// <summary>Takes one item off the orbit — the same delete the Elements screen's own rows do.</summary>
    private void RemoveOrbitItem(OrbitItem item)
    {
        if (!_manager.Config.Primary.Items.Remove(item))
            return;

        if (ReferenceEquals(_expandedGroup, item)) CollapseGroup();
        if (ReferenceEquals(_selectedItem, item)) HideInfo();
        _manager.NotifyItemsChanged();
        RenderPreview();
        RebuildItemsList();
    }

    /// <summary>
    /// Quadrant counterpart of <see cref="ReplaceOrbitItem"/>. Writes into the corner's own stored
    /// arrangement and nowhere else — the orbit keeps whatever it had, exactly as
    /// <see cref="RemoveQuadrantItem"/> and <see cref="AddQuadrantItem"/> already do.
    /// </summary>
    private void ReplaceQuadrantItem(int slot)
    {
        _manager.OpenAddItem(
            added =>
            {
                var list = HotCornerItems.Edit(_manager.Config, SystemOrbitItems.CreateSettings());
                if (slot < 0 || slot >= list.Count)
                    return;

                list[slot] = added;
                _manager.Save();
                HideInfo();
                RenderPreview();
            },
            _manager.Config.HotCornerOrbit,
            heading: "Replace on this corner",
            confirmLabel: "Replace");
    }

    private void EndPreviewDrag()
    {
        if (_dragBorder is not null)
            _dragBorder.RenderTransform = null;
        _dragBorder = null;
        _dragItem = null;
        _dragSlot = -1;
        _dragMoved = false;
        _dragTentativeSlot = -1;
        _dragTentativeQuadrantSlot = -1;
    }

    /// <summary>
    /// Which preview slot a canvas point is in, or -1. With <paramref name="requireHit"/> the
    /// point must actually be on that slot's bubble, so a press in the empty middle or out past
    /// the orbit doesn't pick one up.
    /// </summary>
    /// <summary>
    /// Which of the corner's four places <paramref name="point"/> is in — by proximity to the
    /// slot centers rather than by angle, so a drop lands where the pointer visibly is. Without
    /// <paramref name="requireHit"/> the nearest place is returned however far away the pointer
    /// is, which is what a drop wants: the intent is the nearest slot, not a precise landing.
    /// </summary>
    private int QuadrantSlotAt(Point point, bool requireHit = true)
    {
        int best = -1;
        double bestGap = double.MaxValue;
        for (int i = 0; i < _quadrantSlotCount; i++)
        {
            double x = _quadrantCenter.X + _quadrantRadius * Math.Cos(_quadrantSlotAngles[i]);
            double y = _quadrantCenter.Y + _quadrantRadius * Math.Sin(_quadrantSlotAngles[i]);
            double gap = Math.Sqrt((point.X - x) * (point.X - x) + (point.Y - y) * (point.Y - y));
            if (gap < bestGap)
            {
                bestGap = gap;
                best = i;
            }
        }

        if (!requireHit)
            return best;
        return bestGap <= _quadrantBubble / 2 + 4 ? best : -1;
    }

    /// <summary>Drags one of the corner's items into another of its places.</summary>
    private void MoveQuadrantItem(int from, int to)
    {
        var list = HotCornerItems.Edit(_manager.Config, SystemOrbitItems.CreateSettings());
        if (from < 0 || from >= list.Count || to < 0)
            return;

        // Dropping past the last item means "put it at the end", not "into an empty place".
        to = Math.Min(to, list.Count - 1);
        if (from == to)
        {
            RenderPreview();
            return;
        }

        var entry = list[from];
        list.RemoveAt(from);
        list.Insert(to, entry);
        _manager.Save();
        RenderPreview();
    }

    /// <summary>Takes one item off the corner. The orbit itself keeps it.</summary>
    private void RemoveQuadrantItem(int slot)
    {
        var list = HotCornerItems.Edit(_manager.Config, SystemOrbitItems.CreateSettings());
        if (slot < 0 || slot >= list.Count || list.Count <= 1)
            return;

        list.RemoveAt(slot);
        _manager.Save();
        HideInfo();
        RenderPreview();
    }

    /// <summary>
    /// Fills an empty place on the corner. The item belongs to the corner rather than to the orbit
    /// — this is the corner's own arrangement — so it goes into that list and nowhere else.
    /// </summary>
    private void AddQuadrantItem()
    {
        var settings = SystemOrbitItems.CreateSettings();
        if (HotCornerItems.Resolve(_manager.Config, settings).Count >= QuadrantLayout.ItemCount)
            return;

        _manager.OpenAddItem(added =>
        {
            var list = HotCornerItems.Edit(_manager.Config, SystemOrbitItems.CreateSettings());
            if (list.Count >= QuadrantLayout.ItemCount)
                return;
            list.Add(added);
            _manager.Save();
            RenderPreview();
        }, _manager.Config.HotCornerOrbit);
    }

    private int PreviewSlotAt(Point point, bool requireHit = true)
    {
        double dx = point.X - _previewCenter.X;
        double dy = point.Y - _previewCenter.Y;
        int slot = OrbitMetrics.NearestSlotIndex(dx, dy);
        if (!requireHit)
            return slot;

        double angle = OrbitMetrics.SlotAngleRad(slot);
        double sx = _previewRadius * Math.Cos(angle);
        double sy = _previewRadius * Math.Sin(angle);
        double gap = Math.Sqrt((dx - sx) * (dx - sx) + (dy - sy) * (dy - sy));
        return gap <= _previewBubble / 2 + 4 ? slot : -1;
    }

    /// <summary>
    /// A preview slot's glyph. Mirrors the orbit's own handling of non-Fluent glyphs such as
    /// "&lt;/&gt;", which render as tofu boxes in the icon font and need a text family instead.
    /// </summary>
    private static FontIcon PreviewGlyph(string glyph, double fontSize, Brush foreground)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = fontSize,
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (GlyphFonts.IsTextGlyph(glyph))
            icon.FontFamily = new FontFamily("Segoe UI");
        return icon;
    }

    private void ReorderItem(OrbitItem dragged, int targetSlot)
    {
        // Slots past the user range hold the pinned Settings/Close bubbles; a drop there is
        // ignored rather than clamped, so the item stays where the user last put it.
        if (targetSlot < 0 || targetSlot >= OrbitMetrics.UserSlotCount) return;

        var visible = _manager.Config.Primary.Items.Where(i => !i.Hidden).Take(OrbitMetrics.UserSlotCount).ToList();
        int from = visible.IndexOf(dragged);
        if (from < 0) return;

        // Clamp onto the last occupied slot: dropping past the end of a part-filled orbit means
        // "put it last", not "leave a hole".
        int to = Math.Min(targetSlot, visible.Count - 1);
        if (to == from) return;

        // Move, don't insert-before-the-occupant. That older form removed the item first and so
        // landed a forward drag one slot short of where it was dropped — dragging an item to the
        // right appeared to do nothing at all.
        visible.RemoveAt(from);
        visible.Insert(to, dragged);

        // Write the new order back over the positions the visible items already occupy, so
        // hidden entries keep their place in the list instead of being shuffled by an index
        // computed from a filtered view of it.
        var items = _manager.Config.Primary.Items;
        var positions = new List<int>();
        for (int i = 0; i < items.Count && positions.Count < visible.Count; i++)
        {
            if (!items[i].Hidden)
                positions.Add(i);
        }
        for (int i = 0; i < positions.Count; i++)
            items[positions[i]] = visible[i];

        _manager.NotifyItemsChanged();
        RenderPreview();
        RebuildItemsList();
    }

    /// <summary>
    /// Shows <paramref name="item"/>'s name/target in the read-only <c>InfoBox</c>. Re-clicking the
    /// item already shown hides it again — the simplest "close" affordance that doesn't need a
    /// dedicated button. This is display-only: real edits happen on the Elements screen.
    /// <para>
    /// A click on a plain main-orbit item is unrelated to whatever group row is currently expanded
    /// (if any), so that row is collapsed first — otherwise it kept sitting there showing a group
    /// that has nothing to do with the newly-selected item. A click on a child <em>inside</em> the
    /// expanded group's own row is the one case that should leave the row open alongside the info
    /// box, since the row is exactly what the user clicked into.
    /// </para>
    /// </summary>
    private void ShowItemInfo(OrbitItem item)
    {
        if (ReferenceEquals(_selectedItem, item) && InfoBox.Visibility == Visibility.Visible)
        {
            HideInfo();
            return;
        }
        if (!IsChildOfExpandedGroup(item))
            CollapseGroup();
        _selectedItem = item;
        InfoName.Text = item.DisplayName;
        InfoTarget.Text = string.IsNullOrEmpty(item.Target) ? "—" : item.Target;
        InfoBox.Visibility = Visibility.Visible;
    }

    private void HideInfo()
    {
        _selectedItem = null;
        InfoBox.Visibility = Visibility.Collapsed;
    }

    private bool IsChildOfExpandedGroup(OrbitItem item) =>
        _expandedGroup is not null && _expandedGroup.Children.Any(c => ReferenceEquals(c, item));

    /// <summary>
    /// Expands (or, on a second click of the same group, collapses) <c>GroupRow</c> — the
    /// horizontal children strip that replaces the old full-orbit drill-down.
    /// <para>
    /// Switching to a different group closes whatever was open first (via <see cref="CollapseGroup"/>)
    /// rather than just overwriting <c>_expandedGroup</c> in place, and any info box left showing
    /// from before is cleared too — both would otherwise linger, showing state for an item or
    /// group the newly-opened row has nothing to do with.
    /// </para>
    /// </summary>
    private void ToggleGroup(OrbitItem group)
    {
        if (ReferenceEquals(_expandedGroup, group))
        {
            CollapseGroup();
            return;
        }
        CollapseGroup();
        HideInfo();
        _expandedGroup = group;
        GroupRowTitle.Text = group.DisplayName;
        GroupRow.Visibility = Visibility.Visible;
        RenderGroupRow();
    }

    /// <summary>Also clears <c>InfoBox</c> when it's showing one of this group's own children —
    /// otherwise closing the row left a child's name/target on screen with no row left to have
    /// come from.</summary>
    private void CollapseGroup()
    {
        if (_selectedItem is not null && IsChildOfExpandedGroup(_selectedItem))
            HideInfo();
        _expandedGroup = null;
        GroupRow.Visibility = Visibility.Collapsed;
        GroupChildrenCanvas.Children.Clear();
        _groupSlotBorders.Clear();
    }

    /// <summary>
    /// Draws <see cref="_expandedGroup"/>'s children as a left-to-right strip of small bubbles,
    /// mirroring <see cref="RenderPreview"/>'s bubble styling (same brushes, same <see cref="PreviewGlyph"/>
    /// helper) at a smaller size. Hidden children are skipped, same as the main orbit.
    /// </summary>
    private void RenderGroupRow()
    {
        GroupChildrenCanvas.Children.Clear();
        _groupSlotBorders.Clear();
        if (_expandedGroup is null) return;

        var borderBrush = ThemeHelper.Brush(Root, "SwiftXBorderBrush");
        var foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush");
        double glyphSize = Math.Max(10, GroupChildBubble * 0.36);

        var children = _expandedGroup.Children.Where(c => !c.Hidden).ToList();
        GroupChildrenCanvas.Width = children.Count == 0
            ? GroupChildBubble
            : children.Count * GroupChildBubble + (children.Count - 1) * GroupChildGap;
        GroupChildrenCanvas.Height = GroupChildBubble;

        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            double x = i * (GroupChildBubble + GroupChildGap);

            var border = new Border
            {
                Width = GroupChildBubble,
                Height = GroupChildBubble,
                CornerRadius = new CornerRadius(GroupChildBubble / 2),
                Background = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitBubbleGlassTint"),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Tag = child,
                Child = PreviewGlyph(child.Glyph, glyphSize, foreground),
            };
            ToolTipService.SetToolTip(border, child.DisplayName);

            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, 0);
            GroupChildrenCanvas.Children.Add(border);
            _groupSlotBorders.Add(border);
        }
    }

    /// <summary>
    /// Press/drag/release for the group children row, the same one-capture-on-the-canvas pattern
    /// as <see cref="Preview_PointerPressed"/>/<see cref="Preview_PointerMoved"/>/
    /// <see cref="Preview_PointerReleased"/>, just projected onto a single horizontal axis instead
    /// of the orbit's circular one.
    /// </summary>
    private void GroupRow_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(GroupChildrenCanvas).Position;
        _groupDragIndex = GroupChildIndexAt(point);
        _groupDragBorder = _groupDragIndex >= 0 ? _groupSlotBorders[_groupDragIndex] : null;
        _groupDragItem = _groupDragBorder?.Tag as OrbitItem;
        _groupDragStart = point;
        _groupDragMoved = false;
        if (_groupDragIndex >= 0)
            GroupChildrenCanvas.CapturePointer(e.Pointer);
    }

    private void GroupRow_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_groupDragBorder is null) return;

        var point = e.GetCurrentPoint(GroupChildrenCanvas).Position;
        double dx = point.X - _groupDragStart.X;
        if (!_groupDragMoved && Math.Abs(dx) > 4)
            _groupDragMoved = true;
        if (_groupDragMoved)
            _groupDragBorder.RenderTransform = new TranslateTransform { X = dx, Y = 0 };
    }

    private void GroupRow_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(GroupChildrenCanvas).Position;
        int index = _groupDragIndex;
        var item = _groupDragItem;
        bool moved = _groupDragMoved;

        GroupChildrenCanvas.ReleasePointerCapture(e.Pointer);
        EndGroupDrag();

        if (index < 0)
            return;

        if (moved)
        {
            if (item is not null)
                ReorderGroupChild(item, GroupChildIndexAt(point, requireHit: false));
            return;
        }

        if (item is not null)
            ShowItemInfo(item);
    }

    private void EndGroupDrag()
    {
        if (_groupDragBorder is not null)
            _groupDragBorder.RenderTransform = null;
        _groupDragBorder = null;
        _groupDragItem = null;
        _groupDragIndex = -1;
        _groupDragMoved = false;
    }

    /// <summary>
    /// Which child bubble in the row a canvas point is in, or -1. Mirrors
    /// <see cref="PreviewSlotAt"/>: with <paramref name="requireHit"/> the point must actually land
    /// on that bubble, so a drop past the last child (or before the first) still resolves to a
    /// slot for <see cref="ReorderGroupChild"/> without picking one up on a plain press.
    /// </summary>
    private int GroupChildIndexAt(Point point, bool requireHit = true)
    {
        if (_groupSlotBorders.Count == 0) return -1;

        double step = GroupChildBubble + GroupChildGap;
        int index = Math.Clamp((int)Math.Floor(point.X / step), 0, _groupSlotBorders.Count - 1);
        if (!requireHit)
            return index;

        double slotX = index * step + GroupChildBubble / 2;
        bool withinX = Math.Abs(point.X - slotX) <= GroupChildBubble / 2 + 4;
        bool withinY = point.Y >= -4 && point.Y <= GroupChildBubble + 4;
        return withinX && withinY ? index : -1;
    }

    /// <summary>
    /// Reorders <see cref="_expandedGroup"/>'s children after a drag in the row, the same
    /// position-preserving move <see cref="ReorderItem"/> does for the main orbit so hidden
    /// siblings keep their place instead of being shuffled by an index from a filtered view.
    /// </summary>
    private void ReorderGroupChild(OrbitItem dragged, int targetIndex)
    {
        if (_expandedGroup is null || targetIndex < 0) return;

        var list = _expandedGroup.Children;
        var visible = list.Where(i => !i.Hidden).ToList();
        int from = visible.IndexOf(dragged);
        if (from < 0) return;

        int to = Math.Min(targetIndex, visible.Count - 1);
        if (to == from) return;

        visible.RemoveAt(from);
        visible.Insert(to, dragged);

        var positions = new List<int>();
        for (int i = 0; i < list.Count && positions.Count < visible.Count; i++)
        {
            if (!list[i].Hidden)
                positions.Add(i);
        }
        for (int i = 0; i < positions.Count; i++)
            list[positions[i]] = visible[i];

        _manager.NotifyItemsChanged();
        RenderGroupRow();
        RenderPreview();
        RebuildItemsList();
    }

    private void AddItem_Click(object sender, RoutedEventArgs e) =>
        _manager.OpenAddItem(added =>
        {
            _manager.Config.Primary.Items.Add(added);
            _manager.NotifyItemsChanged();
            RenderPreview();
            RebuildItemsList();
        }, _manager.Config.Primary.Items);

    private void AddAppItem_Click(object sender, RoutedEventArgs e) =>
        _manager.OpenAddItem(added =>
        {
            _manager.Config.Primary.Items.Add(added);
            _manager.NotifyItemsChanged();
            RenderPreview();
            RebuildItemsList();
        }, _manager.Config.Primary.Items, isGenericAddItem: true);

    // ===================== the Orbit screen's sidebar, and its two modes =====================

    /// <summary>
    /// Which panel the Orbit screen's sidebar is showing. Not persisted: it is a mode you are in
    /// while arranging the orbit, not a preference, and coming back to the screen should show you
    /// its settings again.
    /// </summary>
    private bool _orbitEditing;

    /// <summary>
    /// Built once, on first entry into edit mode, and again whenever the orbit changes under it —
    /// the rows carry an "already on the orbit" state that goes stale the moment one is clicked.
    /// </summary>
    private bool _orbitEditBuilt;

    private void OrbitEditToggle_Click(object sender, RoutedEventArgs e) =>
        SetOrbitEditing(!_orbitEditing);

    /// <summary>
    /// Swaps the sidebar between the orbit's settings and the list of things that can go on it.
    /// <para>
    /// Logi puts these in two different places — its ring settings are a separate screen behind a
    /// nav button, and its sidebar is only ever the drag-source accordion. SwiftX has one screen
    /// for the orbit, so the sidebar is what changes instead: the canvas, the toolbar and the
    /// header stay exactly where they are, and only the 320px column swaps. That is the whole
    /// reason this is a Visibility swap between two siblings rather than a navigation — the canvas
    /// keeps its live slot geometry, its selection and its drag state across the change, none of
    /// which would survive being torn down and rebuilt.
    /// </para>
    /// </summary>
    private void SetOrbitEditing(bool editing)
    {
        _orbitEditing = editing;

        if (editing && !_orbitEditBuilt)
            BuildOrbitEditSidebar();

        OrbitSidebarSettings.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        OrbitSidebarEdit.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

        OrbitEditLabel.Text = editing ? "Done" : "Edit";
        OrbitEditIcon.Glyph = editing ? "\uE73E" : "\uE70F";
        ToolTipService.SetToolTip(OrbitEditToggle, editing
            ? "Back to the orbit's settings"
            : "Show the things you can put on the orbit");
    }

    /// <summary>
    /// Fills the edit sidebar: four categories of thing that can go on the orbit, each an
    /// expandable list, modelled on LogiApp's own <c>AccordionCategoryItem</c>.
    /// <para>
    /// Where Logi's twelve categories are hard-coded XAML with placeholder rows, these are built
    /// from what SwiftX actually has and this machine actually carries:
    /// <see cref="OnboardingCatalog.Available"/> already existence-checks every app, folder and
    /// link it offers — it is what the first-run guide proposes from — and
    /// <see cref="ToolCatalog.All"/> is the built-in tools. Offering an app that is not installed
    /// would produce a dead orbit item the moment it was clicked, which is the same reason that
    /// catalog is a method rather than a static list.
    /// </para>
    /// <para>
    /// Every category also ends in a row that opens the full add dialog, so the browse-for-anything
    /// path is never more than one click further away than the suggestions are.
    /// </para>
    /// </summary>
    private void BuildOrbitEditSidebar()
    {
        OrbitEditCategories.Children.Clear();

        var suggestions = OnboardingCatalog.Available();

        List<OrbitItem> OfKind(params OrbitItemKind[] kinds) =>
            suggestions.Where(s => kinds.Contains(s.Item.Kind)).Select(s => s.Item).ToList();

        OrbitEditCategories.Children.Add(BuildEditCategory(
            "APPS", "\uE7C3", OfKind(OrbitItemKind.Application)));

        OrbitEditCategories.Children.Add(BuildEditCategory(
            "SHORTCUTS", "\uE71B", OfKind(OrbitItemKind.Folder, OrbitItemKind.File, OrbitItemKind.Shortcut)));

        OrbitEditCategories.Children.Add(BuildEditCategory(
            "LINKS", "\uE774", OfKind(OrbitItemKind.WebLink)));

        // The tools are a catalog rather than a machine scan, so they are built here rather than
        // filtered out of the suggestions: every one of them always exists.
        OrbitEditCategories.Children.Add(BuildEditCategory(
            "TOOLS", "\uE943",
            ToolCatalog.All.Select(t => new OrbitItem
            {
                Kind = OrbitItemKind.Tool,
                DisplayName = t.DisplayName,
                Target = t.Kind.ToString(),
                CustomGlyph = t.Glyph,
            }).ToList()));

        // One row for the full picker, below the four categories rather than repeated inside each
        // of them: the dialog takes no starting kind, so a per-category "Browse for an app" would
        // have promised a preselection it cannot make, four times over.
        var browse = BuildEditRowShell("\uE710", "Add something else\u2026", null, null);
        browse.Margin = new Thickness(0, 8, 0, 0);
        browse.Click += AddItem_Click;
        ToolTipService.SetToolTip(browse, "Open the full add dialog: apps, files, folders, links, groups and tools");
        OrbitEditCategories.Children.Add(browse);

        _orbitEditBuilt = true;
        UpdateOrbitEditSummary();
    }

    /// <summary>
    /// How many of the orbit's user slots are free, said in the sidebar rather than discovered by
    /// clicking a row and having nothing happen.
    /// </summary>
    private void UpdateOrbitEditSummary()
    {
        int used = _manager.Config.Primary.Items.Count(i => !i.Hidden);
        int free = Math.Max(0, OrbitMetrics.UserSlotCount - used);
        OrbitEditSummary.Text = free switch
        {
            0 => "The orbit is full. Remove something to make room.",
            1 => "One free slot. Click anything below to put it there.",
            _ => $"{free} free slots. Click anything below to fill one.",
        };
    }

    /// <summary>
    /// One accordion category: a header that expands, over rows that add. Logi's own geometry —
    /// a 36px header at CornerRadius 4 with a 20px icon column and a chevron that swaps
    /// <c>E70D</c>/<c>E70E</c>, over 28px rows.
    /// </summary>
    private FrameworkElement BuildEditCategory(string name, string glyph, List<OrbitItem> items)
    {
        var rows = new StackPanel { Spacing = 2, Visibility = Visibility.Collapsed };
        foreach (var item in items)
            rows.Children.Add(BuildEditRow(item));
        if (items.Count == 0)
            rows.Children.Add(BuildEmptyCategoryRow());

        var chevron = new FontIcon
        {
            Glyph = "\uE70D",
            FontSize = 10,
            Foreground = ThemeHelper.Brush(Root, "SwiftXMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var header = new Grid { ColumnSpacing = 0 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 13,
            Foreground = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitCanvasAccentBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(icon, 0);
        header.Children.Add(icon);

        var label = new TextBlock
        {
            Text = name,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            CharacterSpacing = 50,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush"),
        };
        Grid.SetColumn(label, 1);
        header.Children.Add(label);

        Grid.SetColumn(chevron, 2);
        header.Children.Add(chevron);

        var button = new Button
        {
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 0, 10, 0),
            Background = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitNodeBackgroundBrush"),
            BorderBrush = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitCanvasBorderBrush"),
            BorderThickness = new Thickness(1),
            Content = header,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
        button.Click += (_, _) =>
        {
            bool open = rows.Visibility == Visibility.Visible;
            rows.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            chevron.Glyph = open ? "\uE70D" : "\uE70E";
        };

        return new StackPanel
        {
            Spacing = 4,
            Children = { button, rows },
        };
    }

    /// <summary>
    /// One addable thing. A row for something already on the orbit is shown ticked and does
    /// nothing when clicked — dropping a second copy of Notepad into a six-slot orbit because the
    /// list gave no sign the first one was there is not an edit anyone means to make.
    /// </summary>
    private FrameworkElement BuildEditRow(OrbitItem template)
    {
        bool present = OrbitHasTarget(_manager.Config.Primary.Items, template.Target);

        var row = BuildEditRowShell(
            template.Glyph, template.DisplayName,
            present ? "\uE73E" : null,
            present ? ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitCanvasAccentBrush") : null);

        ToolTipService.SetToolTip(row, present
            ? $"{template.DisplayName} is already on the orbit"
            : $"Add {template.DisplayName} to the orbit");

        if (!present)
        {
            row.Click += (_, _) => AddToOrbitFromSidebar(template);
        }
        else
        {
            row.IsEnabled = false;
        }

        return row;
    }

    /// <summary>
    /// What a category with nothing in it says. Reachable in practice: the link suggestions are
    /// unconditional but the app and folder ones are existence-checked against the machine, so a
    /// stripped-down Windows can empty a category out. An accordion that opens onto nothing at all
    /// reads as broken.
    /// </summary>
    private FrameworkElement BuildEmptyCategoryRow() => new TextBlock
    {
        Text = "Nothing to suggest here \u2014 use Add something else below.",
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(10, 2, 10, 4),
        Foreground = ThemeHelper.Brush(Root, "SwiftXMutedBrush"),
    };

    private Button BuildEditRowShell(string glyph, string text, string? badgeGlyph, Brush? badgeBrush)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 12,
            Foreground = ThemeHelper.Brush(Root, "SwiftXMutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var label = new TextBlock
        {
            Text = text,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush"),
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        if (badgeGlyph is not null)
        {
            var badge = new FontIcon
            {
                Glyph = badgeGlyph,
                FontSize = 11,
                Foreground = badgeBrush ?? ThemeHelper.Brush(Root, "SwiftXMutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }

        var button = new Button
        {
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 0, 10, 0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = grid,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, text);
        return button;
    }

    /// <summary>
    /// Puts one of the sidebar's suggestions on the orbit, or says why it cannot. A fresh copy of
    /// the template every time: <see cref="OnboardingCatalog.Available"/> hands out live
    /// <see cref="OrbitItem"/> instances, and adding the same instance twice would put one object
    /// in two slots — rename it in one and it changes in the other.
    /// </summary>
    private void AddToOrbitFromSidebar(OrbitItem template)
    {
        var items = _manager.Config.Primary.Items;
        if (items.Count(i => !i.Hidden) >= OrbitMetrics.UserSlotCount)
        {
            UpdateOrbitEditSummary();
            return;
        }

        items.Add(new OrbitItem
        {
            Kind = template.Kind,
            DisplayName = template.DisplayName,
            Target = template.Target,
            Arguments = template.Arguments,
            CustomIconPath = template.CustomIconPath,
            CustomGlyph = template.CustomGlyph,
        });

        // Through the manager, so the orbit overlay and anything else watching hear about it and
        // the config is written — the same path AddItem_Click takes.
        _manager.NotifyItemsChanged();

        // The rows carry an "already there" state that this click just invalidated for one of
        // them, so the list is rebuilt rather than patched.
        BuildOrbitEditSidebar();
        RenderPreview();
        RebuildItemsList();
    }

    /// <summary>
    /// Whether anything on the orbit — at the top level or inside a group — already points at
    /// <paramref name="target"/>.
    /// </summary>
    private static bool OrbitHasTarget(List<OrbitItem> items, string target)
    {
        foreach (var item in items)
        {
            if (string.Equals(item.Target, target, StringComparison.OrdinalIgnoreCase))
                return true;
            if (item.Children.Count > 0 && OrbitHasTarget(item.Children, target))
                return true;
        }
        return false;
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        // Every item the selection/expansion state could be pointing at is about to be replaced.
        HideInfo();
        CollapseGroup();
        // The corner's own arrangement was built out of the items being replaced, so it goes back
        // to following the orbit rather than holding four copies of the old one.
        HotCornerItems.Reset(_manager.Config);
        _manager.SeedDefaults(_manager.Config.Primary);
        _manager.NotifyItemsChanged();
        RenderPreview();
        RebuildItemsList();
    }

    /// <summary>
    /// Brings the theme cards in line with <paramref name="theme"/> without applying anything —
    /// the load pass's counterpart to <see cref="ThemeCard_Click"/>.
    /// </summary>
    private void SelectTheme(AppTheme theme) => UpdateThemeCardSelection(theme);

    /// <summary>
    /// Repaints all four theme cards for <paramref name="theme"/>: a 1.5px accent border and the
    /// filled radio glyph (<c>E73E</c>) on the selected one, the card border and the hollow glyph
    /// (<c>E73A</c>) in muted on the other three. Logi's own <c>UpdateThemeCardSelection</c>, with
    /// a fourth card for Midnight.
    /// <para>
    /// The brushes are built here rather than bound with <c>{ThemeResource}</c> because selection
    /// is a per-element assignment — which is also why <see cref="ApplyAccentColorOverride"/> and
    /// the <c>ActualThemeChanged</c> handler both re-run this: neither a user's accent pick nor an
    /// OS theme flip can reach a brush already written onto the element.
    /// <see cref="ThemeHelper.ColorFor(bool, AppTheme, string)"/> is what supplies the unselected
    /// colours, since a window whose theme disagrees with Windows' cannot read them back out of the
    /// app's own resources (see that class's own warning).
    /// </para>
    /// </summary>
    private void UpdateThemeCardSelection(AppTheme theme)
    {
        bool dark = ThemeHelper.IsDark(Root);
        // Which card is ticked comes from the argument; what the *unticked* ones are painted with
        // comes from the theme the window is actually wearing. Usually the same value — but
        // ResetAll calls this with Dark before the config has been written back, and the unselected
        // borders still have to match the surface they are sitting on.
        var liveTheme = _manager.Config.Theme;
        var selectedBrush = new SolidColorBrush(CurrentAccentColor());
        var restingBrush = new SolidColorBrush(
            ThemeHelper.ColorFor(dark, liveTheme, "ShellCardBorderBrush"));
        var mutedBrush = new SolidColorBrush(ThemeHelper.ColorFor(dark, "SettingsMutedBrush"));

        void Paint(Border card, FontIcon check, AppTheme cardTheme)
        {
            bool on = cardTheme == theme;
            card.BorderBrush = on ? selectedBrush : restingBrush;
            check.Glyph = on ? "\uE73E" : "\uE73A";
            check.Foreground = on ? selectedBrush : mutedBrush;
        }

        Paint(ThemeCardSystem, ThemeCheckSystem, AppTheme.System);
        Paint(ThemeCardLight, ThemeCheckLight, AppTheme.Light);
        Paint(ThemeCardDark, ThemeCheckDark, AppTheme.Dark);
        Paint(ThemeCardBlack, ThemeCheckBlack, AppTheme.Black);
        Paint(ThemeCardFrosted, ThemeCheckFrosted, AppTheme.FrostedGlass);
    }

    /// <summary>
    /// Fills <c>LanguageBox</c> from <see cref="LanguageCatalog.Options"/> and selects whichever
    /// entry matches <see cref="AppConfig.Language"/>. Built in code-behind rather than as static
    /// ComboBoxItems in XAML (unlike HotCornerBox) because each entry's Content is that language's
    /// own native name — data, not markup.
    /// </summary>
    private void PopulateLanguageBox()
    {
        LanguageBox.Items.Clear();
        foreach (var option in LanguageCatalog.Options)
        {
            var item = new ComboBoxItem { Content = option.NativeName, Tag = option.Language.ToString() };
            LanguageBox.Items.Add(item);
            if (option.Language == _manager.Config.Language)
                LanguageBox.SelectedItem = item;
        }
    }

    /// <summary>
    /// Paints the Hover style pill's two segments for <paramref name="style"/>: the selected
    /// segment filled with the app's live accent colour (<see cref="CurrentAccentColor"/> — tracks
    /// a custom pick, not just the built-in teal/purple) with a foreground computed for contrast
    /// against it (<see cref="AccentPalette.OnAccent(Color)"/>), the other left as a plain outline.
    /// <para>
    /// Local Setters rather than a Style/ControlTemplate — the same choice
    /// <see cref="UpdateThemeCardSelection"/> makes for the theme cards, and for the same reason:
    /// this is a per-element paint driven by which of two Buttons was chosen, not something a Style
    /// alone can express. As with the theme cards, a value painted here becomes a local value that
    /// will not itself track a later theme or accent change made elsewhere — this call already runs
    /// on load and on every selection, which covers ordinary use; only a theme/accent change made
    /// while this exact screen is already open and already painted would need this re-run again,
    /// which nothing outside the Orbit screen's own code currently does.
    /// </para>
    /// </summary>
    private void SelectHoverStyle(OrbitHoverStyle style)
    {
        bool dark = ThemeHelper.IsDark(Root);
        var theme = _manager.Config.Theme;
        var accent = CurrentAccentColor();
        var accentBrush = new SolidColorBrush(accent);
        var onAccentBrush = new SolidColorBrush(AccentPalette.OnAccent(accent));
        var restingBorder = new SolidColorBrush(ThemeHelper.ColorFor(dark, theme, "ShellCardBorderBrush"));
        var mutedBrush = new SolidColorBrush(ThemeHelper.ColorFor(dark, "SettingsMutedBrush"));

        Paint(HoverStyleInvertPill, HoverStyleInvertText, style == OrbitHoverStyle.Invert);
        Paint(HoverStyleZoomPill, HoverStyleZoomText, style == OrbitHoverStyle.Zoom);

        void Paint(Border pill, TextBlock label, bool selected)
        {
            pill.Background = selected ? accentBrush : null;
            pill.BorderBrush = selected ? accentBrush : restingBorder;
            label.Foreground = selected ? onAccentBrush : mutedBrush;
        }
    }

    private void SelectItemSize(OrbitItemSize size)
    {
        ItemSizeSmallOption.IsChecked = size == OrbitItemSize.Small;
        ItemSizeMediumOption.IsChecked = size == OrbitItemSize.Medium;
        ItemSizeLargeOption.IsChecked = size == OrbitItemSize.Large;
    }

    private void ThemeCard_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not FrameworkElement card) return;
        // Tag is the AppTheme member name verbatim — "Black" for the card labelled Midnight. See
        // the picker's comment in ShellWindow.xaml for why that name cannot be changed.
        if (Enum.TryParse<AppTheme>(card.Tag as string, out var theme))
            ApplyThemeChoice(theme);
    }

    /// <summary>
    /// Persists a theme choice and repaints everything that follows from it — the orbit and the
    /// other windows through the manager, this window through its own pass. Shared by the theme
    /// cards and the header's cycle button so the two can never diverge.
    /// </summary>
    private void ApplyThemeChoice(AppTheme theme)
    {
        _manager.Config.Theme = theme;
        _manager.Save();
        // Reaches every open window, this one included: OrbitManager.ApplyTheme fans out to the
        // orbit, the tool windows, the add-item dialog and the guide, and back to this window's own
        // ApplyTheme — which is what repaints the swatch and the theme cards for the new theme's
        // built-in accent. This used to call ApplyTheme() again directly as belt and braces; now
        // that the pass rebuilds the Elements and Tools cards too (see RepaintThemedContent) that
        // second pass is a full rebuild for nothing, so the fan-out is left to do it once.
        _manager.ApplyTheme();
        // Not part of that pass: the header glyph says which theme is *selected*, which only
        // changes here. An OS flip under System changes what is on screen without changing it.
        UpdateThemeToggleIcon();
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedItem is not ComboBoxItem item) return;
        if (Enum.TryParse<AppLanguage>(item.Tag as string, out var language))
        {
            _manager.Config.Language = language;
            _manager.Save();
            _manager.ApplyLanguage();
            // The Home header is the one piece of this window's text that is built from the string
            // table at runtime rather than written into XAML, so it is the one piece that can
            // follow a language change without the restart the rest of the window needs. See
            // Services/Loc.cs's own note on that split.
            RefreshGreeting();
        }
    }

    /// <summary>
    /// PLACEHOLDER. SwiftX has no Microsoft Store listing yet — it builds unpackaged
    /// (<c>WindowsPackageType=None</c>), so there is no product id to point at. Every Store link on
    /// the Feedback &amp; Support page is derived from this one constant: put the real twelve-character
    /// product id here once the app is listed and all three resolve on their own, with nothing else
    /// to change. Until then they open a Store page that does not exist.
    /// </summary>
    private const string StoreProductId = "0000000000000";

    /// <summary>
    /// The Store links, as <c>https://apps.microsoft.com</c> URLs rather than
    /// <c>ms-windows-store://</c> protocol ones. Both reach the same listing, but the page's own
    /// closing note promises that links open in the browser, and the protocol form opens the Store
    /// app instead — the web form is the one that keeps that promise. (If a deep link into the Store
    /// app is ever wanted, the equivalents are <c>ms-windows-store://review/?ProductId=</c> and
    /// <c>ms-windows-store://pdp/?ProductId=</c>.)
    /// </summary>
    private const string StoreListingUrl = "https://apps.microsoft.com/detail/" + StoreProductId;
    private const string StoreReviewUrl = StoreListingUrl + "#reviews";

    /// <summary>
    /// The repository itself, not an issue or discussion: this is the "star us" link beside the
    /// rating, so it wants the page with the star button on it.
    /// </summary>
    private const string GitHubRepoUrl = "https://github.com/framedparadox/SwiftX";

    private void ShareFeedback_Click(object sender, RoutedEventArgs e) =>
        Launcher.OpenUrl(StoreReviewUrl);

    private void RateOnStore_Click(object sender, RoutedEventArgs e) =>
        Launcher.OpenUrl(StoreReviewUrl);

    private void StarOnGitHub_Click(object sender, RoutedEventArgs e) =>
        Launcher.OpenUrl(GitHubRepoUrl);

    /// <summary>
    /// The terms the app ships under, as the copy of them that lives with the source rather than a
    /// string of legal text baked into the binary: the document can then be corrected without a new
    /// build, and the link keeps pointing at whatever the current one says. Held beside the other
    /// outbound links rather than in About's own code so every URL this window can open is in one
    /// place.
    /// </summary>
    private const string TermsUrl = GitHubRepoUrl + "/blob/main/TERMS.md";

    private void ReportIssue_Click(object sender, RoutedEventArgs e) =>
        Launcher.OpenUrl(StoreListingUrl);

    private void OpenTerms_Click(object sender, RoutedEventArgs e) =>
        Launcher.OpenUrl(TermsUrl);

    /// <summary>The inline "Read privacy" link at the foot of Feedback &amp; Support.</summary>
    private void OpenPrivacyFromFeedback_Click(Hyperlink sender, HyperlinkClickEventArgs args) =>
        GoToPrivacyPage();

    /// <summary>About's own Privacy link, beside Terms and conditions.</summary>
    private void OpenPrivacyFromAbout_Click(object sender, RoutedEventArgs e) =>
        GoToPrivacyPage();

    /// <summary>
    /// Moves to Settings › Privacy &amp; Data instead of opening anything — it is a section of this
    /// window, not a page on the web. Goes through <see cref="ShowScreen"/> and
    /// <see cref="SelectSection"/> rather than flipping Visibility directly, so those two stay the
    /// only places that know which panel and which rail row belong together, and the rail lights up
    /// the way it would have if the user had clicked the row themselves.
    /// </summary>
    private void GoToPrivacyPage()
    {
        ShowScreen(ScreenSettings);
        SelectSection(SectionPrivacy);
    }

    /// <summary>
    /// Persists the star rating and acknowledges it inline. Saved locally and never transmitted —
    /// see <see cref="AppConfig.FeedbackRating"/> and Settings' own Privacy page.
    /// <para>
    /// Driven by a DependencyProperty change callback (registered in the constructor) rather than
    /// <c>RatingControl.ValueChanged</c>: that event does not fire when Value is set through the
    /// control's UIA RangeValue provider, so the rating silently failed to save for anyone driving
    /// Settings with assistive technology — or with an automated UI test, which is how this was
    /// caught. The DP callback fires for every path that changes the value, pointer and programmatic
    /// alike.
    /// </para>
    /// </summary>
    private void OnRatingValueChanged()
    {
        if (_loading) return;
        // RatingControl reports -1 for "cleared", which is the same thing as never having rated as
        // far as the config is concerned; normalise so the stored value is only ever 0-5.
        var value = ExperienceRating.Value < 0 ? 0 : (int)ExperienceRating.Value;
        if (value == _manager.Config.FeedbackRating) return;
        _manager.Config.FeedbackRating = value;
        _manager.Save();
        UpdateRatingThanks();
    }

    /// <summary>
    /// The line beside the stars. Deliberately does not thank the user for a low rating — "Thanks
    /// for rating!" next to one star reads as though nobody looked. Hidden entirely until there is
    /// a rating to acknowledge.
    /// </summary>
    private void UpdateRatingThanks()
    {
        int rating = _manager.Config.FeedbackRating;
        if (rating <= 0)
        {
            RatingThanks.Visibility = Visibility.Collapsed;
            return;
        }
        RatingThanks.Text = rating >= 4
            ? "Thanks — glad it's working for you."
            : "Thanks. If something's wrong, the support link below will reach us.";
        RatingThanks.Visibility = Visibility.Visible;
    }


    private void HoverStyleOption_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not FrameworkElement option) return;
        if (Enum.TryParse<OrbitHoverStyle>(option.Tag as string, out var style))
        {
            _manager.Config.HoverStyle = style;
            _manager.Save();
            SelectHoverStyle(style);
        }
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        await StartupService.SetEnabledAsync(StartupToggle.IsOn);
        _manager.Config.LaunchAtStartup = StartupToggle.IsOn;
        _manager.Save();
    }

    /// <summary>
    /// Points the Desktop shortcut switch at what is actually on the desktop, not at what the
    /// config remembers being asked for. The shortcut is an ordinary file the user can delete from
    /// Explorer at any moment, and a switch still reading "on" over a shortcut that is gone would
    /// simply be wrong — worse, turning it off and on again would then be the only way to get the
    /// shortcut back. Where the two disagree the stored preference is corrected to match reality,
    /// so an exported config carries the truth too.
    /// </summary>
    private void SyncDesktopShortcutToggle()
    {
        bool present = DesktopShortcutService.Exists();
        DesktopShortcutToggle.IsOn = present;
        if (_manager.Config.DesktopShortcut == present)
            return;
        _manager.Config.DesktopShortcut = present;
        _manager.Save();
    }

    /// <summary>
    /// Creates or deletes the desktop shortcut, then shows whatever actually happened.
    /// <para>
    /// The switch is snapped back when the request could not be honored — a desktop the app cannot
    /// write to, or a <c>SwiftX.lnk</c> that SwiftX did not create and therefore will not delete
    /// (see <see cref="DesktopShortcutService"/>). Leaving the switch sitting in the position the
    /// user just dragged it to would claim something untrue about their desktop.
    /// </para>
    /// </summary>
    private void DesktopShortcutToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        bool actual = DesktopShortcutService.Apply(DesktopShortcutToggle.IsOn);
        if (actual != DesktopShortcutToggle.IsOn)
        {
            // Guarded, or putting the switch back fires this handler again and undoes the work.
            _loading = true;
            DesktopShortcutToggle.IsOn = actual;
            _loading = false;
        }

        _manager.Config.DesktopShortcut = actual;
        _manager.Save();

        if (actual)
            DesktopShortcutOffer.IsOpen = false;
    }

    /// <summary>
    /// The first-run prompt's own button. It drives the switch rather than the service directly,
    /// so there is exactly one code path that creates the shortcut and persists the choice.
    /// </summary>
    private void CreateDesktopShortcut_Click(object sender, RoutedEventArgs e)
    {
        DesktopShortcutOffer.IsOpen = false;
        if (!DesktopShortcutToggle.IsOn)
            DesktopShortcutToggle.IsOn = true;
    }

    /// <summary>
    /// Moves to Settings › General, for the same reason <see cref="GoToPrivacyPage"/> does:
    /// <see cref="ShowScreen"/> and <see cref="SelectSection"/> stay the one place that knows which
    /// panel belongs to which rail row.
    /// </summary>
    private void GoToGeneralPage()
    {
        ShowScreen(ScreenSettings);
        SelectSection(SectionGeneral);
    }

    /// <summary>
    /// About's spec card — Logi's own "System &amp; Runtime Specifications" block, with SwiftX's
    /// four honest rows.
    /// <para>
    /// Every value here is read at runtime rather than written into the XAML. Logi hard-codes its
    /// version string in two places and its spec rows in one; a hard-coded "Windows 11" or
    /// ".NET 10" is a line that silently stops being true the next time the csproj moves, and an
    /// About page that lies about the runtime is worse than one that omits it.
    /// </para>
    /// </summary>
    private void FillSpecCard()
    {
        SpecOs.Text = RuntimeInformation.OSDescription;
        SpecRuntime.Text = RuntimeInformation.FrameworkDescription;
        SpecDistribution.Text = (PackagedRuntime.IsPackaged
            ? "MSIX (Microsoft Store)"
            : "Portable (unpackaged)")
            + " · " + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.SuggestedFileName = "orbit";
        picker.FileTypeChoices.Add("JSON", [".json"]);
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
            OrbitStore.ExportTo(_manager.Config, file.Path);
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        var imported = OrbitStore.ImportFrom(file.Path);
        if (imported is null) return;
        // The whole Orbits collection is being replaced, so nothing the selection/expansion state
        // could be pointing at survives.
        HideInfo();
        CollapseGroup();
        _manager.Config.CopyAppSettingsFrom(imported);
        _manager.Config.Orbits.Clear();
        _manager.Config.Orbits.AddRange(imported.Orbits);
        _manager.Config.EnsurePrimary();
        _manager.NotifyItemsChanged();
        _manager.ApplyTheme();
        _manager.ApplyLanguage();
        _manager.ApplyHotCorner();
        ApplyTheme();
        RefreshGreeting();
        _loading = true;
        PopulateLanguageBox();
        HotCornerEnabledToggle.IsOn = _manager.Config.HotCornerEnabled;
        SelectHotCorner(_manager.Config.HotCorner);
        SelectHotCornerLayout(_manager.Config.HotCornerLayout);
        SyncHotCornerControls();
        _loading = false;
        RenderPreview();
        RebuildItemsList();
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        // SeedDefaults below replaces every orbit item, so drop anything the selection/expansion
        // state was pointing at first.
        HideInfo();
        CollapseGroup();
        _manager.Config.Theme = AppTheme.Dark;
        _manager.Config.HoverStyle = OrbitHoverStyle.Invert;
        _manager.Config.Hotkey = HotkeyGesture.Default.ToString();
        _manager.Config.HotkeyEnabled = true;
        _manager.Config.HotCornerEnabled = true;
        _manager.Config.HotCorner = HotCorner.TopLeft;
        _manager.Config.HotCornerLayout = HotCornerLayout.Orbit;
        HotCornerItems.Reset(_manager.Config);
        // Driven off the catalog rather than a hand-written list of flags. The hand-written list
        // had already gone stale — it set four of the five, silently leaving Quick Format hidden
        // for anyone who had hidden it, which no amount of pressing Reset would undo. Every tool
        // ships visible (see AppConfig's initialisers), so "reset" is "show them all".
        foreach (var tool in ToolCatalog.All)
            _manager.Config.SetToolShown(tool.Kind, true);
        _manager.Config.FeedbackRating = 0;
        _manager.Config.AccentColorHex = null;
        _manager.SeedDefaults(_manager.Config.Primary);
        // Through the manager, not a raw Config assignment: OrbitMetrics holds the geometry the
        // orbit actually draws with.
        _manager.SetItemSize(OrbitItemSize.Medium);
        _manager.SetRadius(null);
        _manager.NotifyItemsChanged();
        _manager.ApplyTheme();
        _manager.ApplyHotkey();
        _manager.ApplyHotCorner();
        ApplyTheme();
        _loading = true;
        SelectTheme(AppTheme.Dark);
        UpdateThemeToggleIcon();
        SelectHoverStyle(OrbitHoverStyle.Invert);
        SelectItemSize(OrbitItemSize.Medium);
        RadiusSlider.Value = OrbitMetrics.DefaultRadius;
        HotkeyEnabledToggle.IsOn = true;
        HotkeyCapture.Gesture = HotkeyGesture.Default;
        HotCornerEnabledToggle.IsOn = true;
        ExperienceRating.Value = -1;
        UpdateRatingThanks();
        SyncAccentControls();
        SelectHotCorner(HotCorner.TopLeft);
        SelectHotCornerLayout(HotCornerLayout.Orbit);
        SyncHotCornerControls();
        RebuildToolsList();
        _loading = false;
        RenderPreview();
        RebuildItemsList();
        UpdateHotkeyStatus();
    }

    private void ItemSize_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton radio) return;
        if (Enum.TryParse<OrbitItemSize>(radio.Tag as string, out var size))
            _manager.SetItemSize(size);
    }

    private void RadiusSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // Connect() wires this handler the moment RadiusSlider itself is created, but applying
        // this element's own Minimum="80" during that same XAML load coerces Value up from its
        // 0 default and fires this handler immediately — before RadiusReadout, a later sibling
        // in the markup, has been connected. Ignore that premature firing; Root.Loaded sets
        // RadiusSlider.Value explicitly once everything is wired, which re-fires this with
        // RadiusReadout in place.
        if (RadiusReadout is null) return;

        // Kept in sync even while _loading: the readout has nothing to guard against re-entrancy
        // (unlike _manager.SetRadius below, which would otherwise re-save on every programmatic
        // Value assignment during the load pass), and it needs to reflect whatever the slider was
        // just set to either way.
        RadiusReadout.Text = $"{(int)e.NewValue} px";
        if (_loading) return;
        _manager.SetRadius(e.NewValue);
    }

    private void OnHotkeyAssigned(HotkeyGesture? gesture)
    {
        if (_loading || gesture is null) return;
        _manager.Config.Hotkey = gesture.ToString();
        _manager.Save();
        UpdateHotkeyStatus();
    }

    private void HotkeyEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _manager.Config.HotkeyEnabled = HotkeyEnabledToggle.IsOn;
        _manager.Save();
        UpdateHotkeyStatus();
    }

    private void SelectHotCorner(HotCorner corner)
    {
        foreach (ComboBoxItem item in HotCornerBox.Items)
        {
            if ((item.Tag as string) == corner.ToString())
            {
                HotCornerBox.SelectedItem = item;
                break;
            }
        }
        UpdateHotCornerIcon(corner);
    }

    /// <summary>
    /// Turns the hot-corner triangle to point at <paramref name="corner"/>. The geometry in
    /// ShellWindow.xaml is authored with its right angle at the top left, so each quarter turn
    /// clockwise walks it round to the next corner in the same order the screen does.
    /// <para>
    /// Driven from the corner itself rather than the ComboBox's index so the two cannot drift apart
    /// if the dropdown is ever reordered.
    /// </para>
    /// </summary>
    private void UpdateHotCornerIcon(HotCorner corner) =>
        HotCornerIconRotation.Angle = CornerTriangleAngle(corner);

    /// <summary>
    /// Clockwise rotation, in degrees, that turns a triangle authored pointing top-left (right
    /// angle in that corner, hypotenuse cutting across to it) so it instead points at
    /// <paramref name="corner"/>. Shared by <see cref="UpdateHotCornerIcon"/> — which turns the
    /// XAML-declared PathIcon on the Orbit screen's Hot corner row — and
    /// <see cref="AddHotCornerButton"/>, which builds the same triangle fresh in code for the
    /// canvas button, so the two can never disagree about which way a given corner turns.
    /// </summary>
    private static double CornerTriangleAngle(HotCorner corner) => corner switch
    {
        HotCorner.TopLeft => 0,
        HotCorner.TopRight => 90,
        HotCorner.BottomRight => 180,
        HotCorner.BottomLeft => 270,
        _ => 0,
    };

    /// <summary>
    /// The right-angle-triangle path shared by the row icon and the canvas button — see
    /// <see cref="CornerTriangleAngle"/>. Authored pointing top-left at a nominal 14x14, the same
    /// coordinates as the XAML-declared PathIcon's <c>Data</c> in ShellWindow.xaml, so the two
    /// read as the same mark rather than merely similar ones. Built fresh per call rather than
    /// cached: Geometry is cheap here and every other themed element on this canvas (brushes,
    /// borders) is already rebuilt on each RenderPreview pass.
    /// </summary>
    private static PathGeometry CornerTriangleGeometry()
    {
        var figure = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true };
        figure.Segments.Add(new LineSegment { Point = new Point(14, 0) });
        figure.Segments.Add(new LineSegment { Point = new Point(0, 14) });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private void SelectHotCornerLayout(HotCornerLayout layout)
    {
        HotCornerLayoutOrbit.IsChecked = layout == HotCornerLayout.Orbit;
        HotCornerLayoutQuadrant.IsChecked = layout == HotCornerLayout.Quadrant;
    }

    /// <summary>
    /// Brings the hot corner's second row in line with the switch above it — which corner, and how
    /// much of the orbit it opens, mean nothing while the corner is off — and drops any quadrant
    /// preview the canvas was showing once there is no corner left to preview.
    /// </summary>
    private void SyncHotCornerControls()
    {
        bool on = HotCornerEnabledToggle.IsOn;
        HotCornerOptionsRow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on)
            _previewQuadrant = false;
    }

    private void HotCornerBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || HotCornerBox.SelectedItem is not ComboBoxItem item) return;
        if (Enum.TryParse<HotCorner>(item.Tag as string, out var corner))
        {
            _manager.Config.HotCorner = corner;
            _manager.Save();
            _manager.ApplyHotCorner();
            UpdateHotCornerIcon(corner);
            // The canvas draws the corner button, and any quadrant preview, at the chosen corner.
            RenderPreview();
        }
    }

    private void HotCornerEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _manager.Config.HotCornerEnabled = HotCornerEnabledToggle.IsOn;
        _manager.Save();
        _manager.ApplyHotCorner();
        SyncHotCornerControls();
        RenderPreview();
    }

    private void HotCornerLayout_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _manager.Config.HotCornerLayout = ReferenceEquals(sender, HotCornerLayoutQuadrant)
            ? HotCornerLayout.Quadrant
            : HotCornerLayout.Orbit;
        _manager.Save();
        SyncHotCornerControls();
        RenderPreview();
    }

    private void UpdateHotkeyStatus()
    {
        bool ok = _manager.ApplyHotkey();
        HotkeyStatus.Text = ok
            ? $"Registered: {_manager.Config.Hotkey}"
            : "Could not register that hotkey (maybe another app owns it).";
    }

    /// <summary>
    /// Builds the Elements screen: every top-level item and every top-level group in one flat
    /// three-column card grid, all cards the same width — a group's own card immediately followed
    /// by a card for each of its children, in orbit order.
    /// <para>
    /// A group used to be skipped entirely here — only its children were listed, folded into the
    /// flat grid under the group's name — which meant a group with nothing in it yet (exactly what
    /// "Add item… › Group" produces) appeared nowhere at all and read as a broken add. A later
    /// design gave each group its own full-width section (a header plus a nested sub-grid of
    /// children), which fixed that but made a group's own card noticeably wider than every other
    /// element's. This is the version that keeps the first fix without the second problem: a
    /// group gets exactly one card — same shape and width as <see cref="BuildItemRow"/>, via
    /// <see cref="BuildGroupRow"/> — and its children are just more cards in the same shared grid
    /// right after it, distinguished from a top-level item only by their own caption text
    /// (<c>BuildGroupRow</c>'s child-count line versus <c>BuildItemRow</c>'s kind/target lines).
    /// </para>
    /// </summary>
    private void RebuildItemsList()
    {
        if (ItemsList is null) return;
        ItemsList.Children.Clear();
        ItemsList.RowDefinitions.Clear();

        var cards = new List<Border>();
        foreach (var item in _manager.Config.Primary.Items)
        {
            // Tools are managed from the Tools screen's own toggles, not listed here too.
            if (item.IsTool)
                continue;

            // Nor is the built-in Tools group: it is wholly owned by the dedicated Tools screen, so
            // showing it here too would duplicate that screen instead of listing something new.
            if (item.IsGroup && string.Equals(
                    item.Target, ToolCatalog.ToolsGroupTarget, StringComparison.OrdinalIgnoreCase))
                continue;

            if (item.IsGroup)
            {
                cards.Add(new Border
                {
                    Style = (Style)Root.Resources["Card"],
                    Child = BuildGroupRow(item),
                });
                foreach (var child in item.Children.Where(c => !c.IsTool))
                    cards.Add(new Border
                    {
                        Style = (Style)Root.Resources["Card"],
                        Child = BuildItemRow(child),
                    });
            }
            else
            {
                cards.Add(new Border
                {
                    Style = (Style)Root.Resources["Card"],
                    Child = BuildItemRow(item),
                });
            }
        }

        int rowCount = (cards.Count + 2) / 3;
        for (int r = 0; r < rowCount; r++)
            ItemsList.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < cards.Count; i++)
        {
            Grid.SetRow(cards[i], i / 3);
            Grid.SetColumn(cards[i], i % 3);
            ItemsList.Children.Add(cards[i]);
        }
    }

    /// <summary>
    /// A top-level group's own card: the same vertical shape <see cref="BuildItemRow"/> builds —
    /// icon, title, two caption lines, a type badge, a divider, a footer strip — so a group's card
    /// is never a different size than an ungrouped item's. Two differences: a group isn't itself a
    /// shell target, so there is nothing sensible for its footer to "Edit" — the footer instead
    /// opens the same scoped "Add item…" flow the header's own add-child affordance used to be the
    /// only way to reach, so that capability stays reachable rather than silently dropped when the
    /// footer stopped being available for it. And its caption lines describe the group itself
    /// (child count, then a static "opens a nested orbit") rather than a kind/target pair, since a
    /// group has neither.
    /// </summary>
    private Grid BuildGroupRow(OrbitItem group)
    {
        var children = group.Children.Where(c => !c.IsTool).ToList();

        string caption1 = children.Count switch
        {
            0 => "No items yet",
            1 => "Group · 1 item",
            _ => $"Group · {children.Count} items",
        };

        var card = BuildCardShell(
            BuildItemIcon(group),
            group.DisplayName,
            caption1,
            "Opens a nested orbit",
            "Group",
            isHidden: group.Hidden,
            onToggle: isOn =>
            {
                group.Hidden = !isOn;
                _manager.NotifyItemsChanged();
                RenderPreview();
            },
            onDelete: () =>
            {
                // The whole group goes, children and all — they only exist inside it.
                _manager.Config.Primary.Items.Remove(group);
                _manager.NotifyItemsChanged();
                RebuildItemsList();
                RenderPreview();
            },
            deleteAutomationName: "Delete " + group.DisplayName,
            footerLabel: "ADD ITEM",
            footerAutomationName: "Add item to " + group.DisplayName,
            onFooter: () => _manager.OpenAddItem(added =>
            {
                // Into this group's own children, not the top-level orbit — and with the Group
                // tile taken away, since the orbit has no third orbit to expand a group inside a
                // group into.
                group.Children.Add(added);
                _manager.NotifyItemsChanged();
                RenderPreview();
                RebuildItemsList();
            }, group.Children, isGenericAddItem: true, allowGroups: false));

        return card;
    }

    /// <summary>
    /// One item's card — icon, title, two caption lines (its kind, then its target), a type-tag
    /// badge, and a footer strip that opens the same Replace… flow the orbit's own right-click menu
    /// uses, via <see cref="EditOrbitItem"/>. Shared by the top-level cards and by a group's own
    /// children, so both look and behave the same — <see cref="EditOrbitItem"/> and this row's own
    /// delete handler both check both locations for exactly that reason.
    /// </summary>
    private Grid BuildItemRow(OrbitItem item)
    {
        return BuildCardShell(
            BuildItemIcon(item),
            item.DisplayName,
            item.Kind.ToString(),
            item.Target,
            KindBadgeLabel(item.Kind),
            isHidden: item.Hidden,
            onToggle: isOn =>
            {
                item.Hidden = !isOn;
                _manager.NotifyItemsChanged();
                RenderPreview();
            },
            onDelete: () =>
            {
                // Top level and inside a group both, since the same card builds for either.
                _manager.Config.Primary.Items.Remove(item);
                foreach (var group in _manager.Config.Primary.Items.Where(i => i.IsGroup))
                    group.Children.Remove(item);
                _manager.NotifyItemsChanged();
                RebuildItemsList();
                RenderPreview();
            },
            deleteAutomationName: "Delete " + item.DisplayName,
            footerLabel: "EDIT",
            footerAutomationName: "Edit " + item.DisplayName,
            onFooter: () => EditOrbitItem(item));
    }

    /// <summary>
    /// Swaps one Elements-screen item for a newly added one in the same place — the Elements card's
    /// own counterpart to <see cref="ReplaceOrbitItem"/>, which only ever looks in the top-level
    /// list and so cannot be reused as-is here: a naive <c>IndexOf</c> against a group's child
    /// returns -1 and would either silently do nothing or append a stray top-level copy instead of
    /// writing back into the group the item actually lives in. This checks both locations the same
    /// way <see cref="BuildItemRow"/>'s own delete handler already has to.
    /// <para>
    /// A group's child can never itself be a group (see <see cref="BuildGroupRow"/>'s own scoped
    /// add), so editing a child keeps that same restriction; a top-level item may become anything,
    /// same as <see cref="ReplaceOrbitItem"/>.
    /// </para>
    /// </summary>
    private void EditOrbitItem(OrbitItem item)
    {
        var topLevel = _manager.Config.Primary.Items;
        bool isTopLevel = topLevel.Contains(item);

        _manager.OpenAddItem(
            added =>
            {
                int at = topLevel.IndexOf(item);
                if (at >= 0)
                {
                    topLevel[at] = added;
                }
                else
                {
                    var group = topLevel.FirstOrDefault(g => g.IsGroup && g.Children.Contains(item));
                    int childAt = group?.Children.IndexOf(item) ?? -1;
                    if (group is not null && childAt >= 0)
                        group.Children[childAt] = added;
                    else
                        topLevel.Add(added); // item moved out from under the dialog — don't drop the edit
                }

                _manager.NotifyItemsChanged();
                RenderPreview();
                RebuildItemsList();
            },
            topLevel,
            allowGroups: isTopLevel,
            heading: "Edit " + item.DisplayName,
            confirmLabel: "Save");
    }

    /// <summary>
    /// The shared vertical layout a plain item's card (<see cref="BuildItemRow"/>), a group's own
    /// card (<see cref="BuildGroupRow"/>), and a tool card (<see cref="BuildToolCard"/>) all build:
    /// icon top-left (optionally preceded by a <paramref name="leading"/> element — the Tools
    /// screen's drag grip; Elements' two callers leave it null and get the original layout
    /// unchanged) with an optional delete button and a show/hide toggle top-right, a bold title,
    /// two muted caption lines, a small rounded type badge, a thin divider, and a footer strip that
    /// is itself one click target end to end (the <c>BoxOpenLabel</c> pattern the Home screen's own
    /// nav-box "OPEN" footer already uses, see <see cref="BuildCardFooterButton"/>).
    /// <paramref name="onDelete"/>/<paramref name="deleteAutomationName"/> are nullable so a card
    /// with nothing sensible to delete (a built-in tool can be hidden but never removed) can leave
    /// the button out entirely rather than wiring it to a no-op — Elements' two callers always pass
    /// a real delegate, so their cards are unaffected. <paramref name="wrapText"/> is similarly
    /// opt-in: Elements' title/caption1 stay single-line-with-ellipsis (an item name or a path reads
    /// better trimmed than reflowed), while Tools turns wrapping on for its 18pt title and
    /// sentence-length description, both capped at two lines so a card's height stays predictable
    /// inside the fixed-size grid slot <see cref="ToolsList"/>'s ItemsWrapGrid gives it.
    /// </summary>
    private Grid BuildCardShell(
        FrameworkElement icon, string title, string caption1, string caption2, string badgeLabel,
        bool isHidden, Action<bool> onToggle, Action? onDelete, string? deleteAutomationName,
        string footerLabel, string footerAutomationName, Action onFooter,
        FrameworkElement? leading = null, bool wrapText = false)
    {
        var card = new Grid { RowSpacing = 8 };
        for (int i = 0; i < 7; i++)
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new Grid { ColumnSpacing = 4 };
        int offset = leading is null ? 0 : 1;
        if (leading is not null)
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (leading is not null)
        {
            Grid.SetColumn(leading, 0);
            head.Children.Add(leading);
        }

        Grid.SetColumn(icon, offset + 0);
        head.Children.Add(icon);

        if (onDelete is not null)
        {
            var remove = BuildRowIconButton(0xE74D, "Delete", deleteAutomationName ?? "Delete");
            remove.Click += (_, _) => onDelete();
            Grid.SetColumn(remove, offset + 2);
            head.Children.Add(remove);
        }

        var toggle = new ToggleSwitch
        {
            IsOn = !isHidden,
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggle.Toggled += (_, _) => onToggle(toggle.IsOn);
        Grid.SetColumn(toggle, offset + 3);
        head.Children.Add(toggle);

        Grid.SetRow(head, 0);
        card.Children.Add(head);

        var titleBlock = new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            FontSize = 18,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = wrapText ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MaxLines = wrapText ? 2 : 0,
        };
        Grid.SetRow(titleBlock, 1);
        card.Children.Add(titleBlock);

        var caption1Block = new TextBlock
        {
            Text = caption1,
            Style = SecondaryCaptionStyle,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = wrapText ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MaxLines = wrapText ? 2 : 0,
        };
        Grid.SetRow(caption1Block, 2);
        card.Children.Add(caption1Block);

        var caption2Block = new TextBlock
        {
            Text = caption2,
            Style = SecondaryCaptionStyle,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = string.IsNullOrEmpty(caption2) ? Visibility.Collapsed : Visibility.Visible,
        };
        Grid.SetRow(caption2Block, 3);
        card.Children.Add(caption2Block);

        var badge = BuildKindBadge(badgeLabel);
        Grid.SetRow(badge, 4);
        card.Children.Add(badge);

        var divider = BuildCardDivider();
        Grid.SetRow(divider, 5);
        card.Children.Add(divider);

        var footer = BuildCardFooterButton(footerLabel, footerAutomationName);
        footer.Click += (_, _) => onFooter();
        Grid.SetRow(footer, 6);
        card.Children.Add(footer);

        return card;
    }

    /// <summary>Short label for a card's type-tag badge — the mockup's own "App/Folder/File/Link/
    /// Shortcut/Group" short forms; the fuller word (e.g. "Application") is what the card's own
    /// first caption line already spells out via <c>item.Kind</c>.</summary>
    private static string KindBadgeLabel(OrbitItemKind kind) => kind switch
    {
        OrbitItemKind.Application => "App",
        OrbitItemKind.File => "File",
        OrbitItemKind.Folder => "Folder",
        OrbitItemKind.WebLink => "Link",
        OrbitItemKind.Shortcut => "Shortcut",
        OrbitItemKind.Group => "Group",
        _ => kind.ToString(),
    };

    /// <summary>The small rounded type-tag under a card's caption lines — background one step off
    /// the card (<c>ShellPillBrush</c>, the same token the Home screen's own meta pill uses), text
    /// in the same muted <see cref="SecondaryCaptionStyle"/> the caption lines above it use.</summary>
    private Border BuildKindBadge(string label) => new()
    {
        Background = ThemeHelper.Brush(Root, "ShellPillBrush"),
        CornerRadius = new CornerRadius(11),
        Padding = new Thickness(10, 3, 10, 3),
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = new TextBlock { Text = label, Style = SecondaryCaptionStyle },
    };

    /// <summary>The thin rule above a card's footer strip.</summary>
    private Border BuildCardDivider() => new()
    {
        Height = 1,
        Background = ThemeHelper.Brush(Root, "ShellDividerBrush"),
    };

    /// <summary>
    /// A card's footer strip — bold, uppercase, centered, accent-colored, cloning the
    /// <c>BoxOpenLabel</c> pattern the Home screen's own nav-box "OPEN" footer already uses — except
    /// the whole strip is a <see cref="Button"/> here rather than a label under one, so the entire
    /// footer is the click target and not just the text sitting inside it.
    /// </summary>
    private Button BuildCardFooterButton(string label, string automationName)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = label, Style = (Style)Root.Resources["BoxOpenLabel"] },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 10, 0, 0),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, automationName);
        return button;
    }

    /// <summary>The borderless icon button an item card and a group card both carry, for delete.</summary>
    private static Button BuildRowIconButton(int glyph, string tooltip, string automationName)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = char.ConvertFromUtf32(glyph), FontSize = 14 },
            Width = 40,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, automationName);
        return button;
    }

    /// <summary>
    /// The medallion at the head of a card: the item's own glyph to begin with, replaced by its real
    /// shell / folder / favicon icon if <see cref="IconService"/> resolves one. The same
    /// image-over-glyph pair the orbit itself draws, just built imperatively here rather than bound.
    /// A group resolves nothing (it has no target), so it simply keeps its group glyph.
    /// <para>
    /// Circular, filled with <c>ShellMedallionBrush</c> and stroked with <c>ShellCardBorderBrush</c>
    /// — the same two tokens the Home screen's own nav-box icon medallion uses — rather than the
    /// flat transparent 28px square this used to be: the vertical card design puts the icon on its
    /// own line at the top of the card instead of a horizontal row, so it needs the same visual
    /// weight a standalone medallion has, not a plate that only read right sitting flush beside text.
    /// </para>
    /// </summary>
    private FrameworkElement BuildItemIcon(OrbitItem item)
    {
        const double plate = 44;

        var glyph = new FontIcon
        {
            Glyph = item.Glyph,
            FontSize = 20,
            Foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Same non-Fluent-glyph handling the orbit and the preview do — "B64" and friends are tofu
        // boxes in the icon font.
        if (GlyphFonts.IsTextGlyph(item.Glyph))
            glyph.FontFamily = new FontFamily("Segoe UI");

        var image = new Image
        {
            Width = plate - 16,
            Height = plate - 16,
            Stretch = Stretch.Uniform,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var content = new Grid();
        content.Children.Add(glyph);
        content.Children.Add(image);

        LoadRowIcon(item, image, glyph);

        return new Border
        {
            Width = plate,
            Height = plate,
            CornerRadius = new CornerRadius(plate / 2),
            Background = ThemeHelper.Brush(Root, "ShellMedallionBrush"),
            BorderBrush = ThemeHelper.Brush(Root, "ShellCardBorderBrush"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = content,
        };
    }

    /// <summary>
    /// Fire-and-forget icon resolution for one row. Started on the UI thread, so the continuation
    /// comes back to it and can touch the two elements directly. A null result means "nothing
    /// resolved, or the user picked a glyph" — the glyph already on screen is then simply left
    /// alone.
    /// </summary>
    private static async void LoadRowIcon(OrbitItem item, Image image, FontIcon glyph)
    {
        var source = await IconService.LoadIconAsync(item);
        if (source is null) return;
        image.Source = source;
        image.Visibility = Visibility.Visible;
        glyph.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// <paramref name="Badge"/> is the short category tag the card grid shows under a tool's
    /// description (mockup: docs/images/6-swiftx-tools.svg) — purely cosmetic, no existing
    /// data-model field backed it before, so it lives here alongside the rest of a row's static
    /// content.
    /// </summary>
    private sealed record ToolRow(
        string Target, ToolKind Kind, string Title, string Description, string Badge, string Glyph,
        bool TextGlyph, Func<AppConfig, bool> GetShown, Action<AppConfig, bool> SetShown);

    // Built from the codepoint, not a literal character in source, so the glyph can't be
    // silently swapped for a different (or invisible) private-use character by a stray
    // copy/paste. See the memory note on this exact failure mode.
    private static readonly string DataGlyph = char.ConvertFromUtf32(0xE943);
    private static readonly string QuickFormatGlyph = char.ConvertFromUtf32(0xE917);
    private static readonly string SecureValuesGlyph = char.ConvertFromUtf32(0xE72E);
    private static readonly string CertificateGlyph = char.ConvertFromUtf32(0xEB95);

    private static readonly ToolRow[] ToolRows =
    [
        new("Data", ToolKind.Data, "Data formatter", "Auto-detect JSON or XML and format it.", "Formatter", DataGlyph, false,
            c => c.ShowDataTool, (c, v) => c.ShowDataTool = v),
        new("QuickFormat", ToolKind.QuickFormat, "Quick Format", "Auto-format your last copied JSON, XML, YAML or CSV.", "Clipboard", QuickFormatGlyph, false,
            c => c.ShowQuickFormatTool, (c, v) => c.ShowQuickFormatTool = v),
        new("SecureValues", ToolKind.SecureValues, "Secure values", "Local key/secret store with masked values.", "Vault", SecureValuesGlyph, false,
            c => c.ShowSecureValuesTool, (c, v) => c.ShowSecureValuesTool = v),
        new("Base64", ToolKind.Base64, "Base64", "Encode and decode Base64 text.", "Codec", "B64", true,
            c => c.ShowBase64Tool, (c, v) => c.ShowBase64Tool = v),
        new("Certificate", ToolKind.Certificate, "Certificate", "Inspect certificate, PKCS#12, and Java KeyStore files.", "Inspector", CertificateGlyph, false,
            c => c.ShowCertificateTool, (c, v) => c.ShowCertificateTool = v),
    ];

    // Bound as ToolsList's ItemsSource: WinUI's CanReorderItems drag pipeline mutates whichever
    // IList backs the list, so this is what actually moves when the user drags a card — observing
    // it (see ToolCards_CollectionChanged, wired in the constructor) is how a drag becomes an orbit
    // reorder.
    private readonly ObservableCollection<Border> _toolCards = new();

    // Guards ToolCards_CollectionChanged against RebuildToolsList's own Clear+Add below — a
    // reorder drag removes and reinserts a card (not a single Move — ObservableCollection only
    // raises that action for an explicit Move() call), so a rebuild and a drag look identical
    // from the event args alone; this flag is what tells them apart.
    private bool _rebuildingToolCards;

    private void RebuildToolsList()
    {
        _rebuildingToolCards = true;
        try
        {
            _toolCards.Clear();
            foreach (var row in ToolRows)
                _toolCards.Add(BuildToolCard(row));
        }
        finally
        {
            _rebuildingToolCards = false;
        }
        ToolsList.ItemsSource = _toolCards;
    }

    /// <summary>
    /// One tool's card in the wrapping grid <see cref="ToolsList"/>'s ItemsWrapGrid lays out —
    /// reuses <see cref="BuildCardShell"/> (the same icon/toggle/title/caption/badge/divider/footer
    /// shape the Elements screen's own cards build) rather than a parallel set of near-identical
    /// helpers, passing <see cref="BuildDragGrip"/>'s grip in as the shell's optional leading
    /// element and leaving <c>onDelete</c> null — a built-in tool can be hidden but never removed,
    /// so there is nothing for a delete button to do here. <see cref="Root"/>'s "Card" style still
    /// wraps the result and still carries <c>row.Target</c> as its <c>Tag</c>: that Tag is exactly
    /// what <see cref="ToolCards_CollectionChanged"/> reads back off <see cref="_toolCards"/> after
    /// a drag, so nothing about how a reorder becomes a persisted order changes here.
    /// </summary>
    private Border BuildToolCard(ToolRow row)
    {
        // The drag affordance — grabbing anywhere on the card works (this is WinUI's own
        // CanReorderItems drag, not a hand-rolled one), but a dedicated grip is what tells the user
        // this card moves at all. Six dots rather than a font glyph: no Segoe Fluent Icons codepoint
        // draws that pattern, and hand-placed dots can't silently turn into a different (or
        // invisible) private-use character the way a glyph literal could (see the memory note on
        // that failure mode).
        var grip = BuildDragGrip(row.Title, ThemeHelper.Brush(Root, "SwiftXForegroundBrush"));

        // The medallion plate Elements' own BuildItemIcon puts a resolved app/file/folder icon
        // into — a tool has no such resolvable icon, just its own fixed Fluent glyph (or, for
        // Base64, the literal "B64" text glyph row.TextGlyph flags), so this is that same glyph
        // relocated into an identical ⌀44 plate rather than new iconography.
        const double plate = 44;
        FrameworkElement glyph = row.TextGlyph
            ? new TextBlock
            {
                Text = row.Glyph,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
            : new FontIcon
            {
                Glyph = row.Glyph,
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                FontSize = 20,
                Foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        var icon = new Border
        {
            Width = plate,
            Height = plate,
            CornerRadius = new CornerRadius(plate / 2),
            Background = ThemeHelper.Brush(Root, "ShellMedallionBrush"),
            BorderBrush = ThemeHelper.Brush(Root, "ShellCardBorderBrush"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = glyph,
        };

        var kind = row.Kind;
        var card = BuildCardShell(
            icon,
            row.Title,
            row.Description,
            caption2: "",
            row.Badge,
            isHidden: !row.GetShown(_manager.Config),
            onToggle: isOn =>
            {
                if (_loading) return;
                row.SetShown(_manager.Config, isOn);
                ApplyToolVisibility();
                _manager.NotifyItemsChanged();
                RenderPreview();
            },
            onDelete: null,
            deleteAutomationName: null,
            footerLabel: "OPEN",
            footerAutomationName: "Open " + row.Title,
            onFooter: () => _manager.OpenTool(kind),
            leading: grip,
            wrapText: true);

        return new Border { Style = (Style)Root.Resources["Card"], Child = card, Tag = row.Target };
    }

    /// <summary>
    /// A <see cref="Grid"/> that exists solely to reach <see cref="UIElement.ProtectedCursor"/> —
    /// WinUI only exposes that setter to subclasses, so a plain <see cref="Grid"/> field can't
    /// change its own hover cursor without one.
    /// </summary>
    private sealed class CursorGrid : Grid
    {
        public void SetCursor(Microsoft.UI.Input.InputCursor? cursor) => ProtectedCursor = cursor;
    }

    /// <summary>
    /// A six-dot grip (two columns of three) at the leading edge of a tool card — the same
    /// affordance shape most reorderable lists use, so it reads as "drag me" without a tooltip
    /// even before the cursor changes over it. The cursor itself becomes a hand on hover, the
    /// closest built-in <see cref="Microsoft.UI.Input.InputSystemCursorShape"/> to a gripping hand.
    /// </summary>
    /// <param name="dotBrush">
    /// Resolved by the caller against this window's own theme (<see cref="ThemeHelper.Brush"/>),
    /// not looked up in here via <c>Application.Current.Resources</c> — this is a <c>static</c>
    /// helper with no <c>Root</c> to resolve against, and the app-wide ambient theme can disagree
    /// with this specific window's (Light Settings while Windows itself runs Dark, or vice versa),
    /// which is exactly what made the dots render in the wrong theme's color.
    /// </param>
    private static FrameworkElement BuildDragGrip(string title, Brush dotBrush)
    {
        var grid = new CursorGrid
        {
            Width = 8,
            Height = 16,
            ColumnSpacing = 2,
            RowSpacing = 2,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center,
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
                var dot = new Ellipse
                {
                    Width = 2,
                    Height = 2,
                    Fill = dotBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(dot, row);
                Grid.SetColumn(dot, column);
                grid.Children.Add(dot);
            }
        }

        ToolTipService.SetToolTip(grid, "Drag to reorder");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(grid, "Drag to reorder " + title);

        var handCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        grid.PointerEntered += (_, _) => grid.SetCursor(handCursor);
        grid.PointerExited += (_, _) => grid.SetCursor(null);

        return grid;
    }

    /// <summary>
    /// A reorder drag mutates <see cref="_toolCards"/> directly (WinUI's CanReorderItems drives
    /// the bound ItemsSource itself — as a remove-then-reinsert, not a single Move, so the action
    /// kind alone can't tell a drag apart from <see cref="RebuildToolsList"/>'s own Clear+Add;
    /// <see cref="_rebuildingToolCards"/> is what actually distinguishes them). Any change that
    /// gets here is therefore the user's own drag finishing.
    /// </summary>
    private void ToolCards_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_rebuildingToolCards) return;
        ApplyToolOrder(_toolCards.Select(c => (string)c.Tag).ToList());
    }

    /// <summary>
    /// Wherever two or more tool-kind items share a list (the top level, or inside a group like
    /// "Dev tools"), re-sorts just those items to match <paramref name="orderedTargets"/> — every
    /// other item in the list keeps its position. A tool kind can have more than one orbit instance
    /// at once (the default seed puts Data both inside "Dev tools" and at the top level, see
    /// <see cref="SeedDefaults"/>), so this walks the whole tree rather than assuming one.
    /// </summary>
    private void ApplyToolOrder(List<string> orderedTargets)
    {
        void Walk(List<OrbitItem> list)
        {
            var toolPositions = new List<int>();
            for (int i = 0; i < list.Count; i++)
                if (list[i].IsTool) toolPositions.Add(i);

            if (toolPositions.Count > 1)
            {
                var sorted = toolPositions
                    .Select(i => list[i])
                    .OrderBy(item => orderedTargets.FindIndex(
                        t => string.Equals(t, item.Target, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                for (int k = 0; k < toolPositions.Count; k++)
                    list[toolPositions[k]] = sorted[k];
            }

            foreach (var item in list.Where(x => x.IsGroup))
                Walk(item.Children);
        }
        Walk(_manager.Config.Primary.Items);

        _manager.NotifyItemsChanged();
        RenderPreview();
    }

    private void ApplyToolVisibility() =>
        ToolCatalog.ApplyVisibility(_manager.Config.Primary.Items, _manager.Config);
}
