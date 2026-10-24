using System.Collections.ObjectModel;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
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
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using SwiftX.Controls;
using SwiftX.Interop;
using SwiftX.Models;
using SwiftX.Services;
using Windows.ApplicationModel.DataTransfer;
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
    // Same reason: ShowScreen needs the tree built first. Null means the ordinary Home default:
    // only OrbitManager's OpenHomeScreen/OpenSettingsScreen/OpenOrbitScreen ever pass one.
    private readonly string? _initialScreen;
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private readonly AppTitleBar _titleBar;
    private bool _loading = true;
    private bool _radiusAdjusting;
    private bool _settingsCursorsApplied;
    private bool _toolsCursorsApplied;
    private bool _revealed;
    private DispatcherTimer? _revealFallbackTimer;
    // Orbit Layout preview only: when set, the name pills are hidden and each shows only while its
    // own node is hovered (see AddSlotLabel / HighlightPreviewPair). A view preference for this
    // screen, not saved — it resets to "shown" each time Settings opens.
    private bool _labelsHidden;

    /// <summary>Guards <see cref="ResetDefaults_Click"/>'s confirmation dialog against a second
    /// reset starting while the first is still awaiting an answer — the toolbar's own reset
    /// button and the left sidebar's (both wired to the same handler, before the sidebar's copy
    /// was removed) could otherwise each open their own dialog back to back.</summary>
    private bool _confirmingResetDefaults;

    /// <summary>Set by <c>Closed</c>. A callback queued on the dispatcher just before the window
    /// closed still runs after it, against elements that are no longer on screen.</summary>
    private bool _closed;

    /// <summary>
    /// Home Orbit / Tools idle marks — Orbit drifts continuously; Tools replays on a timer
    /// while Home is showing.
    /// </summary>
    private DispatcherTimer? _homeOrbitMarkTimer;
    private AppLogo.Mark? _homeOrbitMark;
    private AppLogo.Mark? _homeToolsMark;
    private DispatcherTimer? _homeHoverHideTimer;

    // Whether the marks above are playing, and the inputs HomeIdlePolicy decides it from. The marks
    // cost ~19% of a core while they play, so they rest unless someone is looking: see the policy.
    private bool _homeIdlePlaying;
    private bool _windowActive;
    private long _lastHomeActivityMs = Environment.TickCount64;
    private DispatcherTimer? _homeRestTimer;

    /// <summary>The one-shot behind <see cref="FlashGroupNotice"/>, tracked for the same reason
    /// the two above are: so <c>Closed</c> can stop it before it ticks at a window that is gone.</summary>
    private DispatcherTimer? _groupNoticeTimer;
    private bool _homeOrbitHoverOpen;
    private bool _homeToolsHoverOpen;
    private readonly Dictionary<Button, List<(DependencyObject Target, Brush? Brush)>> _orbitGlyphRest = new();
    private bool? _orbitEditDropUsable;
    private int _orbitEditDragToken;

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
    private int _dragTentativeGroupSlot = -1;

    /// <summary>The slot an external drag (an app/shortcut/link dragged in from outside SwiftX) is
    /// currently hovering, so <see cref="Preview_DragOver"/> only recomputes the highlight when the
    /// pointer actually crosses into a different slot. -1 means no external drag is over the ring,
    /// or it is over empty space between slots.</summary>
    private int _previewDropSlot = -1;

    // Preview geometry and slot occupancy, recorded by RenderPreview so the canvas-level pointer
    // handlers can work out which slot an event landed in without walking the visual tree.
    private readonly Border?[] _slotBorders = new Border?[OrbitMetrics.SlotCount];
    // The empty user slots' borders, kept apart from _slotBorders because every reader of that array
    // takes a non-null entry to mean "an item lives here". Only a drag-over needs these, to
    // highlight the place a drop would fill.
    private readonly Border?[] _emptySlotBorders = new Border?[OrbitMetrics.SlotCount];
    private Border? _hoveredPreviewOrigin;
    private readonly Dictionary<Border, Border> _previewHoverPartners = new();
    private readonly List<Action> _hoveredPreviewRestores = new();
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

    // Group-arc geometry and slot occupancy, RenderGroupArc's counterpart to _slotBorders. A
    // List rather than a fixed array because the slot count is however many children the group
    // has plus its two "add" ends, not a fixed layout — the leading and trailing add slots hold
    // a null border here so a real child's index in this list lines up with its index on the arc.
    private readonly List<Border?> _groupSlotBorders = new();

    // What the details card is currently about, and which group the canvas has open.
    // Neither swaps out which list the orbit renders anymore — a click just picks what, if anything,
    // is shown below the orbit — so plain "currently selected / currently expanded" fields replace
    // the old _editing/_editList-swapping model entirely.
    private OrbitItem? _selectedItem;
    private OrbitItem? _expandedGroup;

    // What "Replace…" (the right-click menu, or the popup details card) is currently replacing —
    // set by ReplaceOrbitItem/ReplaceQuadrantItem, read by AddToOrbitFromSidebar and
    // AddDroppedTargetsToOrbit so a click or a drop on the edit sidebar swaps that place instead of
    // appending to the orbit. Exactly one of the two is ever set at a time; see IsReplacing.
    private OrbitItem? _replaceItem;
    private int _replaceQuadrantSlot = -1;
    private string? _replaceItemName;

    private bool IsReplacing => _replaceItem is not null || _replaceQuadrantSlot >= 0;

    /// <summary>
    /// The "ADD TO ORBIT" sidebar row currently being dragged, if any. Set by the row's own
    /// DragStarting (see BuildEditRow) and read by Preview_DragOver/Preview_Drop once the drag
    /// reaches the ring — same-process, so the dragged <see cref="OrbitItem"/> template travels as
    /// a live reference here rather than round-tripping through the DataPackage the way a drag from
    /// outside SwiftX (Explorer, a browser) has to.
    /// </summary>
    private OrbitItem? _sidebarDragTemplate;

    /// <summary>
    /// How far the pointer has to travel off a sidebar row before the press counts as a drag rather
    /// than a click, matching the same 4px slack the ring's own bubble drag allows (see
    /// <see cref="Preview_PointerMoved"/>).
    /// </summary>
    private const double SidebarDragThreshold = 4;


    /// <param name="offerDesktopShortcut">
    /// Opens this window on Settings › General with the desktop-shortcut prompt showing. Set only
    /// by the first run (see <see cref="OrbitManager.Start"/>), because a Store-packaged SwiftX
    /// cannot create that shortcut at install time and so has to ask for it once, from inside the
    /// app.
    /// </param>
    public ShellWindow(OrbitManager manager, bool offerDesktopShortcut = false, string? initialScreen = null)
    {
        _manager = manager;
        _offerDesktopShortcut = offerDesktopShortcut;
        _initialScreen = initialScreen;
        InitializeComponent();

        // Every control this touches already exists once InitializeComponent returns — none of it
        // is nested inside a ScrollViewer or other template that defers its children past this
        // point (contrast OrbitSidebarSettings's cursor wiring, which has to wait for SizeChanged).
        // This window is built once from static XAML and never re-reads the language after that
        // (see Loc's own class doc comment), so one pass here is enough; a language change picked
        // in Settings takes effect the next time this window opens, same as every other XAML-literal
        // window in the app.
        ApplyLocalizedText();

        // No backdrop is attached here any more: ApplyTheme below attaches whichever one the theme
        // calls for (see ApplyBackdrop). Mica is still what four of the five get, and still
        // regardless of which — Light's own brushes stay fully opaque, so it never has anything
        // translucent to show through there. Attaching it here as well would only have built one
        // for Frosted glass to throw away a few lines later.

        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));
        Title = "SwiftX app";

        // Cloaked from here until the first screen has painted (see RevealWindow): OrbitManager
        // calls Activate() the instant this constructor returns, which makes the HWND visible a beat
        // before WinUI composes its first frame — and that beat is the blank white frame the window
        // used to flash on every cold open. Set before Activate so the window is never once drawn
        // uncloaked; the reveal is driven from the Loaded handler below and guaranteed by a fallback
        // timer so a slow or failed first paint can delay the window but never hide it for good.
        WindowChrome.SetCloaked(_hwnd, true);

        _titleBar = AppTitleBar.WithAppMark();
        Grid.SetRow(_titleBar, 0);
        Root.Children.Insert(0, _titleBar);
        // The Orbit screen's own way back to Home lives beside the mark instead of in its own
        // header now (see SetHomeButtonVisible's call in ShowScreen) — same Back_Click behaviour
        // as every other screen's own button, just relocated.
        _titleBar.HomeClicked += TitleBarHome_Click;

        // Extend content under the caption so the title bar strip is part of the same surface as
        // the rest of the window (AppTitleBar paints nothing of its own, so the window's own
        // background reaches all the way up). Must precede HideWindowBorder or
        // ExtendsContentIntoTitleBar clobbers the rim color.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(_titleBar);
        _titleBar.SetMarkVisible(false);

        // Built once here rather than in the Loaded handler that fills in the rest of the About
        // card: Loaded can fire again if the tree is ever re-parented, and the mark has nothing to
        // wait for — unlike the version string and the toggles, it reads no state.
        AboutMark.Content = AppLogo.Create(56);
        HomeBrandMark.Content = AppLogo.Create(30);

        WireOrbitGlyphHover(OrbitHomeButton);

        WireOrbitGlyphHover(OrbitSettingsButton);
        OrbitSettingsButton.Click += (_, _) => ShowScreen(ScreenSettings);

        // No disc on hover, same as the orbit's own icon chrome (WireOrbitGlyphHover) — just the
        // chevron tinting to the accent colour below. A hover/pressed fill used to be written here
        // too (the theme's own near-black-on-Light/white-on-Dark header colour), which read as the
        // button's background flipping to its opposite on hover rather than a normal highlight.
        SuppressChromeFill(SettingsBackButton);
        SettingsBackButton.PointerEntered += OrbitGlyph_PointerEntered;
        SettingsBackButton.PointerExited += OrbitGlyph_PointerExited;
        SettingsBackButton.PointerCanceled += OrbitGlyph_PointerExited;
        ToolsBackButton.PointerEntered += OrbitGlyph_PointerEntered;
        ToolsBackButton.PointerExited += OrbitGlyph_PointerExited;
        ToolsBackButton.PointerCanceled += OrbitGlyph_PointerExited;
        _homeOrbitMark = AppLogo.CreateFloating(160);
        HomeOrbitMarkHost.Content = _homeOrbitMark;
        _homeToolsMark = AppLogo.CreateFloating(160);
        HomeToolsMarkHost.Content = _homeToolsMark;

        PreviewCanvas.PointerPressed += Preview_PointerPressed;
        PreviewCanvas.PointerMoved += Preview_PointerMoved;
        PreviewCanvas.PointerReleased += Preview_PointerReleased;
        PreviewCanvas.PointerCaptureLost += (_, _) => EndPreviewDrag();
        PreviewCanvas.RightTapped += Preview_RightTapped;
        PreviewCanvas.AllowDrop = true;
        PreviewCanvas.DragOver += Preview_DragOver;
        PreviewCanvas.DragLeave += Preview_DragLeave;
        PreviewCanvas.Drop += Preview_Drop;

        WireOrbitGlyphHover(OrbitHelpButton);
        WireOrbitGlyphHover(LabelToggleButton);
        WireOrbitGlyphHover(OrbitResetButton);
        WireOrbitGlyphHover(ElementsShortcutButton);
        WireOrbitGlyphHover(ThemeToggleButton);
        WireOrbitGlyphHover(SettingsButton);
        WireOrbitGlyphHover(RadiusResetButton);
        SuppressChromeFill(OrbitBox);
        SuppressChromeFill(ToolsBox);

        RadiusSlider.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler(RadiusSlider_DragStarted), handledEventsToo: true);
        RadiusSlider.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler(RadiusSlider_DragEnded), handledEventsToo: true);
        RadiusSlider.AddHandler(UIElement.PointerCanceledEvent,
            new PointerEventHandler(RadiusSlider_DragEnded), handledEventsToo: true);
        RadiusSlider.PointerCaptureLost += RadiusSlider_DragEnded;

        // Any pointer or keyboard activity anywhere in the window wakes the Home marks (see
        // HomeIdlePolicy). On Root, a child element, so these handlers do not touch the window's own
        // lifetime (contrast OnWindowClosed); handledEventsToo because the controls consume most input.
        Root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Root_Activity), handledEventsToo: true);
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Root_Activity), handledEventsToo: true);
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Root_Activity), handledEventsToo: true);

        // The Orbit screen's own "ORBIT SETTINGS" sidebar (item size/shape, radius, hot corner,
        // hover style) is every bit as clickable as the ring and the "Add to Orbit" sidebar rows,
        // both of which already show the hand cursor (see WirePreviewHover/HandButton/HandBorder) —
        // its radio buttons, buttons, the slider and the toggle just never got the same treatment.
        // Deferred to OrbitSidebarSettings.SizeChanged rather than walked right here, for the same
        // reason EnsureOrbitShadowReceivers defers to PreviewCanvas's: OrbitSidebarSettings starts
        // Collapsed (nested in OrbitScreen, itself Collapsed until Orbit is first shown), and a
        // ScrollViewer's own template — which is what makes its content reachable from
        // VisualTreeHelper at all — is not applied until its first real layout pass, which a
        // Collapsed element never gets. SizeChanged firing is that pass having happened.
        OrbitSidebarSettings.SizeChanged += WireSidebarSettingsCursorsOnceSized;

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
        // The safety net for the cloak set above: if the Loaded handler's reveal never runs (a first
        // paint that throws, or is slow enough to be noticed), this fires once and reveals the window
        // anyway. 600ms is long enough that the Loaded path wins in the normal case and short enough
        // that a worst case is a brief delay rather than a window that looks like it failed to open.
        // Stopped in RevealWindow the moment either path reveals, and in OnWindowClosed otherwise.
        _revealFallbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _revealFallbackTimer.Tick += (_, _) => RevealWindow();
        _revealFallbackTimer.Start();

        // Every subscription below that targets this window is a named method and is removed again
        // in OnWindowClosed — including OnWindowClosed itself. That is load-bearing, not tidiness:
        // see OnWindowClosed for what happens to a window that leaves any of them in place.
        Activated += OnWindowActivated;

        // Minimise and restore, which activation alone does not track: a restore from the taskbar
        // activates the window while it is still minimised, and a minimise from the taskbar happens
        // after the window has already lost activation. Removed in OnWindowClosed like the rest.
        _appWindow.Changed += OnAppWindowChanged;

        // A DispatcherTimer keeps its tick handler — and through it this window — alive on the
        // dispatcher's own queue. OrbitManager drops its reference on Closed (see OpenSettings);
        // without this, the timer would hold the window past that and keep firing at a tree that is
        // no longer on screen.
        // OrbitMetrics raises this when Item size or Item shape changes. Nothing in this window
        // listened to it before, which is why picking a size wrote the setting and left the canvas
        // showing the old one until some unrelated edit forced a render. It is a static event, so
        // the handler it holds would keep this window alive past its Closed — hence the matching
        // unsubscribe in OnWindowClosed, in the same place the Home mark timer is stopped and for
        // the same reason.
        OrbitMetrics.Changed += OnMetricsChanged;

        // The manager outlives every ShellWindow (a new one is built each time Settings opens), so
        // this is the same hazard as the static event above, one level removed.
        _manager.ItemsChanged += OnItemsChanged;

        Closed += OnWindowClosed;

        if (_appWindow.Presenter is OverlappedPresenter p)
        {
            // Maximizing was already disabled (see IsMaximizable below); the window otherwise
            // defaulted to resizable, which let the user drag it away from the fixed, comfortable
            // size the three Orbit columns below are tuned for — cramping the canvas or leaving
            // the sidebars swimming in slack space. Fixed in both axes now, same as the maximize
            // button already was.
            p.IsResizable = false;
            p.IsMaximizable = false;
        }
        _appWindow.IsShownInSwitchers = true;

        // 1240 x 700 (was 1180 x 700, before the "ADD TO ORBIT" sidebar grew from 260 to 320 to
        // match Logi's own settings sidebar exactly): the Tools card is still 268 wide, and the
        // Orbit screen now splits the width three ways — a fixed 320px "ADD TO ORBIT" sidebar, a
        // 600px canvas column, and Logi's own fixed 320px settings sidebar — with the canvas
        // column still wide enough for the (now 520px, see PillCanvasWidth) label canvas inside it
        // with a 40px margin either side, same as before the left sidebar grew.
        WindowChrome.SetClientSizeDip(_appWindow, _hwnd, 1240, 700);
        WindowChrome.SetRoundedCorners(_hwnd);
        WindowChrome.CenterOnCursor(_appWindow, _appWindow.Id);

        Root.Loaded += async (_, _) =>
        {
            _loading = true;
            SelectTheme(_manager.Config.Theme);
            UpdateThemeToggleIcon();
            PopulateLanguageBox();
            SelectHoverStyle(_manager.Config.HoverStyle);
            SelectItemSize(_manager.Config.ItemSize);
            SelectItemShape(_manager.Config.ItemShape);
            RadiusSlider.Value = _manager.Config.OrbitRadius ?? OrbitMetrics.DefaultRadius;
            HotkeyEnabledToggle.IsOn = _manager.Config.HotkeyEnabled;
            MouseTriggerEnabledToggle.IsOn = _manager.Config.MouseTriggerEnabled;
            SelectMouseTriggerButton(_manager.Config.MouseTriggerButton);
            SelectMouseTriggerAction(_manager.Config.MouseTriggerAction);
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
            {
                HotkeyStatus.Text = Loc.Get("Settings.General.Hotkey.NeedsModifier");
                HotkeyStatus.Visibility = Visibility.Visible;
            };
            RebuildToolsList();
            // The left sidebar's "ADD TO ORBIT" categories used to be built lazily on first entry
            // into the old "editing" mode; now that the sidebar is permanently on screen there is
            // no later moment that still counts as lazy, so it is built once here with everything
            // else this handler already populates up front.
            BuildOrbitEditSidebar();
            SyncAccentControls();
            StartupToggle.IsOn = await StartupService.IsEnabledAsync();
            SyncDesktopShortcutToggle();
            VersionText.Text = Loc.Format("Settings.About.Version", typeof(App).Assembly.GetName().Version?.ToString() ?? "3.0.0");
            FillAboutDetails();
            // Wired here rather than in XAML because ThemeShadow.Receivers is a collection with no
            // XAML syntax of its own on a shared resource. Without a receiver the Home tiles
            // cast no shadow at all (outside a popup a ThemeShadow draws only onto its receivers),
            // which is the whole point of lifting them with Translation. Guarded on the resource
            // lookup rather than assumed: an element that is not yet in the visual tree cannot be a
            // receiver, and Loaded is the first moment HomeShadowPlate is. Try/catch for the same
            // reason EnsureOrbitShadowReceivers below has one: Receivers.Add on this exact
            // UIElementWeakCollection type has thrown "The parameter is incorrect" in a case Contains
            // and an ActualWidth/Height check both read as already safe, and a drop shadow is never
            // worth crashing the window over.
            try
            {
                if (Root.Resources["BoxShadow"] is ThemeShadow boxShadow &&
                    !boxShadow.Receivers.Contains(HomeShadowPlate))
                {
                    boxShadow.Receivers.Add(HomeShadowPlate);
                }
            }
            catch (Exception ex)
            {
                Diag.Log("Home shadow receiver registration failed (Home tiles will render without shadows): " + ex);
            }

            // Same trick for the orbit ring's discs, the group back button and the name pills: the
            // discs and the button sit on PreviewCanvas, the pills on PreviewLabelCanvas stacked
            // above it (see ShellWindow.xaml), so both need to be receivers (OrbitBubbleShadow,
            // in the resources above). Unlike HomeShadowPlate, OrbitScreen starts Collapsed (Home
            // is shown first below), so the canvases have no realized size yet here — registering
            // a zero-size element as a ThemeShadow receiver throws "The parameter is incorrect."
            // Deferred to EnsureOrbitShadowReceivers, which runs once the canvas actually has a
            // size (first visit to the Orbit screen).
            PreviewCanvas.SizeChanged += EnsureOrbitShadowReceivers;
            EnsureOrbitShadowReceivers(PreviewCanvas, null);

            _loading = false;
            RenderPreview();
            RebuildItemsList();
            UpdateHotkeyStatus();
            ApplyHandCursors(Root);
            // Home first, with Settings parked on its own default section so the rail is already
            // consistent the moment the gear is pressed.
            SelectSection(SectionGeneral);
            ShowScreen(ScreenHome);

            if (_offerDesktopShortcut)
            {
                // Land on Settings › General rather than Home so the offer and the switch it
                // corresponds to are both on screen at once — an InfoBar on a page the user has to
                // be told to go to is just a notification with extra steps. Takes priority over
                // _initialScreen below: the two never really coincide (a first run whose guide was
                // dismissed is not a launch that also named a specific screen), but if they ever
                // did, a one-time question the user hasn't answered yet outranks it.
                GoToGeneralPage();
                DesktopShortcutOffer.IsOpen = true;
            }
            else if (_initialScreen is not null)
            {
                ShowScreen(_initialScreen);
            }

            // The screen is populated and laid out; reveal the cloaked window now. Low priority so
            // it lands after this frame's render pass rather than in the middle of it, which is what
            // makes the window appear already painted instead of mid-build. Idempotent with the
            // fallback timer — whichever runs first wins (see RevealWindow).
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, RevealWindow);
        };
    }

    /// <summary>
    /// Sets every static, design-time-literal piece of text under the Orbit Layout screen and the
    /// Settings screen (nav rail, General/Support/Privacy/About, both sidebars, header and toolbar)
    /// from <see cref="Loc"/>, in <see cref="Loc.CurrentLanguage"/> as it stands at the moment this
    /// runs. Called once, right after <see cref="InitializeComponent"/>, to localize the window as
    /// it is first built, and again from <see cref="LanguageBox_SelectionChanged"/> after
    /// <c>OrbitManager.ApplyLanguage</c> re-points <see cref="Loc.CurrentLanguage"/>, so picking a
    /// new language takes effect on this window immediately rather than on the next launch. Safe to
    /// call more than once: it is a flat pass of property assignments with no event-subscription
    /// side effects. Add item, Onboarding and the orbit's own tooltips are unrelated windows/popups
    /// and still only pick up a language change the next time they open (see <see cref="Loc"/>'s own
    /// class doc comment).
    /// <para>
    /// Text that is computed at runtime instead of set once here — <see cref="HotkeyStatus"/>,
    /// <see cref="CheckForUpdatesLink"/>'s two labels, <see cref="VersionText"/>,
    /// <see cref="AboutFramework"/>, <see cref="RadiusReadout"/>,
    /// <see cref="HoverZoomDirectionText"/> and <see cref="OrbitEditSummary"/>/
    /// <see cref="OrbitEditEyebrow"/> — calls <see cref="Loc.Get"/>/<see cref="Loc.Format"/> directly
    /// at its own call site instead, so it stays correct every time it is recomputed, not just on
    /// this first pass.
    /// </para>
    /// <para>
    /// The Home screen, AddItemWindow, OnboardingWindow and the live OrbitWindow popup are out of
    /// scope here (a deliberate, separate follow-up) and untouched. So is everything built in code
    /// rather than declared in this window's XAML — the orbit canvas's own item-details flyout, the
    /// "Add to group" context menu, the Elements screen's cards, and the Animate hover style's motion
    /// picker — none of which is "both sidebars, header, toolbar" of the Orbit Layout screen.
    /// </para>
    /// </summary>
    private void ApplyLocalizedText()
    {
        // Mirrors the whole window for Arabic/Hebrew, the same way ToolWindowBase does for the tool
        // windows — see that type's own FlowDirection assignment. Read once here, like the rest of
        // this method: a language change takes effect the next time this window opens.
        Root.FlowDirection = Loc.CurrentIsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        // ---------- Orbit Layout: header / toolbar ----------
        OrbitLayoutTitle.Text = Loc.Get("OrbitLayout.Title");
        string howTo = Loc.Get("OrbitLayout.HowTo");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(OrbitHelpButton, howTo);
        ToolTipService.SetToolTip(OrbitHelpButton, howTo);
        OrbitHelpRunOnboardingItem.Text = Loc.Get("OrbitLayout.Help.RunOnboarding");
        OrbitHelpChooseWhatGoesOnItem.Text = Loc.Get("OrbitLayout.Help.ChooseWhatGoesOn");
        OrbitHelpReadHandbookItem.Text = Loc.Get("OrbitLayout.Help.ReadHandbook");
        string resetDefaults = Loc.Get("OrbitLayout.ResetDefaults");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(OrbitResetButton, resetDefaults);
        ToolTipService.SetToolTip(OrbitResetButton, resetDefaults);

        // ---------- Orbit Layout: left sidebar ("ADD TO ORBIT") ----------
        OrbitEditEyebrow.Text = Loc.Get("OrbitLayout.AddToOrbit.Eyebrow");
        OrbitEditCancelButton.Content = Loc.Get("Common.Cancel");
        OrbitEditCaption.Text = Loc.Get("OrbitLayout.AddToOrbit.Caption");
        OrbitEditDropOverlayText.Text = Loc.Get("OrbitLayout.AddToOrbit.DropToAdd");

        // ---------- Orbit Layout: right sidebar ("ORBIT SETTINGS") ----------
        OrbitSettingsEyebrow.Text = Loc.Get("OrbitLayout.Settings.Eyebrow");

        ItemSizeHeading.Text = Loc.Get("OrbitLayout.ItemSize.Heading");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ItemSizeInfoButton, Loc.Get("OrbitLayout.ItemSize.About"));
        ItemSizeInfoText.Text = Loc.Get("OrbitLayout.ItemSize.Info");
        string itemSizeSmall = Loc.Get("OrbitLayout.ItemSize.Small");
        string itemSizeMedium = Loc.Get("OrbitLayout.ItemSize.Medium");
        string itemSizeLarge = Loc.Get("OrbitLayout.ItemSize.Large");
        ItemSizeSmallOption.Content = itemSizeSmall;
        ItemSizeMediumOption.Content = itemSizeMedium;
        ItemSizeLargeOption.Content = itemSizeLarge;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ItemSizeSmallOption, Loc.Format("OrbitLayout.ItemSize.NameTemplate", itemSizeSmall));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ItemSizeMediumOption, Loc.Format("OrbitLayout.ItemSize.NameTemplate", itemSizeMedium));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ItemSizeLargeOption, Loc.Format("OrbitLayout.ItemSize.NameTemplate", itemSizeLarge));

        ItemShapeHeading.Text = Loc.Get("OrbitLayout.ItemShape.Heading");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ItemShapeInfoButton, Loc.Get("OrbitLayout.ItemShape.About"));
        ItemShapeInfoText.Text = Loc.Get("OrbitLayout.ItemShape.Info");
        string itemShapeCircle = Loc.Get("OrbitLayout.ItemShape.Circle");
        string itemShapeSquircle = Loc.Get("OrbitLayout.ItemShape.Squircle");
        string itemShapeSquare = Loc.Get("OrbitLayout.ItemShape.Square");
        string itemShapeHexagon = Loc.Get("OrbitLayout.ItemShape.Hexagon");
        ItemShapeCircleOption.Content = itemShapeCircle;
        ItemShapeSquircleOption.Content = itemShapeSquircle;
        ItemShapeSquareOption.Content = itemShapeSquare;
        ItemShapeHexagonOption.Content = itemShapeHexagon;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ItemShapeCircleOption, Loc.Format("OrbitLayout.ItemShape.NameTemplate", itemShapeCircle));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ItemShapeSquircleOption, Loc.Format("OrbitLayout.ItemShape.NameTemplate", itemShapeSquircle));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ItemShapeSquareOption, Loc.Format("OrbitLayout.ItemShape.NameTemplate", itemShapeSquare));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ItemShapeHexagonOption, Loc.Format("OrbitLayout.ItemShape.NameTemplate", itemShapeHexagon));
        ToolTipService.SetToolTip(ItemShapeSquircleOption, Loc.Get("OrbitLayout.ItemShape.SquircleTooltip"));
        ToolTipService.SetToolTip(ItemShapeHexagonOption, Loc.Get("OrbitLayout.ItemShape.HexagonTooltip"));

        RadiusHeading.Text = Loc.Get("OrbitLayout.Radius.Heading");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(RadiusInfoButton, Loc.Get("OrbitLayout.Radius.About"));
        RadiusInfoText.Text = Loc.Get("OrbitLayout.Radius.Info");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(RadiusResetButton, Loc.Get("OrbitLayout.Radius.ResetName"));
        ToolTipService.SetToolTip(RadiusResetButton, Loc.Get("OrbitLayout.Radius.ResetTooltip"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(RadiusSlider, Loc.Get("OrbitLayout.Radius.SliderName"));

        HotCornerHeading.Text = Loc.Get("OrbitLayout.HotCorner.Heading");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HotCornerInfoButton, Loc.Get("OrbitLayout.HotCorner.About"));
        HotCornerInfoText.Text = Loc.Get("OrbitLayout.HotCorner.Info");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HotCornerEnabledToggle, Loc.Get("OrbitLayout.HotCorner.ToggleName"));
        HotCornerLayoutOrbit.Content = Loc.Get("OrbitLayout.HotCorner.Orbit");
        HotCornerLayoutQuadrant.Content = Loc.Get("OrbitLayout.HotCorner.Quadrant");

        HoverStyleHeading.Text = Loc.Get("OrbitLayout.HoverStyle.Heading");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HoverStyleInfoButton, Loc.Get("OrbitLayout.HoverStyle.About"));
        HoverStyleInfoText.Text = Loc.Get("OrbitLayout.HoverStyle.Info");
        string hoverInvert = Loc.Get("OrbitLayout.HoverStyle.Invert");
        string hoverZoom = Loc.Get("OrbitLayout.HoverStyle.Zoom");
        string hoverTint = Loc.Get("OrbitLayout.HoverStyle.Tint");
        string hoverAnimate = Loc.Get("OrbitLayout.HoverStyle.Animate");
        HoverStyleInvertText.Text = hoverInvert;
        HoverStyleZoomText.Text = hoverZoom;
        HoverStyleTintText.Text = hoverTint;
        HoverStyleAnimateText.Text = hoverAnimate;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HoverStyleInvertButton, Loc.Format("OrbitLayout.HoverStyle.NameTemplate", hoverInvert));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HoverStyleZoomButton, Loc.Format("OrbitLayout.HoverStyle.NameTemplate", hoverZoom));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HoverStyleTintButton, Loc.Format("OrbitLayout.HoverStyle.NameTemplate", hoverTint));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HoverStyleAnimateButton, Loc.Format("OrbitLayout.HoverStyle.NameTemplate", hoverAnimate));
        string hoverColor = Loc.Get("OrbitLayout.HoverStyle.HoverColor");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HoverTintButton, hoverColor);
        ToolTipService.SetToolTip(HoverTintButton, hoverColor);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HoverTintPicker, Loc.Get("OrbitLayout.HoverStyle.HoverColorPicker"));
        HoverTintResetButton.Content = Loc.Get("Common.ResetToDefault");

        // ---------- Settings: nav rail ----------
        NavGeneralText.Text = Loc.Get("Settings.Nav.General");
        NavFeedbackText.Text = Loc.Get("Settings.Nav.Support");
        NavPrivacyText.Text = Loc.Get("Settings.Nav.Privacy");
        NavAboutText.Text = Loc.Get("Settings.Nav.About");
        string backToHome = Loc.Get("Settings.Nav.BackToHome");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SettingsBackButton, backToHome);
        ToolTipService.SetToolTip(SettingsBackButton, backToHome);
        SettingsEditorialSettingsText.Text = Loc.Get("Tray.Settings");

        DesktopShortcutOffer.Title = Loc.Get("Settings.General.DesktopOffer.Title");
        DesktopShortcutOffer.Message = Loc.Get("Settings.General.DesktopOffer.Message");
        CreateDesktopShortcutButton.Content = Loc.Get("Settings.General.DesktopOffer.CreateShortcut");

        // ---------- Settings: General ----------
        GeneralHeader.Text = Loc.Get("Settings.General.Header");
        ColorThemeHeading.Text = Loc.Get("Settings.General.ColorTheme");
        string themeFollowOs = Loc.Get("Settings.General.Theme.FollowOs");
        string themeLight = Loc.Get("Settings.General.Theme.Light");
        string themeDark = Loc.Get("Settings.General.Theme.Dark");
        string themeMidnight = Loc.Get("Settings.General.Theme.Midnight");
        string themeFrostedGlass = Loc.Get("Settings.General.Theme.FrostedGlass");
        ThemeCardSystemLabel.Text = themeFollowOs;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ThemeCardSystemButton, themeFollowOs);
        ThemeCardLightLabel.Text = themeLight;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ThemeCardLightButton, themeLight);
        ThemeCardDarkLabel.Text = themeDark;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ThemeCardDarkButton, themeDark);
        ThemeCardBlackLabel.Text = themeMidnight;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ThemeCardBlackButton, themeMidnight);
        ThemeCardFrostedLabel.Text = themeFrostedGlass;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ThemeCardFrostedButton, themeFrostedGlass);
        string frostDark = Loc.Get("Settings.General.Frost.Dark");
        string frostMedium = Loc.Get("Settings.General.Frost.Medium");
        string frostLight = Loc.Get("Settings.General.Frost.Light");
        ToolTipService.SetToolTip(FrostedLevelDarkButton, frostDark);
        ToolTipService.SetToolTip(FrostedLevelMediumButton, frostMedium);
        ToolTipService.SetToolTip(FrostedLevelLightButton, frostLight);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(FrostedLevelDarkButton, Loc.Format("Settings.General.Frost.NameTemplate", frostDark));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(FrostedLevelMediumButton, Loc.Format("Settings.General.Frost.NameTemplate", frostMedium));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(FrostedLevelLightButton, Loc.Format("Settings.General.Frost.NameTemplate", frostLight));

        HotkeyCardTitle.Text = Loc.Get("Settings.Hotkey");
        HotkeyCardBody.Text = Loc.Get("Settings.General.Hotkey.Body");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HotkeyEnabledToggle, Loc.Get("Settings.General.Hotkey.ToggleName"));

        MouseTriggerCardTitle.Text = Loc.Get("Settings.Mouse.Title");
        MouseTriggerCardBody.Text = Loc.Get("Settings.Mouse.Body");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MouseTriggerButtonBox, Loc.Get("Settings.Mouse.ButtonName"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MouseTriggerActionBox, Loc.Get("Settings.Mouse.GestureName"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MouseTriggerEnabledToggle, Loc.Get("Settings.Mouse.ToggleName"));
        MouseTriggerButtonLeftItem.Content = Loc.Get("Settings.Mouse.LeftButton");
        MouseTriggerButtonRightItem.Content = Loc.Get("Settings.Mouse.RightButton");
        MouseTriggerButtonMiddleItem.Content = Loc.Get("Settings.Mouse.MiddleButton");
        MouseTriggerButtonBackItem.Content = Loc.Get("Settings.Mouse.BackButton");
        MouseTriggerButtonForwardItem.Content = Loc.Get("Settings.Mouse.ForwardButton");
        MouseTriggerActionLongPressItem.Content = Loc.Get("Settings.Mouse.LongPress");
        MouseTriggerActionDoubleClickItem.Content = Loc.Get("Settings.Mouse.DoubleClick");
        MouseTriggerActionNoneItem.Content = Loc.Get("Settings.Mouse.None");

        LanguageCardTitle.Text = Loc.Get("Settings.General.Language");
        string accentColor = Loc.Get("Settings.General.AccentColor");
        AccentColorCardTitle.Text = accentColor;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AccentColorSwatch, accentColor);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AccentColorPicker, Loc.Get("Settings.General.AccentColorPicker"));
        AccentColorResetButton.Content = Loc.Get("Common.ResetToDefault");

        string startWithWindows = Loc.Get("Settings.General.StartWithWindows");
        StartWithWindowsTitle.Text = startWithWindows;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(StartupToggle, startWithWindows);
        string desktopShortcut = Loc.Get("Settings.General.DesktopShortcut");
        DesktopShortcutTitle.Text = desktopShortcut;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(DesktopShortcutToggle, desktopShortcut);

        BackupRestoreTitle.Text = Loc.Get("Settings.General.BackupRestore.Title");
        BackupRestoreBody.Text = Loc.Get("Settings.General.BackupRestore.Body");
        ExportButton.Content = Loc.Get("Settings.General.BackupRestore.Export");
        ImportButton.Content = Loc.Get("Settings.General.BackupRestore.Import");

        string resetTitle = Loc.Get("Settings.General.Reset.Title");
        ResetCardTitle.Text = resetTitle;
        ResetAllButton.Content = resetTitle;
        ResetCardBody.Text = Loc.Get("Settings.General.Reset.Body");

        // ---------- Settings: Support ----------
        SupportHeader.Text = Loc.Get("Settings.Feedback.Header");
        SetupGuideHeading.Text = Loc.Get("Settings.Feedback.SetupGuide.Heading");
        SetupGuideIntro.Text = Loc.Get("Settings.Feedback.SetupGuide.Intro");
        SetupGuideStep1.Text = Loc.Get("Settings.Feedback.SetupGuide.Step1");
        SetupGuideStep2.Text = Loc.Get("Settings.Feedback.SetupGuide.Step2");
        SetupGuideStep3.Text = Loc.Get("Settings.Feedback.SetupGuide.Step3");
        OpenGuideButton.Content = Loc.Get("Settings.Feedback.SetupGuide.OpenGuide");
        TroubleshootingHeading.Text = Loc.Get("Settings.Feedback.Troubleshooting.Heading");
        TroubleshootingBody.Text = Loc.Get("Settings.Feedback.Troubleshooting.Body");
        ReportIssueLink.Content = Loc.Get("Settings.Feedback.Troubleshooting.Link");
        FeedbackFooterRun.Text = Loc.Get("Settings.Feedback.Footer");
        ReadPrivacyLink.Inlines.Clear();
        ReadPrivacyLink.Inlines.Add(new Run { Text = Loc.Get("Settings.Feedback.ReadPrivacy") });

        // ---------- Settings: Privacy ----------
        PrivacyHeader.Text = Loc.Get("Settings.Privacy.Header");
        PrivacyDeviceHeading.Text = Loc.Get("Settings.Privacy.Device.Heading");
        PrivacyDeviceBody.Text = Loc.Get("Settings.Privacy.Device.Body");
        PrivacyNoTrackingHeading.Text = Loc.Get("Settings.Privacy.NoTracking.Heading");
        PrivacyNoTrackingBody.Text = Loc.Get("Settings.Privacy.NoTracking.Body");
        PrivacySecureValuesHeading.Text = Loc.Get("Settings.Privacy.SecureValues.Heading");
        PrivacySecureValuesBody.Text = Loc.Get("Settings.Privacy.SecureValues.Body");
        PrivacyToolsOfflineHeading.Text = Loc.Get("Settings.Privacy.ToolsOffline.Heading");
        PrivacyToolsOfflineBody.Text = Loc.Get("Settings.Privacy.ToolsOffline.Body");
        PrivacyNetworkHeading.Text = Loc.Get("Settings.Privacy.Network.Heading");
        PrivacyNetworkBody.Text = Loc.Get("Settings.Privacy.Network.Body");
        PrivacyTakeItBackHeading.Text = Loc.Get("Settings.Privacy.TakeItBack.Heading");
        PrivacyTakeItBackBody.Text = Loc.Get("Settings.Privacy.TakeItBack.Body");
        PrivacyRatingHeading.Text = Loc.Get("Settings.Privacy.Rating.Heading");
        PrivacyRatingBody.Text = Loc.Get("Settings.Privacy.Rating.Body");

        // ---------- Settings: About ----------
        AboutHeader.Text = Loc.Get("Settings.About.Header");
        AboutBlurb.Text = Loc.Get("Settings.About.Blurb");
        TermsLink.Content = Loc.Get("Settings.About.TermsAndConditions");
        AboutPrivacyLink.Content = Loc.Get("Settings.Nav.Privacy");

        FeedbackRatingTitle.Text = Loc.Get("Settings.About.FeedbackRating.Title");
        FeedbackRatingBody.Text = Loc.Get("Settings.About.FeedbackRating.Body");
        ShareFeedbackLink.Content = Loc.Get("Settings.About.FeedbackRating.ShareFeedback");
        RateOnStoreLink.Content = Loc.Get("Settings.About.FeedbackRating.RateOnStore");
        StarOnGitHubLink.Content = Loc.Get("Settings.About.FeedbackRating.StarOnGitHub");

        // Starts as "check"; CheckForUpdates_Click flips it to the install label itself once a
        // check finds something, so this only ever needs to set the resting state.
        CheckForUpdatesLink.Content = Loc.Get("Settings.About.Updates.CheckLink");
    }

    // PreviewCanvas is zero-size while OrbitScreen is Collapsed (Home shows first), and a
    // ThemeShadow.Receivers.Add on a zero-size element throws "The parameter is incorrect." This
    // fires on every PreviewCanvas SizeChanged until it has a real size — which happens the first
    // time the user opens the Orbit screen — then unsubscribes itself; the Contains guards make it
    // safe to also call eagerly from Loaded in case the canvas somehow already has a size by then.
    // Whether the ring's shadow receivers are registered — see EnsureOrbitShadowReceivers. A flag
    // rather than trusting ThemeShadow.Receivers.Contains (a UIElementWeakCollection, whose
    // membership check has not reliably prevented the add it was meant to guard — see that
    // method's own remarks) to tell this window it has already succeeded.
    private bool _orbitShadowReceiversRegistered;

    /// <summary>
    /// The actual root cause of every "The parameter is incorrect" this has thrown: a ThemeShadow
    /// receiver must not be an ancestor of whatever casts a shadow onto it, and PreviewCanvas /
    /// PreviewLabelCanvas — the original receivers — are exactly that, the parents of the discs and
    /// pills ApplyBubbleShadow lifts off them. That is a hard API rule, not a timing fluke, which is
    /// why it failed on the very first Receivers.Add every single time rather than intermittently.
    /// PreviewShadowPlate (see ShellWindow.xaml) is the fix: a dedicated sibling behind both
    /// canvases, the same shape HomeShadowPlate already is for the Home tiles, which is why that one
    /// never threw. Still deferred to a SizeChanged and wrapped in try/catch — a drop shadow is pure
    /// polish, never worth a crash over, and there is no reason to trust a next WinUI quirk here any
    /// more than the last one.
    /// </summary>
    private void EnsureOrbitShadowReceivers(object sender, SizeChangedEventArgs? e)
    {
        if (_orbitShadowReceiversRegistered) return;
        if (PreviewCanvas.ActualWidth == 0 || PreviewCanvas.ActualHeight == 0) return;

        try
        {
            if (Root.Resources["OrbitBubbleShadow"] is ThemeShadow orbitShadow)
                orbitShadow.Receivers.Add(PreviewShadowPlate);
            _orbitShadowReceiversRegistered = true;
            PreviewCanvas.SizeChanged -= EnsureOrbitShadowReceivers;
        }
        catch (Exception ex)
        {
            Diag.Log("EnsureOrbitShadowReceivers failed (orbit ring will render without shadows): " + ex);
        }
    }

    private bool _sidebarSettingsCursorsWired;

    /// <summary>
    /// Runs <see cref="WireSidebarSettingsCursors"/> once OrbitSidebarSettings actually has a size —
    /// see the subscription's own remarks for why a Collapsed ScrollViewer cannot be walked before
    /// that — then unsubscribes; nothing later ever un-sizes it back to zero, so there is no reason
    /// to keep listening.
    /// </summary>
    private void WireSidebarSettingsCursorsOnceSized(object sender, SizeChangedEventArgs e)
    {
        if (_sidebarSettingsCursorsWired) return;
        if (OrbitSidebarSettings.ActualWidth == 0 || OrbitSidebarSettings.ActualHeight == 0) return;

        WireSidebarSettingsCursors(OrbitSidebarSettings);
        _sidebarSettingsCursorsWired = true;
        OrbitSidebarSettings.SizeChanged -= WireSidebarSettingsCursorsOnceSized;
    }

    /// <summary>
    /// Gives every RadioButton, Button, Slider and ToggleSwitch under <paramref name="root"/> the
    /// same hand cursor the ring and the sidebar rows already show on hover (see
    /// <see cref="Controls.CursorHost"/>) — plain controls have no cursor of their own to set (see
    /// <see cref="Controls.HandCursor"/>'s own remarks), so Root borrows its for exactly as long as
    /// the pointer is over one, same as <see cref="HighlightPreviewPair"/> already does for a bubble.
    /// </summary>
    private void WireSidebarSettingsCursors(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is RadioButton or Button or Slider or ToggleSwitch)
            {
                var element = (UIElement)child;
                element.PointerEntered += (_, _) => Root.SetCursor(InputSystemCursorShape.Hand);
                element.PointerExited += (_, _) => Root.ClearCursor();
            }
            WireSidebarSettingsCursors(child);
        }
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        // Activation is "somebody is here" (the window just came forward, so wake the marks);
        // deactivation — another window took focus, or this one was minimised — starts their rest.
        // Recorded before the chrome is repainted, so that this event's state is the one that
        // stands even if repainting the chrome throws or raises another activation.
        _windowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (_windowActive)
            _lastHomeActivityMs = Environment.TickCount64;
        UpdateHomeIdle();
        ArmHomeRestTimer();

        ApplyChromeTheme();
    }

    /// <summary>
    /// Re-asks <see cref="HomeIdlePolicy"/> when the window is minimised, restored, shown or hidden
    /// (a minimise shows up as a move to Windows' minimised position). The rest timer is only
    /// touched when the marks actually start or stop here, so moving or resizing the window does not
    /// push back the rest of marks playing in a window nobody is using.
    /// </summary>
    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidVisibilityChange && !args.DidPresenterChange && !args.DidPositionChange && !args.DidSizeChange)
            return;

        bool wasPlaying = _homeIdlePlaying;
        UpdateHomeIdle();
        if (_homeIdlePlaying != wasPlaying)
            ArmHomeRestTimer();
    }

    private void Root_Activity(object sender, RoutedEventArgs e)
    {
        // Pointer-move arrives at the display's rate; once the marks are already playing the only
        // thing to do is push the deadline back, at most twice a second.
        long now = Environment.TickCount64;
        if (_homeIdlePlaying && now - _lastHomeActivityMs < 500)
            return;

        _lastHomeActivityMs = now;
        UpdateHomeIdle();
        ArmHomeRestTimer();
    }

    /// <summary>
    /// Starts or stops the Home marks to match <see cref="HomeIdlePolicy"/>. Cheap to call: it only
    /// does anything when the answer changes.
    /// </summary>
    private void UpdateHomeIdle()
    {
        if (_closed || _loading)
            return;

        bool should = HomeIdlePolicy.ShouldAnimate(
            onHome: _currentScreen == ScreenHome,
            windowShown: IsShownOnScreen(),
            windowActive: _windowActive,
            closed: _closed,
            sinceActivity: TimeSpan.FromMilliseconds(Environment.TickCount64 - _lastHomeActivityMs));

        if (should && !_homeIdlePlaying)
        {
            _homeIdlePlaying = true;
            StartHomeOrbitMark();
        }
        else if (!should && _homeIdlePlaying)
        {
            _homeIdlePlaying = false;
            StopHomeOrbitMark();
        }
    }

    /// <summary>
    /// Whether the window is on screen at all: shown, and not minimised. The marks may keep playing
    /// for a while in a window that is visible but not the active one (see
    /// <see cref="HomeIdlePolicy"/>), never in one nobody can see.
    /// </summary>
    private bool IsShownOnScreen() =>
        _appWindow.IsVisible &&
        _appWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    /// <summary>(Re)starts the one-shot that, once <see cref="HomeIdlePolicy.RestAfter"/> has passed
    /// without activity, lets the marks rest. Not armed while they are not playing.</summary>
    private void ArmHomeRestTimer()
    {
        if (_closed || !_homeIdlePlaying)
        {
            _homeRestTimer?.Stop();
            return;
        }

        _homeRestTimer ??= CreateHomeRestTimer();
        _homeRestTimer.Stop();
        _homeRestTimer.Start();
    }

    private DispatcherTimer CreateHomeRestTimer()
    {
        // A little past the policy's own limit, so a timer that fires a few milliseconds early
        // still finds the deadline passed — otherwise the marks would keep playing until the next
        // bit of activity, with nothing left to stop them.
        var timer = new DispatcherTimer { Interval = HomeIdlePolicy.RestAfter + TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            UpdateHomeIdle();
        };
        return timer;
    }

    private void OnMetricsChanged() => DispatcherQueue.TryEnqueue(RenderPreview);

    private void OnItemsChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed)
                return;

            // An edit from elsewhere (e.g. a delete on the Elements screen) can remove the
            // item the Orbit screen currently has selected or expanded — drop stale state rather than
            // leave the inspector or children row pointing at something that no longer exists.
            RefreshOpenGroup();
            // The corner can hold an item the orbit does not (see HotCornerItems), and its own edits
            // come through here now: one of those being selected is not a stale selection.
            if (_selectedItem is not null &&
                !ItemExists(_manager.Config.Primary.Items, _selectedItem) &&
                !ItemExists(_manager.Config.HotCornerOrbit, _selectedItem))
                HideItemDetails();
            RenderPreview();
            RebuildItemsList();
            // The edit sidebar's rows say whether each thing is already on the orbit, which a
            // delete or an add made anywhere else has just changed the answer to.
            if (_orbitEditBuilt) BuildOrbitEditSidebar();
        });
    }

    /// <summary>
    /// Everything that has to stop when the window closes — and the reason it must all stop.
    /// <para>
    /// <b>A <see cref="Window"/> that subscribes to its own events is never garbage collected.</b>
    /// <c>Window</c> is not a <c>DependencyObject</c>, so it does not take part in the XAML
    /// reference-tracking that lets the collector see through a managed-to-native cycle: a delegate
    /// on the window's own <c>Activated</c> or <c>Closed</c> that targets the window is a strong
    /// reference the native side holds for as long as it lives, and the native window lives for as
    /// long as that reference is there. Measured on a minimal WinUI 3 app (Windows App SDK 2.4,
    /// .NET 10, 30 windows opened and closed, full GC afterwards): 30 of 30 windows alive with
    /// <c>Activated</c> or <c>Closed</c> subscribed to a method of the window; 0 of 30 with no
    /// subscription, with a weak-reference delegate target, or with every one of them removed in
    /// <c>Closed</c> (the pattern here). Leaving a single one subscribed leaks all 30.
    /// </para>
    /// <para>
    /// For this window that was ~7 MB, ~49 handles and ~39 GDI objects per Settings open (a heap
    /// dump after 15 opens held 15 live windows), so the process's 10 000 GDI-object quota would
    /// have run out after ~250 opens, and every closed window kept re-running
    /// <c>RenderPreview</c> and <c>RebuildItemsList</c> on each edit. Now ~1 MB, ~33 handles and
    /// ~26 GDI per open, and one live window in a dump after 40 opens; what is left is the
    /// framework's own cost of a custom title bar, which application code cannot remove.
    /// Unsubscribing only the <see cref="OrbitManager.ItemsChanged"/> handler, and stopping the Home
    /// animations, did not change the numbers: it is the window's own two events that matter.
    /// <c>EventSubscriptionLifetimeTests</c> pins the rule for every closeable window.
    /// </para>
    /// </summary>
    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnWindowClosed;
        Activated -= OnWindowActivated;
        _appWindow.Changed -= OnAppWindowChanged;
        _closed = true;
        _homeIdlePlaying = false;
        _homeRestTimer?.Stop();
        _homeRestTimer = null;
        StopHomeOrbitMark();
        CancelHomeHoverHide();
        StopGroupNoticeFlash();
        _revealFallbackTimer?.Stop();
        _revealFallbackTimer = null;
        OrbitMetrics.Changed -= OnMetricsChanged;
        _manager.ItemsChanged -= OnItemsChanged;
    }

    /// <summary>
    /// Reveals the window that the constructor cloaked (see <see cref="WindowChrome.SetCloaked"/>),
    /// once — the first caller wins and the fallback timer is stopped. Driven from two places so the
    /// window is shown the instant it is ready but can never be stranded invisible: the normal path
    /// is a low-priority enqueue from the Loaded handler, which runs after the first screen has laid
    /// out and painted; the safety net is <see cref="_revealFallbackTimer"/>, which fires regardless
    /// if that first paint is slow or throws.
    /// </summary>
    private void RevealWindow()
    {
        if (_revealed || _closed) return;
        _revealed = true;
        _revealFallbackTimer?.Stop();
        _revealFallbackTimer = null;
        WindowChrome.SetCloaked(_hwnd, false);
    }

    /// <summary>
    /// Keeps the group the canvas has open honest after the orbit changed under it: closes it when
    /// the thing it showed is gone, and swaps in the live object when it was only ever a snapshot.
    /// <para>
    /// A group opened from the corner preview is not always one of the orbit's own items. The
    /// corner's QuickAccess is gathered afresh every time it is resolved (see
    /// <see cref="QuadrantLayout.IsQuickAccess"/>), so the object held here goes stale on the next
    /// link added or removed and is re-gathered rather than closed; it closes only when nothing is
    /// left to gather. A group the corner holds a copy of after the orbit dropped it is kept for
    /// as long as the corner holds it.
    /// </para>
    /// </summary>
    private void RefreshOpenGroup()
    {
        var group = _expandedGroup;
        if (group is null)
            return;

        if (QuadrantLayout.IsQuickAccess(group))
        {
            var fresh = QuadrantLayout.BuildQuickAccess(_manager.Config.Primary.Items);
            if (fresh is null)
                CollapseGroup();
            else
                _expandedGroup = fresh;
            return;
        }

        if (ItemExists(_manager.Config.Primary.Items, group))
            return;
        // A detached group (open from the sidebar's Groups list, off the ring) is not in Items, but
        // it is not gone either — keep it open so adding an item to it does not close it.
        if (_manager.Config.Primary.DetachedGroups.Contains(group))
            return;
        if (_manager.Config.HotCornerOrbit.Any(entry => ReferenceEquals(entry, group)))
            return;
        CollapseGroup();
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
    /// button on them, all built in code by <see cref="BuildCardShell"/> (Elements) or <see cref="BuildToolCardShell"/> (Tools) — kept the colours they
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
        SelectHoverStyle(_manager.Config.HoverStyle);
        SelectSection(_currentSection);
        UpdateThemeCardSelection(_manager.Config.Theme);
        UpdateFrostedLevelSelection(_manager.Config.FrostedGlass);
        UpdateThemeToggleIcon();
        RebuildItemsList();
        RebuildToolsList();
        if (_orbitEditBuilt) BuildOrbitEditSidebar();
        if (_howToOpen) BuildHowToGuide();
        RefreshChromeGlyphColors();
        UpdateLabelToggleVisual();
        UpdateElementsGlyphs();
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
            if (SystemBackdrop is FrostedGlassBackdrop frosted)
            {
                // Already the right kind, so the level is the only thing that can have moved —
                // and a float written onto the live controller is all that takes. Rebuilding to
                // change it would tear the composition target down and flicker the window.
                frosted.UpdateProperties();
            }
            else
            {
                SystemBackdrop = new FrostedGlassBackdrop();
            }
            WindowChrome.SuppressClientErase(_hwnd);
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

        // Logi's three accent-derived Settings tokens. They are StaticResource aliases inside each
        // theme dictionary (Themes/Colors.xaml), and a StaticResource alias resolves once, at the
        // point that dictionary is parsed — so shadowing SettingsAccentBrush above does not reach
        // them and they would have stayed on the built-in teal while every other accented control
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
        int rim = _manager.Config.Theme switch
        {
            AppTheme.Black => ShellRimColorBlack,
            AppTheme.FrostedGlass => ShellRimColorFrosted,
            _ => dark ? ShellRimColorDark : ShellRimColorLight,
        };
        WindowChrome.ApplyShellFrame(
            _appWindow, _hwnd, dark,
            frosted: _manager.Config.Theme == AppTheme.FrostedGlass,
            rim,
            resizable: false);
    }

    /// <summary>
    /// Rim colors for opaque shell themes, as COLORREFs (0x00BBGGRR) — these must track
    /// <c>ShellBackgroundBrush</c> in <c>Themes/Colors.xaml</c> (Logi's own #2B2B2B and #F2F2F2),
    /// not the shared black/white every other window's rim still uses. Leave one stale after a
    /// palette change and the window grows a visible hairline where the rim stops matching the
    /// surface behind it. All three are neutral greys, so channel order is moot.
    /// <see cref="ShellRimColorBlack"/> matches <see cref="BlackOverrides"/>' own page value rather
    /// than Dark's. Frosted Glass uses the acrylic tint rather than
    /// <see cref="NativeMethods.DWMWA_COLOR_NONE"/>, which painted a white caption along the top.
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

    // Internal rather than private: OrbitManager's OpenHomeScreen/OpenSettingsScreen/OpenOrbitScreen
    // pass these straight through to the constructor's initialScreen and to NavigateTo, so there is
    // exactly one place these five strings are spelled.
    internal const string ScreenHome = "home";
    internal const string ScreenOrbit = "orbit";
    internal const string ScreenTools = "tools";
    internal const string ScreenElements = "elements";
    internal const string ScreenSettings = "settings";

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
    /// <summary>
    /// Switches an already-open window straight to <paramref name="screen"/> — what
    /// <see cref="OrbitManager"/>'s <c>OpenHomeScreen</c>/<c>OpenSettingsScreen</c>/<c>OpenOrbitScreen</c>
    /// call when this window already exists, so asking for one of them lands there even if the
    /// window was sitting on something else.
    /// </summary>
    internal void NavigateTo(string screen) => ShowScreen(screen);

    private void ShowScreen(string screen)
    {
        _currentScreen = screen;
        HomeScreen.Visibility = screen == ScreenHome ? Visibility.Visible : Visibility.Collapsed;
        OrbitScreen.Visibility = screen == ScreenOrbit ? Visibility.Visible : Visibility.Collapsed;
        ToolsScreen.Visibility = screen == ScreenTools ? Visibility.Visible : Visibility.Collapsed;
        ElementsScreen.Visibility = screen == ScreenElements ? Visibility.Visible : Visibility.Collapsed;
        SettingsScreen.Visibility = screen == ScreenSettings ? Visibility.Visible : Visibility.Collapsed;

        // Arriving on Home counts as activity; leaving it ends the marks. Whether they actually play
        // is HomeIdlePolicy's call (window active, motion not reduced, ...), not this method's.
        if (screen == ScreenHome)
            _lastHomeActivityMs = Environment.TickCount64;
        UpdateHomeIdle();
        ArmHomeRestTimer();
        if (screen == ScreenOrbit)
        {
            // An open group is not a preference — coming back to the screen should show the
            // orbit, not whichever group you happened to be inside when you left.
            CollapseGroup();
            RenderPreview();
        }
        if (screen == ScreenElements) RebuildItemsList();
        if (screen == ScreenTools) RebuildToolsList();
        if (screen == ScreenSettings && !_settingsCursorsApplied)
        {
            _settingsCursorsApplied = true;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => ApplyHandCursors(SettingsScreen));
        }
        if (screen == ScreenTools && !_toolsCursorsApplied)
        {
            _toolsCursorsApplied = true;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => ApplyHandCursors(ToolsScreen));
        }
        UpdateElementsGlyphs();
        _titleBar.SetMarkVisible(screen != ScreenHome);
        // Home button lives directly in the Orbit canvas header row (see OrbitScreen in
        // ShellWindow.xaml), so the title bar never shows it any more.
        _titleBar.SetHomeButtonVisible(false);
        if (screen != ScreenHome)
        {
            HideHomeOrbitHover();
            HideHomeToolsHover();
            // The guide belongs to Home: leaving must not strand it open, waiting there the next time
            // Home is shown.
            HideHowTo(immediate: true);
        }
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
        SettingsScroll.HorizontalScrollMode = ScrollMode.Disabled;
        SettingsScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        bool compact = section is SectionGeneral or SectionAbout;
        SettingsScroll.VerticalScrollMode = compact ? ScrollMode.Disabled : ScrollMode.Enabled;
        SettingsScroll.VerticalScrollBarVisibility = compact
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;
    }

    /// <summary>
    /// Starts Home idle marks: Orbit keeps a small hub with a slow continuous ring drift;
    /// Tools replays the tile-spin grid every five seconds. Called only when
    /// <see cref="HomeIdlePolicy"/> says they may play, and undone by <see cref="StopHomeOrbitMark"/>
    /// when it says they may not — they cost ~19% of a core while they run.
    /// </summary>
    private void StartHomeOrbitMark()
    {
        // The timer only: this starts the marks, it never resets them.
        StopHomeOrbitTimer();
        _homeOrbitMark?.PlayOrbitDock();
        _homeToolsMark?.PlayEncode();
        _homeOrbitMarkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _homeOrbitMarkTimer.Tick += (_, _) =>
        {
            // Orbit drifts forever once started; only Tools needs a periodic replay.
            _homeToolsMark?.PlayEncode();
        };
        _homeOrbitMarkTimer.Start();
    }

    /// <summary>
    /// Ends the Home idle: the replay timer <em>and</em> the animations on both marks.
    /// <para>
    /// Stopping the timer alone is what this used to do, which left the Orbit mark's ring turning
    /// on a <c>Forever</c> composition animation — one that never ends by itself — after Home was
    /// left and after the window was closed. (Not what kept closed windows in memory: see
    /// <see cref="OnWindowClosed"/>.)
    /// </para>
    /// </summary>
    private void StopHomeOrbitMark()
    {
        StopHomeOrbitTimer();
        _homeOrbitMark?.Reset();
        _homeToolsMark?.Reset();
    }

    private void StopHomeOrbitTimer()
    {
        if (_homeOrbitMarkTimer is null) return;
        _homeOrbitMarkTimer.Stop();
        _homeOrbitMarkTimer = null;
    }

    private void UpdateElementsGlyphs()
    {
        var rest = ThemeHelper.Brush(Root, "SettingsHeaderBrush");
        var accent = new SolidColorBrush(CurrentAccentColor());
        if (ElementsShortcutGlyph is not null)
            ElementsShortcutGlyph.Foreground = _currentScreen == ScreenElements ? accent : rest;
    }

    private void HomeOrbitHover_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        CancelHomeHoverHide();
        HideHomeToolsHover();
        ShowHomeOrbitHover();
    }

    private void HomeOrbitHover_PointerExited(object sender, PointerRoutedEventArgs e) =>
        ScheduleHomeHoverHide(HideHomeOrbitHover);

    private void HomeToolsHover_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        CancelHomeHoverHide();
        HideHomeOrbitHover();
        ShowHomeToolsHover();
    }

    private void HomeToolsHover_PointerExited(object sender, PointerRoutedEventArgs e) =>
        ScheduleHomeHoverHide(HideHomeToolsHover);

    private void HomeOrbitHoverAdd_Click(object sender, RoutedEventArgs e) => ShowScreen(ScreenOrbit);

    private void ShowHomeOrbitHover()
    {
        int used = OrbitSlots.Used(_manager.Config.Primary.Items);
        int slots = OrbitMetrics.UserSlotCount;
        HomeOrbitHoverCount.Text = $"{used} of {slots} slots in use";
        HomeOrbitHoverSummary.Text =
            "Pick what sits on the orbit, how large the items are, and whether a screen corner summons it.";
        string hotkey = _manager.Config.HotkeyEnabled
            ? (_manager.Config.Hotkey ?? HotkeyGesture.Default.ToString())
            : "Off";
        string corner = _manager.Config.HotCornerEnabled ? "Hot corner on" : "Hot corner off";
        HomeOrbitHoverMeta.Text = $"Shortcut  {hotkey}  •  {corner}";

        HomeOrbitHoverPanel.Visibility = Visibility.Visible;
        HomeOrbitHoverSlide.X = _homeOrbitHoverOpen ? 0 : -28;
        HomeOrbitHoverPanel.Opacity = _homeOrbitHoverOpen ? 1 : 0;
        _homeOrbitHoverOpen = true;

        var fade = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(180) };
        Storyboard.SetTarget(fade, HomeOrbitHoverPanel);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var slide = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(180) };
        Storyboard.SetTarget(slide, HomeOrbitHoverSlide);
        Storyboard.SetTargetProperty(slide, "X");
        var sb = new Storyboard();
        sb.Children.Add(fade);
        sb.Children.Add(slide);
        sb.Begin();
    }

    private void HideHomeOrbitHover()
    {
        _homeOrbitHoverOpen = false;
        HomeOrbitHoverPanel.Visibility = Visibility.Collapsed;
        HomeOrbitHoverPanel.Opacity = 0;
        HomeOrbitHoverSlide.X = -28;
    }

    private void ShowHomeToolsHover()
    {
        var tools = ToolCatalog.All.ToList();
        int shown = tools.Count(t => _manager.Config.IsToolShown(t.Kind));
        HomeToolsHoverCount.Text = $"{shown} of {tools.Count} tools shown";
        HomeToolsHoverSummary.Text =
            "Quick utilities that live in SwiftX — formatters, secrets, and the rest. Toggle which ones appear on the Dev Tools page.";
        HomeToolsHoverMeta.Text = shown == 0
            ? "Every tool is currently hidden."
            : string.Join("  •  ", tools.Where(t => _manager.Config.IsToolShown(t.Kind)).Select(t => t.DisplayName));

        if (!_homeToolsHoverOpen)
        {
            HomeToolsHoverIcons.Children.Clear();
            foreach (var tool in tools.Where(t => _manager.Config.IsToolShown(t.Kind)))
            {
                var icon = new FontIcon
                {
                    Glyph = tool.Glyph,
                    FontSize = 16,
                    Foreground = ThemeHelper.Brush(Root, "SettingsHeaderBrush"),
                };
                var button = new Button
                {
                    Width = 32,
                    Height = 32,
                    Padding = new Thickness(0),
                    CornerRadius = new CornerRadius(8),
                    Content = icon,
                };
                SuppressChromeFill(button);
                WireOrbitGlyphHover(button);
                ToolTipService.SetToolTip(button, tool.DisplayName);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tool.DisplayName);
                var kind = tool.Kind;
                button.Click += (_, _) => _manager.OpenTool(kind);
                HomeToolsHoverIcons.Children.Add(button);
            }
        }

        HomeToolsHoverPanel.Visibility = Visibility.Visible;
        HomeToolsHoverSlide.X = _homeToolsHoverOpen ? 0 : 28;
        HomeToolsHoverPanel.Opacity = _homeToolsHoverOpen ? 1 : 0;
        _homeToolsHoverOpen = true;

        var fade = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(180) };
        Storyboard.SetTarget(fade, HomeToolsHoverPanel);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var slide = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(180) };
        Storyboard.SetTarget(slide, HomeToolsHoverSlide);
        Storyboard.SetTargetProperty(slide, "X");
        var sb = new Storyboard();
        sb.Children.Add(fade);
        sb.Children.Add(slide);
        sb.Begin();
    }

    private void HideHomeToolsHover()
    {
        _homeToolsHoverOpen = false;
        HomeToolsHoverPanel.Visibility = Visibility.Collapsed;
        HomeToolsHoverPanel.Opacity = 0;
        HomeToolsHoverSlide.X = 28;
        HomeToolsHoverIcons.Children.Clear();
    }

    private void ScheduleHomeHoverHide(Action hide)
    {
        CancelHomeHoverHide();
        _homeHoverHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _homeHoverHideTimer.Tick += (_, _) =>
        {
            CancelHomeHoverHide();
            hide();
        };
        _homeHoverHideTimer.Start();
    }

    private void CancelHomeHoverHide()
    {
        if (_homeHoverHideTimer is null) return;
        _homeHoverHideTimer.Stop();
        _homeHoverHideTimer = null;
    }

    // ---- How-to guide ----------------------------------------------------------------------

    /// <summary>Width of the guide's drawer, which is also how far off-screen it waits.</summary>
    private const double HowToPanelWidth = 400;

    private bool _howToOpen;

    /// <summary>
    /// Bumped by every open and close, so that the end of a close animation collapses the drawer
    /// only if nothing has opened it again in the meantime.
    /// </summary>
    private int _howToGeneration;

    private Storyboard? _howToStoryboard;

    private void HowToTab_Click(object sender, RoutedEventArgs e) => ShowHowTo();

    private void HowToClose_Click(object sender, RoutedEventArgs e) => HideHowTo();

    private void HowToPanel_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape)
            return;
        e.Handled = true;
        HideHowTo();
    }

    /// <summary>
    /// Slides the guide in over the main area of the Home screen (the row under the brand and
    /// toolbar, which it leaves clear). It is a drawer inside this window rather than a second
    /// window: it cannot be lost behind the app, it needs no window management, and closing it
    /// leaves the user exactly where they were. The content is rebuilt on every open, from the live
    /// configuration, so it always names the shortcut and corner the user has now.
    /// <para>
    /// With animations off in Windows the drawer simply appears, the same courtesy the orbit's own
    /// Animate hover style extends (see <see cref="ReducedMotion"/>).
    /// </para>
    /// </summary>
    private void ShowHowTo()
    {
        if (_howToOpen)
            return;
        _howToOpen = true;
        _howToGeneration++;
        _howToStoryboard?.Stop();

        BuildHowToGuide();
        HowToTab.Visibility = Visibility.Collapsed;
        HowToPanel.Visibility = Visibility.Visible;

        if (ReducedMotion.IsActive())
        {
            HowToPanelSlide.X = 0;
        }
        else
        {
            HowToPanelSlide.X = HowToPanelWidth;
            RunHowToStoryboard(slideTo: 0, milliseconds: 240, ease: EasingMode.EaseOut, then: () =>
            {
                // Hand the end position back to the base value so the next animation starts from it
                // instead of from a value the finished one is still holding.
                HowToPanelSlide.X = 0;
            });
        }

        HowToCloseButton.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Slides the guide back out. <paramref name="immediate"/> skips the motion, for when the
    /// drawer has to be gone before the screen it sits on is (see <see cref="ShowScreen"/>).
    /// </summary>
    private void HideHowTo(bool immediate = false)
    {
        if (!_howToOpen)
            return;
        _howToOpen = false;
        int generation = ++_howToGeneration;
        _howToStoryboard?.Stop();
        HowToTab.Visibility = Visibility.Visible;

        void Finish()
        {
            if (generation != _howToGeneration)
                return;
            HowToPanelSlide.X = HowToPanelWidth;
            HowToPanel.Visibility = Visibility.Collapsed;
        }

        if (immediate || ReducedMotion.IsActive())
            Finish();
        else
            RunHowToStoryboard(
                slideTo: HowToPanelWidth, milliseconds: 180, ease: EasingMode.EaseIn, then: Finish);

        // Back to the tab that opened it, so a keyboard user is not left with nothing focused.
        if (!immediate)
            HowToTab.Focus(FocusState.Programmatic);
    }

    private void RunHowToStoryboard(double slideTo, int milliseconds, EasingMode ease, Action then)
    {
        var slide = new DoubleAnimation
        {
            To = slideTo,
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            EasingFunction = new CubicEase { EasingMode = ease },
        };
        Storyboard.SetTarget(slide, HowToPanelSlide);
        Storyboard.SetTargetProperty(slide, "X");

        var storyboard = new Storyboard();
        storyboard.Children.Add(slide);
        storyboard.Completed += (_, _) =>
        {
            // Only the storyboard still in charge gets to finish: a stopped one never completes, but
            // an old one that had already finished when a new one began must not undo it.
            if (!ReferenceEquals(_howToStoryboard, storyboard))
                return;
            storyboard.Stop();
            then();
        };
        _howToStoryboard = storyboard;
        storyboard.Begin();
    }

    /// <summary>
    /// Fills the drawer: one card per topic from <see cref="HowToGuide"/>, each a heading, a line
    /// that says where things stand now, numbered steps, and a button that goes to the place the
    /// topic is about. Rebuilt rather than repainted on a theme change (see
    /// <see cref="RepaintThemedContent"/>), because the badges carry local brushes.
    /// </summary>
    private void BuildHowToGuide()
    {
        HowToContent.Children.Clear();

        var accent = new SolidColorBrush(CurrentAccentColor());
        var badgeFill = ThemeHelper.Brush(Root, "ShellPillBrush");
        var badgeText = ThemeHelper.Brush(Root, "SwiftXForegroundBrush");

        foreach (var section in HowToGuide.Sections(_manager.Config))
        {
            var stack = new StackPanel { Spacing = 10 };

            var heading = new Grid { ColumnSpacing = 10 };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var glyph = new FontIcon
            {
                Glyph = section.Glyph,
                FontSize = 16,
                Foreground = accent,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var title = new TextBlock
            {
                Text = section.Title,
                Style = (Style)Root.Resources["SettingTitle"],
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHeadingLevel(
                title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
            Grid.SetColumn(title, 1);
            heading.Children.Add(glyph);
            heading.Children.Add(title);
            stack.Children.Add(heading);

            stack.Children.Add(new TextBlock
            {
                Text = section.Summary,
                Style = (Style)Root.Resources["SecondaryCaption"],
                TextWrapping = TextWrapping.Wrap,
            });

            for (int i = 0; i < section.Steps.Count; i++)
            {
                var step = new Grid { ColumnSpacing = 10 };
                step.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                step.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var number = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    Background = badgeFill,
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock
                    {
                        Text = (i + 1).ToString(),
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = badgeText,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                var text = new TextBlock
                {
                    Text = section.Steps[i],
                    Style = (Style)Root.Resources["SettingBody"],
                    IsTextSelectionEnabled = true,
                };
                Grid.SetColumn(text, 1);
                step.Children.Add(number);
                step.Children.Add(text);
                stack.Children.Add(step);
            }

            if (section.Action != GuideAction.None)
            {
                var action = section.Action;
                var button = new Button
                {
                    Content = section.ActionLabel,
                    Padding = new Thickness(14, 6, 14, 6),
                    CornerRadius = new CornerRadius(6),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 4, 0, 0),
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, section.ActionLabel);
                button.Click += (_, _) => RunGuideAction(action);
                stack.Children.Add(button);
            }

            HowToContent.Children.Add(new Border
            {
                Style = (Style)Root.Resources["Card"],
                Padding = new Thickness(16, 14, 16, 14),
                Child = stack,
            });
        }
    }

    /// <summary>
    /// Takes the user to what a topic is about. The drawer is closed first and without motion, since
    /// the screen it belongs to is about to be hidden under it.
    /// </summary>
    private void RunGuideAction(GuideAction action)
    {
        HideHowTo(immediate: true);
        switch (action)
        {
            case GuideAction.OpenOrbit:
            case GuideAction.OpenOrbitSettings:
                // ShowScreen lands on the orbit's own settings sidebar (editing is always off on
                // arrival), which is where both the arrangement and the hot corner live.
                ShowScreen(ScreenOrbit);
                break;
            case GuideAction.OpenGeneralSettings:
                GoToGeneralPage();
                break;
            case GuideAction.OpenElements:
                ShowScreen(ScreenElements);
                break;
        }
    }

    private void OrbitBox_Click(object sender, RoutedEventArgs e) => ShowScreen(ScreenOrbit);

    private void ToolsBox_Click(object sender, RoutedEventArgs e) => ShowScreen(ScreenTools);

    private void ElementsShortcutButton_Click(object sender, RoutedEventArgs e) => ShowScreen(ScreenElements);

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowScreen(ScreenSettings);

    /// <summary>Walks the visual tree rooted at <paramref name="root"/> and attaches
    /// PointerEntered/PointerExited handlers to every interactive control so the root
    /// <see cref="CursorHost"/> shows the pointing-hand cursor over them.</summary>
    private void ApplyHandCursors(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button or ComboBox or RadioButton or ToggleSwitch or HyperlinkButton or Slider)
            {
                var element = (UIElement)child;
                element.PointerEntered += (_, _) => Root.SetCursor(InputSystemCursorShape.Hand);
                element.PointerExited += (_, _) => Root.ClearCursor();
            }
            ApplyHandCursors(child);
        }
    }

    /// <summary>Every sub-screen's back arrow. There is one place to go back to — Home.</summary>
    private void Back_Click(object sender, RoutedEventArgs e)
    {
        // A replace armed on the Orbit screen must not survive leaving it — there is no path back
        // to this exact right-click or details-card Replace short of starting over, and letting the
        // state outlive the screen would have some unrelated later click on a sidebar elsewhere
        // silently complete a stale swap.
        EndReplace();
        ShowScreen(ScreenHome);
    }

    /// <summary>The Orbit screen's own Home button, relocated to the title bar (see
    /// SetHomeButtonVisible's call in ShowScreen) — otherwise exactly <see cref="Back_Click"/>.</summary>
    private void TitleBarHome_Click(object sender, RoutedEventArgs e) => Back_Click(sender, e);

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
    /// Header theme button. Light and Dark swap. Midnight hides the button entirely.
    /// Frosted glass cycles Light → Medium → Dark frost levels without leaving the theme.
    /// Follow OS uses the appearance currently on screen as Light/Dark.
    /// </summary>
    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_manager.Config.Theme == AppTheme.Black)
            return;

        if (_manager.Config.Theme == AppTheme.FrostedGlass)
        {
            var next = _manager.Config.FrostedGlass switch
            {
                FrostedGlassLevel.Light => FrostedGlassLevel.Medium,
                FrostedGlassLevel.Medium => FrostedGlassLevel.Dark,
                _ => FrostedGlassLevel.Light,
            };
            ApplyFrostedLevel(next);
            return;
        }

        ApplyThemeChoice(ThemeHelper.IsDark(Root) ? AppTheme.Light : AppTheme.Dark);
    }

    /// <summary>
    /// Glyph on the header theme button: brightness in Light, the power glyph in Dark.
    /// Hidden under Midnight. Frosted glass keeps the brightness glyph at every frost level.
    /// </summary>
    private void UpdateThemeToggleIcon()
    {
        var theme = _manager.Config.Theme;
        ThemeToggleButton.Visibility = theme == AppTheme.Black
            ? Visibility.Collapsed
            : Visibility.Visible;
        // The hairline that used to sit beside the Home Elements button stays hidden.
        ThemeToggleLeadingDivider.Visibility = Visibility.Collapsed;

        ThemeToggleIcon.Glyph = theme switch
        {
            // Frosted glass keeps the theme glyph at every frost level. Dark frost used to
            // borrow the power glyph (\uE708) that Dark mode uses.
            AppTheme.FrostedGlass => "\uE706",
            _ when ThemeHelper.IsDark(Root) => "\uE708",
            _ => "\uE706",
        };

        ToolTipService.SetToolTip(ThemeToggleButton, theme == AppTheme.FrostedGlass
            ? "Cycle frosted glass"
            : "Toggle theme");
    }

    /// <summary>
    /// The orbit preview's own canvas, square because <see cref="RenderQuadrantPreview"/> and
    /// <see cref="AddHotCornerButton"/> both place against <see cref="QuadrantLayout"/>, which
    /// works in one dimension. 380, not the original 470: shrunk, in the same proportion as
    /// <see cref="PillCanvasWidth"/>, to make room for the Orbit screen's new left sidebar. Must
    /// match PreviewCanvas's own Width/Height in ShellWindow.xaml — every position this file
    /// renders onto that canvas is computed relative to this constant, not read back off the
    /// element, so the two cannot drift independently.
    /// </summary>
    private const double PreviewCanvasSize = 380;

    /// <summary>
    /// How far, in device-independent pixels, a disc/pill/back-button is lifted off PreviewCanvas
    /// so its shared <c>OrbitBubbleShadow</c> (ShellWindow.xaml) has something to cast onto —
    /// ThemeShadow only draws once the element wearing it has a positive Z-translation. The value
    /// itself only has to be big enough that the shadow it produces is visible; it is not a real
    /// depth anything else on the canvas measures against.
    /// </summary>
    private const float BubbleElevation = 24;

    /// <summary>
    /// Lifts <paramref name="element"/> onto the ring's shared drop shadow — see
    /// <see cref="BubbleElevation"/> and the <c>OrbitBubbleShadow</c> resource in
    /// ShellWindow.xaml, whose only receiver is PreviewCanvas (wired in the window's Loaded
    /// handler). Matches docs/orbit-screen-layouts.html's <c>.disc</c> / <c>.pill</c> box-shadow —
    /// a soft shadow and no outline, rather than the 1px ring a BorderBrush used to draw.
    /// </summary>
    private void ApplyBubbleShadow(UIElement element)
    {
        element.Translation = new Vector3(0, 0, BubbleElevation);
        if (Root.Resources["OrbitBubbleShadow"] is ThemeShadow shadow)
            element.Shadow = shadow;
    }

    /// <summary>
    /// The label canvas behind it is wider, because a pill reaches further out sideways than the
    /// ring ever does. The two are centred on each other (see ShellWindow.xaml), so converting a
    /// point from the preview's coordinates to the label canvas's is this one constant. 520, not
    /// the original 640 (see <see cref="PreviewCanvasSize"/>'s own doc comment) — must match
    /// PreviewLabelCanvas's own Width in ShellWindow.xaml.
    /// </summary>
    private const double PillCanvasWidth = 520;
    private const double PillCanvasOffsetX = (PillCanvasWidth - PreviewCanvasSize) / 2;

    /// <summary>
    /// Logi's own label pill: 30 tall, 6px corners, 12,4 padding, 12px SemiBold. Width is the title
    /// itself: capping and ellipsis produced truncated names on hover ("To….").
    /// </summary>
    private const double PillHeight = 30;

    /// <summary>
    /// How far the pill reaches onto its node, as a fraction of the node's own diameter, rather than
    /// sitting clear of its edge: a caption overlaying the icon's outer rim, not a separate label
    /// floating beside it. The glyph inside is centred and far smaller than the 60% of the radius
    /// this leaves untouched at every bubble size SwiftX offers, so the overlap never reaches it.
    /// </summary>
    private const double PillOverlapFraction = 0.20;

    /// <summary>
    /// How far a group's or a tool's label is pushed out *past* its node edge instead, as a fraction
    /// of the node's diameter. Those two wear a mark on their outward rim (the group arc / notch),
    /// and the usual inward <see cref="PillOverlapFraction"/> would lay the label right over it; this
    /// keeps the label clear of the edge so the mark stays visible.
    /// </summary>
    private const double PillEdgeClearanceFraction = 0.06;

    /// <summary>
    /// The Settings canvas's own group/Tools arc gap, in place of <see cref="OrbitMetrics.FlyoutStepDegreesFor"/>'s
    /// default — passed explicitly at every call in this file so the live orbit (<c>OrbitWindow</c>,
    /// which takes that default unchanged) and this canvas can each have the spacing that actually
    /// suits them, after a shared constant tried to be both at once and read as too tight on one and
    /// then too loose on the other. Keep every call site below in step: they compute the same arc
    /// that <see cref="GroupSlotAt"/> hit-tests against, and disagreeing would misalign a click from
    /// what <see cref="RenderGroupArc"/> actually drew.
    /// </summary>
    private const double SettingsGroupArcItemGap = 40;

    /// <summary>
    /// The outward name pill for one filled slot, on <c>PreviewLabelCanvas</c> rather than the
    /// canvas the bubbles live on.
    /// <para>
    /// Title pills sit on a canvas stacked above the bubbles and handle their own taps so clicking
    /// a name opens the same details flyout as clicking the node. Empty canvas does not swallow
    /// hits, so bubble drag still lands on PreviewCanvas.
    /// </para>
    /// <para>
    /// Placement follows Logi's own table rather than a single polar formula: the top and bottom
    /// slots take their pill above and below, centred on the node, and the other six take theirs
    /// out to the side, vertically centred on the node. A pure radial placement instead pulls the
    /// four diagonals in towards the ring, where they crowd their neighbours.
    /// </para>
    /// </summary>
    private Border? AddSlotLabel(
        OrbitItem item, double cx, double cy, double angle, double radius, double bubble)
    {
        string text = item.DisplayName;
        if (string.IsNullOrWhiteSpace(text)) return null;

        // No BorderBrush/BorderThickness: docs/orbit-screen-layouts.html's .pill carries only a
        // shadow (ApplyBubbleShadow below), never an outline.
        var pill = new Border
        {
            Height = PillHeight,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 4, 12, 4),
            Background = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitLabelPillBrush"),
            Tag = item,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                Foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush"),
                IsHitTestVisible = false,
            },
        };
        ApplyBubbleShadow(pill);
        ToolTipService.SetToolTip(pill, item.DisplayName);
        pill.Tapped += (_, e) =>
        {
            e.Handled = true;
            var origin = pill.TransformToVisual(PreviewCanvas).TransformPoint(new Point(0, 0));
            ShowItemDetails(item, origin);
        };

        // A drop on the name lands where a drop on the icon does. The pills are not under the
        // canvas the drop handlers are on, so a drag over one never reached them, and only the
        // icon took a drop. These are the canvas's own handlers: they tell a pill by its sender.
        pill.AllowDrop = true;
        pill.DragOver += Preview_DragOver;
        pill.DragLeave += Preview_DragLeave;
        pill.Drop += Preview_Drop;

        // A Canvas gives its children no alignment, so the pill has to be placed by a corner — and
        // the six side pills are placed by whichever corner faces the hub, which for the left-hand
        // three is the one a width has to be subtracted to reach. Measuring here rather than
        // letting it size itself is what makes that width knowable before it is in the tree.
        pill.Measure(new Size(double.PositiveInfinity, PillHeight));
        double measured = pill.DesiredSize.Width;
        if (measured < 1)
        {
            // Measure is being asked of an element with no XamlRoot behind it yet, which is not
            // guaranteed to come back with anything. A rough advance width for 12px Segoe UI plus
            // the pill's own 12px-a-side padding is a bad label; a zero-width one is an invisible
            // label, and every pill on the canvas at once.
            measured = 24 + text.Length * 6.6;
        }
        double width = Math.Ceiling(measured);

        double ux = Math.Cos(angle), uy = Math.Sin(angle);
        double nodeCx = cx + radius * ux;
        double nodeCy = cy + radius * uy;
        // In from the node's edge for a plain item (see PillOverlapFraction); out past it for a group
        // or a tool, whose outward rim carries a mark the inward overlap would cover (see
        // PillEdgeClearanceFraction).
        double reach = item.IsGroup || item.IsTool
            ? bubble / 2 + bubble * PillEdgeClearanceFraction
            : bubble / 2 - bubble * PillOverlapFraction;

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

        Canvas.SetLeft(pill, Math.Clamp(px + PillCanvasOffsetX, 0, Math.Max(0, PillCanvasWidth - width)));
        Canvas.SetTop(pill, Math.Clamp(py, 0, PreviewCanvasSize - PillHeight));
        // Labels hidden: the pill stays in the tree (so the disc/pill hover partnership still pairs
        // it) but is not shown until its node is hovered — see HighlightPreviewPair.
        if (_labelsHidden)
            pill.Visibility = Visibility.Collapsed;
        PreviewLabelCanvas.Children.Add(pill);
        return pill;
    }

    private void LabelToggle_Click(object sender, RoutedEventArgs e)
    {
        _labelsHidden = !_labelsHidden;
        UpdateLabelToggleVisual();
        RenderPreview();
    }

    /// <summary>
    /// Keeps the Hide/Show labels toggle in step with <see cref="_labelsHidden"/>: its glyph goes to
    /// the accent colour while labels are hidden (the button is "active") and back to the header
    /// colour when they are shown, and the tooltip/name say which the press will do next. Re-run on a
    /// theme or accent change through <see cref="RepaintThemedContent"/>, the same way
    /// <see cref="UpdateElementsGlyphs"/> owns the Elements shortcut's glyph, so the hover save/restore
    /// never leaves it on a stale colour.
    /// </summary>
    private void UpdateLabelToggleVisual()
    {
        LabelToggleIcon.Foreground = _labelsHidden
            ? new SolidColorBrush(CurrentAccentColor())
            : ThemeHelper.Brush(Root, "SettingsHeaderBrush");
        string tip = _labelsHidden ? "Show labels" : "Hide labels";
        ToolTipService.SetToolTip(LabelToggleButton, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(LabelToggleButton, tip);
    }

    private void RenderPreview()
    {
        if (PreviewCanvas is null) return;
        PreviewCanvas.Children.Clear();
        PreviewLabelCanvas.Children.Clear();
        ClearPreviewHover();
        _previewHoverPartners.Clear();
        // Back on for every render. Preview_PointerMoved is the only thing that takes them away,
        // and a render is what always follows the drop it was hiding them for.
        PreviewLabelCanvas.Visibility = Visibility.Visible;
        const double size = PreviewCanvasSize;
        // Dead centre now. The nudge that used to lift the ring was making room for the Add item /
        // Reset defaults row, which sat on the canvas itself; those live in the screen's own 60px
        // toolbar since the Logi layout landed, so the canvas is the ring's alone again.
        double cx = size / 2, cy = size / 2;

        // A group takes the canvas over entirely — see OpenGroup. Checked before the corner
        // preview because the two are mutually exclusive and a group is the nearer state: you get
        // into one by clicking a bubble on whichever orbit was showing, corner preview included.
        if (_expandedGroup is not null)
        {
            RenderGroupArc(size);
            return;
        }

        if (_radiusAdjusting)
        {
            RenderRadiusAdjustPreview(size, cx, cy);
            return;
        }

        if (_previewQuadrant && _manager.Config.HotCornerEnabled)
        {
            // The orbit's own slots are not on screen, so drop them: a press must not reach a
            // bubble that is no longer drawn. The two orbit-wide buttons go with them — they act on
            // the orbit, and a bottom corner's fan would run straight through where they sit.
            Array.Clear(_slotBorders);
            Array.Clear(_emptySlotBorders);
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
        Array.Clear(_emptySlotBorders);

        // Resolve against this window's theme — not Application.Current.Resources.
        var muted = ThemeHelper.Brush(Root, "SwiftXMutedBrush");
        var foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush");
        var canvasAccent = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitCanvasAccentBrush");

        // Match the orbit: the center is the app mark alone, painted on nothing — floating form and
        // all, so the preview shows the same shadow the real hub has. A filled plate here read as a
        // big empty hole that does not exist on the real thing.
        double logo = OrbitMetrics.HubLogo * scale;
        var hub = AppLogo.CreateFloating(logo);
        Canvas.SetLeft(hub, cx - logo / 2);
        Canvas.SetTop(hub, cy - logo / 2);
        PreviewCanvas.Children.Add(hub);

        var items = OrbitSlots.Visible(_manager.Config.Primary.Items);

        for (int i = 0; i < OrbitMetrics.SlotCount; i++)
        {
            double angle = (-90 + i * 45) * Math.PI / 180.0;
            double x = cx + radius * Math.Cos(angle) - bubble / 2;
            double y = cy + radius * Math.Sin(angle) - bubble / 2;

            // The orbit synthesizes the logo into the last slot, so the preview does the same
            // rather than reading it out of the profile — it is not in it (see SystemOrbitItems).
            // Home, Settings and Close are regular items now, found the normal way below.
            bool pinned = i >= OrbitMetrics.UserSlotCount;
            OrbitItem? item = pinned
                ? SystemOrbitItems.CreateOrbitLogo()
                : i < items.Count && !items[i].IsEmpty ? items[i] : null;

            // No BorderBrush/BorderThickness: the reference study's .disc has no outline, only the
            // soft shadow ApplyBubbleShadow adds below. A filled or pinned item gets that shadow,
            // an empty "add" slot does not, so the shadow is what marks a disc as occupied.
            var border = new Border
            {
                Width = bubble,
                Height = bubble,
                Background = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitNodeBackgroundBrush"),
                Tag = item,
            };

            if (pinned)
            {
                // The SwiftX mark itself, not a glyph — see SystemOrbitItems.CreateOrbitLogo. Flat
                // (AppLogo.Create), not CreateFloating: the bubble's own ApplyBubbleShadow already
                // lifts it, and a second shadow from the mark would double up.
                border.Child = AppLogo.Create(bubble * 0.6);
                ApplyBubbleShadow(border);
                ToolTipService.SetToolTip(border, $"{item!.DisplayName} — always on the orbit");
            }
            else if (item is null)
            {
                border.Child = PreviewGlyph("\uE710", glyphSize, muted);
                _emptySlotBorders[i] = border;
            }
            else
            {
                border.Child = PreviewBubbleContent(item, glyphSize, foreground, bubble, angle);
                ToolTipService.SetToolTip(border, $"{item.DisplayName} — right-click to replace or delete");
                _slotBorders[i] = border;
                ApplyBubbleShadow(border);
            }

            OrbitItemGeometry.Apply(border, bubble);

            if (item is not null)
                AttachPreviewHover(border, AddSlotLabel(item, cx, cy, angle, radius, bubble));
            else
                AttachPreviewHover(border);

            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);
            PreviewCanvas.Children.Add(border);
        }

        AddHotCornerButton(size);
    }

    /// <summary>
    /// While the radius slider is held, the canvas shows only the hub and the orbit logo so the
    /// changing distance is readable. The live dock still updates through
    /// <see cref="OrbitManager.SetRadius"/>. Pointer-up restores the full preview.
    /// </summary>
    private void RenderRadiusAdjustPreview(double size, double cx, double cy)
    {
        CanvasActions.Visibility = Visibility.Visible;
        Array.Clear(_slotBorders);
        Array.Clear(_emptySlotBorders);

        double scale = size * 0.65 / (2 * (OrbitMetrics.Radius + OrbitMetrics.Bubble / 2));
        double radius = OrbitMetrics.Radius * scale;
        double bubble = OrbitMetrics.Bubble * scale;
        double logo = OrbitMetrics.HubLogo * scale;

        _previewCenter = new Point(cx, cy);
        _previewRadius = radius;
        _previewBubble = bubble;

        var hub = AppLogo.CreateFloating(logo);
        Canvas.SetLeft(hub, cx - logo / 2);
        Canvas.SetTop(hub, cy - logo / 2);
        PreviewCanvas.Children.Add(hub);

        const double angle = -Math.PI / 2;
        var border = new Border
        {
            Width = bubble,
            Height = bubble,
            Background = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitNodeBackgroundBrush"),
            // The orbit logo, not Settings — see RenderPreview's own pinned-slot remarks on why the
            // logo is what is actually always there now.
            Child = AppLogo.Create(bubble * 0.6),
        };
        ApplyBubbleShadow(border);
        OrbitItemGeometry.Apply(border, bubble);
        ToolTipService.SetToolTip(border, "Orbit radius");
        Canvas.SetLeft(border, cx + radius * Math.Cos(angle) - bubble / 2);
        Canvas.SetTop(border, cy + radius * Math.Sin(angle) - bubble / 2);
        PreviewCanvas.Children.Add(border);
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

        var borderBrush = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitCanvasBorderBrush");
        var muted = ThemeHelper.Brush(Root, "SwiftXMutedBrush");
        var foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush");

        for (int i = 0; i < slots; i++)
        {
            double angle = QuadrantLayout.ScreenAngleRad(
                corner, QuadrantLayout.LocalAngleRad(i, slots, hoverBubble, radius));
            _quadrantSlotAngles[i] = angle;

            double left = cx + previewRadius * Math.Cos(angle) - previewBubble / 2;
            double top = cy + previewRadius * Math.Sin(angle) - previewBubble / 2;
            var item = i < items.Count && !items[i].IsEmpty ? items[i] : null;

            var border = new Border
            {
                Width = previewBubble,
                Height = previewBubble,
                Background = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitBubbleGlassTint"),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Tag = item,
                Child = item is null
                    ? PreviewGlyph("\uE710", glyphSize, muted)
                    : PreviewBubbleContent(item, glyphSize,
                        item.IsSystem ? muted : foreground, previewBubble, angle),
            };
            OrbitItemGeometry.Apply(border, previewBubble);
            ToolTipService.SetToolTip(border, item is null
                ? "Add to this corner"
                : $"{item.DisplayName} — right-click to replace or remove");
            AttachPreviewHover(border);
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
    /// The mark is the four logo leaves, with the selected corner's leaf at full strength — the
    /// same glyph the sidebar picker uses. While the quadrant preview is up, it swaps to a back
    /// chevron so the button reads as a way out rather than as another pointer at the corner it
    /// is already sitting in.
    /// </para>
    /// </summary>
    private void AddHotCornerButton(double size)
    {
        if (!_manager.Config.HotCornerEnabled)
            return;

        const double diameter = 40;
        const double inset = 2;
        var corner = _manager.Config.HotCorner;

        var logo = _previewQuadrant ? null : AppLogo.CreateCornerGlyph(28, corner);
        var button = new Button
        {
            Width = diameter,
            Height = diameter,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(diameter / 2),
            Content = logo ?? new FontIcon
            {
                Glyph = "\uE72B",
                FontSize = 14,
            },
        };

        if (logo is null)
        {
            // The back chevron is an ordinary glyph, so it tints to the accent like the canvas's
            // other chrome buttons.
            WireOrbitGlyphHover(button);
        }
        else
        {
            // The logo does not: an accent fill flattened it to one colour and left three leaves
            // dimmed. Hover lights the whole mark in its own colours instead \u2014 see
            // AppLogo.SetCornerGlyphHighlighted. Still no disc behind it, same as the others.
            SuppressChromeFill(button);
            void Highlight(object sender, PointerRoutedEventArgs e) => AppLogo.SetCornerGlyphHighlighted(logo, true);
            void Rest(object sender, PointerRoutedEventArgs e) => AppLogo.SetCornerGlyphHighlighted(logo, false);
            button.PointerEntered += Highlight;
            button.PointerExited += Rest;
            button.PointerCanceled += Rest;
        }

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
    /// Orbit canvas chrome (Elements, Settings, info, reset, and the hot corner's back chevron):
    /// no disc on hover. The glyph tints to the accent instead. The hot corner's logo is the one
    /// exception — it lights up in its own colours; see <see cref="AddHotCornerButton"/>.
    /// </summary>
    private void WireOrbitGlyphHover(Button button)
    {
        SuppressChromeFill(button);
        button.PointerEntered += OrbitGlyph_PointerEntered;
        button.PointerExited += OrbitGlyph_PointerExited;
        button.PointerCanceled += OrbitGlyph_PointerExited;
    }

    private static void SuppressChromeFill(Button button)
    {
        var transparent = new SolidColorBrush(Colors.Transparent);
        button.Resources["ButtonBackground"] = transparent;
        button.Resources["ButtonBackgroundPointerOver"] = transparent;
        button.Resources["ButtonBackgroundPressed"] = transparent;
        button.Resources["ButtonBorderBrush"] = transparent;
        button.Resources["ButtonBorderBrushPointerOver"] = transparent;
        button.Resources["ButtonBorderBrushPressed"] = transparent;
    }

    private void OrbitGlyph_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var accent = new SolidColorBrush(CurrentAccentColor());
        var saved = new List<(DependencyObject Target, Brush? Brush)>();
        foreach (var node in WalkContent(button))
        {
            if (node is IconElement icon)
            {
                saved.Add((icon, icon.Foreground));
                icon.Foreground = accent;
            }
            else if (node is Shape shape)
            {
                saved.Add((shape, shape.Fill));
                shape.Fill = accent;
            }
            // A plain-text row button (e.g. the icon picker's "Reset") built with an explicit
            // TextBlock as its Content rather than a bare string — see BuildIconPicker — tints the
            // same way an icon glyph does, so it can share this hover treatment instead of needing
            // its own.
            else if (node is TextBlock text)
            {
                saved.Add((text, text.Foreground));
                text.Foreground = accent;
            }
        }
        _orbitGlyphRest[button] = saved;
    }

    private void OrbitGlyph_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Button button || !_orbitGlyphRest.Remove(button, out var saved))
            return;
        foreach (var (target, brush) in saved)
        {
            if (target is IconElement icon)
                icon.Foreground = brush;
            else if (target is Shape shape)
                shape.Fill = brush;
            else if (target is TextBlock text)
                text.Foreground = brush;
        }

        // What was saved on entry is the colour from *before* the hover, and a click in between
        // can change what the resting colour should be. The Home screen's Elements shortcut is the
        // case that bites: clicking it navigates to the Elements screen and UpdateElementsGlyphs
        // paints it in the accent to show it is active — then the pointer leaves and the restore
        // above puts back the inactive colour it saved, so the button reads as off while its
        // screen is open. The active state is re-derived from _currentScreen rather than trusted
        // to the snapshot.
        if (button == ElementsShortcutButton)
            UpdateElementsGlyphs();
    }

    /// <summary>
    /// Re-applies the resting glyph colour of the persistent header/orbit chrome buttons (Home,
    /// Settings, the theme toggle, the two Back buttons, reset, help) on a theme change.
    /// <para>
    /// Those glyphs take their colour from a <c>{ThemeResource}</c> in XAML, which would normally
    /// re-resolve on its own when the theme changes — except that the accent-tint hover
    /// (<see cref="OrbitGlyph_PointerEntered"/>/<see cref="OrbitGlyph_PointerExited"/>) writes a
    /// plain <see cref="SolidColorBrush"/> straight onto <c>Foreground</c>, which replaces the
    /// theme-resource reference with a static value. After a glyph has been hovered even once it no
    /// longer follows the theme, so Home and Settings sat on the previous theme's colour until the
    /// app was relaunched. Re-resolving here, on every repaint, is what lets them keep up without a
    /// restart. <see cref="ElementsShortcutButton"/> is intentionally not in the list — its glyph is
    /// owned in both states by <see cref="UpdateElementsGlyphs"/>, which runs in the same repaint.
    /// </para>
    /// </summary>
    private void RefreshChromeGlyphColors()
    {
        SetChromeGlyphRest(OrbitHomeButton, "SettingsHeaderBrush");
        SetChromeGlyphRest(OrbitSettingsButton, "SettingsHeaderBrush");
        SetChromeGlyphRest(OrbitHelpButton, "SettingsHeaderBrush");
        SetChromeGlyphRest(OrbitResetButton, "SettingsHeaderBrush");
        SetChromeGlyphRest(ThemeToggleButton, "SettingsHeaderBrush");
        SetChromeGlyphRest(SettingsButton, "SettingsHeaderBrush");
        SetChromeGlyphRest(SettingsBackButton, "SettingsHeaderBrush");
        SetChromeGlyphRest(ToolsBackButton, "SettingsHeaderBrush");
        SetChromeGlyphRest(RadiusResetButton, "SettingsMutedBrush");
    }

    private void SetChromeGlyphRest(Button button, string key)
    {
        var brush = ThemeHelper.Brush(Root, key);

        // Hovered right now: it is showing the accent tint, so updating the saved snapshot rather
        // than the live Foreground is what makes the colour it restores to on exit the new theme's
        // rather than the one in force before the switch.
        if (_orbitGlyphRest.TryGetValue(button, out var saved))
        {
            for (int i = 0; i < saved.Count; i++)
                saved[i] = (saved[i].Target, brush);
            return;
        }

        foreach (var node in WalkContent(button))
        {
            if (node is IconElement icon)
                icon.Foreground = brush;
            else if (node is Shape shape)
                shape.Fill = brush;
            else if (node is TextBlock text)
                text.Foreground = brush;
        }
    }

    private static IEnumerable<DependencyObject> WalkContent(DependencyObject node)
    {
        yield return node;
        switch (node)
        {
            case Panel panel:
                foreach (UIElement child in panel.Children)
                {
                    if (child is DependencyObject inner)
                    {
                        foreach (var x in WalkContent(inner))
                            yield return x;
                    }
                }
                break;
            case Border border when border.Child is DependencyObject inner:
                foreach (var x in WalkContent(inner))
                    yield return x;
                break;
            case Viewbox viewbox when viewbox.Child is DependencyObject inner:
                foreach (var x in WalkContent(inner))
                    yield return x;
                break;
            case ContentControl control when control.Content is DependencyObject inner:
                foreach (var x in WalkContent(inner))
                    yield return x;
                break;
        }
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
        _dragSlot = _expandedGroup is not null
            ? GroupSlotAt(point)
            : (_previewQuadrant ? QuadrantSlotAt(point) : PreviewSlotAt(point));
        _dragBorder = _dragSlot >= 0
            ? (_expandedGroup is not null
                ? _groupSlotBorders[_dragSlot]
                : (_previewQuadrant ? _quadrantSlotBorders[_dragSlot] : _slotBorders[_dragSlot]))
            : null;
        _dragItem = _dragBorder?.Tag as OrbitItem;
        _dragStart = point;
        _dragMoved = false;
        // Starts equal to the origin slot, not -1: until the pointer actually crosses into a
        // different target, "tentative" is just "still where it started", so a drop with no real
        // movement resolves as a no-op the same way it always did.
        _dragTentativeSlot = _dragSlot;
        _dragTentativeQuadrantSlot = _dragSlot;
        _dragTentativeGroupSlot = _dragSlot;
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

        if (_expandedGroup is not null)
        {
            int target = GroupSlotAt(point, requireHit: false);
            if (target == _dragTentativeGroupSlot) return;
            _dragTentativeGroupSlot = target;
            if (target < 0) return;

            // A gathered group has no order of its own to change (see GroupAddEnds).
            if (QuadrantLayout.IsQuickAccess(_expandedGroup)) return;

            var children = _expandedGroup.Children.Where(c => !c.Hidden).ToList();
            int from = children.IndexOf(_dragItem);
            if (from < 0) return;
            // The leading add slot is index 0 on the arc but position 0 among children, so the
            // slot index is one ahead of the child index it targets; the trailing add slot clamps
            // down to the last child the same way dropping past the end of the top-level orbit
            // clamps to its last occupied slot.
            int to = Math.Clamp(target - GroupAddEnds(_expandedGroup), 0, children.Count - 1);
            if (to != from)
            {
                children.RemoveAt(from);
                children.Insert(to, _dragItem);
            }
            ReflowGroupBorders(_expandedGroup, children);
            return;
        }

        if (_previewQuadrant)
        {
            int target = QuadrantSlotAt(point, requireHit: false);
            if (target == _dragTentativeQuadrantSlot) return;
            _dragTentativeQuadrantSlot = target;
            if (target < 0) return;

            // The places as drawn right now (empty ones as placeholders), so the preview moves the
            // very items on screen and the drop lands exactly where this last showed them.
            var drawn = new List<OrbitItem>(_quadrantSlotCount);
            for (int i = 0; i < _quadrantSlotCount; i++)
                drawn.Add(_quadrantSlotBorders[i]?.Tag as OrbitItem ?? OrbitSlots.NewEmpty());
            if (!drawn.Contains(_dragItem)) return;
            ReflowQuadrantBorders(OrbitSlots.Moved(drawn, _dragItem, target, QuadrantLayout.ItemCount));
            return;
        }

        int targetSlot = PreviewSlotAt(point, requireHit: false);
        if (targetSlot == _dragTentativeSlot) return;
        _dragTentativeSlot = targetSlot;
        if (targetSlot < 0 || targetSlot >= OrbitMetrics.UserSlotCount) return;

        var visible = OrbitSlots.Visible(_manager.Config.Primary.Items);
        if (!visible.Contains(_dragItem)) return;
        ReflowOrbitBorders(OrbitSlots.Moved(visible, _dragItem, targetSlot));
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

    /// <summary>Group-arc counterpart of <see cref="ReflowOrbitBorders"/>. <paramref name="tentativeOrder"/>
    /// is indexed by child position; the arc slot for child <c>i</c> is <c>i + 1</c>, since slot 0
    /// is the leading "add" end (see <see cref="GroupSlotAt"/>, and <see cref="GroupAddEnds"/> for
    /// the group that has none).</summary>
    private void ReflowGroupBorders(OrbitItem group, List<OrbitItem> tentativeOrder)
    {
        int count = _groupSlotBorders.Count;
        if (count == 0) return;

        double step = OrbitMetrics.FlyoutStepDegreesFor(_previewRadius, SettingsGroupArcItemGap) * Math.PI / 180.0;
        double first = -((count - 1) / 2.0) * step;
        int ends = GroupAddEnds(group);

        for (int i = 0; i < tentativeOrder.Count; i++)
        {
            var item = tentativeOrder[i];
            if (ReferenceEquals(item, _dragItem)) continue;
            var border = FindSlotBorder(_groupSlotBorders, item);
            if (border is null) continue;
            double angle = first + (i + ends) * step;
            double x = _previewCenter.X + _previewRadius * Math.Cos(angle) - _previewBubble / 2;
            double y = _previewCenter.Y + _previewRadius * Math.Sin(angle) - _previewBubble / 2;
            AnimateBubbleTo(border, x, y);
        }
    }

    private static Border? FindSlotBorder(IReadOnlyList<Border?> borders, OrbitItem item)
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
        int tentativeGroupSlot = _dragTentativeGroupSlot;
        var group = _expandedGroup;

        PreviewCanvas.ReleasePointerCapture(e.Pointer);
        EndPreviewDrag();

        if (slot < 0)
            return;

        if (group is not null)
        {
            if (moved)
            {
                // A drag out of an add slot is not an add — it is a drag of nothing.
                if (item is not null)
                    ReorderGroupChild(item, tentativeGroupSlot);
            }
            else if (item is not null)
            {
                ShowItemDetails(item, e.GetCurrentPoint(PreviewCanvas).Position);
            }
            else
            {
                AddToOpenGroup();
            }
            return;
        }

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
                // A group opens onto its children, as it does on the ring: the corner's Tools and
                // QuickAccess are groups, and a click on one that only showed its details gave no
                // way to see what was in it. The canvas's centre closes it again, back to the corner.
                if (item.IsGroup)
                    OpenGroup(item);
                else
                    ShowItemDetails(item, e.GetCurrentPoint(PreviewCanvas).Position);
            }
            else
            {
                AddQuadrantItem(slot);
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
                OpenGroup(item);
            else
                ShowItemDetails(item, e.GetCurrentPoint(PreviewCanvas).Position);
        }
        else if (slot < OrbitMetrics.UserSlotCount)
        {
            // The ring's own "+" clears any armed replace and refreshes the always-visible
            // "ADD TO ORBIT" sidebar's summary text, rather than opening the add dialog in a
            // second window. The dialog stays one click away through the sidebar's own
            // "Add to Orbit…" row.
            EndReplace();
            UpdateOrbitEditSummary();
        }
    }

    /// <summary>
    /// Lets an app, file, folder or link land straight on a place in the ring rather than only on
    /// the "ADD TO ORBIT" sidebar — an empty slot picks it up the same way clicking it does, and a
    /// filled one is replaced by it, the same swap the right-click menu's "Replace…" performs.
    /// Two different drags reach here: one dragged in from outside SwiftX (Explorer, the Start
    /// menu, a browser's address bar — recognised by <see cref="OrbitItemFactory.LooksDroppable"/>),
    /// and one dragged off a sidebar suggestion row (<see cref="_sidebarDragTemplate"/>, set by
    /// that row's own DragStarting in <see cref="BuildEditRow"/>). Declined inside an open group and
    /// on the pinned Settings/Close slots, the same exclusions <see cref="Preview_RightTapped"/> and
    /// <see cref="BuildOrbitItemMenu"/> already make.
    /// <para>
    /// The name pill beside an item takes the drop as well as its icon (see <see cref="AddSlotLabel"/>,
    /// which points the pill's own drag events here). <paramref name="sender"/> is what tells the
    /// two apart, through <see cref="DropSlotAt"/>.
    /// </para>
    /// </summary>
    private void Preview_DragOver(object sender, DragEventArgs e)
    {
        bool internalDrag = _sidebarDragTemplate is not null;
        if (_expandedGroup is not null || (!internalDrag && !OrbitItemFactory.LooksDroppable(e.DataView)))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            ClearPreviewDropHighlight();
            return;
        }

        int slot = DropSlotAt(sender, e);
        if (!_previewQuadrant && slot >= OrbitMetrics.UserSlotCount)
            slot = -1;

        // The pinned Settings and Close pills label no place that can take anything, so a pill
        // with no slot behind it says no rather than offering an add that would go nowhere.
        if (slot < 0 && sender is Border)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            ClearPreviewDropHighlight();
            return;
        }

        var border = slot < 0
            ? null
            : (_previewQuadrant
                ? (slot < _quadrantSlotBorders.Length ? _quadrantSlotBorders[slot] : null)
                : (slot < _slotBorders.Length ? _slotBorders[slot] ?? _emptySlotBorders[slot] : null));

        // What the drop would actually do, said before it happens: an occupied place is replaced,
        // not added beside, and a tool is not swappable at all (see DropOntoPreviewSlot) — so the
        // drag says no over one rather than accepting a drop it would then quietly ignore.
        var occupant = border?.Tag as OrbitItem;
        bool intoGroup = occupant is { IsGroup: true } &&
                         !ElementLibrary.IsBuiltInToolsGroup(occupant) &&
                         !QuadrantLayout.IsQuickAccess(occupant) &&
                         _sidebarDragTemplate is not { IsGroup: true };
        bool blocked = occupant is { IsTool: true };
        e.AcceptedOperation = blocked ? DataPackageOperation.None : DataPackageOperation.Copy;
        if (e.DragUIOverride is { } ui)
        {
            ui.Caption = occupant is null
                ? "Drop to add to the orbit"
                : blocked
                    ? $"{occupant.DisplayName} is a tool — switch it off instead"
                    : intoGroup
                        ? $"Drop to add to {occupant.DisplayName}"
                        : $"Drop to replace {occupant.DisplayName}";
            ui.IsCaptionVisible = true;
            ui.IsGlyphVisible = true;
        }

        if (slot == _previewDropSlot) return;
        _previewDropSlot = slot;

        if (border is not null)
            HighlightPreviewPair(border);
        else
            ClearPreviewHover();
    }

    private void Preview_DragLeave(object sender, DragEventArgs e) => ClearPreviewDropHighlight();

    /// <summary>
    /// The place a drag is aimed at. Over the canvas it is the one nearest the pointer. Over an
    /// item's name pill it is that item's own place, wherever on the pill the pointer is: the pill
    /// sits out beside the icon, where the nearest place by angle can be its neighbour's. -1 when
    /// the pill's item holds no place a drop can go (the pinned Settings and Close).
    /// </summary>
    private int DropSlotAt(object sender, DragEventArgs e)
    {
        if (sender is Border { Tag: OrbitItem labelled })
            return Array.FindIndex(_slotBorders, b => ReferenceEquals(b?.Tag, labelled));

        var point = e.GetPosition(PreviewCanvas);
        return _previewQuadrant
            ? QuadrantSlotAt(point, requireHit: false)
            : PreviewSlotAt(point, requireHit: false);
    }

    private void ClearPreviewDropHighlight()
    {
        _previewDropSlot = -1;
        ClearPreviewHover();
    }

    private async void Preview_Drop(object sender, DragEventArgs e)
    {
        ClearPreviewDropHighlight();
        if (_expandedGroup is not null) return;

        int slot = DropSlotAt(sender, e);
        if (slot < 0)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        // A sidebar row's own drag: the template travelled here as a live field reference (see
        // _sidebarDragTemplate's own doc comment), so there is nothing to await off the DataView —
        // it exists only to make the OS treat this as a real drag.
        if (_sidebarDragTemplate is { } template)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            // A group that is already on the ring is repositioned, not copied. The sidebar's Groups
            // list is the ring's own groups plus the detached ones, so dragging an on-ring group's
            // row back onto the ring used to drop a second, independent group of the same name and
            // contents (CloneOrbitTemplate) — which reads as the one group duplicated. Moving it (the
            // same thing dragging its ring bubble does) is what the gesture means.
            if (!_previewQuadrant && template.IsGroup &&
                _manager.Config.Primary.Items.Contains(template))
            {
                ReorderItem(template, slot);
            }
            // A detached group (sidebar only) is moved onto the ring: the real group is placed and
            // taken out of the library, so it lives in exactly one place. If the slot will not take
            // it, it is put back rather than lost.
            else if (!_previewQuadrant && template.IsGroup &&
                     _manager.Config.Primary.DetachedGroups.Remove(template))
            {
                DropOntoPreviewSlot(slot, template);
                if (!_manager.Config.Primary.Items.Contains(template))
                {
                    _manager.Config.Primary.DetachedGroups.Add(template);
                    _manager.NotifyItemsChanged();
                    DispatcherQueue.TryEnqueue(BuildOrbitEditSidebar);
                }
            }
            else
            {
                DropOntoPreviewSlot(slot, CopyForPreviewDrop(template));
            }
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var targets = await OrbitItemFactory.ReadDroppedTargetsAsync(e.DataView);
            if (targets.Count == 0)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Copy;
            DropOntoPreviewSlot(slot, OrbitItemFactory.FromTarget(targets[0]));
        }
        catch (Exception ex)
        {
            Diag.Log("Preview_Drop failed: " + ex);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// What a sidebar row's drag puts on the canvas: a fresh copy, as everywhere else, except that a
    /// group already on the orbit goes onto the corner under its own id.
    /// <para>
    /// The corner stores copies and maps each one back to the orbit's item of the same id when it
    /// is drawn (see <see cref="HotCornerItems.Resolve"/>). A copy that carries the group's id is
    /// therefore another place to see that group, not another group: what is added to it from the
    /// sidebar's Groups list shows on the corner and on the dock at once, and what is dropped on it
    /// on the corner shows in the list. A copy with an id of its own was a group of its own, frozen
    /// as it was dragged, and nothing in the sidebar could add to it afterwards.
    /// </para>
    /// <para>
    /// Only on the corner. A drop on the ring puts a second group beside the first, which is the
    /// point of dragging one there, and two groups of the same id on the ring would be one group
    /// drawn twice.
    /// </para>
    /// </summary>
    private OrbitItem CopyForPreviewDrop(OrbitItem template)
    {
        var copy = CloneOrbitTemplate(template);
        return _previewQuadrant
            ? HotCornerItems.Entry(copy, template, _manager.Config.Primary.Items)
            : copy;
    }

    /// <summary>
    /// Fills an empty ring slot, or replaces whatever is already in it, with <paramref name="added"/>
    /// — the drop counterpart of clicking an empty slot / the right-click menu's "Replace…". A
    /// standalone tool item is left alone, same as <see cref="BuildOrbitItemMenu"/>'s own guard: a
    /// tool is taken off the orbit by switching it off, not by a stray drop landing on it.
    /// </summary>
    private void DropOntoPreviewSlot(int slot, OrbitItem added)
    {
        if (_previewQuadrant)
        {
            if (slot < 0 || slot >= _quadrantSlotBorders.Length) return;
            var occupant = _quadrantSlotBorders[slot]?.Tag as OrbitItem;
            // A group in the corner is a place to put things, as on the ring: dropping an item on it
            // adds to it. The gathered QuickAccess group and the Tools group are not the user's to fill.
            if (occupant is { IsGroup: true } && !added.IsGroup &&
                !ElementLibrary.IsBuiltInToolsGroup(occupant) && !QuadrantLayout.IsQuickAccess(occupant))
            {
                if (GroupDropRules.TryAdd(occupant, added) != GroupDropVerdict.Allowed) return;
                // Through the manager, as the ring's drop below does, and not a bare Save: the group
                // may be the orbit's own, and the dock if it is open, the Elements list and the
                // sidebar's counts all show what is in it.
                _manager.NotifyItemsChanged();
                EndArmedReplaceAfterDrop();
                RenderPreview();
                RebuildItemsList();
                DispatcherQueue.TryEnqueue(BuildOrbitEditSidebar);
                return;
            }

            if (occupant is not null && ReferenceEquals(_selectedItem, occupant))
                HideItemDetails();
            PutInQuadrantSlot(slot, added);
            EndArmedReplaceAfterDrop();
            RenderPreview();
            return;
        }

        if (slot < 0 || slot >= OrbitMetrics.UserSlotCount) return;
        var items = _manager.Config.Primary.Items;
        if (_slotBorders[slot]?.Tag is OrbitItem item)
        {
            if (item.IsTool) return;

            // A group on the ring is a place to put things, not something to swap out: dropping
            // on it adds to it (a group taking a group, a full group, or a duplicate is refused
            // by GroupDropRules and leaves everything as it was).
            if (item.IsGroup && !added.IsGroup && !ElementLibrary.IsBuiltInToolsGroup(item))
            {
                if (GroupDropRules.TryAdd(item, added) != GroupDropVerdict.Allowed) return;
                _manager.NotifyItemsChanged();
                EndArmedReplaceAfterDrop();
                RenderPreview();
                RebuildItemsList();
                DispatcherQueue.TryEnqueue(BuildOrbitEditSidebar);
                return;
            }

            int at = items.IndexOf(item);
            if (at < 0) return;
            items[at] = added;
            if (ReferenceEquals(_expandedGroup, item)) CollapseGroup();
            if (ReferenceEquals(_selectedItem, item)) HideItemDetails();
        }
        else
        {
            if (!OrbitSlots.PlaceAt(items, slot, added)) return;
        }

        _manager.NotifyItemsChanged();
        EndArmedReplaceAfterDrop();
        RenderPreview();
        RebuildItemsList();

        // The sidebar's rows carry an "already on the orbit" state this drop just invalidated for
        // one of them, the same rebuild AddToOrbitFromSidebar does after a click. Off the queue
        // because the row that started the drag is one of the rows about to be replaced, and the
        // drag it started is still finishing.
        DispatcherQueue.TryEnqueue(BuildOrbitEditSidebar);
    }

    /// <summary>
    /// A drop onto the ring names its own place, so it settles a replace armed by "Replace…" rather
    /// than leaving the sidebar still waiting to be told what to swap in. The drop is the answer to
    /// that question, even when it landed somewhere other than the place that asked it.
    /// </summary>
    private void EndArmedReplaceAfterDrop()
    {
        if (!IsReplacing) return;
        EndReplace();
        UpdateOrbitEditSummary();
    }

    /// <summary>
    /// The dock's own edit menu: right-clicking an item on either view offers to swap what is in
    /// that place or take it out of it, so the two things you can do to an item you can already
    /// see are done where you see it, rather than only on the Elements screen (the orbit) or through a
    /// badge pinned to every bubble (the corner, which no longer has one).
    /// </summary>
    private void Preview_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // Same reason Preview_PointerPressed bails: the menu is built from a slot index, and inside
        // a group every slot index answers for a top-level item that is not on screen.
        if (_expandedGroup is not null) return;

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
        if (_slotBorders[slot] is not { Tag: OrbitItem item } slotBorder)
            return null;

        // A standalone tool is the Tools screen's, exactly as on the Elements screen, which leaves
        // it out of its own list for the same reason: a tool is taken off the orbit by switching it
        // off there, not by deleting it. The Tools group itself carries no such restriction — unlike
        // one of its individual tools, deleting the whole group is not silently undone: EnsureToolsGroup
        // never recreates it once every tool it held is gone.
        if (item.IsTool)
            return null;

        return BuildItemMenu(
            item.DisplayName,
            replace: () => ReplaceOrbitItem(item),
            remove: () => RemoveOrbitItem(item),
            // Groups are permanently deleted; apps and links are only removed from the layout and
            // remain findable in the sidebar (their category is a scan / saved list, not the ring).
            removeLabel: item.IsGroup ? "Delete" : "Remove",
            canRemove: true,
            // A group's mark is the one thing about it with no other way to change: its name and
            // contents are edited elsewhere, and it is not a shell target with an icon of its own
            // to fall back on.
            editIcon: item.IsGroup ? () => ShowIconPicker(item, slotBorder) : null,
            // Already a group: nothing to consolidate it into.
            newGroup: item.IsGroup ? null : () => CreateGroupFromExistingItem(item));
    }

    /// <summary>
    /// Wraps an item already on the orbit in a brand new group, in the exact place that item held
    /// — the group takes over its slot rather than needing one of its own, so this stays offered
    /// even when <see cref="OrbitIsFull"/>. The item's own right-click "New group…"
    /// (<see cref="BuildOrbitItemMenu"/>): unlike <see cref="OpenNewGroupDialog"/>'s plain sidebar
    /// button, the item to consolidate is already known before the window opens.
    /// </summary>
    private void CreateGroupFromExistingItem(OrbitItem item)
    {
        _manager.OpenAddItem(
            added =>
            {
                if (added.IsGroup) ConsolidateIntoGroup(added, item);
            },
            _manager.Config.Primary.Items,
            startAsGroup: true,
            heading: "New group with " + item.DisplayName);
    }

    /// <summary>
    /// The actual consolidation <see cref="CreateGroupFromExistingItem"/> lands on: <paramref name="item"/>'s slot
    /// becomes <paramref name="group"/>'s, with <paramref name="item"/> itself — not a copy — as
    /// the group's first child, so renaming or re-icon-ing it afterwards still reaches the one
    /// instance of it that ever existed.
    /// </summary>
    private void ConsolidateIntoGroup(OrbitItem group, OrbitItem item)
    {
        var items = _manager.Config.Primary.Items;
        int at = items.IndexOf(item);
        if (at < 0) return;

        group.Children.Add(item);
        items[at] = group;

        if (ReferenceEquals(_expandedGroup, item)) CollapseGroup();
        if (ReferenceEquals(_selectedItem, item)) HideItemDetails();

        _manager.NotifyItemsChanged();
        RenderPreview();
        RebuildItemsList();
        DispatcherQueue.TryEnqueue(BuildOrbitEditSidebar);
    }

    /// <summary>
    /// Quadrant counterpart of <see cref="BuildOrbitItemMenu"/>. The Tools group is editable here
    /// where it is not on the orbit: taking it off the corner is a change to the corner's own
    /// arrangement, and leaves the orbit's copy exactly where it was.
    /// </summary>
    private MenuFlyout? BuildQuadrantItemMenu(Point point)
    {
        int slot = QuadrantSlotAt(point);
        if (slot < 0 || _quadrantSlotBorders[slot] is not { Tag: OrbitItem item } slotBorder)
            return null;

        // The last item stays: a corner with nothing on it would summon an empty screen. Shown
        // greyed rather than left out, so the menu is the same shape whichever item it opens on.
        int count = HotCornerItems.Resolve(_manager.Config, SystemOrbitItems.CreateSettings())
            .Count(i => !i.IsEmpty);

        return BuildItemMenu(
            item.DisplayName,
            replace: () => ReplaceQuadrantItem(slot),
            remove: () => RemoveQuadrantItem(slot),
            removeLabel: "Remove from corner",
            canRemove: count > 1,
            // A group's mark is the one thing about it with no other way to change, here as on the
            // ring (see BuildOrbitItemMenu). A group on the corner is the orbit's own group, so the
            // mark is changed on it and the ring shows the same one; QuickAccess, which only the
            // corner has, keeps its own (see ApplyIconChoice).
            editIcon: item.IsGroup ? () => ShowIconPicker(item, slotBorder) : null);
    }

    private static MenuFlyout BuildItemMenu(
        string name, Action replace, Action remove, string removeLabel, bool canRemove,
        Action? editIcon = null, Action? newGroup = null)
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

        // Wraps this item in a new group that takes over its own place, so it is offered even
        // when every slot is taken: unlike the sidebar's "New group…", this one never needs a
        // free slot of its own (see CreateGroupFromExistingItem).
        if (newGroup is not null)
        {
            var newGroupItem = new MenuFlyoutItem
            {
                Text = "New group…",
                Icon = new FontIcon { Glyph = char.ConvertFromUtf32(0xE710) },
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(newGroupItem, "New group from " + name);
            newGroupItem.Click += (_, _) => newGroup();
            menu.Items.Add(newGroupItem);
        }

        if (editIcon is not null)
        {
            var editIconItem = new MenuFlyoutItem
            {
                Text = "Edit icon…",
                Icon = new FontIcon { Glyph = char.ConvertFromUtf32(0xE91B) },
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(editIconItem, "Edit icon of " + name);
            editIconItem.Click += (_, _) => editIcon();
            menu.Items.Add(editIconItem);
        }

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

    /// <summary>
    /// Starts replacing one top-level orbit item. Rather than the old add dialog, this opens the
    /// same "ADD TO ORBIT" sidebar the Elements toolbar button does, in a mode where the next thing
    /// clicked or dragged there swaps into <paramref name="item"/>'s place instead of being appended
    /// — see <see cref="CompleteReplace"/>, <see cref="AddToOrbitFromSidebar"/> and
    /// <see cref="AddDroppedTargetsToOrbit"/>.
    /// </summary>
    private void ReplaceOrbitItem(OrbitItem item)
    {
        _replaceItem = item;
        _replaceQuadrantSlot = -1;
        _replaceItemName = item.DisplayName;
        BeginReplaceViaSidebar();
    }

    /// <summary>Takes one item off the orbit — the same delete the Elements screen's own rows do.</summary>
    private void RemoveOrbitItem(OrbitItem item)
    {
        if (!OrbitSlots.Vacate(_manager.Config.Primary.Items, item))
        {
            // Not on the ring. A detached user group is permanently deleted from the store.
            if (_manager.Config.Primary.DetachedGroups.Remove(item))
            {
                if (ReferenceEquals(_expandedGroup, item)) CollapseGroup();
                if (ReferenceEquals(_selectedItem, item)) HideItemDetails();
                _manager.NotifyItemsChanged();
                RenderPreview();
                RebuildItemsList();
                DispatcherQueue.TryEnqueue(BuildOrbitEditSidebar);
                return;
            }
            // A group only the hot corner has — removing it takes it off the corner.
            int slot = _manager.Config.HotCornerOrbit.FindIndex(entry => ReferenceEquals(entry, item));
            if (slot >= 0)
                RemoveQuadrantItem(slot);
            return;
        }

        // User groups are permanently deleted — no longer kept in a DetachedGroups holding area.
        _manager.Config.Primary.DetachedGroups.Remove(item);

        if (ReferenceEquals(_expandedGroup, item)) CollapseGroup();
        if (ReferenceEquals(_selectedItem, item)) HideItemDetails();
        _manager.NotifyItemsChanged();
        RenderPreview();
        RebuildItemsList();
        DispatcherQueue.TryEnqueue(BuildOrbitEditSidebar);
    }

    /// <summary>Quadrant counterpart of <see cref="ReplaceOrbitItem"/>: the sidebar's next pick
    /// writes into the corner's own stored arrangement instead of the top-level orbit.</summary>
    private void ReplaceQuadrantItem(int slot)
    {
        _replaceItem = null;
        _replaceQuadrantSlot = slot;
        _replaceItemName = slot >= 0 && slot < _quadrantSlotBorders.Length &&
            _quadrantSlotBorders[slot]?.Tag is OrbitItem existing
                ? existing.DisplayName
                : "this item";
        BeginReplaceViaSidebar();
    }

    /// <summary>Arms the always-visible "ADD TO ORBIT" sidebar's replace mode and refreshes its
    /// summary text to show it — <see cref="ReplaceOrbitItem"/> and <see cref="ReplaceQuadrantItem"/>
    /// set which place is being replaced before calling this.</summary>
    private void BeginReplaceViaSidebar()
    {
        HideItemDetails();
        UpdateOrbitEditSummary();
    }

    /// <summary>Drops out of replace mode without picking anything — the sidebar's Cancel link, and
    /// anywhere else that has to guarantee a stale replace target can't outlive the state it was
    /// captured from (navigating home, or opening Elements' sidebar fresh from a right-click).</summary>
    private void EndReplace()
    {
        _replaceItem = null;
        _replaceQuadrantSlot = -1;
        _replaceItemName = null;
    }

    /// <summary>
    /// Drops out of replace mode without picking anything, reverting to the plain "ADD TO ORBIT"
    /// sidebar it was showing before "Replace…" armed it — not the orbit's own settings, which is
    /// a different panel entirely and not where this button found the user.
    /// </summary>
    private void OrbitEditCancel_Click(object sender, RoutedEventArgs e)
    {
        EndReplace();
        UpdateOrbitEditSummary();
    }

    /// <summary>
    /// Finishes a replace armed by <see cref="ReplaceOrbitItem"/>/<see cref="ReplaceQuadrantItem"/>:
    /// <paramref name="added"/> — picked by clicking a sidebar suggestion or dragging an app,
    /// shortcut or link onto it — takes the place of whatever was being replaced, and the sidebar
    /// reverts to its plain "ADD TO ORBIT" self, the same end state Cancel leaves it in, rather
    /// than jumping away to the orbit's own settings the way the old dialog closing used to.
    /// <para>
    /// Replacing a group takes its children with it, the same way deleting one does on the Elements
    /// screen — they exist only inside it.
    /// </para>
    /// </summary>
    private void CompleteReplace(OrbitItem added)
    {
        if (_replaceItem is { } target)
        {
            var items = _manager.Config.Primary.Items;
            int at = items.IndexOf(target);
            if (at < 0)
                items.Add(added);
            else
                items[at] = added;

            if (ReferenceEquals(_expandedGroup, target)) CollapseGroup();
            if (ReferenceEquals(_selectedItem, target)) HideItemDetails();
            _manager.NotifyItemsChanged();
            RebuildItemsList();
        }
        else if (_replaceQuadrantSlot >= 0)
        {
            PutInQuadrantSlot(_replaceQuadrantSlot, added);
        }
        else
        {
            return;
        }

        EndReplace();
        RenderPreview();
        UpdateOrbitEditSummary();
    }

    private void EndPreviewDrag()
    {
        if (_dragBorder is not null)
            _dragBorder.RenderTransform = null;
        // Preview_PointerMoved hides the name pills for the length of a drag and a render puts them
        // back, but a drop that changes nothing (onto its own place, or in a group whose order is
        // fixed) never renders.
        PreviewLabelCanvas.Visibility = Visibility.Visible;
        _dragBorder = null;
        _dragItem = null;
        _dragSlot = -1;
        _dragMoved = false;
        _dragTentativeSlot = -1;
        _dragTentativeQuadrantSlot = -1;
        _dragTentativeGroupSlot = -1;
    }

    /// <summary>
    /// Which place on an open group's arc <paramref name="point"/> is nearest to, by proximity —
    /// <see cref="QuadrantSlotAt"/>'s approach, not <see cref="PreviewSlotAt"/>'s, because the arc's
    /// slot count varies with how many children the group has and its slots don't sit at the fixed
    /// angles a full orbit's do. Includes the leading and trailing "add" ends, so dragging a child
    /// toward either one targets the front or back of the list rather than only the slots between
    /// them.
    /// </summary>
    private int GroupSlotAt(Point point, bool requireHit = true)
    {
        int count = _groupSlotBorders.Count;
        if (count == 0) return -1;

        double step = OrbitMetrics.FlyoutStepDegreesFor(_previewRadius, SettingsGroupArcItemGap) * Math.PI / 180.0;
        double first = -((count - 1) / 2.0) * step;

        int best = -1;
        double bestGap = double.MaxValue;
        for (int i = 0; i < count; i++)
        {
            double angle = first + i * step;
            double x = _previewCenter.X + _previewRadius * Math.Cos(angle);
            double y = _previewCenter.Y + _previewRadius * Math.Sin(angle);
            double gap = Math.Sqrt((point.X - x) * (point.X - x) + (point.Y - y) * (point.Y - y));
            if (gap < bestGap)
            {
                bestGap = gap;
                best = i;
            }
        }

        if (!requireHit)
            return best;
        return bestGap <= _previewBubble / 2 + 4 ? best : -1;
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

    /// <summary>
    /// Drags one of the corner's items into another of its places. Dropped on an empty place past
    /// the last item it goes exactly there, its old place left empty (see <see cref="OrbitSlots.Moved"/>).
    /// </summary>
    private void MoveQuadrantItem(int from, int to)
    {
        var list = HotCornerItems.Edit(_manager.Config, SystemOrbitItems.CreateSettings());
        if (from < 0 || from >= list.Count || to < 0 || to >= QuadrantLayout.ItemCount || from == to)
        {
            RenderPreview();
            return;
        }

        var moved = OrbitSlots.Moved(list, list[from], to, QuadrantLayout.ItemCount);
        list.Clear();
        list.AddRange(moved);
        OrbitSlots.TrimTrailing(list);
        SaveCornerArrangement();
        RenderPreview();
    }

    /// <summary>
    /// Writes a change to the corner's own arrangement (<see cref="AppConfig.HotCornerOrbit"/>) and
    /// tells everything that shows it. Through <see cref="OrbitManager.NotifyItemsChanged"/> and not
    /// <see cref="OrbitManager.Save"/> alone: a bare save leaves the file right and a dock that is
    /// open beside this window drawing the arrangement it had before, until it is next summoned.
    /// </summary>
    private void SaveCornerArrangement() => _manager.NotifyItemsChanged();

    /// <summary>
    /// Takes one item off the corner. The orbit itself keeps it, and the other items stay where they
    /// are: the place it leaves is empty, not closed up.
    /// </summary>
    private void RemoveQuadrantItem(int slot)
    {
        var list = HotCornerItems.Edit(_manager.Config, SystemOrbitItems.CreateSettings());
        if (slot < 0 || slot >= list.Count || list[slot].IsEmpty || list.Count(i => !i.IsEmpty) <= 1)
            return;

        OrbitSlots.Vacate(list, list[slot]);
        SaveCornerArrangement();
        HideItemDetails();
        RenderPreview();
    }

    /// <summary>
    /// Puts <paramref name="added"/> in corner place <paramref name="slot"/>: an empty place, one past
    /// the end (the "+" after the last item), or an occupied one, which it replaces. Items in the
    /// places around it do not move.
    /// </summary>
    private void PutInQuadrantSlot(int slot, OrbitItem added)
    {
        var list = HotCornerItems.Edit(_manager.Config, SystemOrbitItems.CreateSettings());
        if (slot < 0 || slot >= QuadrantLayout.ItemCount) return;

        if (slot < list.Count)
            list[slot] = added;
        else
            OrbitSlots.PlaceAt(list, slot, added, QuadrantLayout.ItemCount);
        SaveCornerArrangement();
    }

    /// <summary>
    /// Opens the "ADD TO ORBIT" sidebar aimed at an empty corner place, the same place the orbit's
    /// own "+" goes: the next thing clicked or dragged there fills <paramref name="slot"/> (see
    /// <see cref="CompleteReplace"/>). The item belongs to the corner rather than to the orbit — this
    /// is the corner's own arrangement — so it goes into that list and nowhere else.
    /// </summary>
    private void AddQuadrantItem(int slot)
    {
        _replaceItem = null;
        _replaceQuadrantSlot = slot;
        _replaceItemName = null;
        BeginReplaceViaSidebar();
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

    /// <summary>
    /// Glyph plus, for a group or the Tools item that still has visible children, the same outward
    /// arc bump the live orbit dock draws.
    /// </summary>
    private UIElement PreviewBubbleContent(
        OrbitItem item, double glyphSize, Brush foreground, double diameter, double angleRad)
    {
        // The item's own glyph colour when it has one — the arc follows it, as it does on the dock,
        // where the arc shares the glyph's brush.
        var tinted = IconColors.BrushFor(item, foreground);
        var glyph = PreviewGlyph(item.Glyph, glyphSize, tinted);
        if (!ShowsGroupArc(item))
            return glyph;

        // Centred explicitly. The bubble's Border hands its child a content area smaller than the
        // diameter (its ring takes 1 to 1.5px off each side), and a child bigger than the room it
        // is given does not stay centred by default: Stretch gives way to top-left, which shifted
        // the glyph and the arc together, right and down by the ring's width, only on items that
        // carry an arc. Centre overflows evenly instead, so both sit on the bubble's own centre.
        var grid = new Grid
        {
            Width = diameter,
            Height = diameter,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(glyph);
        grid.Children.Add(BuildPreviewGroupArc(diameter, angleRad, tinted));
        return grid;
    }

    private static bool ShowsGroupArc(OrbitItem item) =>
        item.IsGroup &&
        !string.Equals(item.Target, SystemOrbitItems.SettingsTarget, StringComparison.OrdinalIgnoreCase) &&
        item.Children.Any(c => !c.Hidden);

    /// <summary>
    /// Copied from <c>OrbitWindow.BuildGroupArc</c>: a 30° stroke hugging the bubble's outer edge
    /// in the direction the item already sits, so a group reads the same here as on the dock.
    /// </summary>
    private static Microsoft.UI.Xaml.Shapes.Path BuildPreviewGroupArc(double diameter, double angleRad, Brush stroke)
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

        return new Microsoft.UI.Xaml.Shapes.Path
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

    private void AttachPreviewHover(Border border, Border? partner = null)
    {
        if (partner is not null)
        {
            _previewHoverPartners[border] = partner;
            _previewHoverPartners[partner] = border;
            WirePreviewHover(partner);
        }
        WirePreviewHover(border);
    }

    private void WirePreviewHover(Border border)
    {
        border.PointerEntered += (_, _) =>
        {
            if (_dragMoved) return;
            HighlightPreviewPair(border);
        };
        border.PointerExited += (_, _) =>
        {
            if (ReferenceEquals(_hoveredPreviewOrigin, border))
                ClearPreviewHover();
        };
    }

    private void HighlightPreviewPair(Border origin)
    {
        ClearPreviewHover();
        _hoveredPreviewOrigin = origin;
        HighlightPreviewBorder(origin);
        if (_previewHoverPartners.TryGetValue(origin, out var partner))
        {
            // With labels hidden, bring this node's own pill up for the duration of the hover, then
            // put it away again on exit (the restore runs in ClearPreviewHover). The partner is the
            // pill when its Child is the label's TextBlock.
            if (_labelsHidden && partner.Visibility == Visibility.Collapsed && partner.Child is TextBlock)
            {
                partner.Visibility = Visibility.Visible;
                _hoveredPreviewRestores.Add(() => partner.Visibility = Visibility.Collapsed);
            }
            HighlightPreviewBorder(partner);
        }

        // Border has no cursor of its own to set (see Controls/HandCursor.cs) — Root is a
        // CursorHost for exactly this, same as the "Add to orbit" sidebar's own hand cursor says
        // every ring item is just as clickable/draggable as a sidebar row is.
        Root.SetCursor(InputSystemCursorShape.Hand);
    }

    /// <summary>
    /// Hover feedback for a disc or a name pill: no outline (docs/orbit-screen-layouts.html's .disc
    /// and .pill have none, which is why item styling dropped BorderBrush/BorderThickness — see
    /// ApplyBubbleShadow), so hover instead inverts the bubble to the opposite theme's colours — a
    /// white disc in light mode goes near-black on hover, exactly as it would look sitting on a dark
    /// orbit, and the reverse in dark mode. <see cref="PreviewBubbleContent"/>/<see cref="AddSlotLabel"/>
    /// build every bubble from one of three known shapes — a bare glyph, a glyph plus group arc, or a
    /// pill's text — so those are matched directly rather than walked generically.
    /// </summary>
    private void HighlightPreviewBorder(Border border)
    {
        bool invertedDark = !ThemeHelper.IsDark(Root);
        var ink = new SolidColorBrush(ThemeHelper.ColorFor(invertedDark, "SwiftXForegroundBrush"));

        switch (border.Child)
        {
            case TextBlock text:
                InvertBubbleBackground(border, invertedDark, "OrbitLabelPillBrush");
                InvertIconForeground(text, ink);
                break;
            case Grid grid:
                InvertBubbleBackground(border, invertedDark, "OrbitNodeBackgroundBrush");
                foreach (var child in grid.Children)
                {
                    if (child is FontIcon icon) InvertIconForeground(icon, ink);
                    else if (child is Microsoft.UI.Xaml.Shapes.Shape shape) InvertShapeStroke(shape, ink);
                }
                break;
            default:
                // FontIcon (a plain item or an empty "add" slot) and anything else alike: the disc
                // swap is still a reasonable hover cue on its own.
                InvertBubbleBackground(border, invertedDark, "OrbitNodeBackgroundBrush");
                if (border.Child is FontIcon only) InvertIconForeground(only, ink);
                break;
        }
    }

    private void InvertBubbleBackground(Border border, bool invertedDark, string key)
    {
        var original = border.Background;
        border.Background = new SolidColorBrush(ThemeHelper.ColorFor(invertedDark, key));
        _hoveredPreviewRestores.Add(() => border.Background = original);
    }

    private void InvertIconForeground(IconElement icon, Brush inverted)
    {
        var original = icon.Foreground;
        icon.Foreground = inverted;
        _hoveredPreviewRestores.Add(() => icon.Foreground = original);
    }

    private void InvertIconForeground(TextBlock text, Brush inverted)
    {
        var original = text.Foreground;
        text.Foreground = inverted;
        _hoveredPreviewRestores.Add(() => text.Foreground = original);
    }

    private void InvertShapeStroke(Microsoft.UI.Xaml.Shapes.Shape shape, Brush inverted)
    {
        var original = shape.Stroke;
        shape.Stroke = inverted;
        _hoveredPreviewRestores.Add(() => shape.Stroke = original);
    }

    private void ClearPreviewHover()
    {
        foreach (var restore in _hoveredPreviewRestores)
            restore();
        _hoveredPreviewRestores.Clear();
        bool wasHovering = _hoveredPreviewOrigin is not null;
        _hoveredPreviewOrigin = null;
        if (wasHovering)
            Root.ClearCursor();
    }

    private void ReorderItem(OrbitItem dragged, int targetSlot)
    {
        // Slots past the user range hold the pinned Settings/Close bubbles; a drop there is
        // ignored rather than clamped, so the item stays where the user last put it.
        if (targetSlot < 0 || targetSlot >= OrbitMetrics.UserSlotCount) return;

        var before = OrbitSlots.Visible(_manager.Config.Primary.Items);
        if (!before.Contains(dragged)) return;

        // Move, don't insert-before-the-occupant. That older form removed the item first and so
        // landed a forward drag one slot short of where it was dropped — dragging an item to the
        // right appeared to do nothing at all. Dropped on an empty slot past the last item it goes
        // exactly there (see OrbitSlots.Moved), leaving its old slot empty.
        var visible = OrbitSlots.Moved(before, dragged, targetSlot);
        if (visible.SequenceEqual(before)) return;

        // Write the new order back over the positions the visible items already occupy, so
        // hidden entries keep their place in the list instead of being shuffled by an index
        // computed from a filtered view of it. A move into empty slots past the end is longer than
        // what was there, so the extra places go on the end.
        var items = _manager.Config.Primary.Items;
        var positions = new List<int>();
        for (int i = 0; i < items.Count && positions.Count < before.Count; i++)
        {
            if (!items[i].Hidden)
                positions.Add(i);
        }
        for (int i = 0; i < positions.Count; i++)
            items[positions[i]] = visible[i];
        for (int i = positions.Count; i < visible.Count; i++)
            items.Add(visible[i]);
        OrbitSlots.TrimTrailing(items);

        _manager.NotifyItemsChanged();
        RenderPreview();
        RebuildItemsList();
    }

    /// <summary>Group-arc counterpart of <see cref="ReorderItem"/>: moves one of the open group's
    /// own children instead of a top-level orbit item. <paramref name="targetSlot"/> is an arc
    /// slot index (see <see cref="GroupSlotAt"/>), one ahead of the child index it targets since
    /// slot 0 is always the leading add end.</summary>
    private void ReorderGroupChild(OrbitItem dragged, int targetSlot)
    {
        var group = _expandedGroup;
        if (group is null || targetSlot < 0 || QuadrantLayout.IsQuickAccess(group)) return;

        var visible = group.Children.Where(c => !c.Hidden).ToList();
        int from = visible.IndexOf(dragged);
        if (from < 0) return;

        int to = Math.Clamp(targetSlot - GroupAddEnds(group), 0, visible.Count - 1);
        if (to == from) return;

        visible.RemoveAt(from);
        visible.Insert(to, dragged);

        var items = group.Children;
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
    /// The details card currently on screen, so a second click can close it and so any teardown
    /// path can take it down without hunting for it.
    /// </summary>
    private Flyout? _detailsFlyout;

    /// <summary>
    /// Shows what an item is, on a card anchored where it was clicked.
    /// <para>
    /// This used to fill a read-only panel at the bottom of the sidebar. A panel on the far side of
    /// the screen made you look away from the thing you had just clicked in order to read its name
    /// back, and it had nowhere sensible to put an icon editor — which is the one thing the card
    /// can do that the panel could not. Clicking the same item again closes it, so the card is its
    /// own toggle the way the panel was.
    /// </para>
    /// <para>
    /// The header's Replace and Close are squircle plates that answer the pointer in their own
    /// colour -- Replace in the accent as the user has set it, Close in the orbit's destructive
    /// red -- see <see cref="BuildDetailsCornerButton"/>.
    /// </para>
    /// </summary>
    private void ShowItemDetails(OrbitItem item, Point at)
    {
        // Whatever put an item's details on screen — the main ring, the quadrant preview, a
        // group's own arc, a title pill, or a tap on the open group's name.
        if (ReferenceEquals(_selectedItem, item) && _detailsFlyout is not null)
        {
            HideItemDetails();
            return;
        }

        HideItemDetails();
        _selectedItem = item;

        var muted = ThemeHelper.Brush(Root, "SwiftXMutedBrush");
        var foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush");

        var card = new Grid { RowSpacing = 10, Width = 264 };
        for (int i = 0; i < 2; i++)
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header row: the kind badge back on the left where the very first version of this card
        // had it, with Replace (when this item has somewhere to send it) and Close pinned to the
        // right rather than sharing a row with the title -- the title moved down beside the logo
        // below and stays there; only the label moved back up. The 6px gap is wider than the 4
        // it used to be so a keyboard focus ring drawn 3px outside one button does not run into
        // its neighbour.
        var corner = new Grid { ColumnSpacing = 6 };
        corner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        corner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        corner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        corner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badge = new Border
        {
            Background = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitNodeBackgroundBrush"),
            BorderBrush = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitCanvasBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = DetailBadgeLabel(item.Kind),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = foreground,
            },
        };
        Grid.SetColumn(badge, 0);
        corner.Children.Add(badge);

        if (CanReplaceFromDetails(item))
        {
            // U+21CC (a real Unicode arrow pair, not a Fluent glyph) needs Segoe UI rather than
            // the icon font BuildDetailsCornerButton defaults to, which has no glyph at that
            // codepoint. Reads as "swap" without doubling up on the pencil the icon-picker badge
            // two rows down already wears for "edit". The hover fill is CurrentAccentColor, read
            // now as the card is built, so it is the user's own pick rather than a built-in colour
            // (OrbitCanvasAccentBrush is a fixed teal that never follows it); the glyph colour is
            // chosen against both the fill and its pressed shade so it cannot flip mid-press.
            var accent = CurrentAccentColor();
            var replace = BuildDetailsCornerButton(
                0x21CC, "Replace", "Replace " + item.DisplayName,
                accent, AccentPalette.OnAccent(accent, AccentPalette.Pressed(accent)), "Segoe UI");
            replace.Click += (_, _) => ReplaceSelectedItem(item);
            Grid.SetColumn(replace, 2);
            corner.Children.Add(replace);
        }

        // Close reuses the fill/glyph pair the orbit's own Close bubble turns on hover, so the two
        // read as the same destructive signal.
        var close = BuildDetailsCornerButton(
            0xE711, "Close", "Close details",
            ThemeHelper.ColorFor(Root, "OrbitCloseHoverBackground"),
            ThemeHelper.ColorFor(Root, "OrbitCloseHoverForeground"));
        close.Click += (_, _) => HideItemDetails();
        Grid.SetColumn(close, 3);
        corner.Children.Add(close);

        Grid.SetRow(corner, 0);
        card.Children.Add(corner);

        // Body: the logo (squircle-plated regardless of the orbit's own item-shape setting -- this
        // is chrome for the card, not a bubble on the ring) beside the title and, under it, what
        // the item actually points at.
        var body = new Grid { ColumnSpacing = 12 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var logo = BuildEditableIcon(item);
        Grid.SetColumn(logo, 0);
        body.Children.Add(logo);

        var textStack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Top };
        textStack.Children.Add(new TextBlock
        {
            Text = item.DisplayName,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            Foreground = foreground,
        });
        textStack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(item.Target) ? "\u2014" : item.Target,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = muted,
        });
        Grid.SetColumn(textStack, 1);
        body.Children.Add(textStack);

        Grid.SetRow(body, 1);
        card.Children.Add(body);

        _detailsFlyout = new Flyout { Content = card, Placement = FlyoutPlacementMode.Right };
        // Closing by clicking away has to clear the selection too, or the next click on the same
        // item would read as the second half of a toggle and open nothing.
        _detailsFlyout.Closed += (_, _) =>
        {
            _detailsFlyout = null;
            _selectedItem = null;
        };
        _detailsFlyout.ShowAt(PreviewCanvas, new FlyoutShowOptions { Position = at });
    }

    /// <summary>
    /// One of the details card's two header buttons (Replace, Close): a squircle plate that answers
    /// the pointer as a whole. At rest it wears the kind badge's own fill and rim so the three
    /// controls in that row read as one family, with the glyph muted; under the pointer it fills
    /// with <paramref name="hoverFill"/> and the glyph flips to <paramref name="onHover"/> so it
    /// stays legible on that fill; pressed darkens the fill by the step
    /// <see cref="AccentPalette.Pressed"/> gives every accented control, so both buttons press the
    /// same way whatever colour they hover in; and keyboard focus tints the rim in the hover
    /// colour on top of the framework's own focus ring, so a tabbed-to button previews what it is
    /// about to do.
    /// <para>
    /// This is not <see cref="BuildRowIconButton"/> plus <see cref="WireOrbitGlyphHover"/>. Those
    /// two leave a button fill-less and only tint its glyph, which is right for the Elements cards'
    /// delete button and the orbit-preview bubbles that share them, and too faint here, where the
    /// control has to say which of the two it is before it is clicked.
    /// </para>
    /// <para>
    /// The default Button template is kept rather than replaced. Its chrome is a single
    /// ContentPresenter that takes <c>CornerRadius</c> from the button, so the rest, pointer-over
    /// and pressed fills and rims are all clipped to the same squircle; the state colours are the
    /// template's own <c>ButtonBackground*</c> / <c>ButtonBorderBrush*</c> / <c>ButtonForeground*</c>
    /// keys, overridden per button the way <see cref="BuildCardFooterButton"/> does. The glyph is
    /// deliberately left without a Foreground of its own so it follows the last of those.
    /// </para>
    /// <para>
    /// Under a Windows High Contrast theme the colours are left to the template, so the system's
    /// own palette (button face, highlight on hover) wins, as it does everywhere else in the app
    /// -- see <see cref="HighContrast"/>. Only the size, corner radius and glyph are ours there.
    /// </para>
    /// </summary>
    private Button BuildDetailsCornerButton(
        int glyph, string tooltip, string automationName, Color hoverFill, Color onHover,
        string? fontFamily = null)
    {
        const double size = 30;

        var icon = new FontIcon { Glyph = char.ConvertFromUtf32(glyph), FontSize = 14 };
        if (fontFamily is not null)
            icon.FontFamily = new FontFamily(fontFamily);

        var button = new Button
        {
            Content = icon,
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            // The squircle proportion (0.28 of the side) the card's logo plate in BuildEditableIcon
            // and OrbitMetrics.CornerRadiusFor(OrbitItemShape.Squircle) both use. Not
            // CornerRadiusFor itself: that follows the orbit's own item-shape setting, and this is
            // the card's chrome, which is a squircle whatever that is set to.
            CornerRadius = new CornerRadius(Math.Round(size * 0.28)),
        };
        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, automationName);

        if (HighContrast.IsActive())
            return button;

        var restBorder = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitCanvasBorderBrush");
        var hover = new SolidColorBrush(hoverFill);
        var pressed = new SolidColorBrush(AccentPalette.Pressed(hoverFill));
        var onFill = new SolidColorBrush(onHover);

        button.Background = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitNodeBackgroundBrush");
        button.BorderBrush = restBorder;
        button.Foreground = ThemeHelper.Brush(Root, "SwiftXMutedBrush");
        button.Resources["ButtonBackgroundPointerOver"] = hover;
        button.Resources["ButtonBorderBrushPointerOver"] = hover;
        button.Resources["ButtonForegroundPointerOver"] = onFill;
        button.Resources["ButtonBackgroundPressed"] = pressed;
        button.Resources["ButtonBorderBrushPressed"] = pressed;
        button.Resources["ButtonForegroundPressed"] = onFill;

        // Keyboard only: the flyout hands focus to its first button when it opens, and that
        // programmatic focus should not leave one of them looking half-pressed before anyone has
        // touched the card.
        button.GotFocus += (_, _) =>
        {
            if (button.FocusState == FocusState.Keyboard)
                button.BorderBrush = hover;
        };
        button.LostFocus += (_, _) => button.BorderBrush = restBorder;
        return button;
    }

    private void HideItemDetails()
    {
        _selectedItem = null;
        var flyout = _detailsFlyout;
        _detailsFlyout = null;
        flyout?.Hide();
    }

    /// <summary>
    /// Whether the details card's Replace button has anywhere to send <paramref name="item"/>. A
    /// standalone tool is left out for the same reason the right-click menu leaves it out (see
    /// <see cref="BuildOrbitItemMenu"/>), and a group's own child is left out because there is no
    /// per-child replace yet — only the group itself, reached via its header, has one.
    /// </summary>
    private bool CanReplaceFromDetails(OrbitItem item) =>
        !item.IsTool && (_expandedGroup is null || ReferenceEquals(_expandedGroup, item));

    /// <summary>
    /// Routes the details card's Replace button to whichever "Replace…" actually owns
    /// <paramref name="item"/>'s place — the quadrant's own list when the corner preview is showing,
    /// the top-level orbit otherwise (including when <paramref name="item"/> is the group currently
    /// open, reached via its header — see <see cref="CanReplaceFromDetails"/>).
    /// </summary>
    private void ReplaceSelectedItem(OrbitItem item)
    {
        if (_previewQuadrant)
        {
            for (int i = 0; i < _quadrantSlotBorders.Length; i++)
            {
                if (_quadrantSlotBorders[i]?.Tag is OrbitItem tagged && ReferenceEquals(tagged, item))
                {
                    ReplaceQuadrantItem(i);
                    return;
                }
            }
            return;
        }

        ReplaceOrbitItem(item);
    }

    /// <summary>
    /// The short tag the details card wears, in the same vocabulary the Elements cards use. A small
    /// duplicate of that screen's own mapping rather than a shared one: this list answers for the
    /// two system items as well, which never reach an Elements card at all.
    /// </summary>
    private static string DetailBadgeLabel(OrbitItemKind kind) => kind switch
    {
        OrbitItemKind.Application => "APP",
        OrbitItemKind.File => "FILE",
        OrbitItemKind.Folder => "FOLDER",
        OrbitItemKind.WebLink => "LINK",
        OrbitItemKind.Shortcut => "SHORTCUT",
        OrbitItemKind.Group => "GROUP",
        OrbitItemKind.Tool => "TOOL",
        _ => "ITEM",
    };

    /// <summary>
    /// The item's icon, large, with a pencil badge that opens a picker — the one thing the old
    /// sidebar inspector had no room for and the reason the card exists at all.
    /// <para>
    /// The picker is built on <see cref="IconChoices.All"/>, a curated glyph set that had been
    /// sitting in the tree unreferenced since it was written. Choosing one writes
    /// <see cref="OrbitItem.CustomGlyph"/>; "Use a picture" writes
    /// <see cref="OrbitItem.CustomIconPath"/>; "Reset" clears both and lets
    /// <see cref="IconService"/> go back to resolving the target's own icon.
    /// <see cref="OrbitItem.IconKey"/> covers all three fields, so any of them changing is enough
    /// for a resolved icon to be re-resolved rather than served stale from the last render.
    /// </para>
    /// </summary>
    private FrameworkElement BuildEditableIcon(OrbitItem item)
    {
        const double plate = 56;

        var glyph = new FontIcon
        {
            Glyph = item.Glyph,
            FontSize = 24,
            Foreground = IconColors.BrushFor(item, ThemeHelper.Brush(Root, "SwiftXForegroundBrush")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (GlyphFonts.IsTextGlyph(item.Glyph))
            glyph.FontFamily = new FontFamily("Segoe UI");

        var image = new Image
        {
            Width = plate - 18,
            Height = plate - 18,
            Stretch = Stretch.Uniform,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        LoadRowIcon(item, image, glyph);

        var content = new Grid();
        content.Children.Add(glyph);
        content.Children.Add(image);

        // Always a squircle, regardless of Config.ItemShape: this plate is the details card's own
        // chrome, not a bubble on the ring, so it does not follow the orbit's item-shape setting the
        // way OrbitItemGeometry.Apply would have it do. Same 0.28 factor OrbitMetrics.CornerRadiusFor
        // uses for OrbitItemShape.Squircle.
        var plateBorder = new Border
        {
            Width = plate,
            Height = plate,
            Background = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitNodeBackgroundBrush"),
            BorderBrush = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitCanvasBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Math.Round(plate * 0.28)),
            Child = content,
        };

        var pencil = new Button
        {
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(11),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -4, -4),
            Content = new FontIcon
            {
                Glyph = "\uE70F",
                FontSize = 10,
                Foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush"),
            },
            Flyout = BuildIconPicker(item),
        };
        ToolTipService.SetToolTip(pencil, "Change this item's icon");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pencil, "Change icon");

        return new Grid
        {
            Width = plate,
            Height = plate,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { plateBorder, pencil },
        };
    }

    /// <summary>Icons and swatches to a row in the icon picker, each a square tile with a gap between.</summary>
    private const int IconPickerColumns = 6;
    private const double IconPickerTile = 34;
    private const double IconPickerGap = 4;

    /// <summary>
    /// The width of one row of tiles, which is the width the whole picker is held to: the
    /// full-width buttons below the grid and the caption line up with the tiles instead of
    /// stretching to a width of their own and leaving the grid short of the flyout's right edge.
    /// </summary>
    private const double IconPickerWidth =
        IconPickerColumns * IconPickerTile + (IconPickerColumns - 1) * IconPickerGap;

    /// <summary>
    /// The glyph grid behind the pencil. Six to a row, which is what fits the card's width without
    /// the flyout growing wider than the card it hangs off.
    /// </summary>
    private Flyout BuildIconPicker(OrbitItem item)
    {
        var grid = new Grid { ColumnSpacing = IconPickerGap, RowSpacing = IconPickerGap };
        for (int c = 0; c < IconPickerColumns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var choices = IconChoices.All;
        for (int r = 0; r <= (choices.Count - 1) / IconPickerColumns; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < choices.Count; i++)
        {
            var choice = choices[i];
            var button = new Button
            {
                Width = IconPickerTile,
                Height = IconPickerTile,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Content = new FontIcon
                {
                    Glyph = choice.Glyph,
                    FontSize = 16,
                    Foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush"),
                },
            };
            ToolTipService.SetToolTip(button, choice.Name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, choice.Name);
            button.Click += (_, _) => ApplyIconChoice(item, choice.Glyph, null);
            Grid.SetRow(button, i / IconPickerColumns);
            Grid.SetColumn(button, i % IconPickerColumns);
            grid.Children.Add(button);
        }

        var picture = new Button { Content = "Use a picture\u2026", HorizontalAlignment = HorizontalAlignment.Stretch };
        picture.Click += (_, _) => PickIconImage(item);

        var module = new Button { Content = "Select an app or DLL\u2026", HorizontalAlignment = HorizontalAlignment.Stretch };
        module.Click += (_, _) => PickModuleIcon(item);

        // Content is an explicit TextBlock, not a bare string, so WireOrbitGlyphHover's walk can
        // reach and tint it the same way it tints an icon glyph — a raw string content is wrapped
        // by the button's own template at a point this walk never sees.
        var resetText = new TextBlock { Text = "Reset" };
        var reset = new Button { Content = resetText, HorizontalAlignment = HorizontalAlignment.Stretch };
        WireOrbitGlyphHover(reset);
        // Everything about the icon back to what it resolves to unaided, the colour included.
        reset.Click += (_, _) =>
        {
            item.CustomIconColor = null;
            ApplyIconChoice(item, null, null);
        };

        var panel = new StackPanel { Spacing = 8, Width = IconPickerWidth };
        panel.Children.Add(new TextBlock
        {
            Text = "CHOOSE AN ICON",
            Style = (Style)Root.Resources["SidebarEyebrow"],
        });
        panel.Children.Add(grid);
        panel.Children.Add(BuildIconColorSection(item));
        panel.Children.Add(picture);
        panel.Children.Add(module);
        panel.Children.Add(reset);

        // A flyout will not go narrower than its presenter's own minimum width (320 by default),
        // which is wider than a row of tiles plus the presenter's padding. Left alone it would
        // stretch every full-width button past the grid, so the minimum is dropped and the panel's
        // own width is what sets it.
        //
        // No scrollbar, ever: the content is a fixed height (the free colour picker is its own
        // flyout, see BuildCustomColorFlyout) and fits whole, so the presenter's own vertical
        // scrolling is switched off as well and nothing here caps the height.
        var presenterStyle = new Style(typeof(FlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
        presenterStyle.Setters.Add(new Setter(
            ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));

        var flyout = new Flyout
        {
            FlyoutPresenterStyle = presenterStyle,
            Content = panel,
        };

        // Remembered while it is up, so applying a choice can close it wherever it was opened from
        // (see ApplyIconChoice): the details card takes its nested picker down with itself, but a
        // picker opened on a sidebar row or the ring (see ShowIconPicker) has nothing else to do it.
        flyout.Opened += (_, _) => _iconPickerFlyout = flyout;
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(_iconPickerFlyout, flyout))
                _iconPickerFlyout = null;
        };
        return flyout;
    }

    /// <summary>The "Choose an icon" flyout that is on screen right now, if any.</summary>
    private Flyout? _iconPickerFlyout;

    /// <summary>
    /// Opens "Choose an icon" for <paramref name="item"/> on <paramref name="anchor"/> — the same
    /// picker the details card's pencil opens, reached from a right-click menu instead (a group
    /// row in the sidebar, a group on the ring).
    /// </summary>
    private void ShowIconPicker(OrbitItem item, FrameworkElement anchor)
    {
        HideItemDetails();
        BuildIconPicker(item).ShowAt(anchor);
    }

    /// <summary>
    /// The colour half of the icon picker: a row of swatches, a tile that opens a full colour
    /// picker for anything else (see <see cref="BuildCustomColorFlyout"/>), and a way back to the
    /// theme's own colour. The swatches apply as soon as they are clicked.
    /// <para>
    /// Only a glyph is tinted (see <see cref="OrbitItem.CustomIconColor"/>), which is what the
    /// caption under the swatches says for an item showing a picture.
    /// </para>
    /// </summary>
    private FrameworkElement BuildIconColorSection(OrbitItem item)
    {
        bool hasColor = IconColors.TryGet(item, out var current);
        var accent = CurrentAccentColor();
        var selectedBrush = new SolidColorBrush(accent);

        var section = new StackPanel { Spacing = 8 };
        section.Children.Add(new TextBlock
        {
            Text = "ICON COLOUR",
            Style = (Style)Root.Resources["SidebarEyebrow"],
        });

        const int columns = IconPickerColumns;
        var swatches = new Grid { ColumnSpacing = IconPickerGap, RowSpacing = IconPickerGap };
        for (int c = 0; c < columns; c++)
            swatches.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        int tiles = IconColors.Presets.Count + 1;
        for (int r = 0; r <= (tiles - 1) / columns; r++)
            swatches.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < IconColors.Presets.Count; i++)
        {
            var swatch = IconColors.Presets[i];
            AccentPalette.TryParseHex(swatch.Hex, out var color);
            bool selected = hasColor &&
                string.Equals(item.CustomIconColor, swatch.Hex, StringComparison.OrdinalIgnoreCase);

            var button = new Button
            {
                Width = IconPickerTile,
                Height = IconPickerTile,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Content = new Border
                {
                    Width = 18,
                    Height = 18,
                    CornerRadius = new CornerRadius(9),
                    Background = new SolidColorBrush(color),
                },
            };
            if (selected)
            {
                button.BorderBrush = selectedBrush;
                button.BorderThickness = new Thickness(2);
            }
            ToolTipService.SetToolTip(button, swatch.Name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, swatch.Name + " icon colour");
            button.Click += (_, _) => ApplyIconColor(item, color);
            Grid.SetRow(button, i / columns);
            Grid.SetColumn(button, i % columns);
            swatches.Children.Add(button);
        }

        // The last tile: any other colour. It opens the full picker in a flyout of its own rather
        // than unfolding it here, because the picker is far taller than everything else in this
        // one put together and the whole icon picker has to stay a fixed height with no scrollbar.
        var custom = new Button
        {
            Width = IconPickerTile,
            Height = IconPickerTile,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon
            {
                Glyph = char.ConvertFromUtf32(0xE790),
                FontSize = 16,
                Foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush"),
            },
            Flyout = BuildCustomColorFlyout(item, hasColor ? current : accent),
        };
        ToolTipService.SetToolTip(custom, "Pick another colour");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(custom, "Pick another icon colour");
        Grid.SetRow(custom, IconColors.Presets.Count / columns);
        Grid.SetColumn(custom, IconColors.Presets.Count % columns);
        swatches.Children.Add(custom);

        section.Children.Add(swatches);

        var defaultColor = new Button
        {
            Content = "Default colour",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = hasColor,
        };
        defaultColor.Click += (_, _) => ApplyIconColor(item, null);
        section.Children.Add(defaultColor);

        section.Children.Add(new TextBlock
        {
            Text = "Colours the icon's mark; an app's or site's own picture keeps its colours.",
            Style = (Style)Root.Resources["SidebarCaption"],
            TextWrapping = TextWrapping.Wrap,
            // Held to the tiles' width: a wrapping line left to size itself runs out to its full
            // one-line length and sets the width of the whole picker.
            MaxWidth = IconPickerWidth,
        });
        return section;
    }

    /// <summary>
    /// The free colour picker behind the palette tile: a full <see cref="ColorPicker"/> and the button
    /// that applies it. Applied on the button, not as the colour changes, because ColorChanged fires
    /// continuously through a drag and every apply rebuilds the sidebar the icon picker may be
    /// anchored to. The icon picker under it goes down with it, as it does for a swatch.
    /// </summary>
    private Flyout BuildCustomColorFlyout(OrbitItem item, Color initial)
    {
        var picker = new ColorPicker
        {
            IsAlphaEnabled = false,
            IsMoreButtonVisible = true,
            Color = initial,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(picker, "Icon colour picker");

        var use = new Button
        {
            Content = "Use this colour",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };

        var flyout = new Flyout
        {
            Content = new StackPanel { Spacing = 8, Children = { picker, use } },
        };
        use.Click += (_, _) =>
        {
            // Both go: hiding the icon picker does not by itself take a flyout opened from it down.
            flyout.Hide();
            ApplyIconColor(item, picker.Color);
        };
        return flyout;
    }

    /// <summary>
    /// Gives the item's glyph a colour (or, with null, the theme's own back) and pushes it
    /// everywhere. No re-resolve is needed, unlike <see cref="ApplyIconChoice"/>: the colour is not
    /// part of <see cref="OrbitItem.IconKey"/>, so no picture is affected.
    /// </summary>
    private void ApplyIconColor(OrbitItem item, Color? color)
    {
        _iconPickerFlyout?.Hide();
        item.CustomIconColor = color is { } c ? IconColors.ToStored(c) : null;
        HotCornerItems.KeepGatheredIcon(_manager.Config, SystemOrbitItems.CreateSettings(), item);

        _manager.NotifyItemsChanged();
        RenderPreview();
        RebuildItemsList();
        HideItemDetails();
    }

    /// <summary>
    /// Writes a chosen icon onto the item and pushes it everywhere. Clearing
    /// <see cref="OrbitItem.IconSignature"/> is what actually forces a re-resolve: the render path
    /// skips an item whose signature still matches its key, which after an icon change it no longer
    /// should.
    /// </summary>
    private void ApplyIconChoice(OrbitItem item, string? glyph, string? imagePath)
    {
        _iconPickerFlyout?.Hide();
        item.CustomGlyph = glyph;
        item.CustomIconPath = imagePath;
        item.IconImage = null;
        item.IconSignature = null;
        // Reset (both null) is what the item resolves to unaided; QuickAccess's own is its default
        // glyph, which KeepGatheredIcon leaves to the group when nothing is stored.
        HotCornerItems.KeepGatheredIcon(_manager.Config, SystemOrbitItems.CreateSettings(), item);

        _manager.NotifyItemsChanged();
        RenderPreview();
        RebuildItemsList();
        HideItemDetails();
    }

    private async void PickIconImage(OrbitItem item)
    {
        // async void, no caller: PickSingleFileAsync throws (rather than returning null) when a
        // picker is already open on this window — reachable from a double-click, or from this
        // picker and Export/Import's overlapping — so this is guarded the same way
        // AddItemWindow.Browse_Click guards the identical call.
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, _hwnd);
            foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".ico" })
                picker.FileTypeFilter.Add(extension);

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            // The glyph goes with it: IconKey carries both, and an item holding a picture and a custom
            // glyph at once would fall back to a glyph the user replaced the moment the file moved.
            ApplyIconChoice(item, null, file.Path);
        }
        catch (Exception ex)
        {
            Diag.Log("ShellWindow.PickIconImage failed: " + ex);
        }
    }

    /// <summary>
    /// "Use a picture…"'s counterpart for an icon that lives inside a module rather than as a file
    /// of its own: browse to an .exe or .dll, then choose which of its embedded icons to use.
    /// <see cref="ShowModuleIconGalleryAsync"/> does the choosing; this just gets a module path and
    /// bails out — straight to the shell's own icon for the file — when it turns out to carry none.
    /// </summary>
    private async void PickModuleIcon(OrbitItem item)
    {
        // async void, no caller, and the same picker call PickIconImage guards: it throws rather than
        // returning null when a picker is already open on this window, and the gallery it leads to
        // builds a tile per icon. Guarded the same way, for the same reason.
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, _hwnd);
            picker.FileTypeFilter.Add(".exe");
            picker.FileTypeFilter.Add(".dll");

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            int count = IconService.CountModuleIcons(file.Path);
            if (count <= 0)
            {
                ApplyIconChoice(item, null, null);
                return;
            }

            await ShowModuleIconGalleryAsync(item, file.Path, count);
        }
        catch (Exception ex)
        {
            Diag.Log("ShellWindow.PickModuleIcon failed: " + ex);
        }
    }

    /// <summary>
    /// Caps how many of a module's icons <see cref="ShowModuleIconGalleryAsync"/> extracts and
    /// shows. A single app's own .exe rarely carries more than a handful; a system DLL like
    /// shell32.dll can carry hundreds, and extracting each one is a real decode, not a lookup — the
    /// cap is what keeps opening the gallery on one of those responsive.
    /// </summary>
    private const int ModuleIconGalleryCap = 60;

    /// <summary>
    /// The gallery "Select an app or DLL…" opens once a module is picked: up to
    /// <see cref="ModuleIconGalleryCap"/> of its embedded icons, each loaded as its own tile fills
    /// in rather than making the whole grid wait on the slowest one. Picking a tile writes
    /// <see cref="OrbitItem.CustomIconPath"/> as "path,index" (see <see cref="IconService"/>) and
    /// closes both this flyout and the details card, the same way "Use a picture…" does.
    /// </summary>
    private async Task ShowModuleIconGalleryAsync(OrbitItem item, string path, int count)
    {
        int shown = Math.Min(count, ModuleIconGalleryCap);

        var grid = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        for (int c = 0; c < 6; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int r = 0; r <= (shown - 1) / 6; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{System.IO.Path.GetFileName(path).ToUpperInvariant()} — {count} ICON{(count == 1 ? "" : "S")}",
            Style = (Style)Root.Resources["SidebarEyebrow"],
        });
        panel.Children.Add(grid);

        var flyout = new Flyout
        {
            Content = new ScrollViewer
            {
                MaxHeight = 320,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel,
            },
        };
        flyout.ShowAt(PreviewCanvas);

        for (int i = 0; i < shown; i++)
        {
            int index = i;
            var image = new Image { Width = 22, Height = 22, Stretch = Stretch.Uniform };
            var button = new Button
            {
                Width = 34,
                Height = 34,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Content = image,
            };
            ToolTipService.SetToolTip(button, $"Icon {index}");
            button.Click += (_, _) =>
            {
                flyout.Hide();
                ApplyIconChoice(item, null, $"{path},{index}");
            };
            Grid.SetRow(button, index / 6);
            Grid.SetColumn(button, index % 6);
            grid.Children.Add(button);

            var source = await IconService.LoadModuleIconAsync(path, index);
            if (source is not null)
                image.Source = source;
        }
    }

    /// <summary>
    /// Opens <paramref name="group"/> on the canvas: the whole orbit is replaced by that group's
    /// own children, fanned on an arc, and the title along the bottom becomes the group's name to
    /// say where you are.
    /// <para>
    /// This used to reveal a horizontal strip below the canvas instead. A strip could show the
    /// children but not where they sit relative to anything, and it left the orbit they came out of
    /// still drawn above it — two arrangements on screen at once, only one of which the clicks were
    /// going to. Taking the canvas over is what the real orbit already does when a group is
    /// summoned, so the preview now previews that rather than a control of its own invention.
    /// </para>
    /// </summary>
    private void OpenGroup(OrbitItem group)
    {
        HideItemDetails();
        _expandedGroup = group;

        OrbitGroupName.Text = group.DisplayName;
        OrbitLayoutTitle.Visibility = Visibility.Collapsed;
        OrbitGroupName.Visibility = Visibility.Visible;

        RenderPreview();
    }

    /// <summary>
    /// Back out to the whole orbit. Safe to call when no group is open — every teardown path
    /// (an item deleted from elsewhere, a reset, leaving the screen) calls it blind.
    /// </summary>
    private void CollapseGroup()
    {
        if (_expandedGroup is null) return;

        _expandedGroup = null;
        HideItemDetails();

        OrbitGroupName.Visibility = Visibility.Collapsed;
        OrbitLayoutTitle.Visibility = Visibility.Visible;

        RenderPreview();
    }

    /// <summary>
    /// The open group's name, made tappable so that reaching the group's details does not require
    /// finding it again on the arc — it is right there in the toolbar the whole time the group is
    /// open.
    /// </summary>
    private void OrbitGroupName_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_expandedGroup is not null)
            ShowItemDetails(_expandedGroup, e.GetPosition(PreviewCanvas));
    }

    /// <summary>
    /// Draws the open group: its own mark at the centre where the hub would be, and its children on
    /// an arc to the right of it, each with the same outward name pill the full orbit's items get.
    /// <para>
    /// The arc is the real orbit's own flyout geometry — <see cref="OrbitMetrics.FlyoutBubble"/>
    /// and <see cref="OrbitMetrics.FlyoutStepDegrees"/> — rather than a fan invented for this
    /// screen, so what the preview shows is the shape a group actually opens into when it is
    /// summoned for real, at the same bubble size and the same angular spacing.
    /// </para>
    /// <para>
    /// A dashed slot sits at each end of the arc. Both open the same scoped add flow, targeted at
    /// this group's children rather than the top level, which is the only way into a group from
    /// this screen now that the strip's own footer button is gone.
    /// </para>
    /// </summary>
    private void RenderGroupArc(double size)
    {
        var group = _expandedGroup;
        if (group is null) return;

        // No slot in _slotBorders belongs to a child: those are the top-level orbit's, and the drag
        // and right-click paths read them. Cleared so a press inside a group cannot reach a bubble
        // that is no longer drawn — the same reason the corner preview clears them.
        Array.Clear(_slotBorders);
        Array.Clear(_emptySlotBorders);
        _groupSlotBorders.Clear();
        // Both of the canvas commands go: Add item adds to the top level, and Reset defaults
        // rebuilds the whole orbit — neither is what "add" or "reset" means while you are looking
        // at one group's contents, and a destructive button that reads as scoped to what is on
        // screen when it is not is worse than no button. Adding to the group is the dashed slot at
        // either end of the arc instead.
        CanvasActions.Visibility = Visibility.Collapsed;

        double cx = size / 2, cy = size / 2;
        double scale = size * 0.65 / (2 * (OrbitMetrics.Radius + OrbitMetrics.Bubble / 2));
        double parent = OrbitMetrics.Bubble * scale;
        double bubble = OrbitMetrics.FlyoutBubble * scale;
        double radius = OrbitMetrics.Radius * scale;
        double glyphSize = Math.Max(10, OrbitMetrics.FlyoutGlyph * scale);

        _previewCenter = new Point(cx, cy);
        _previewRadius = radius;
        _previewBubble = bubble;

        var muted = ThemeHelper.Brush(Root, "SwiftXMutedBrush");
        var foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush");
        var accent = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitCanvasAccentBrush");

        // The hub shows the group's mark while the group is open; the back button to the left is
        // the explicit way out (AddGroupBackButton below), so the hub no longer doubles as a close
        // target \u2014 clicking it had no visible affordance until hover anyway.
        double markSize = glyphSize * 1.15;
        var groupMark = PreviewBubbleContent(group, markSize, accent, parent, 0);

        // No BorderBrush/BorderThickness: see ApplyBubbleShadow - every disc on this canvas now
        // reads its "filled" state from its shadow alone, the hub included.
        var parentBubble = new Border
        {
            Width = parent,
            Height = parent,
            Background = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitNodeBackgroundBrush"),
            Child = groupMark,
        };
        ApplyBubbleShadow(parentBubble);
        OrbitItemGeometry.Apply(parentBubble, parent);
        ToolTipService.SetToolTip(parentBubble, group.DisplayName);
        AttachPreviewHover(parentBubble);
        Canvas.SetLeft(parentBubble, cx - parent / 2);
        Canvas.SetTop(parentBubble, cy - parent / 2);
        PreviewCanvas.Children.Add(parentBubble);

        AddGroupBackButton(cx, cy, parent, glyphSize, foreground);

        var children = group.Children.Where(c => !c.Hidden).ToList();

        // One dashed slot either side of the children, so the arc always has at least the two —
        // except on a group that is gathered rather than kept (see GroupAddEnds), which has
        // nothing to add to.
        int ends = GroupAddEnds(group);
        int count = children.Count + 2 * ends;
        double step = OrbitMetrics.FlyoutStepDegreesFor(radius, SettingsGroupArcItemGap) * Math.PI / 180.0;
        double first = -((count - 1) / 2.0) * step;

        for (int i = 0; i < count; i++)
        {
            double angle = first + i * step;
            double x = cx + radius * Math.Cos(angle) - bubble / 2;
            double y = cy + radius * Math.Sin(angle) - bubble / 2;

            bool isAdd = ends > 0 && (i == 0 || i == count - 1);
            var child = isAdd ? null : children[i - ends];

            // No BorderBrush/BorderThickness here either - an add slot stays recognizable as empty
            // by having no shadow (ApplyBubbleShadow runs only in the else branch below), the same
            // rule RenderPreview's own empty slots follow.
            var border = new Border
            {
                Width = bubble,
                Height = bubble,
                Background = isAdd
                    ? new SolidColorBrush(Colors.Transparent)
                    : ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitNodeBackgroundBrush"),
                Tag = child,
            };

            if (isAdd)
            {
                border.Child = PreviewGlyph("\uE710", glyphSize, muted);
                ToolTipService.SetToolTip(border, "Add something to " + group.DisplayName);
                // No Tapped handler: an add slot has no OrbitItem to drag, so
                // Preview_PointerReleased's own "no item at this slot" branch calls
                // AddToOpenGroup for it, the same way the canvas-level path already does.
                _groupSlotBorders.Add(null);
                AttachPreviewHover(border);
            }
            else
            {
                border.Child = PreviewBubbleContent(child!, glyphSize, foreground, bubble, angle);
                ApplyBubbleShadow(border);
                ToolTipService.SetToolTip(border, child!.DisplayName);
                var pill = AddSlotLabel(child, cx, cy, angle, radius, bubble);
                // No per-bubble Tapped handler: the canvas-level press/drag/release path (see
                // Preview_PointerPressed's remarks on why the top-level orbit works this way)
                // now reads _groupSlotBorders too, so a tap and a drag on a child settle in the
                // same place instead of the per-item capture that used to make dragging children
                // impossible.
                _groupSlotBorders.Add(border);
                AttachPreviewHover(border, pill);
            }

            OrbitItemGeometry.Apply(border, bubble);
            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);
            PreviewCanvas.Children.Add(border);
        }
    }

    /// <summary>How far, in device-independent pixels, the group back button sits outside the
    /// hub's own edge. Raised from the original 16, which read as crowding the hub.</summary>
    private const double BackButtonGap = 28;

    /// <summary>
    /// The explicit way out of an expanded group: a round button to the left of the hub, visible
    /// for as long as <see cref="_expandedGroup"/> is set, calling <see cref="CollapseGroup"/> on a
    /// tap. Drawn beside <paramref name="parent"/> (the hub's own diameter) rather than replacing
    /// its hover-to-close affordance — that stays for the pointer that finds it, this is for anyone
    /// who does not, touch included.
    /// <para>
    /// A Border, not a Button: every other ring element is one, and <see cref="AttachPreviewHover"/>
    /// is what gives it the same hover invert and hand cursor those get — a stock Button's own
    /// PointerOver visual state would have fought a Background set from code the same way, which is
    /// why this never picked up that transition while it was one.
    /// </para>
    /// </summary>
    private void AddGroupBackButton(double cx, double cy, double parent, double glyphSize, Brush foreground)
    {
        double size = Math.Max(28, parent * 0.78);
        var button = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitNodeBackgroundBrush"),
            Child = PreviewGlyph("", glyphSize, foreground),
        };
        ApplyBubbleShadow(button);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Back to the orbit");
        ToolTipService.SetToolTip(button, "Back to the orbit");
        button.Tapped += (_, e) =>
        {
            e.Handled = true;
            CollapseGroup();
        };
        AttachPreviewHover(button);

        Canvas.SetLeft(button, cx - parent / 2 - BackButtonGap - size);
        Canvas.SetTop(button, cy - size / 2);
        PreviewCanvas.Children.Add(button);
    }

    /// <summary>
    /// How many dashed "add" slots <paramref name="group"/>'s arc has at each end: one, or none for
    /// the corner's QuickAccess (see <see cref="QuadrantLayout.IsQuickAccess"/>). That group is
    /// gathered from the orbit's links and shortcuts every time it is resolved, so an item added
    /// to it or moved within it would be gone on the next redraw. It is shown for what it holds,
    /// and its children are changed where they live.
    /// </summary>
    private static int GroupAddEnds(OrbitItem group) => QuadrantLayout.IsQuickAccess(group) ? 0 : 1;

    /// <summary>
    /// Adds to whichever group the canvas currently has open, through the same dialog the top-level
    /// Add item button opens — scoped to the group's own children, and with the Group tile hidden,
    /// because a group inside a group is a shape the orbit has no way to open.
    /// </summary>
    private void AddToOpenGroup()
    {
        var group = _expandedGroup;
        if (group is null) return;

        _manager.OpenAddItem(
            added =>
            {
                group.Children.Add(added);
                _manager.NotifyItemsChanged();
                RenderPreview();
                RebuildItemsList();
            },
            group.Children,
            allowGroups: false,
            heading: "Add to " + group.DisplayName);
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        // Mid-replace, the toolbar's own "Add item…" is still reachable (it never leaves the
        // canvas) — so it has to finish the replace too, or it would silently append a duplicate
        // instead of the swap the sidebar's banner just promised.
        if (IsReplacing)
        {
            _manager.OpenAddItem(CompleteReplace, _manager.Config.Primary.Items);
            return;
        }

        _manager.OpenAddItem(added =>
        {
            AddToTopLevel(added);
            _manager.NotifyItemsChanged();
            RenderPreview();
            RebuildItemsList();
        }, _manager.Config.Primary.Items);
    }

    /// <summary>
    /// Puts something the add dialog returned on the top-level orbit: in the first empty slot, else
    /// after the last. The dialog does not check for room itself, so a full orbit still takes it at
    /// the end, as it always did.
    /// </summary>
    private void AddToTopLevel(OrbitItem added)
    {
        var items = _manager.Config.Primary.Items;
        if (!OrbitSlots.Place(items, added))
            items.Add(added);
    }

    private void AddAppItem_Click(object sender, RoutedEventArgs e) =>
        _manager.OpenAddItem(added =>
        {
            AddToTopLevel(added);
            _manager.NotifyItemsChanged();
            RenderPreview();
            RebuildItemsList();
        }, _manager.Config.Primary.Items, isGenericAddItem: true);

    // ===================== the Orbit screen's left sidebar =====================

    /// <summary>
    /// Built once, right after the window loads, and again whenever the orbit changes under it —
    /// the rows carry an "already on the orbit" state that goes stale the moment one is clicked.
    /// Used to be deferred to first entry into the sidebar's old "editing" mode (see
    /// <c>SetOrbitEditing</c>, since removed); now that the sidebar is permanently on screen there
    /// is no later moment to defer it to.
    /// </summary>
    private bool _orbitEditBuilt;

    /// <summary>
    /// The categories whose accordion is open. Kept outside the rows because every rebuild throws
    /// the rows away: without it a user with Links open would watch it snap shut the moment a link
    /// was added, which is exactly when they are looking at it.
    /// </summary>
    private readonly HashSet<ElementCategory> _orbitEditOpen = new();

    /// <summary>
    /// A rebuild was asked for while a drag was still in flight (see
    /// <see cref="OrbitEditDragInFlight"/>) and is owed once it ends.
    /// </summary>
    private bool _orbitEditRefreshPending;

    /// <summary>The Groups row a drag is currently over, if any; see <see cref="WireEditGroupRow"/>.</summary>
    private ContentControl? _orbitEditHotGroupRow;

    // Built from codepoints rather than written as literals: a private-use character is invisible in
    // an editor, so a stray paste can swap it for another one without anyone seeing why an icon broke.
    private static readonly string SidebarChevronUpGlyph = char.ConvertFromUtf32(0xE70E);
    private static readonly string SidebarChevronDownGlyph = char.ConvertFromUtf32(0xE70D);
    private static readonly string SidebarChevronRightGlyph = char.ConvertFromUtf32(0xE76C);
    private static readonly string SidebarAddGlyph = char.ConvertFromUtf32(0xE710);
    private static readonly string SidebarEllipsis = char.ConvertFromUtf32(0x2026);

    /// <summary>Groups whose child list is currently collapsed in the sidebar.</summary>
    private readonly HashSet<string> _collapsedGroups = new();

    /// <summary>
    /// Whether a drag that the sidebar's rows depend on is still going: one of the sidebar's own
    /// rows being dragged, an outside item held over the sidebar, or one held over a group row.
    /// Rebuilding then would pull the row the drag started from (or the one it is about to land on)
    /// out from under it.
    /// </summary>
    private bool OrbitEditDragInFlight =>
        _sidebarDragTemplate is not null ||
        _orbitEditHotGroupRow is not null ||
        OrbitEditDropOverlay.Visibility == Visibility.Visible;

    /// <summary>
    /// Fills the edit sidebar: one category per <see cref="ElementCategories.All"/> entry (apps,
    /// shortcuts, folders, links, groups, tools), each an expandable list, modelled on LogiApp's own
    /// <c>AccordionCategoryItem</c>.
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
    /// A category is not only those suggestions: <see cref="ElementLibrary.RowsFor"/> unions them
    /// with whatever the orbit already carries, so a link typed into the add dialog or a folder
    /// dropped on the ring turns up under Links or Folders too. That is also why this is rebuilt
    /// every time <see cref="OrbitManager.ItemsChanged"/> fires (see the subscription in the
    /// constructor), with each accordion's open state carried across in
    /// <see cref="_orbitEditOpen"/>, and why a rebuild asked for mid-drag is held back until the
    /// drag ends (<see cref="OrbitEditDragInFlight"/>).
    /// </para>
    /// <para>
    /// The list ends in a row that opens the full add dialog, so the browse-for-anything
    /// path is never more than one click further away than the suggestions are.
    /// </para>
    /// </summary>
    private void BuildOrbitEditSidebar()
    {
        if (OrbitEditDragInFlight)
        {
            _orbitEditRefreshPending = true;
            return;
        }
        _orbitEditRefreshPending = false;

        OrbitEditCategories.Children.Clear();

        // Everything that could be offered, as fresh template items: the machine-scanned
        // suggestions, then the built-in tools. The tools come from a catalog rather than a scan
        // because every one of them always exists; the Tools group's own children are exactly that
        // list, built by the catalog itself.
        var catalog = OnboardingCatalog.Available().Select(s => s.Item).ToList();
        catalog.AddRange(ToolCatalog.CreateToolsGroup().Children);
        var orbit = _manager.Config.Primary.Items;

        foreach (var category in ElementCategories.All)
        {
            var rows = category switch
            {
                ElementCategory.Groups => BuildEditGroupRows(SidebarGroups()),
                ElementCategory.Controls => BuildControlRows(),
                _ => ElementLibrary.RowsFor(category, catalog, orbit).Select(BuildEditRow).ToList(),
            };
            OrbitEditCategories.Children.Add(BuildEditCategory(category, rows));
        }

        // One row for the full picker, below the categories rather than repeated inside each of
        // them: this row opens the dialog on its first tile whatever category it sits under, so a
        // per-category "Browse for an app" would have promised a preselection it does not make,
        // once for every category. (Only "New group…" opens it on a tile of its own.)
        var browse = BuildEditRowShell("\uE710", "Add to Orbit\u2026", null, null);
        browse.Margin = new Thickness(0, 8, 0, 0);
        browse.Click += AddItem_Click;
        ToolTipService.SetToolTip(browse, "Open the full add dialog: apps, files, folders, links, groups and tools");
        OrbitEditCategories.Children.Add(browse);

        _orbitEditBuilt = true;
        UpdateOrbitEditSummary();
    }

    /// <summary>
    /// Every group the sidebar can put something in: the orbit's own, then any the hot corner has
    /// that the orbit does not (see <see cref="HotCornerItems.OwnGroups"/>). Both are listed because
    /// both are shown on the dock, and a group the sidebar cannot reach is a group nothing added
    /// there can ever show up in.
    /// </summary>
    private IReadOnlyList<OrbitItem> SidebarGroups() =>
        ElementLibrary.UserGroups(_manager.Config.Primary.Items)
            .Concat(_manager.Config.Primary.DetachedGroups)
            .Concat(HotCornerItems.OwnGroups(_manager.Config, SystemOrbitItems.CreateSettings()))
            .ToList();

    /// <summary>
    /// How many of the orbit's user slots are free, said in the sidebar rather than discovered by
    /// clicking a row and having nothing happen.
    /// </summary>
    private void UpdateOrbitEditSummary()
    {
        bool fillingEmpty = IsReplacing && _replaceItemName is null;
        OrbitEditEyebrow.Text = Loc.Get(IsReplacing && !fillingEmpty
            ? "OrbitLayout.AddToOrbit.Replace"
            : "OrbitLayout.AddToOrbit.Eyebrow");
        OrbitEditCancelButton.Visibility = IsReplacing ? Visibility.Visible : Visibility.Collapsed;

        if (fillingEmpty)
        {
            OrbitEditSummary.Text = Loc.Get("OrbitLayout.AddToOrbit.FillEmpty");
            return;
        }

        if (IsReplacing)
        {
            OrbitEditSummary.Text = Loc.Format("OrbitLayout.AddToOrbit.ReplacingSummary", _replaceItemName ?? "");
            return;
        }

        int used = OrbitSlots.Used(_manager.Config.Primary.Items);
        int free = Math.Max(0, OrbitMetrics.UserSlotCount - used);
        OrbitEditSummary.Text = free switch
        {
            0 => Loc.Get("OrbitLayout.AddToOrbit.Full"),
            1 => Loc.Get("OrbitLayout.AddToOrbit.OneFreeSlot"),
            _ => Loc.Format("OrbitLayout.AddToOrbit.FreeSlots", free),
        };
    }

    private async void OrbitSidebarEdit_DragEnter(object sender, DragEventArgs e)
    {
        int token = ++_orbitEditDragToken;

        // One of the sidebar's own rows, on its way to the ring. It carries its display name as
        // text purely to be a real drag operation (see WireEditRow), which would otherwise be read
        // here as a target to add; a row dropped back on the list it came from does nothing.
        if (_sidebarDragTemplate is not null)
        {
            SetOrbitEditDropState(false, e);
            return;
        }

        try
        {
            var data = e.DataView;
            if (OrbitItemFactory.LooksDroppable(data))
            {
                SetOrbitEditDropState(true, e);
                return;
            }

            if (!data.Contains(StandardDataFormats.Text))
            {
                SetOrbitEditDropState(false, e);
                return;
            }

            SetOrbitEditDropState(null, e);
            var deferral = e.GetDeferral();
            bool usable;
            try
            {
                usable = OrbitItemFactory.TryReadTextTarget(await data.GetTextAsync(), out _);
            }
            finally
            {
                deferral.Complete();
            }
            if (token == _orbitEditDragToken)
                SetOrbitEditDropState(usable, null);
        }
        catch (Exception ex)
        {
            Diag.Log("OrbitSidebarEdit_DragEnter failed: " + ex);
            if (token == _orbitEditDragToken)
                SetOrbitEditDropState(false, null);
        }
    }

    private void OrbitSidebarEdit_DragOver(object sender, DragEventArgs e) =>
        SetOrbitEditDropState(_orbitEditDropUsable, e);

    private void OrbitSidebarEdit_DragLeave(object sender, DragEventArgs e)
    {
        _orbitEditDragToken++;
        SetOrbitEditDropState(null, null);
        FlushOrbitEditRefresh();
    }

    private async void OrbitSidebarEdit_Drop(object sender, DragEventArgs e)
    {
        _orbitEditDragToken++;
        SetOrbitEditDropState(null, null);

        var deferral = e.GetDeferral();
        try
        {
            var targets = await OrbitItemFactory.ReadDroppedTargetsAsync(e.DataView);
            if (targets.Count == 0)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Copy;
            AddDroppedTargetsToOrbit(targets);
        }
        catch (Exception ex)
        {
            Diag.Log("OrbitSidebarEdit_Drop failed: " + ex);
        }
        finally
        {
            deferral.Complete();
            FlushOrbitEditRefresh();
        }
    }

    private void SetOrbitEditDropState(bool? usable, DragEventArgs? e)
    {
        _orbitEditDropUsable = usable;
        bool show = usable == true || (usable is null && e is not null);
        OrbitEditDropOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            var accent = CurrentAccentColor();
            OrbitEditDropOverlay.BorderBrush = new SolidColorBrush(accent);
            OrbitEditDropOverlay.Background = new SolidColorBrush(Color.FromArgb(48, accent.R, accent.G, accent.B));
            OrbitEditDropOverlayText.Text = IsReplacing ? "Drop to replace" : "Drop to add";
        }

        if (e is null) return;
        bool ok = usable != false;
        e.AcceptedOperation = ok ? DataPackageOperation.Copy : DataPackageOperation.None;
        if (!ok || e.DragUIOverride is not { } ui) return;
        ui.Caption = IsReplacing ? "Replace with this" : "Add to orbit";
        ui.IsCaptionVisible = true;
        ui.IsGlyphVisible = true;
    }

    private void AddDroppedTargetsToOrbit(IReadOnlyList<string> targets)
    {
        if (targets.Count == 0) return;

        // Replacing takes one thing, whichever was dropped first — the same one-for-one swap a
        // sidebar row's click performs, not the batch add the loop below does.
        if (IsReplacing)
        {
            CompleteReplace(OrbitItemFactory.FromTarget(targets[0]));
            return;
        }

        var items = _manager.Config.Primary.Items;
        bool added = false;
        foreach (var target in targets)
        {
            if (!OrbitSlots.Place(items, OrbitItemFactory.FromTarget(target)))
                break;
            added = true;
        }

        if (!added)
        {
            UpdateOrbitEditSummary();
            return;
        }

        _manager.NotifyItemsChanged();
        BuildOrbitEditSidebar();
        RenderPreview();
        RebuildItemsList();
    }

    /// <summary>
    /// One accordion category: a header that expands, over rows that add. Logi's own geometry —
    /// a 36px header at CornerRadius 4 with a 20px icon column and a chevron that swaps
    /// <c>E70D</c>/<c>E70E</c>, over 28px rows.
    /// </summary>
    private FrameworkElement BuildEditCategory(ElementCategory category, List<FrameworkElement> items)
    {
        string name = ElementCategories.Label(category);
        string glyph = ElementCategories.Glyph(category);
        bool startOpen = _orbitEditOpen.Contains(category);

        var rows = new StackPanel { Spacing = 2 };
        foreach (var item in items)
            rows.Children.Add(item);
        if (items.Count == 0)
            rows.Children.Add(BuildEmptyCategoryRow());

        // The selected (expanded) section's children sit on a card background — the same
        // ShellCardBackgroundBrush every other card in this window uses — so it is clear at a
        // glance which category's rows are the ones currently showing.
        var rowsCard = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Visibility = startOpen ? Visibility.Visible : Visibility.Collapsed,
            Background = startOpen
                ? ThemeHelper.Brush(Root, _manager.Config.Theme, "ShellCardBackgroundBrush")
                : new SolidColorBrush(Colors.Transparent),
            Child = rows,
        };

        var chevron = new FontIcon
        {
            Glyph = "\uE70D",
            FontSize = 10,
            Foreground = ThemeHelper.Brush(Root, "SwiftXMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (startOpen)
            chevron.Glyph = SidebarChevronUpGlyph;

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

        var button = new HandButton
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
            bool open = rowsCard.Visibility == Visibility.Visible;
            rowsCard.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            rowsCard.Background = open
                ? new SolidColorBrush(Colors.Transparent)
                : ThemeHelper.Brush(Root, _manager.Config.Theme, "ShellCardBackgroundBrush");
            if (open)
                _orbitEditOpen.Remove(category);
            else
                _orbitEditOpen.Add(category);
            chevron.Glyph = open ? "\uE70D" : "\uE70E";
        };

        return new StackPanel
        {
            Spacing = 4,
            Children = { button, rowsCard },
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
            present ? ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitCanvasAccentBrush") : null,
            iconSource: template);

        ToolTipService.SetToolTip(row, present
            ? $"{template.DisplayName} is already on the orbit. Drag it onto the ring or a group to add another."
            : $"Add {template.DisplayName} to the orbit");

        // Always draggable, ticked or not: the tick says it is already on the orbit, not that it
        // may not go anywhere else. Only the click is held back for a ticked row, so a stray click
        // does not put a second copy in the next free slot.
        WireEditRow(row, template, present ? null : () => AddToOrbitFromSidebar(template));
        row.ContextFlyout = BuildEditRowMenu(row, template, present);

        return row;
    }

    /// <summary>
    /// The right-click menu of a suggestion row: put a copy in one of the orbit's groups (or in a
    /// new one), or take the thing off the orbit wherever it sits. A tool can go in a group like
    /// anything else, but cannot be taken off the orbit from here: the Tools group owns it and puts
    /// it back on every launch, so Remove is greyed. A copy in a group of the user's own is removed
    /// from that group's own rows.
    /// </summary>
    private MenuFlyout BuildEditRowMenu(FrameworkElement anchor, OrbitItem template, bool present)
    {
        var menu = new MenuFlyout();

        var addToGroup = new MenuFlyoutSubItem
        {
            Text = "Add to group",
            Icon = new FontIcon { Glyph = char.ConvertFromUtf32(0xE8F4) },
        };
        foreach (var group in SidebarGroups())
        {
            var verdict = GroupDropRules.Check(group, template);
            var entry = new MenuFlyoutItem
            {
                Text = $"{group.DisplayName}  ({GroupDropRules.Count(group)}/{GroupDropRules.Capacity})",
                IsEnabled = verdict == GroupDropVerdict.Allowed,
            };
            if (GroupDropRules.Reason(verdict) is { } why)
                ToolTipService.SetToolTip(entry, why);
            entry.Click += (_, _) =>
            {
                var result = GroupDropRules.TryAdd(group, CloneOrbitTemplate(template));
                if (result == GroupDropVerdict.Allowed)
                {
                    _manager.NotifyItemsChanged();
                    RefreshAfterGroupChange();
                    OrbitEditSummary.Text = $"Added {template.DisplayName} to {group.DisplayName}.";
                }
                else if (GroupDropRules.Reason(result) is { } reason)
                {
                    OrbitEditSummary.Text = reason;
                }
            };
            addToGroup.Items.Add(entry);
        }

        if (addToGroup.Items.Count > 0)
            addToGroup.Items.Add(new MenuFlyoutSeparator());
        var newGroup = new MenuFlyoutItem
        {
            Text = "New group" + SidebarEllipsis,
            Icon = new FontIcon { Glyph = SidebarAddGlyph },
        };
        newGroup.Click += (_, _) => OpenNewGroupDialog(anchor, template);
        addToGroup.Items.Add(newGroup);
        menu.Items.Add(addToGroup);

        var remove = new MenuFlyoutItem
        {
            Text = "Remove",
            Icon = new FontIcon { Glyph = char.ConvertFromUtf32(0xE74D) },
            IsEnabled = present && !template.IsTool,
        };
        remove.Click += (_, _) => RemoveFromOrbitByTarget(template.Target);
        menu.Items.Add(remove);

        return menu;
    }

    /// <summary>
    /// Takes every copy of <paramref name="target"/> off the orbit: top-level places are left empty,
    /// and a copy inside a group is simply removed from it.
    /// </summary>
    private void RemoveFromOrbitByTarget(string target)
    {
        var items = _manager.Config.Primary.Items;
        foreach (var item in items.Where(i => !i.IsGroup && !i.IsEmpty &&
                     string.Equals(i.Target, target, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (ReferenceEquals(_selectedItem, item)) HideItemDetails();
            OrbitSlots.Vacate(items, item);
        }
        foreach (var group in items.Where(i => i.IsGroup && !ElementLibrary.IsBuiltInToolsGroup(i)))
            group.Children.RemoveAll(c =>
                string.Equals(c.Target, target, StringComparison.OrdinalIgnoreCase));

        _manager.NotifyItemsChanged();
        RenderPreview();
        RebuildItemsList();
    }

    /// <summary>
    /// The two ways a sidebar suggestion puts its thing on the orbit: clicked, it goes to the next
    /// free place; dragged onto the ring, it fills the place it lands on or replaces whatever is
    /// already sitting there (see <see cref="Preview_DragOver"/> and <see cref="Preview_Drop"/>,
    /// the same pair that catches an app dragged in from outside SwiftX).
    /// <para>
    /// The drag is started by hand rather than left to <see cref="UIElement.CanDrag"/> alone. A
    /// Button takes the pointer capture on press and marks the press handled, so the framework's
    /// own drag gesture never fires on one — <c>CanDrag</c> by itself leaves the row clickable and
    /// nothing more, which is exactly what these rows used to be. Hence pointer handlers added with
    /// <c>handledEventsToo</c> (the Button has already handled them), and an explicit
    /// <see cref="UIElement.StartDragAsync"/> once the pointer has travelled far enough to mean a
    /// drag rather than a click.
    /// </para>
    /// <para>
    /// Same-process, so the dragged template travels as a live field reference
    /// (<see cref="_sidebarDragTemplate"/>) rather than round-tripping through the DataPackage; the
    /// text put on the package only exists to give the OS a real drag operation to run.
    /// </para>
    /// </summary>
    private void WireEditRow(Button row, OrbitItem template, Action? onClick)
    {
        // Per row rather than on the window: a drag and the click it must suppress belong to the
        // same row, and none of this outlives the sidebar rebuild that replaces that row.
        bool dragged = false;

        row.Click += (_, _) =>
        {
            // A drag ends in a release, and a release on a Button is also a click. Adding the item
            // again here would undo the very replace the drop just performed.
            if (dragged)
            {
                dragged = false;
                return;
            }
            onClick?.Invoke();
        };

        WireSidebarDrag(row, template, onPress: () => dragged = false, onDragged: () => dragged = true);
    }

    /// <summary>
    /// The drag half of a sidebar row, for anything that can carry <paramref name="template"/> to
    /// the ring or a group: a suggestion Button (<see cref="WireEditRow"/>) or a Groups row
    /// (<see cref="BuildEditGroupRow"/>).
    /// <para>
    /// Started by hand once the pointer has travelled <see cref="SidebarDragThreshold"/>, rather
    /// than left to <see cref="UIElement.CanDrag"/> alone, because the framework's own gesture is
    /// not reliable for these: a Button takes the pointer on press, and a row whose content is out
    /// of hit-testing (the Groups rows) gives it nothing to start from. <c>CanDrag</c> stays set
    /// because it is what makes <see cref="UIElement.DragStarting"/> fire for a drag started
    /// here. Whichever of the two starts first wins: a drag already in flight
    /// (<see cref="_sidebarDragTemplate"/> set) is never started a second time.
    /// </para>
    /// <para>
    /// <paramref name="canStart"/> is asked, with the press's <c>OriginalSource</c>, whether a press
    /// may arm the drag at all. The Groups rows need it: their child rows and buttons sit inside the
    /// row, so their presses reach the row's handlers too.
    /// </para>
    /// </summary>
    private void WireSidebarDrag(
        UIElement row, OrbitItem template, Action? onPress = null, Action? onDragged = null,
        Func<object?, bool>? canStart = null)
    {
        bool armed = false;
        Point origin = default;

        row.CanDrag = true;
        row.DragStarting += (_, e) =>
        {
            _sidebarDragTemplate = template;
            e.Data.SetText(template.DisplayName);
            e.Data.RequestedOperation = DataPackageOperation.Copy;
        };
        row.DropCompleted += (_, _) => EndSidebarDrag();

        row.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            var point = e.GetCurrentPoint(row);
            if (point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed)
                return;
            // A press that began on something inside the row with a job of its own (a child row, a
            // button) is not the start of this row's drag, though it bubbles up through it.
            if (canStart is not null && !canStart(e.OriginalSource))
                return;
            origin = point.Position;
            armed = true;
            onPress?.Invoke();
        }), handledEventsToo: true);

        row.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(async (_, e) =>
        {
            if (!armed) return;

            var point = e.GetCurrentPoint(row);
            if (!point.Properties.IsLeftButtonPressed)
            {
                armed = false;
                return;
            }
            if (Math.Abs(point.Position.X - origin.X) <= SidebarDragThreshold &&
                Math.Abs(point.Position.Y - origin.Y) <= SidebarDragThreshold)
                return;

            armed = false;
            onDragged?.Invoke();

            // The framework's own gesture got there first.
            if (_sidebarDragTemplate is not null)
                return;

            try
            {
                // A Button grabbed the pointer on press; the drag cannot follow a pointer it is
                // still holding, so it lets go first.
                row.ReleasePointerCaptures();
                await row.StartDragAsync(point);
            }
            catch (Exception ex)
            {
                Diag.Log("Sidebar row drag failed: " + ex);
            }
            finally
            {
                // DropCompleted covers the ordinary end of a drag; this covers one that never
                // started, so a failed drag cannot leave the ring thinking one is still in flight.
                EndSidebarDrag();
            }
        }), handledEventsToo: true);

        row.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler((_, _) => armed = false), handledEventsToo: true);
        row.AddHandler(UIElement.PointerCaptureLostEvent,
            new PointerEventHandler((_, _) => armed = false), handledEventsToo: true);
    }

    /// <summary>
    /// A fresh copy of a sidebar suggestion's template, ready to add or replace with — never the
    /// template instance itself, since <see cref="OnboardingCatalog.Available"/> hands out live
    /// <see cref="OrbitItem"/> objects and putting the same one in two slots would edit both at
    /// once. Shared by the click and the drag/drop paths onto the sidebar and the ring alike.
    /// </summary>
    private static OrbitItem CloneOrbitTemplate(OrbitItem template)
    {
        var clone = new OrbitItem
        {
            Kind = template.Kind,
            DisplayName = template.DisplayName,
            Target = template.Target,
            Arguments = template.Arguments,
            CustomIconPath = template.CustomIconPath,
            CustomGlyph = template.CustomGlyph,
            CustomIconColor = template.CustomIconColor,
        };
        // A group is copied with what it holds, each child a fresh item of its own.
        clone.Children.AddRange(template.Children.Where(c => !c.IsEmpty).Select(CloneOrbitTemplate));
        return clone;
    }

    /// <summary>
    /// What a category with nothing in it says. Reachable in practice: the link suggestions are
    /// unconditional but the app and folder ones are existence-checked against the machine, so a
    /// stripped-down Windows can empty a category out. An accordion that opens onto nothing at all
    /// reads as broken.
    /// </summary>
    private FrameworkElement BuildEmptyCategoryRow() => new TextBlock
    {
        Text = "Nothing to suggest here \u2014 use Add to Orbit below.",
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(10, 2, 10, 4),
        Foreground = ThemeHelper.Brush(Root, "SwiftXMutedBrush"),
    };

    private Button BuildEditRowShell(
        string glyph, string text, string? badgeGlyph, Brush? badgeBrush, OrbitItem? iconSource = null)
    {
        var button = new HandButton
        {
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 0, 10, 0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = BuildEditRowContent(glyph, text, badgeGlyph, badgeBrush, iconSource),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, text);
        return button;
    }

    /// <summary>
    /// What a sidebar row shows: icon, name and an optional trailing badge, in the 20px / star / auto
    /// columns every row shares. Split out of <see cref="BuildEditRowShell"/> so a Groups row, which
    /// is a drop target rather than a button, can wear exactly the same content.
    /// </summary>
    private Grid BuildEditRowContent(
        string glyph, string text, string? badgeGlyph, Brush? badgeBrush, OrbitItem? iconSource = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 12,
            // The thing's own glyph colour when the row stands for one that has it (a group the user
            // coloured), so the sidebar shows what the orbit will.
            Foreground = IconColors.BrushFor(iconSource, ThemeHelper.Brush(Root, "SwiftXMutedBrush")),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        // The suggestion's own resolved icon (the app's real shell icon, the site's favicon, and
        // so on) rather than the generic category glyph alone — the same swap LoadRowIcon performs
        // for the Elements screen's own cards and the details card's logo, so a row here looks the
        // same as the very item it is offering to put on the orbit rather than a placeholder for
        // it. Left out for a category header or the plain "Add to Orbit…" row, neither of
        // which stands for one specific thing.
        if (iconSource is not null)
        {
            var image = new Image
            {
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(image, 0);
            grid.Children.Add(image);
            LoadRowIcon(iconSource, image, icon);
        }

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

        return grid;
    }

    /// <summary>
    /// Ends a drag that started on one of the sidebar's own rows, and pays back a rebuild that was
    /// held off while it lasted (see <see cref="OrbitEditDragInFlight"/>).
    /// </summary>
    private void EndSidebarDrag()
    {
        _sidebarDragTemplate = null;
        FlushOrbitEditRefresh();
    }

    /// <summary>
    /// Runs a sidebar rebuild that <see cref="BuildOrbitEditSidebar"/> put off because a drag was
    /// in flight. Queued rather than run inline, since the callers are drag events and a row must
    /// not be torn down inside its own event; the rebuild checks again when it runs, so a drag that
    /// has moved on to another row by then simply defers it once more.
    /// </summary>
    private void FlushOrbitEditRefresh()
    {
        if (_orbitEditRefreshPending && !OrbitEditDragInFlight)
            DispatcherQueue.TryEnqueue(BuildOrbitEditSidebar);
    }

    /// <summary>Whether every user slot on the orbit is taken.</summary>
    private bool OrbitIsFull =>
        !OrbitSlots.HasRoom(_manager.Config.Primary.Items);

    /// <summary>
    /// The Controls category: Home, Settings and Close, offered exactly like any other suggestion
    /// (<see cref="BuildEditRow"/> — already-on-the-orbit badge, drag, right-click included) rather
    /// than through <see cref="ElementLibrary.RowsFor"/>'s catalog, since none of the three is
    /// something a scan could ever find. Fixed order and fixed set: unlike every other category,
    /// nothing here is ever added to or taken off the list itself, only placed on the orbit or not.
    /// </summary>
    private List<FrameworkElement> BuildControlRows() =>
    [
        BuildEditRow(SystemOrbitItems.CreateHome()),
        BuildEditRow(SystemOrbitItems.CreateSettings()),
        BuildEditRow(SystemOrbitItems.CreateClose()),
    ];

    /// <summary>
    /// The Groups category: a "New group" row, then one drop-target row for each group on the orbit
    /// (<see cref="ElementLibrary.UserGroups"/>; the built-in Tools group is the Tools screen's).
    /// </summary>
    private List<FrameworkElement> BuildEditGroupRows(IReadOnlyList<OrbitItem> groups)
    {
        var rows = new List<FrameworkElement> { BuildNewGroupRow() };
        rows.AddRange(groups.Select(BuildEditGroupRow));
        return rows;
    }

    /// <summary>
    /// "New group": opens the "Add to orbit" window on its Group tile (see
    /// <see cref="OpenNewGroupDialog"/>). Dragged onto the ring instead, it drops an empty group
    /// there with no questions asked.
    /// </summary>
    private Button BuildNewGroupRow()
    {
        var row = BuildEditRowShell(SidebarAddGlyph, "New group" + SidebarEllipsis, null, null);
        ToolTipService.SetToolTip(row, "Name a new group and put it on the orbit, or drag it onto the ring");
        WireEditRow(row, ElementLibrary.NewGroup(null), () => OpenNewGroupDialog(row));
        return row;
    }

    /// <summary>
    /// Opens the "Add to orbit" window already on the Group tile, so naming the group is the whole
    /// of the job; what it returns goes where any other sidebar pick goes
    /// (<see cref="AddToOrbitFromSidebar"/>), which is what settles an armed replace. Always opens
    /// immediately — a full orbit used to be said here, before the window ever showed, which from
    /// the row that was clicked looked like "New group" simply did nothing. A full ring no longer
    /// refuses the group at all: the callback below puts a brand-new group that has nowhere to land
    /// into the sidebar's Groups list as a detached group (<see cref="OrbitProfile.DetachedGroups"/>),
    /// to be dragged onto the ring once there is room.
    /// </summary>
    /// <param name="seed">
    /// Something to put in the new group as it is created: the thing a right-click on a suggestion
    /// chose "New group…" for.
    /// </param>
    private void OpenNewGroupDialog(FrameworkElement anchor, OrbitItem? seed = null)
    {
        _manager.OpenAddItem(
            added =>
            {
                // The tile can be changed in the window; only a group can take the seed.
                if (seed is not null && added.IsGroup)
                    added.Children.Add(CloneOrbitTemplate(seed));

                // A replace swaps rather than appends, so it has room whatever the count is. Short
                // of that, a full ring has nowhere to put a brand-new group — so it goes into the
                // sidebar's Groups list as a detached group (OrbitProfile.DetachedGroups), ready to
                // be dragged onto the ring once there is room. Nothing is suggested and nothing is
                // consolidated: making a group makes an empty group, and where it waits is the
                // sidebar. Only an actual group detaches this way — the window can swap the Group
                // tile for anything else, and a plain item has no such holding place.
                if (!IsReplacing && OrbitIsFull && added.IsGroup)
                {
                    _manager.Config.Primary.DetachedGroups.Add(added);
                    _manager.Save();
                    DispatcherQueue.TryEnqueue(BuildOrbitEditSidebar);
                    return;
                }

                AddToOrbitFromSidebar(added);
            },
            _manager.Config.Primary.Items,
            startAsGroup: true);
    }

    /// <summary>A line of explanation in a small flyout on the row it answers.</summary>
    private static void ShowSidebarNotice(FrameworkElement anchor, string text)
    {
        new Flyout
        {
            Placement = FlyoutPlacementMode.Bottom,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 240 },
        }.ShowAt(anchor);
    }

    /// <summary>How full a group is, as the row shows it: children over what the flyout can hold.</summary>
    private static string GroupCountText(OrbitItem group) =>
        $"{GroupDropRules.Count(group)}/{GroupDropRules.Capacity}";

    /// <summary>
    /// One group on the orbit, listed under Groups as a place to drop things: its name, how full it
    /// is, and drag feedback (see <see cref="WireEditGroupRow"/>). Not a Button: it is a drop
    /// target first, and clicking it opens the group on the canvas rather than adding anything.
    /// </summary>
    private FrameworkElement BuildEditGroupRow(OrbitItem group)
    {
        var count = new TextBlock
        {
            Text = GroupCountText(group),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeHelper.Brush(Root, "SwiftXMutedBrush"),
        };

        var content = BuildEditRowContent(group.Glyph, group.DisplayName, null, null, iconSource: group);
        Grid.SetColumn(count, 2);
        content.Children.Add(count);
        // Only the row itself is the drop target. With its content out of hit-testing, moving the
        // pointer across the icon and the name raises no enter or leave of its own to flicker the
        // highlight.
        content.IsHitTestVisible = false;

        // What the group holds, listed under it as indented child rows. They sit inside the drop
        // target rather than beside it, so a drop anywhere on the group and its children lands in
        // the group instead of falling through to the sidebar's add-to-orbit. Unlike the header,
        // each child row takes the pointer itself: it is what is dragged to reorder, what carries
        // the remove button and what the right-click menu opens on.
        var childRows = new List<HandBorder>();
        var children = new StackPanel();
        var listed = GroupChildren.Listed(group);
        foreach (var child in listed)
        {
            var childRow = BuildEditGroupChildRow(group, child, listed);
            childRows.Add(childRow);
            children.Children.Add(childRow);
        }

        // Collapse/expand toggle — chevron in the far-right of the header row.
        bool collapsed = _collapsedGroups.Contains(group.Id);
        children.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

        var chevronIcon = new FontIcon
        {
            Glyph = collapsed ? SidebarChevronRightGlyph : SidebarChevronDownGlyph,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
        };
        chevronIcon.Tapped += (_, e) =>
        {
            e.Handled = true;
            if (_collapsedGroups.Contains(group.Id))
            {
                _collapsedGroups.Remove(group.Id);
                children.Visibility = Visibility.Visible;
                chevronIcon.Glyph = SidebarChevronDownGlyph;
            }
            else
            {
                _collapsedGroups.Add(group.Id);
                children.Visibility = Visibility.Collapsed;
                chevronIcon.Glyph = SidebarChevronRightGlyph;
            }
        };

        // Header row: content (star) | chevron (auto), height 28.
        var headerRow = new Grid { Height = 28 };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(content, 0);
        Grid.SetColumn(chevronIcon, 1);
        headerRow.Children.Add(content);
        headerRow.Children.Add(chevronIcon);

        var body = new StackPanel { Children = { headerRow, children } };

        // Transparent rather than null, so the whole row takes the pointer, not just what is drawn.
        // The 1px border that is only painted while a drag is over it is paid for out of the padding,
        // so the icon and name sit where they do on every other row.
        var row = new HandBorder
        {
            MinHeight = 28,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(9, 0, 9, 0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(1),
            AllowDrop = true,
            Content = body,
        };

        bool full = GroupDropRules.IsFull(group);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            row, $"{group.DisplayName}, group, {GroupDropRules.Count(group)} of {GroupDropRules.Capacity} items");
        // A group only the hot corner has is listed like the orbit's, so it can be added to from here;
        // the tooltip is where it says whose it is.
        string place = ElementLibrary.IsOrbitUserGroup(_manager.Config.Primary.Items, group)
            ? group.DisplayName
            : $"{group.DisplayName} (on the hot corner only)";
        ToolTipService.SetToolTip(row, full
            ? $"{place} is full ({GroupDropRules.Capacity} items)"
            : $"Click to see what is in {place}. Drag an app, file, folder or link here to add it to it.");

        WireEditGroupRow(row, group, count, childRows);
        // Draggable onto the full ring or the corner's quadrant: the group travels as a live
        // reference. The ring drops a copy of it, children included (see CloneOrbitTemplate); the
        // corner is shown the group itself, by id (see CopyForPreviewDrop).
        // Only from the header: a press on one of its child rows is that item's own drag.
        bool dragged = false;
        WireSidebarDrag(
            row, group,
            onPress: () => dragged = false,
            onDragged: () => dragged = true,
            canStart: source => !IsWithin(source, children));

        // Clicked, the group opens on the Orbit screen's canvas with its children on their arc —
        // what clicking it on the ring does, and what the Tools group has always shown. Without
        // this the only way to see what a group held, or to watch an add land, was to find the
        // group on the ring first. A press that became a drag is not a click, as for a Button.
        row.Tapped += (_, e) =>
        {
            if (dragged)
            {
                dragged = false;
                return;
            }
            // A tap on one of the children is for that child (its remove button, say), and the list
            // it was in may be about to be rebuilt under the group it would otherwise open.
            if (IsWithin(e.OriginalSource, children))
                return;
            // A replace is aimed at a place on the ring, which a group open on the canvas hides.
            if (IsReplacing)
                return;
            e.Handled = true;
            OpenGroup(group);
        };

        // The drag image is a snapshot of the row, taken once DragStarting returns. The fill count
        // is for the row at rest, not something to carry across the screen, so it is hidden (Opacity,
        // so the row keeps its layout) for the snapshot and put back when the drag ends. The
        // deferral holds the snapshot back until the hidden state has been rendered.
        row.DragStarting += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try
            {
                count.Opacity = 0;
                await Task.Delay(16);
            }
            finally
            {
                deferral.Complete();
            }
        };
        row.DropCompleted += (_, _) => count.Opacity = 1;

        // A group's mark can be changed and the group removed: what goes in it is dragged in, and
        // what comes out of it is managed on the Elements screen.
        var editIcon = new MenuFlyoutItem
        {
            Text = "Edit icon" + SidebarEllipsis,
            Icon = new FontIcon { Glyph = char.ConvertFromUtf32(0xE91B) },
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(editIcon, "Edit icon of " + group.DisplayName);
        editIcon.Click += (_, _) => ShowIconPicker(group, row);

        var removeGroup = new MenuFlyoutItem
        {
            Text = "Delete",
            Icon = new FontIcon { Glyph = char.ConvertFromUtf32(0xE74D) },
        };
        removeGroup.Click += (_, _) => RemoveOrbitItem(group);
        row.ContextFlyout = new MenuFlyout { Items = { editIcon, removeGroup } };
        return row;
    }

    /// <summary>
    /// One item listed under a group in the sidebar: its icon and name, a remove button, and a
    /// right-click menu with Move up, Move down and Remove from group. Dragged onto another of the
    /// group's items it is put in that place (see <see cref="WireEditGroupRow"/>, which is the drop
    /// target for the whole group and answers for these rows).
    /// <para>
    /// The row takes the pointer as a whole and its content does not, like the group row above it,
    /// so crossing the icon and the name raises no enter or leave of its own. The remove button is
    /// the exception and is kept out of the drag's start, as the child rows are out of the group's.
    /// </para>
    /// </summary>
    private HandBorder BuildEditGroupChildRow(OrbitItem group, OrbitItem child, IReadOnlyList<OrbitItem> listed)
    {
        var content = BuildEditRowContent(child.Glyph, child.DisplayName, null, null, iconSource: child);
        content.Height = 24;
        content.Opacity = child.Hidden ? 0.5 : 1;
        content.IsHitTestVisible = false;

        var remove = new HandButton
        {
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon
            {
                Glyph = char.ConvertFromUtf32(0xE711),
                FontSize = 10,
                Foreground = ThemeHelper.Brush(Root, "SwiftXMutedBrush"),
            },
        };
        WireOrbitGlyphHover(remove);
        ToolTipService.SetToolTip(remove, "Remove from " + group.DisplayName);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            remove, $"Remove {child.DisplayName} from {group.DisplayName}");
        remove.Click += (_, _) => RemoveFromGroup(group, child);

        var layout = new Grid { ColumnSpacing = 4 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.Children.Add(content);
        Grid.SetColumn(remove, 1);
        layout.Children.Add(remove);

        // Transparent rather than null, so the whole row takes the pointer; the border is only
        // painted while a reordering drag is over it (see WireEditGroupRow).
        var row = new HandBorder
        {
            Margin = new Thickness(20, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(1),
            Tag = child,
            Content = layout,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, $"{child.DisplayName}, in {group.DisplayName}");
        ToolTipService.SetToolTip(row, $"{child.DisplayName}. Drag onto another item to reorder; right-click for more.");

        // The item travels as a live reference, like every sidebar row (see _sidebarDragTemplate);
        // dropped on another of the group's items it is moved, dropped on the ring or on another
        // group it is added there as a copy, like any other.
        WireSidebarDrag(row, child, canStart: source => !IsWithin(source, remove));

        int at = listed.ToList().FindIndex(c => ReferenceEquals(c, child));
        var moveUp = new MenuFlyoutItem
        {
            Text = "Move up",
            Icon = new FontIcon { Glyph = char.ConvertFromUtf32(0xE74A) },
            IsEnabled = at > 0,
        };
        moveUp.Click += (_, _) => MoveInGroup(group, child, -1);
        var moveDown = new MenuFlyoutItem
        {
            Text = "Move down",
            Icon = new FontIcon { Glyph = char.ConvertFromUtf32(0xE74B) },
            IsEnabled = at >= 0 && at < listed.Count - 1,
        };
        moveDown.Click += (_, _) => MoveInGroup(group, child, +1);
        var removeItem = new MenuFlyoutItem
        {
            Text = "Remove from group",
            Icon = new FontIcon { Glyph = char.ConvertFromUtf32(0xE74D) },
        };
        removeItem.Click += (_, _) => RemoveFromGroup(group, child);
        row.ContextFlyout = new MenuFlyout { Items = { moveUp, moveDown, new MenuFlyoutSeparator(), removeItem } };

        return row;
    }

    /// <summary>Takes one item out of a group, from the sidebar's list of what it holds.</summary>
    private void RemoveFromGroup(OrbitItem group, OrbitItem child)
    {
        if (!GroupChildren.Remove(group, child))
            return;

        if (ReferenceEquals(_selectedItem, child))
            HideItemDetails();
        _manager.NotifyItemsChanged();
        RefreshAfterGroupChange();
    }

    /// <summary>Moves one of a group's items a place up or down, from its right-click menu.</summary>
    private void MoveInGroup(OrbitItem group, OrbitItem child, int delta)
    {
        if (!GroupChildren.MoveBy(group, child, delta))
            return;

        _manager.NotifyItemsChanged();
        RefreshAfterGroupChange();
    }

    /// <summary>Whether <paramref name="source"/> is <paramref name="ancestor"/> or something inside it.</summary>
    private static bool IsWithin(object? source, DependencyObject ancestor)
    {
        for (var node = source as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, ancestor))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Makes a Groups row a drop target: a sidebar row dragged onto it, or an app, file, folder,
    /// shortcut or link dragged in from outside SwiftX, becomes a child of that group.
    /// <para>
    /// Every handler marks its event handled. The drag events are routed events that bubble, and the
    /// whole sidebar is itself a drop target that adds to the top level
    /// (<see cref="OrbitSidebarEdit_Drop"/>). Left to bubble, a drop on this row would be added to
    /// the group here and to the orbit there, and the sidebar's DragOver would overwrite the
    /// "no" this row answered with its own "yes". A handled event is not delivered to handlers
    /// registered the ordinary way, which is how the sidebar's are. It does mean the sidebar's
    /// full-height "Drop to add" overlay is not raised while the pointer is over a row: the row
    /// paints its own feedback instead, and the overlay comes back when the pointer leaves.
    /// </para>
    /// <para>
    /// What a drag would do is settled on enter and replayed on every over, rather than recomputed
    /// each time: the answer cannot change mid-drag, and over-events are frequent. Replaying it also
    /// repaints the highlight, so an enter and a leave arriving in either order at the boundary
    /// cannot leave the row looking as though nothing were happening.
    /// </para>
    /// </summary>
    private void WireEditGroupRow(
        ContentControl row, OrbitItem group, TextBlock count, IReadOnlyList<HandBorder> childRows)
    {
        // Null while a text payload is still being read to see whether it is a path or a link.
        bool? usable = false;
        string? reason = null;

        // What the row is painted as right now (null: at rest), so the over-events that replay the
        // answer do not rebuild the same two brushes on every pointer move.
        bool? painted = null;

        // The child row painted as where a reordering drag would land, while one of this group's own
        // items is being dragged over it. A reorder is painted on the child, not the whole row: the
        // row's accent says "this goes in the group", which is not what is happening.
        HandBorder? marked = null;

        void Unmark()
        {
            if (marked is null)
                return;
            marked.BorderBrush = new SolidColorBrush(Colors.Transparent);
            marked.Background = new SolidColorBrush(Colors.Transparent);
            marked = null;
        }

        void Mark(HandBorder? target)
        {
            if (ReferenceEquals(marked, target))
                return;
            Unmark();
            if (target is null)
                return;
            var accent = CurrentAccentColor();
            target.BorderBrush = new SolidColorBrush(accent);
            target.Background = new SolidColorBrush(Color.FromArgb(48, accent.R, accent.G, accent.B));
            marked = target;
        }

        // The child row under the pointer. A drag is raised on the group row as a whole, since that
        // is what allows the drop, so which child it is over comes from where the pointer is.
        HandBorder? ChildRowAt(DragEventArgs e)
        {
            foreach (var childRow in childRows)
            {
                var at = e.GetPosition(childRow);
                if (at.X >= 0 && at.X < childRow.ActualWidth && at.Y >= 0 && at.Y < childRow.ActualHeight)
                    return childRow;
            }
            return null;
        }

        void Clear()
        {
            if (ReferenceEquals(_orbitEditHotGroupRow, row))
                _orbitEditHotGroupRow = null;
            painted = null;
            Unmark();
            row.BorderBrush = new SolidColorBrush(Colors.Transparent);
            row.Background = new SolidColorBrush(Colors.Transparent);
        }

        void Paint()
        {
            // A payload that is nothing this could ever take (a picture, say) is refused without
            // the red: the "no" is for things that were nearly right, and it needs a reason to give.
            if (usable == false && reason is null)
            {
                Clear();
                return;
            }

            _orbitEditHotGroupRow = row;
            bool accept = usable != false;
            if (painted == accept)
                return;
            painted = accept;

            if (accept)
            {
                var accent = CurrentAccentColor();
                row.BorderBrush = new SolidColorBrush(accent);
                row.Background = new SolidColorBrush(Color.FromArgb(48, accent.R, accent.G, accent.B));
            }
            else
            {
                row.BorderBrush = ThemeHelper.Brush(Root, "ToolCriticalBrush");
                row.Background = ThemeHelper.Brush(Root, "ToolCriticalBackgroundBrush");
            }
        }

        void Answer(DragEventArgs e)
        {
            e.Handled = true;

            // One of this group's own items, dragged by its row here: a reorder. It is accepted only
            // over another of the group's items, which is where it will be put.
            if (_sidebarDragTemplate is { } own && GroupChildren.Holds(group, own))
            {
                var over = ChildRowAt(e);
                bool lands = over is not null && !ReferenceEquals(over.Tag, own);
                Mark(lands ? over : null);
                e.AcceptedOperation = lands ? DataPackageOperation.Copy : DataPackageOperation.None;
                if (e.DragUIOverride is { } moveUi)
                {
                    moveUi.IsCaptionVisible = lands;
                    if (lands)
                        moveUi.Caption = "Move here";
                    moveUi.IsGlyphVisible = true;
                }
                return;
            }

            e.AcceptedOperation = usable != false ? DataPackageOperation.Copy : DataPackageOperation.None;
            if (e.DragUIOverride is { } ui)
            {
                string? caption = usable == false ? reason : $"Add to {group.DisplayName}";
                ui.IsCaptionVisible = caption is not null;
                if (caption is not null)
                    ui.Caption = caption;
                ui.IsGlyphVisible = true;
            }
            Paint();
        }

        row.DragEnter += async (sender, e) =>
        {
            int token = ++_orbitEditDragToken;
            (usable, reason) = GroupDropOutlook(group, e);
            Answer(e);
            if (usable is not null)
                return;

            var deferral = e.GetDeferral();
            try
            {
                bool readable = OrbitItemFactory.TryReadTextTarget(await e.DataView.GetTextAsync(), out _);
                if (token == _orbitEditDragToken)
                    usable = readable;
            }
            catch (Exception ex)
            {
                Diag.Log("Groups row DragEnter failed: " + ex);
                if (token == _orbitEditDragToken)
                    usable = false;
            }
            finally
            {
                deferral.Complete();
            }
            if (token == _orbitEditDragToken)
                Paint();
        };

        row.DragOver += (_, e) => Answer(e);

        row.DragLeave += (_, e) =>
        {
            e.Handled = true;
            _orbitEditDragToken++;
            Clear();
            FlushOrbitEditRefresh();
        };

        row.Drop += async (_, e) =>
        {
            e.Handled = true;
            _orbitEditDragToken++;
            Clear();

            // Disabled, not redirected: a replace swaps one place on the ring, and a drop that
            // landed on a group row is not that. GroupDropOutlook has already said no to the drag.
            if (IsReplacing)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                FlushOrbitEditRefresh();
                return;
            }

            // One of this group's own items dropped on another of them: a reorder, put where the one
            // it landed on was. Anywhere else in the group it does nothing — it is already in it.
            // The pointer is read first: Clear above only repaints, but the list is rebuilt below.
            if (_sidebarDragTemplate is { } dragged && GroupChildren.Holds(group, dragged))
            {
                var landedOn = ChildRowAt(e)?.Tag as OrbitItem;
                bool moved = landedOn is not null && GroupChildren.Move(group, dragged, landedOn);
                e.AcceptedOperation = moved ? DataPackageOperation.Copy : DataPackageOperation.None;
                if (moved)
                {
                    _manager.NotifyItemsChanged();
                    RefreshAfterGroupChange();
                }
                return;
            }

            // A sidebar row's own drag: the template is a live field, as it is for the ring (see
            // _sidebarDragTemplate), so there is nothing to read off the DataView. It stays set
            // until the drag's own DropCompleted, which is what releases any rebuild held meanwhile.
            if (_sidebarDragTemplate is { } template)
            {
                var verdict = GroupDropRules.TryAdd(group, CloneOrbitTemplate(template));
                e.AcceptedOperation = verdict == GroupDropVerdict.Allowed
                    ? DataPackageOperation.Copy
                    : DataPackageOperation.None;
                FinishGroupDrop(group, count, verdict == GroupDropVerdict.Allowed ? 1 : 0, verdict);
                return;
            }

            var deferral = e.GetDeferral();
            try
            {
                var targets = await OrbitItemFactory.ReadDroppedTargetsAsync(e.DataView);
                if (targets.Count == 0)
                {
                    e.AcceptedOperation = DataPackageOperation.None;
                    return;
                }

                int added = 0;
                GroupDropVerdict? refusal = null;
                foreach (var target in targets)
                {
                    var verdict = GroupDropRules.TryAdd(group, OrbitItemFactory.FromTarget(target));
                    if (verdict == GroupDropVerdict.Allowed)
                        added++;
                    else
                        refusal ??= verdict;
                }

                e.AcceptedOperation = added > 0 ? DataPackageOperation.Copy : DataPackageOperation.None;
                FinishGroupDrop(group, count, added, refusal);
            }
            catch (Exception ex)
            {
                Diag.Log("Groups row Drop failed: " + ex);
            }
            finally
            {
                deferral.Complete();
                FlushOrbitEditRefresh();
            }
        };
    }

    /// <summary>
    /// What a drag over <paramref name="group"/>'s row would do, as far as can be told without
    /// reading the payload: whether it would be taken, and if not, why. A null answer means
    /// undecided: a plain-text payload is only usable if it turns out to be a path or a link, which
    /// takes reading it.
    /// </summary>
    private (bool? Usable, string? Reason) GroupDropOutlook(OrbitItem group, DragEventArgs e)
    {
        if (IsReplacing)
            return (false, "Finish or cancel the replace first");

        if (_sidebarDragTemplate is { } template)
        {
            // Its own item, dragged within it: a reorder, which Check would refuse as a duplicate.
            // Where it may land is settled by the pointer, over the group's rows (see WireEditGroupRow).
            if (GroupChildren.Holds(group, template))
                return (true, null);

            var verdict = GroupDropRules.Check(group, template);
            return (verdict == GroupDropVerdict.Allowed, GroupDropRules.Reason(verdict));
        }

        // Anything from outside is a new item, so all that is known before it is read is whether
        // there is room for one.
        if (GroupDropRules.IsFull(group))
            return (false, GroupDropRules.Reason(GroupDropVerdict.Full));

        if (OrbitItemFactory.LooksDroppable(e.DataView))
            return (true, null);

        return e.DataView.Contains(StandardDataFormats.Text) ? (null, null) : (false, null);
    }

    /// <summary>
    /// Settles a drop onto a group row: saves and refreshes if anything went in, and says on the row
    /// why the rest did not. The row's own count is updated in place because the sidebar rebuild
    /// that follows <see cref="OrbitManager.NotifyItemsChanged"/> can be held back a moment by a
    /// drag still finishing (see <see cref="OrbitEditDragInFlight"/>).
    /// </summary>
    private void FinishGroupDrop(OrbitItem group, TextBlock count, int added, GroupDropVerdict? refusal)
    {
        if (added > 0)
        {
            _manager.NotifyItemsChanged();
            RefreshAfterGroupChange();
            count.Text = GroupCountText(group);
        }

        if (refusal is { } why && GroupDropRules.Reason(why) is { } text)
            FlashGroupNotice(count, group, text);
    }

    /// <summary>
    /// Brings the Orbit screen's canvas and the Elements list up to date with an item just added to
    /// a group, now rather than on the queued pass <see cref="OrbitManager.ItemsChanged"/> schedules:
    /// a group open on the canvas shows the new child on its arc at once, and a group on the ring
    /// gains the arc that says it has something in it. The sidebar's own rebuild is left to that
    /// queued pass, which holds it back while a drag is still finishing (see
    /// <see cref="OrbitEditDragInFlight"/>).
    /// </summary>
    private void RefreshAfterGroupChange()
    {
        RenderPreview();
        RebuildItemsList();
    }

    /// <summary>
    /// Shows <paramref name="text"/> in a group row's trailing slot for a moment, in the critical
    /// colour, then puts the count back. The row is where the user is looking when a drop is
    /// refused, whereas the sidebar's summary line may be scrolled out of sight above it.
    /// </summary>
    /// <remarks>
    /// The timer is tracked and stopped on <c>Closed</c> alongside the window's other two (see the
    /// constructor). It is a 2.5-second one-shot on a control in this window's tree, so closing
    /// Settings within 2.5 seconds of a refused drop — which is an entirely ordinary thing to do,
    /// the refusal being a reason to give up on the drag — left it to fire against a torn-down
    /// tree and read <c>Root</c> for a brush, out of a <c>Tick</c> handler where a throw is an
    /// unhandled exception rather than something a <c>try</c> here would catch.
    /// </remarks>
    private void FlashGroupNotice(TextBlock count, OrbitItem group, string text)
    {
        count.Text = text;
        count.Foreground = ThemeHelper.Brush(Root, "ToolCriticalBrush");

        // One at a time: a second refused drop while the first notice is still showing restarts
        // the clock rather than leaving two timers to put two different counts back.
        StopGroupNoticeFlash();
        _groupNoticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _groupNoticeTimer.Tick += (_, _) =>
        {
            StopGroupNoticeFlash();
            count.Text = GroupCountText(group);
            count.Foreground = ThemeHelper.Brush(Root, "SwiftXMutedBrush");
        };
        _groupNoticeTimer.Start();
    }

    private void StopGroupNoticeFlash()
    {
        if (_groupNoticeTimer is null) return;
        _groupNoticeTimer.Stop();
        _groupNoticeTimer = null;
    }

    /// <summary>
    /// Puts one of the sidebar's suggestions on the orbit, or says why it cannot. A fresh copy of
    /// the template every time: <see cref="OnboardingCatalog.Available"/> hands out live
    /// <see cref="OrbitItem"/> instances, and adding the same instance twice would put one object
    /// in two slots — rename it in one and it changes in the other.
    /// <para>
    /// Mid-replace (see <see cref="ReplaceOrbitItem"/>), the same click finishes the swap instead of
    /// appending — the whole reason "Replace…" opens this sidebar rather than the add dialog.
    /// </para>
    /// <para>
    /// Also where "New group" ends up, with a new empty group as its template, so a full orbit and
    /// an armed replace are answered there exactly as they are for a suggestion.
    /// </para>
    /// </summary>
    private void AddToOrbitFromSidebar(OrbitItem template)
    {
        var clone = CloneOrbitTemplate(template);

        if (IsReplacing)
        {
            CompleteReplace(clone);
            return;
        }

        if (OrbitIsFull)
        {
            UpdateOrbitEditSummary();
            return;
        }

        OrbitSlots.Place(_manager.Config.Primary.Items, clone);

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
    private static bool OrbitHasTarget(List<OrbitItem> items, string target) =>
        ElementLibrary.ContainsTarget(items, target);

    /// <summary>
    /// Resetting throws away every app, folder, link, group and tool the user has put on the
    /// orbit — not recoverable short of building it all back up by hand — so this asks first
    /// rather than acting the instant the button is pressed. No existing yes/no
    /// <see cref="ContentDialog"/> helper exists elsewhere in this app to reuse (the closest,
    /// <see cref="ToolWindows.ToolWindowBase"/>'s discard-changes prompt, is a tool window's own
    /// save/discard/cancel dialog with a custom close-button title row, not a generic
    /// confirmation), so this builds a minimal one directly.
    /// </summary>
    private async void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (_confirmingResetDefaults) return;
        _confirmingResetDefaults = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = "Reset to defaults?",
                Content = new TextBlock
                {
                    Text = "This puts the orbit back to its default apps, links and layout. " +
                           "Anything you have added, removed or rearranged will be lost.",
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "Reset",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            // Every item the selection/expansion state could be pointing at is about to be
            // replaced.
            HideItemDetails();
            CollapseGroup();
            // The corner's own arrangement was built out of the items being replaced, so it goes
            // back to following the orbit rather than holding four copies of the old one.
            HotCornerItems.Reset(_manager.Config);
            _manager.SeedDefaults(_manager.Config.Primary);
            _manager.NotifyItemsChanged();
            RenderPreview();
            RebuildItemsList();
        }
        finally
        {
            _confirmingResetDefaults = false;
        }
    }

    /// <summary>
    /// Brings the theme cards in line with <paramref name="theme"/> without applying anything —
    /// the load pass's counterpart to <see cref="ThemeCard_Click"/>.
    /// </summary>
    private void SelectTheme(AppTheme theme) => UpdateThemeCardSelection(theme);

    /// <summary>
    /// Repaints all five theme cards for <paramref name="theme"/>: a 1.5px accent border on the
    /// selected one, the card border on the others. Selection is the border alone — there is no
    /// checkbox glyph.
    /// </summary>
    private void UpdateThemeCardSelection(AppTheme theme)
    {
        bool dark = ThemeHelper.IsDark(Root);
        var liveTheme = _manager.Config.Theme;
        var selectedBrush = new SolidColorBrush(CurrentAccentColor());
        var restingBrush = new SolidColorBrush(
            ThemeHelper.ColorFor(dark, liveTheme, "ShellCardBorderBrush"));

        void Paint(Border card, AppTheme cardTheme)
        {
            card.BorderBrush = cardTheme == theme ? selectedBrush : restingBrush;
        }

        Paint(ThemeCardSystem, AppTheme.System);
        Paint(ThemeCardLight, AppTheme.Light);
        Paint(ThemeCardDark, AppTheme.Dark);
        Paint(ThemeCardBlack, AppTheme.Black);
        Paint(ThemeCardFrosted, AppTheme.FrostedGlass);
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
        var effective = LanguageCatalog.ResolveEffective(_manager.Config.Language);
        foreach (var option in LanguageCatalog.Options)
        {
            var item = new ComboBoxItem { Content = option.NativeName, Tag = option.Language.ToString() };
            LanguageBox.Items.Add(item);
            if (option.Language == effective)
                LanguageBox.SelectedItem = item;
        }
        if (LanguageBox.SelectedItem is null && LanguageBox.Items.Count > 0)
            LanguageBox.SelectedIndex = 0;
    }

    /// <summary>
    /// Paints the Hover style pills (Invert, Zoom, Tint, Animate) for <paramref name="style"/>: the selected
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
    /// <para>
    /// Zoom, Tint and Animate are split buttons. Selected, the option's own pill narrows to the left
    /// 70% of its cell with its outer corners rounded and its inner ones square, and the side pill
    /// (<c>HoverZoomDirectionButton</c>: In/Out; <c>HoverTintButton</c>: the colour chip;
    /// <c>HoverAnimationButton</c>: the chosen motion's symbol) appears in the right 30% to adjoin it. Unselected, the side pill is collapsed and the option spans its
    /// whole cell as a single pill. The side pills only ever exist while selected, so they are
    /// always painted as selected ones. This also re-syncs the direction label and the colour
    /// controls from the config, so a load, a reset, an import and a theme change all leave them
    /// showing what is stored.
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
        Paint(HoverStyleTintPill, HoverStyleTintText, style == OrbitHoverStyle.Tint);
        Paint(HoverStyleAnimatePill, HoverStyleAnimateText, style == OrbitHoverStyle.Animate);
        Paint(HoverZoomDirectionPill, HoverZoomDirectionText, selected: true);
        Paint(HoverAnimationPill, HoverAnimationSymbol, selected: true);

        HoverTintPill.Background = accentBrush;
        HoverTintPill.BorderBrush = accentBrush;
        HoverTintChip.BorderBrush = onAccentBrush;

        Split(HoverStyleZoomButton, HoverStyleZoomPill, HoverZoomDirectionButton, style == OrbitHoverStyle.Zoom);
        Split(HoverStyleTintButton, HoverStyleTintPill, HoverTintButton, style == OrbitHoverStyle.Tint);
        Split(HoverStyleAnimateButton, HoverStyleAnimatePill, HoverAnimationButton, style == OrbitHoverStyle.Animate);

        PaintHoverZoomDirection();
        SyncHoverTintControls();
        PaintHoverAnimation();

        void Paint(Border pill, TextBlock label, bool selected)
        {
            pill.Background = selected ? accentBrush : null;
            pill.BorderBrush = selected ? accentBrush : restingBorder;
            label.Foreground = selected ? onAccentBrush : mutedBrush;
        }

        static void Split(Button main, Border mainPill, Button side, bool split)
        {
            Grid.SetColumnSpan(main, split ? 1 : 2);
            mainPill.CornerRadius = split ? new CornerRadius(8, 0, 0, 8) : new CornerRadius(8);
            side.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Sets the Zoom side pill's label, and what a screen reader announces for it, from
    /// <see cref="AppConfig.HoverZoomDirection"/>. The pill shows the current direction rather than
    /// the one a click would switch to, so the name says which it is and the tooltip says what
    /// pressing it does.
    /// </summary>
    private void PaintHoverZoomDirection()
    {
        bool zoomIn = _manager.Config.HoverZoomDirection == HoverZoomDirection.In;
        string zoomDirectionWord = Loc.Get(zoomIn ? "OrbitLayout.HoverStyle.ZoomIn" : "OrbitLayout.HoverStyle.ZoomOut");
        HoverZoomDirectionText.Text = zoomDirectionWord;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            HoverZoomDirectionButton, Loc.Format("OrbitLayout.HoverStyle.ZoomDirectionNameTemplate", zoomDirectionWord));
        ToolTipService.SetToolTip(
            HoverZoomDirectionButton, Loc.Get(zoomIn ? "OrbitLayout.HoverStyle.ZoomGrowingTooltip" : "OrbitLayout.HoverStyle.ZoomShrinkingTooltip"));
    }

    /// <summary>
    /// Brings the hover-colour chip and its picker in line with <see cref="OrbitHover.TintColor"/>.
    /// Seeding the picker re-enters <see cref="HoverTintPicker_ColorChanged"/>, which would otherwise
    /// save the value it was just handed as though the user had chosen it, and turn "unset" into an
    /// explicit black. Guarded the same way, and for the same reason, as
    /// <see cref="SyncAccentControls"/>.
    /// </summary>
    private void SyncHoverTintControls()
    {
        var tint = OrbitHover.TintColor(_manager.Config);
        bool wasLoading = _loading;
        _loading = true;
        HoverTintPicker.Color = tint;
        _loading = wasLoading;
        HoverTintChip.Background = new SolidColorBrush(tint);
    }

    private void HoverTintPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_loading) return;
        _manager.Config.HoverTintColorHex = AccentPalette.ToHex(args.NewColor);
        _manager.Save();
        HoverTintChip.Background = new SolidColorBrush(args.NewColor);
    }

    private void HoverTintReset_Click(object sender, RoutedEventArgs e)
    {
        _manager.Config.HoverTintColorHex = null;
        _manager.Save();
        SyncHoverTintControls();
        HoverTintButton.Flyout?.Hide();
    }

    /// <summary>
    /// One option in the Animate picker. Kept so that a change of selection, accent or theme repaints
    /// the options in place instead of rebuilding a flyout that may be open.
    /// </summary>
    private sealed record HoverAnimationOption(
        HoverAnimation Animation, Border Pill, TextBlock Symbol, TextBlock Label);

    private readonly List<HoverAnimationOption> _hoverAnimationOptions = new();

    /// <summary>
    /// Side of the square each option's symbol is drawn and animated in. Fixed, so a motion's travel
    /// (a fraction of it) and its rotation (about its middle) mean the same for every option and no
    /// neighbour is nudged as one moves.
    /// </summary>
    private const double HoverAnimationStage = 32;

    /// <summary>
    /// Shows the chosen motion on the Animate side pill, and keeps the picker behind it in step. The
    /// pill names the motion by its symbol alone, so the automation name and the tooltip spell it
    /// out. Called wherever <see cref="SelectHoverStyle"/> is, which makes a load, a reset, an import
    /// and a theme change all leave the pill and the picker showing what is stored.
    /// </summary>
    private void PaintHoverAnimation()
    {
        var animation = _manager.Config.HoverAnimation;
        string label = HoverMotion.Label(animation);

        HoverAnimationSymbol.Text = HoverMotion.Symbol(animation);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            HoverAnimationButton, "Hover animation: " + label);
        ToolTipService.SetToolTip(HoverAnimationButton, label + ". Click to choose another motion.");

        if (HoverAnimationFlyout.Content is null)
            BuildHoverAnimationPicker();
        PaintHoverAnimationOptions();
    }

    /// <summary>
    /// The picker behind the Animate side pill: every motion as a symbol over its name, three across.
    /// <para>
    /// Hovering (or keyboard-focusing) an option plays that motion on its symbol, through the same
    /// <see cref="HoverMotionPlayer"/> the orbit uses. The orbit is a separate window and this
    /// sidebar's preview ring does not reproduce hover, so this is the only place in Settings where
    /// the choice can be seen rather than guessed from a word. With animations off in Windows nothing
    /// plays, matching what the orbit does.
    /// </para>
    /// <para>
    /// Each option is a transparent button around a painted border, the way the hover pills are, with
    /// the button's own pointer-over and pressed fills switched off: the default template would draw a
    /// hard-cornered grey rectangle behind the rounded border.
    /// </para>
    /// </summary>
    private void BuildHoverAnimationPicker()
    {
        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        for (int column = 0; column < 3; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int row = 0; row < (HoverMotion.All.Count + 2) / 3; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        for (int i = 0; i < HoverMotion.All.Count; i++)
        {
            var animation = HoverMotion.All[i];
            var spec = HoverMotion.For(animation);

            var symbol = new TextBlock
            {
                Text = HoverMotion.Symbol(animation),
                FontFamily = new FontFamily(HoverMotion.SymbolFontFamily),
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var stage = new Grid
            {
                Width = HoverAnimationStage,
                Height = HoverAnimationStage,
                Children = { symbol },
            };
            var label = new TextBlock
            {
                Text = HoverMotion.Label(animation),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var pill = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 8, 8, 6),
                Child = new StackPanel
                {
                    Spacing = 2,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { stage, label },
                },
            };

            var option = new Button
            {
                MinWidth = 76,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Content = pill,
            };
            foreach (var key in new[]
            {
                "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed",
                "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed",
            })
                option.Resources[key] = transparent;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                option, "Hover animation: " + label.Text);

            option.Click += (_, _) =>
            {
                _manager.Config.HoverAnimation = animation;
                _manager.Save();
                PaintHoverAnimation();
                HoverAnimationFlyout.Hide();
            };
            option.PointerEntered += (_, _) => PreviewHoverAnimation(stage, spec, play: true);
            option.PointerExited += (_, _) => PreviewHoverAnimation(stage, spec, play: false);
            option.GotFocus += (_, _) =>
            {
                if (option.FocusState == FocusState.Keyboard)
                    PreviewHoverAnimation(stage, spec, play: true);
            };
            option.LostFocus += (_, _) => PreviewHoverAnimation(stage, spec, play: false);

            Grid.SetRow(option, i / 3);
            Grid.SetColumn(option, i % 3);
            grid.Children.Add(option);
            _hoverAnimationOptions.Add(new HoverAnimationOption(animation, pill, symbol, label));
        }

        HoverAnimationFlyout.Content = grid;
    }

    /// <summary>
    /// Plays or settles one picker option's motion. The pulse's ceiling is generous because the
    /// symbol is small and a swell that stays inside the orbit's growth budget would barely register.
    /// </summary>
    private static void PreviewHoverAnimation(UIElement stage, MotionSpec spec, bool play)
    {
        if (play)
        {
            if (!ReducedMotion.IsActive())
                HoverMotionPlayer.Start(stage, HoverAnimationStage, spec, restScale: 1f, maxScale: 1.5f);
            return;
        }

        if (spec.Axis == MotionAxis.Scale)
            HoverMotionPlayer.RestScale(stage, 1f, returnMs: 150);
        else
            HoverMotionPlayer.Stop(stage, spec, returnMs: 150);
    }

    /// <summary>
    /// Paints the picker's options: the chosen one filled with the live accent (which tracks a
    /// custom pick, as the hover pills do) and the rest as plain outlines. Local values, so like
    /// <see cref="SelectHoverStyle"/> this has to be re-run on a theme or accent change, which it is,
    /// through the same call.
    /// </summary>
    private void PaintHoverAnimationOptions()
    {
        bool dark = ThemeHelper.IsDark(Root);
        var accent = CurrentAccentColor();
        var accentBrush = new SolidColorBrush(accent);
        var onAccentBrush = new SolidColorBrush(AccentPalette.OnAccent(accent));
        var restingBorder = new SolidColorBrush(ThemeHelper.ColorFor(dark, _manager.Config.Theme, "ShellCardBorderBrush"));
        var foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush");
        var muted = ThemeHelper.Brush(Root, "SwiftXMutedBrush");

        foreach (var option in _hoverAnimationOptions)
        {
            bool selected = option.Animation == _manager.Config.HoverAnimation;
            option.Pill.Background = selected ? accentBrush : null;
            option.Pill.BorderBrush = selected ? accentBrush : restingBorder;
            option.Symbol.Foreground = selected ? onAccentBrush : foreground;
            option.Label.Foreground = selected ? onAccentBrush : muted;
        }
    }

    private void SelectItemSize(OrbitItemSize size)
    {
        ItemSizeSmallOption.IsChecked = size == OrbitItemSize.Small;
        ItemSizeMediumOption.IsChecked = size == OrbitItemSize.Medium;
        ItemSizeLargeOption.IsChecked = size == OrbitItemSize.Large;
    }

    private void SelectItemShape(OrbitItemShape shape)
    {
        ItemShapeCircleOption.IsChecked = shape == OrbitItemShape.Circle;
        ItemShapeSquircleOption.IsChecked = shape == OrbitItemShape.Squircle;
        ItemShapeSquareOption.IsChecked = shape == OrbitItemShape.Square;
        ItemShapeHexagonOption.IsChecked = shape == OrbitItemShape.Hexagon;
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
    /// <summary>
    /// Picks one of Frosted glass's three frost levels. Saves it, points every frosted surface at
    /// it, and re-runs the same pass a theme change does — the surfaces are computed from the level
    /// (see <see cref="FrostedGlassPalette"/>), so moving it is a repaint of exactly the same set
    /// of keys, and the backdrop behind them has to move with them or the two disagree.
    /// <para>
    /// Guarded on the value actually changing rather than fired on every click: this is a
    /// three-button group where two of the three presses are re-selecting what is already chosen,
    /// and a full theme pass now rebuilds the Elements and Tools cards.
    /// </para>
    /// </summary>
    private void FrostedLevel_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not FrameworkElement source) return;
        if (!Enum.TryParse<FrostedGlassLevel>(source.Tag as string, out var level)) return;
        ApplyFrostedLevel(level);
    }

    /// <summary>
    /// Points every frosted surface at <paramref name="level"/> and re-runs the theme pass when
    /// the value actually moved. Shared by the frost-level pills and the header toggle.
    /// </summary>
    private void ApplyFrostedLevel(FrostedGlassLevel level)
    {
        _manager.Config.FrostedGlass = level;
        _manager.Save();
        UpdateFrostedLevelSelection(level);
        UpdateThemeToggleIcon();

        if (!FrostedGlassBackdrop.SetLevel(level)) return;
        _manager.ApplyTheme();
    }

    /// <summary>
    /// Shows which frost level is selected, and shows the group at all only under Frosted glass —
    /// under any other theme it is a control for something not on screen.
    /// <para>
    /// Painted from code for the same reason the theme cards above it are: selection here is a
    /// per-element assignment, which no dictionary swap can reach, which is also why
    /// <see cref="RepaintThemedContent"/> re-runs it.
    /// </para>
    /// </summary>
    private void UpdateFrostedLevelSelection(FrostedGlassLevel level)
    {
        FrostedLevelPanel.Visibility = _manager.Config.Theme == AppTheme.FrostedGlass
            ? Visibility.Visible
            : Visibility.Collapsed;
        bool rail = FrostedLevelPanel.Visibility == Visibility.Visible;
        ThemeCardFrostedPreview.Margin = rail
            ? new Thickness(12, 14, 28, 0)
            : new Thickness(12, 14, 12, 0);
        ThemeCardFrostedLabel.Margin = rail
            ? new Thickness(0, 0, 22, 0)
            : new Thickness(0);

        bool dark = ThemeHelper.IsDark(Root);
        var accent = new SolidColorBrush(CurrentAccentColor());
        var resting = new SolidColorBrush(
            ThemeHelper.ColorFor(dark, _manager.Config.Theme, "ShellCardBorderBrush"));

        void Paint(Border pill, FrostedGlassLevel pillLevel)
        {
            bool on = pillLevel == level;
            pill.BorderBrush = on ? accent : resting;
            pill.BorderThickness = new Thickness(on ? 2.5 : 1.5);
        }

        Paint(FrostedLevelLightPill, FrostedGlassLevel.Light);
        Paint(FrostedLevelMediumPill, FrostedGlassLevel.Medium);
        Paint(FrostedLevelDarkPill, FrostedGlassLevel.Dark);
    }

    // ========================== the Orbit toolbar's help menu ==========================
    //
    // The gear that used to sit beside the help button here now lives in the left sidebar's own
    // "ADD TO ORBIT" panel, permanently on screen rather than behind a header toggle, and the
    // wrench itself moved down into CanvasActions next to Reset defaults. Only its menu's
    // handlers are still here.

    // The wrench's menu mirrors the reference's How To / Support / What's New, re-pointed at
    // things this app actually has. A menu entry that goes nowhere is worse than no menu, and
    // SwiftX has no device features to introduce and no video library to link to, so each of the
    // three below says which reference entry it stands in for rather than borrowing its label.

    /// <summary>Stands in for the reference's "Customization tips": the fastest way to learn what
    /// can go on the orbit is the panel that lists it, so this opens that rather than describing
    /// it.</summary>
    private void HowToArrange_Click(object sender, RoutedEventArgs e) => ShowScreen(ScreenOrbit);

    /// <summary>Stands in for the reference's "How To video library". SwiftX has no videos; it has
    /// a README that documents every screen, which is the same thing in the medium this project
    /// actually keeps up to date.</summary>
    private void HowToHandbook_Click(object sender, RoutedEventArgs e) =>
        Launcher.OpenUrl(GitHubRepoUrl + "#readme");

    /// <summary>The reference's "Support", pointed at where support actually happens.</summary>
    private void OrbitSupport_Click(object sender, RoutedEventArgs e) =>
        Launcher.OpenUrl(GitHubRepoUrl + "/issues");

    /// <summary>The reference's "What's New", pointed at the releases this is cut from.</summary>
    private void WhatsNew_Click(object sender, RoutedEventArgs e) =>
        Launcher.OpenUrl(GitHubRepoUrl + "/releases");

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
            // ApplyLanguage only re-points Loc.CurrentLanguage and refreshes the tray (see its own
            // doc comment in OrbitManager) — it never touches this window's own controls, which
            // were localized once from static XAML. ApplyLocalizedText is a flat pass of property
            // assignments with no event-subscription side effects, so it is safe to run again here
            // and makes the switch take effect immediately instead of waiting for the next launch.
            // RebuildItemsList/RebuildToolsList/BuildOrbitEditSidebar are deliberately NOT re-run:
            // none of what they build (ElementSections, ToolCatalog, ElementCategories, the "Add to
            // Orbit…" row) calls Loc.Get anywhere — that content is plain English today, not
            // localized, so there is nothing in it to go stale.
            _manager.ApplyLanguage();
            ApplyLocalizedText();
        }
    }

    /// <summary>
    /// PLACEHOLDER. SwiftX has no Microsoft Store listing yet — it builds unpackaged
    /// (<c>WindowsPackageType=None</c>), so there is no product id to point at. In-app rating
    /// (<see cref="StoreFeedback.RequestRatingAsync"/>) still works from a packaged build.
    /// Put the real twelve-character product id on <see cref="StoreFeedback.ProductId"/> once
    /// the app is listed; listing URLs and the Store deep-link fallback all derive from it.
    /// </summary>
    private const string StoreProductId = StoreFeedback.ProductId;

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

    private async void ShareFeedback_Click(object sender, RoutedEventArgs e) =>
        await StoreFeedback.OpenFeedbackHubAsync();

    private async void RateOnStore_Click(object sender, RoutedEventArgs e) =>
        await StoreFeedback.RequestRatingAsync(_hwnd);

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
    /// Moves to Settings › Privacy instead of opening anything — it is a section of this
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
    /// Selects a hover style from its main pill. Pressing the pill of the style already in force
    /// does nothing: for Zoom and Tint that is the left 70% of a split pill, which is a label for
    /// the side pill beside it and not a switch.
    /// </summary>
    private void HoverStyleOption_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not FrameworkElement option) return;
        if (!Enum.TryParse<OrbitHoverStyle>(option.Tag as string, out var style)) return;
        if (style == _manager.Config.HoverStyle) return;

        _manager.Config.HoverStyle = style;
        _manager.Save();
        SelectHoverStyle(style);
    }

    /// <summary>Flips the Zoom side pill between In (grow) and Out (shrink).</summary>
    private void HoverZoomDirection_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _manager.Config.HoverZoomDirection =
            _manager.Config.HoverZoomDirection == HoverZoomDirection.In
                ? HoverZoomDirection.Out
                : HoverZoomDirection.In;
        _manager.Save();
        PaintHoverZoomDirection();
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
    /// About's framework and runtime lines — read at runtime rather than written into the XAML
    /// so a csproj bump cannot leave the page lying about what is actually running.
    /// </summary>
    private void FillAboutDetails()
    {
        string runtime = RuntimeInformation.FrameworkDescription;
        string arch = RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant();
        AboutFramework.Text = Loc.Format("Settings.About.Framework", runtime, arch);

        // Always visible; disabled with a tooltip on the unpackaged build since StoreContext
        // requires package identity — see OrbitManager.UpdateChecksSupported.
        CheckForUpdatesLink.Visibility = Visibility.Visible;
        if (!_manager.UpdateChecksSupported)
        {
            CheckForUpdatesLink.IsEnabled = false;
            ToolTipService.SetToolTip(CheckForUpdatesLink, "Available in the Microsoft Store build");
        }
    }

    /// <summary>Whether a check has found an update the link hasn't yet offered to install. The
    /// updates themselves stay inside <see cref="StoreUpdateChecker"/> — this window only needs to
    /// know whether to relabel the link and which method to call next.</summary>
    private bool _hasPendingStoreUpdate;

    /// <summary>
    /// The one control the About card's Terms/Privacy row offers for updates (see
    /// <see cref="OrbitManager.UpdateChecksSupported"/> for why it is disabled on the
    /// unpackaged build). It does double duty rather than growing a second button: at rest it
    /// reads "Check for updates" and runs <see cref="StoreUpdateChecker.CheckForUpdateAsync"/>;
    /// once a check finds something, it relabels itself to "Install update" and the next click
    /// runs <see cref="StoreUpdateChecker.RequestInstallAsync"/> instead. Either way, the result is
    /// reported with a real OS toast (see <see cref="ShowUpdateToast"/>) rather than an in-window
    /// status line — that's what "from the bottom" in the task meant, and it is also what lets this
    /// stay a single compact link instead of the old dedicated Updates card.
    /// </summary>
    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_hasPendingStoreUpdate)
        {
            await InstallStoreUpdateAsync();
            return;
        }

        CheckForUpdatesLink.IsEnabled = false;
        var result = await StoreUpdateChecker.CheckForUpdateAsync();
        CheckForUpdatesLink.IsEnabled = true;

        if (!result.Ok)
        {
            ShowUpdateToast(
                Loc.Get("Settings.About.Updates.CouldNotCheckTitle"),
                Loc.Get("Settings.About.Updates.CouldNotCheckBody"));
            return;
        }

        if (!result.UpdateAvailable)
        {
            ShowUpdateToast(
                Loc.Get("Settings.About.Updates.UpToDateTitle"),
                Loc.Get("Settings.About.Updates.UpToDateBody"));
            return;
        }

        _hasPendingStoreUpdate = true;
        CheckForUpdatesLink.Content = Loc.Get("Settings.About.Updates.InstallLink");
        ShowUpdateToast(
            Loc.Get("Settings.About.Updates.AvailableTitle"),
            Loc.Format("Settings.About.Updates.AvailableBody", result.LatestVersion ?? ""));
    }

    /// <summary>
    /// Hands the update a prior check found to the Store's own download/install UI — a
    /// system-owned flow, not one this window draws any progress for itself (see
    /// <see cref="StoreUpdateChecker.RequestInstallAsync"/>). The link returns to its resting
    /// "check" state either way: a failed or cancelled install is not this app's to retry blindly,
    /// and the Store's own UI will have already told the user what happened.
    /// </summary>
    private async Task InstallStoreUpdateAsync()
    {
        CheckForUpdatesLink.IsEnabled = false;
        bool installed = await StoreUpdateChecker.RequestInstallAsync(_hwnd);
        CheckForUpdatesLink.IsEnabled = true;

        _hasPendingStoreUpdate = false;
        CheckForUpdatesLink.Content = Loc.Get("Settings.About.Updates.CheckLink");

        if (!installed)
        {
            ShowUpdateToast(
                Loc.Get("Settings.About.Updates.InstallFailedTitle"),
                Loc.Get("Settings.About.Updates.InstallFailedBody"));
        }
    }

    /// <summary>
    /// Posts a real OS-level toast (bottom-right, outside this window) for an update check's
    /// result — see <see cref="App.OnLaunched"/> for the one-time
    /// <c>AppNotificationManager.Default.Register()</c> this depends on. Informational only, with
    /// no buttons of its own: the task that asked for this allowed either an actionable toast or
    /// install staying a click on <see cref="CheckForUpdatesLink"/>, and the latter needed no new
    /// <c>Package.appxmanifest</c> extension (<c>windows.toastNotificationActivation</c> is only
    /// for handling a toast's own click/button, which this doesn't have) — the simpler of the two
    /// options the task allowed for. Never throws: a toast that fails to post is logged and
    /// otherwise shrugged off, the same as every other best-effort notification in this app.
    /// </summary>
    private static void ShowUpdateToast(string title, string body)
    {
        try
        {
            var builder = new AppNotificationBuilder().AddText(title);
            if (!string.IsNullOrEmpty(body))
                builder.AddText(body);
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            Diag.Log($"ShellWindow: couldn't show the update-check toast: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        // async void, no caller: guarded for the same reason as PickIconImage — PickSaveFileAsync
        // throws rather than returning null when a picker is already open on this window.
        try
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, _hwnd);
            picker.SuggestedFileName = "orbit";
            picker.FileTypeChoices.Add("JSON", [".json"]);
            var file = await picker.PickSaveFileAsync();
            if (file is not null)
                OrbitStore.ExportTo(_manager.Config, file.Path);
        }
        catch (Exception ex)
        {
            Diag.Log("ShellWindow.Export_Click failed: " + ex);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        // async void, no caller: guarded for the same reason as PickIconImage/Export_Click, and
        // because everything from HideItemDetails() on applies an imported dock.json's contents
        // straight into this window's live controls — OrbitStore.ImportFrom already validates the
        // JSON shape, but a structurally-valid, semantically-odd import (e.g. HotCornerLayout the
        // combo box below has no entry for) has no business taking the whole app down with it.
        try
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
            HideItemDetails();
            CollapseGroup();
            _manager.Config.CopyAppSettingsFrom(imported);
            _manager.Config.Orbits.Clear();
            _manager.Config.Orbits.AddRange(imported.Orbits);
            _manager.Config.EnsurePrimary();
            // A file exported by an earlier build can carry corner copies of groups; the same repair
            // the app makes at launch (see OrbitManager.Start), here because an import skips it.
            HotCornerItems.RelinkOrbitGroups(_manager.Config);
            _manager.NotifyItemsChanged();
            _manager.ApplyTheme();
            _manager.ApplyLanguage();
            _manager.ApplyMouseTrigger();
            _manager.ApplyHotCorner();
            ApplyTheme();
            _loading = true;
            PopulateLanguageBox();
            MouseTriggerEnabledToggle.IsOn = _manager.Config.MouseTriggerEnabled;
            SelectMouseTriggerButton(_manager.Config.MouseTriggerButton);
            SelectMouseTriggerAction(_manager.Config.MouseTriggerAction);
            HotCornerEnabledToggle.IsOn = _manager.Config.HotCornerEnabled;
            SelectHotCorner(_manager.Config.HotCorner);
            SelectHotCornerLayout(_manager.Config.HotCornerLayout);
            SyncHotCornerControls();
            _loading = false;
            RenderPreview();
            RebuildItemsList();
        }
        catch (Exception ex)
        {
            _loading = false;
            Diag.Log("ShellWindow.Import_Click failed: " + ex);
        }
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        // SeedDefaults below replaces every orbit item, so drop anything the selection/expansion
        // state was pointing at first.
        HideItemDetails();
        CollapseGroup();
        _manager.Config.Theme = AppTheme.Dark;
        _manager.Config.FrostedGlass = FrostedGlassLevel.Light;
        _manager.Config.HoverStyle = OrbitHoverStyle.Invert;
        _manager.Config.HoverZoomDirection = HoverZoomDirection.In;
        _manager.Config.HoverTintColorHex = null;
        _manager.Config.HoverAnimation = HoverAnimation.Pulse;
        _manager.Config.Hotkey = HotkeyGesture.Default.ToString();
        _manager.Config.HotkeyEnabled = true;
        _manager.Config.MouseTriggerEnabled = true;
        _manager.Config.MouseTriggerButton = MouseTriggerButton.Middle;
        _manager.Config.MouseTriggerAction = MouseTriggerAction.LongPress;
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
        _manager.SetItemShape(OrbitItemShape.Circle);
        _manager.SetRadius(null);
        _manager.NotifyItemsChanged();
        _manager.ApplyTheme();
        _manager.ApplyHotkey();
        _manager.ApplyMouseTrigger();
        _manager.ApplyHotCorner();
        ApplyTheme();
        _loading = true;
        SelectTheme(AppTheme.Dark);
        UpdateThemeToggleIcon();
        SelectHoverStyle(OrbitHoverStyle.Invert);
        SelectItemSize(OrbitItemSize.Medium);
        SelectItemShape(OrbitItemShape.Circle);
        RadiusSlider.Value = OrbitMetrics.DefaultRadius;
        HotkeyEnabledToggle.IsOn = true;
        HotkeyCapture.Gesture = HotkeyGesture.Default;
        MouseTriggerEnabledToggle.IsOn = true;
        SelectMouseTriggerButton(MouseTriggerButton.Middle);
        SelectMouseTriggerAction(MouseTriggerAction.LongPress);
        HotCornerEnabledToggle.IsOn = true;
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

    /// <summary>
    /// Size's own counterpart. Goes through the manager for the same reason
    /// <see cref="ItemSize_Checked"/> does — the setting has to reach <see cref="OrbitMetrics"/>,
    /// which is what every draw site reads, not just the config.
    /// </summary>
    private void ItemShape_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton radio) return;
        if (Enum.TryParse<OrbitItemShape>(radio.Tag as string, out var shape))
            _manager.SetItemShape(shape);
    }

    private void RadiusReset_Click(object sender, RoutedEventArgs e)
    {
        _radiusAdjusting = false;
        _loading = true;
        RadiusSlider.Value = OrbitMetrics.DefaultRadius;
        _loading = false;
        _manager.SetRadius(null);
        RenderPreview();
    }

    private void RadiusSlider_DragStarted(object sender, PointerRoutedEventArgs e)
    {
        if (_loading) return;
        _radiusAdjusting = true;
        RenderPreview();
    }

    private void RadiusSlider_DragEnded(object sender, PointerRoutedEventArgs e)
    {
        if (!_radiusAdjusting) return;
        _radiusAdjusting = false;
        _manager.Save();
        RenderPreview();
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
        RadiusReadout.Text = Loc.Format("OrbitLayout.Radius.Value", (int)e.NewValue);
        if (_loading) return;
        _manager.SetRadius(e.NewValue, persist: !_radiusAdjusting);
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

    private void MouseTriggerEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _manager.Config.MouseTriggerEnabled = MouseTriggerEnabledToggle.IsOn;
        _manager.Save();
        _manager.ApplyMouseTrigger();
    }

    /// <summary>Picks the row in <see cref="MouseTriggerButtonBox"/> whose Tag names
    /// <paramref name="button"/> — the picker's own counterpart to <see cref="SelectHotCorner"/>.</summary>
    private void SelectMouseTriggerButton(MouseTriggerButton button)
    {
        foreach (var obj in MouseTriggerButtonBox.Items)
        {
            if (obj is ComboBoxItem item &&
                string.Equals(item.Tag as string, button.ToString(), StringComparison.Ordinal))
            {
                MouseTriggerButtonBox.SelectedItem = item;
                return;
            }
        }
    }

    private void MouseTriggerButtonBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MouseTriggerButtonBox.SelectedItem is not ComboBoxItem item) return;
        if (Enum.TryParse<MouseTriggerButton>(item.Tag as string, out var button))
        {
            _manager.Config.MouseTriggerButton = button;
            _manager.Save();
            _manager.ApplyMouseTrigger();
        }
    }

    /// <summary>Picks the row in <see cref="MouseTriggerActionBox"/> whose Tag names
    /// <paramref name="action"/> — <see cref="SelectMouseTriggerButton"/>'s own counterpart for the
    /// gesture picker.</summary>
    private void SelectMouseTriggerAction(MouseTriggerAction action)
    {
        foreach (var obj in MouseTriggerActionBox.Items)
        {
            if (obj is ComboBoxItem item &&
                string.Equals(item.Tag as string, action.ToString(), StringComparison.Ordinal))
            {
                MouseTriggerActionBox.SelectedItem = item;
                return;
            }
        }
    }

    private void MouseTriggerActionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MouseTriggerActionBox.SelectedItem is not ComboBoxItem item) return;
        if (Enum.TryParse<MouseTriggerAction>(item.Tag as string, out var action))
        {
            _manager.Config.MouseTriggerAction = action;
            _manager.Save();
            _manager.ApplyMouseTrigger();
        }
    }

    private void SelectHotCorner(HotCorner corner) => RebuildHotCornerPicker(corner);

    /// <summary>
    /// Rebuilds the four-leaf hot-corner picker so the selected leaf matches
    /// <paramref name="corner"/>. Rebuilt rather than mutated because the glyph is a fresh tree of
    /// petals from <see cref="AppLogo.CreateCornerGlyph"/>.
    /// </summary>
    private void RebuildHotCornerPicker(HotCorner corner)
    {
        if (HotCornerPickerHost is null) return;
        HotCornerPickerHost.Content = AppLogo.CreateCornerGlyph(56, corner, picked =>
        {
            if (_loading) return;
            _manager.Config.HotCorner = picked;
            _manager.Save();
            _manager.ApplyHotCorner();
            RebuildHotCornerPicker(picked);
            RenderPreview();
        });
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
        {
            _previewQuadrant = false;
            // QuickAccess exists only on the corner, so an open one has nothing left to be a view of.
            if (_expandedGroup is { } open && QuadrantLayout.IsQuickAccess(open))
                CollapseGroup();
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
        if (ok)
        {
            HotkeyStatus.Visibility = Visibility.Collapsed;
            return;
        }

        HotkeyStatus.Text = Loc.Get("Settings.General.Hotkey.NotRegistered");
        HotkeyStatus.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Builds the Elements screen: the orbit's items grouped by category (Apps, Shortcuts,
    /// Folders, Links, Groups, in the same order and under the same headings as the "Add to orbit"
    /// sidebar, both fed by <see cref="ElementCategories"/>), each a heading over its own
    /// two-column grid of compact cards. <see cref="ElementSections.Build"/> decides what is listed and where;
    /// this only draws it. Tools are not a section here: the Tools screen owns them and the
    /// built-in Tools group.
    /// <para>
    /// Every category is drawn even when nothing is in it, with a muted one-line note instead of a
    /// grid, so Folders and Groups can be found before the first one exists. A group's children are
    /// not sorted by kind into the other sections: they stay with their group, in the Groups
    /// section (see <see cref="BuildGroupBlock"/>).
    /// </para>
    /// <para>
    /// A group used to be skipped here altogether, which meant a group with nothing in it yet
    /// (exactly what adding a Group produces) appeared nowhere and read as a broken add. Each group
    /// gets one card the same shape and width as every other element's; an earlier full-width
    /// group section made the group card noticeably wider than its neighbours.
    /// </para>
    /// <para>
    /// Rebuilt from scratch on every change rather than patched: every add, edit, replace, drop,
    /// delete and reorder ends in <see cref="OrbitManager.NotifyItemsChanged"/>, whose
    /// <c>ItemsChanged</c> handler (see the constructor) calls this, and <see cref="ShowScreen"/>
    /// calls it again on arriving here. That is what puts a newly added or re-typed item in the
    /// right section without any caller having to know about sections.
    /// </para>
    /// </summary>
    private void RebuildItemsList()
    {
        if (ItemsList is null) return;
        ItemsList.Children.Clear();

        int generation = ++_itemsListGeneration;
        _missingFlags.Clear();

        foreach (var section in ElementSections.Build(_manager.Config.Primary.Items))
            ItemsList.Children.Add(BuildElementSection(section));

        _ = MarkMissingItemsAsync(generation, _missingFlags.ToList());
    }

    /// <summary>
    /// Bumped by every <see cref="RebuildItemsList"/>, so a "Missing" check that finishes after the
    /// cards it was for have been replaced can tell, and drop its answer instead of writing it
    /// onto elements nobody can see any more.
    /// </summary>
    private int _itemsListGeneration;

    /// <summary>
    /// The hidden "Missing" note on each card whose item points at a local path worth checking,
    /// with that path, collected while the cards are built and handed to
    /// <see cref="MarkMissingItemsAsync"/> once they are all in place.
    /// </summary>
    private readonly List<(string Path, TextBlock Flag)> _missingFlags = new();

    /// <summary>
    /// Looks up, off the UI thread, whether each flagged path still exists, and reveals the note on
    /// the ones that do not. Off the thread because even a local disk can be asleep, and the UI
    /// thread must never wait on the file system for a card to appear.
    /// <see cref="ElementSections.IsMissing"/> is what keeps this safe otherwise: it answers "not
    /// missing" for anything but a fixed local drive, so a network path is never touched.
    /// </summary>
    private async Task MarkMissingItemsAsync(int generation, List<(string Path, TextBlock Flag)> flags)
    {
        if (flags.Count == 0) return;

        bool[] missing;
        try
        {
            var paths = flags.Select(f => f.Path).ToList();
            missing = await Task.Run(() => paths.Select(ElementSections.IsMissing).ToArray());
        }
        catch (Exception ex)
        {
            Diag.Log("MarkMissingItemsAsync failed: " + ex);
            return;
        }

        if (generation != _itemsListGeneration) return;
        for (int i = 0; i < flags.Count; i++)
        {
            if (missing[i])
                flags[i].Flag.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// One category: a heading, then either its cards or a note that there are none. The heading
    /// copies the sidebar's own category header (<see cref="BuildEditCategory"/>): an accent glyph
    /// in a 20px column beside a bold upper-case label at 11pt, plus the number of things listed.
    /// It is exposed as a heading so a screen reader can jump section to section.
    /// </summary>
    private FrameworkElement BuildElementSection(ElementSection section)
    {
        string label = ElementCategories.Label(section.Category);
        int count = section.Entries.Count;

        var glyph = new FontIcon
        {
            Glyph = ElementCategories.Glyph(section.Category),
            FontSize = 13,
            Foreground = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitCanvasAccentBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var title = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            CharacterSpacing = 50,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush"),
        };
        title.Inlines.Add(new Run { Text = label });
        title.Inlines.Add(new Run
        {
            Text = "   " + count,
            FontWeight = FontWeights.Normal,
            Foreground = ThemeHelper.Brush(Root, "SwiftXMutedBrush"),
        });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            title,
            System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(label.ToLowerInvariant())
                + ", " + (count == 1 ? "1 item" : count + " items"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHeadingLevel(
            title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(glyph, 0);
        Grid.SetColumn(title, 1);
        header.Children.Add(glyph);
        header.Children.Add(title);

        FrameworkElement body;
        if (count == 0)
            body = BuildEmptySectionRow(section.Category);
        else if (section.Category == ElementCategory.Groups)
            body = BuildGroupBlocks(section);
        else
            body = BuildCardGrid(section.Entries.Select(e => BuildElementCard(BuildItemRow(e.Item))).ToList());

        return new StackPanel
        {
            Spacing = 12,
            Children = { header, body },
        };
    }

    /// <summary>
    /// What a category with nothing in it says, in the same muted 11pt the sidebar's own
    /// <see cref="BuildEmptyCategoryRow"/> uses, lined up under the heading's label.
    /// </summary>
    private FrameworkElement BuildEmptySectionRow(ElementCategory category) => new TextBlock
    {
        Text = $"No {ElementCategories.Label(category).ToLowerInvariant()} yet. Use Add item to add one.",
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(20, 0, 0, 0),
        Foreground = ThemeHelper.Brush(Root, "SwiftXMutedBrush"),
    };

    /// <summary>A card's outer surface, the same "Card" style every screen's cards share.</summary>
    private Border BuildElementCard(FrameworkElement content) => new()
    {
        Style = (Style)Root.Resources["Card"],
        Child = content,
    };

    /// <summary>
    /// Lays cards out two to a row, every column the same width. Two rather than three: a compact
    /// card is a single row of icon, name, switch and buttons, and needs the width to show a path
    /// as more than an ellipsis. The rows are as tall as their tallest card, which with cards this
    /// size is only ever a matter of a pixel or two.
    /// </summary>
    private static Grid BuildCardGrid(IReadOnlyList<FrameworkElement> cards)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        for (int c = 0; c < 2; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < (cards.Count + 1) / 2; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < cards.Count; i++)
        {
            Grid.SetRow(cards[i], i / 2);
            Grid.SetColumn(cards[i], i % 2);
            grid.Children.Add(cards[i]);
        }
        return grid;
    }

    /// <summary>The Groups section's body: one <see cref="BuildGroupBlock"/> per group.</summary>
    private FrameworkElement BuildGroupBlocks(ElementSection section)
    {
        var blocks = new StackPanel { Spacing = 24 };
        foreach (var entry in section.Entries)
            blocks.Children.Add(BuildGroupBlock(entry));
        return blocks;
    }

    /// <summary>
    /// One group and what is inside it, read as a unit: the group's own card first, then a card per
    /// child flowing after it in the same two-column grid, so a group is no wider than any other
    /// element. A slim accent rail down the block's left edge (drawn in the page gutter, so the
    /// cards stay aligned with every other section's) ties the children to their group, and each
    /// block starts a fresh row, so where one group ends and the next begins is never in doubt.
    /// </summary>
    private FrameworkElement BuildGroupBlock(ElementEntry entry)
    {
        var cards = new List<FrameworkElement> { BuildElementCard(BuildGroupRow(entry.Item)) };
        foreach (var child in entry.Children)
            cards.Add(BuildElementCard(BuildItemRow(child)));

        var rail = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(1.5),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(-14, 0, 0, 0),
            Opacity = 0.6,
            Background = ThemeHelper.Brush(Root, _manager.Config.Theme, "OrbitCanvasAccentBrush"),
        };

        return new Grid { Children = { BuildCardGrid(cards), rail } };
    }

    /// <summary>
    /// A top-level group's compact card: its icon and name, how many items it holds, a plus that
    /// opens the scoped add flow into the group, and a delete that takes the group and everything
    /// in it. A group is not itself a shell target, so unlike an item's card there is nothing here
    /// to edit; adding to it is what its card is for. Which items it contains, by name, is in the
    /// tooltip (see <see cref="BuildElementTooltip"/>).
    /// </summary>
    private Grid BuildGroupRow(OrbitItem group)
    {
        int count = group.Children.Count;

        return BuildCardShell(
            BuildItemIcon(group),
            group.DisplayName,
            count switch
            {
                0 => "No items yet",
                1 => "1 item",
                _ => $"{count} items",
            },
            BuildElementTooltip(group),
            isHidden: group.Hidden,
            onToggle: isOn =>
            {
                group.Hidden = !isOn;
                _manager.NotifyItemsChanged();
                RenderPreview();
            },
            toggleAutomationName: "Show " + group.DisplayName + " on the orbit",
            actions:
            [
                new CardAction(0xE710, "Add item", "Add item to " + group.DisplayName, () =>
                    _manager.OpenAddItem(added =>
                    {
                        // Into this group's own children, not the top-level orbit, and with the
                        // Group tile taken away: the orbit has no third orbit to expand a group
                        // inside a group into.
                        group.Children.Add(added);
                        _manager.NotifyItemsChanged();
                        RenderPreview();
                        RebuildItemsList();
                    }, group.Children, isGenericAddItem: true, allowGroups: false)),
                new CardAction(0xE74D, "Delete", "Delete " + group.DisplayName, () =>
                {
                    // The whole group goes, children and all: they only exist inside it.
                    OrbitSlots.Vacate(_manager.Config.Primary.Items, group);
                    _manager.NotifyItemsChanged();
                    RebuildItemsList();
                    RenderPreview();
                }),
            ]);
    }

    /// <summary>
    /// One item's compact card: its icon, its name, and the one line that says what it points at (a
    /// path or a URL, trimmed to fit, in full in the tooltip), with the show/hide switch, an edit
    /// button that runs the same Replace flow the orbit's own right-click menu uses (see
    /// <see cref="EditOrbitItem"/>) and a delete button. What kind of thing it is needs no line of
    /// its own, because the section it sits in says so. Shared by the top-level cards and by a
    /// group's children, so both look and behave the same; <see cref="EditOrbitItem"/> and this
    /// card's own delete handler both check both locations for exactly that reason.
    /// <para>
    /// A local path that no longer exists gets a "Missing" flag beside the path. It is built hidden
    /// and registered in <see cref="_missingFlags"/>, and whether to reveal it is settled off the UI
    /// thread by <see cref="MarkMissingItemsAsync"/>.
    /// </para>
    /// </summary>
    private Grid BuildItemRow(OrbitItem item)
    {
        TextBlock? missing = null;
        if (ElementSections.LocalPathToCheck(item) is { } localPath)
        {
            missing = new TextBlock
            {
                Text = "Missing",
                Style = SecondaryCaptionStyle,
                FontWeight = FontWeights.SemiBold,
                Visibility = Visibility.Collapsed,
            };
            _missingFlags.Add((localPath, missing));
        }

        return BuildCardShell(
            BuildItemIcon(item),
            item.DisplayName,
            item.Target ?? "",
            BuildElementTooltip(item),
            isHidden: item.Hidden,
            onToggle: isOn =>
            {
                item.Hidden = !isOn;
                _manager.NotifyItemsChanged();
                RenderPreview();
            },
            toggleAutomationName: "Show " + item.DisplayName + " on the orbit",
            actions:
            [
                new CardAction(0xE70F, "Edit", "Edit " + item.DisplayName, () => EditOrbitItem(item)),
                new CardAction(0xE74D, "Delete", "Delete " + item.DisplayName, () =>
                {
                    // Top level and inside a group both, since the same card builds for either.
                    OrbitSlots.Vacate(_manager.Config.Primary.Items, item);
                    foreach (var group in _manager.Config.Primary.Items.Where(i => i.IsGroup))
                        group.Children.Remove(item);
                    _manager.NotifyItemsChanged();
                    RebuildItemsList();
                    RenderPreview();
                }),
            ],
            missing);
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
    /// <para>
    /// The item itself goes to the dialog, not just a heading saying its name. Until it did, this
    /// was an edit in label only: the form opened with no type picked, no target, no name and no
    /// icon, so pressing Save replaced the item with whatever the user had managed to retype from
    /// memory and dropped the rest. <see cref="AddItemWindow"/> fills its fields from what is
    /// passed here and carries the item's id, hidden state and off-form fields across its own
    /// commit, so what comes back through this callback really is the same element, edited.
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
            confirmLabel: "Save",
            existing: item);
    }

    /// <summary>One icon button on an Elements card: what it looks like, what a screen reader and a
    /// tooltip call it, and what it does.</summary>
    private sealed record CardAction(int Glyph, string Tooltip, string AutomationName, Action Invoke);

    /// <summary>
    /// The compact layout an Elements card is built on, shared by a plain item's card
    /// (<see cref="BuildItemRow"/>) and a group's (<see cref="BuildGroupRow"/>): the icon, then the
    /// name over one muted line, then the show/hide switch and the card's icon buttons, all on a
    /// single row about as tall as the icon.
    /// <para>
    /// It carries only what is needed to recognise an item and act on it. Everything else that is
    /// known about it (its full path, arguments, icon and shown/hidden state, and what a group
    /// contains) is in <paramref name="tooltip"/>, which is what the cards used to spell out in a
    /// block of their own and which made each one several times this tall. The name and the line
    /// under it trim to the room they have rather than wrapping, so a long path never changes a
    /// card's height; the tooltip has it whole. The Tools screen's cards have a layout of their
    /// own, <see cref="BuildToolCardShell"/>.
    /// </para>
    /// </summary>
    private Grid BuildCardShell(
        FrameworkElement icon, string title, string caption, string tooltip,
        bool isHidden, Action<bool> onToggle, string toggleAutomationName,
        IReadOnlyList<CardAction> actions, TextBlock? missing = null)
    {
        var card = new Grid { ColumnSpacing = 10 };
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        icon.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(icon, 0);
        card.Children.Add(icon);

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Root.Resources["SettingTitle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        });

        if (!string.IsNullOrEmpty(caption))
        {
            // The flag sits after the line rather than in it, so a long path trims and the flag
            // does not: it is the one part of the line that must never be cut off.
            var line = new Grid { ColumnSpacing = 8 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(new TextBlock
            {
                Text = caption,
                Style = SecondaryCaptionStyle,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            });
            if (missing is not null)
            {
                Grid.SetColumn(missing, 1);
                line.Children.Add(missing);
            }
            text.Children.Add(line);
        }
        Grid.SetColumn(text, 1);
        card.Children.Add(text);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var toggle = new ToggleSwitch
        {
            IsOn = !isHidden,
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggle.Toggled += (_, _) => onToggle(toggle.IsOn);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, toggleAutomationName);
        controls.Children.Add(toggle);

        foreach (var action in actions)
        {
            var button = BuildRowIconButton(action.Glyph, action.Tooltip, action.AutomationName);
            button.Width = 32;
            button.Height = 32;
            var invoke = action.Invoke;
            button.Click += (_, _) => invoke();
            controls.Children.Add(button);
        }
        Grid.SetColumn(controls, 2);
        card.Children.Add(controls);

        ToolTipService.SetToolTip(card, tooltip);
        return card;
    }

    /// <summary>
    /// Everything known about an item, as the tooltip on its card: name, what kind of thing it is,
    /// the full path or URL, the arguments if there are any, whether the icon is the default or a
    /// custom one, and whether it is shown on the orbit. A group lists what it contains instead of a
    /// path. The item's id is left out; it means nothing to the person reading the card.
    /// </summary>
    private static string BuildElementTooltip(OrbitItem item)
    {
        var lines = new List<string> { item.DisplayName, KindLabel(item.Kind) };

        if (item.IsGroup)
        {
            var names = item.Children.Select(c => c.DisplayName).ToList();
            lines.Add("Contains: " + (names.Count == 0 ? "nothing yet" : string.Join(", ", names)));
        }
        else if (!string.IsNullOrWhiteSpace(item.Target))
        {
            lines.Add((item.Kind == OrbitItemKind.WebLink ? "URL: " : "Path: ") + item.Target);
        }

        if (!string.IsNullOrWhiteSpace(item.Arguments))
            lines.Add("Arguments: " + item.Arguments);

        lines.Add("Icon: " + ElementSections.IconKindOf(item) switch
        {
            ElementIconKind.Image => "custom image, " + item.CustomIconPath,
            ElementIconKind.Glyph => "custom glyph",
            _ => "default",
        });
        lines.Add(item.Hidden ? "Hidden from the orbit" : "Shown on the orbit");

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The full word for a kind, for a card's tooltip. <c>Kind.ToString()</c> would say
    /// "WebLink".</summary>
    private static string KindLabel(OrbitItemKind kind) => kind switch
    {
        OrbitItemKind.Application => "Application",
        OrbitItemKind.File => "File",
        OrbitItemKind.Folder => "Folder",
        OrbitItemKind.WebLink => "Web link",
        OrbitItemKind.Shortcut => "Shortcut",
        OrbitItemKind.Group => "Group",
        _ => kind.ToString(),
    };

    /// <summary>The small rounded tag under a tool card's title — background one step off
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
            VerticalContentAlignment = VerticalAlignment.Center,
            MinWidth = 0,
            MinHeight = 0,
            // Same 16px total as before (so the footer's overall height, and the card's, don't
            // move), just split evenly — the old 12/4 split put visibly more air above "OPEN" than
            // below it, since with MinHeight 0 the button hugs its content and there is no slack
            // for VerticalContentAlignment alone to redistribute.
            Padding = new Thickness(0, 8, 0, 8),
            // The default Button template draws its pointer-over/pressed fill as a flat rectangle
            // with no rounding of its own — invisible at rest (the fill is transparent), but every
            // other surface in this card (the card itself, the kind badge) is rounded, so a bare
            // hard-cornered rectangle lighting up here read as a rendering glitch rather than a
            // hover state.
            CornerRadius = new CornerRadius(8),
        };
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.Resources["ButtonBackground"] = transparent;
        button.Resources["ButtonBackgroundPointerOver"] = ThemeHelper.Brush(Root, "ShellPillBrush");
        button.Resources["ButtonBackgroundPressed"] = ThemeHelper.Brush(Root, "ShellPillBrush");
        button.Resources["ButtonBorderBrush"] = transparent;
        button.Resources["ButtonBorderBrushPointerOver"] = transparent;
        button.Resources["ButtonBorderBrushPressed"] = transparent;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, automationName);
        return button;
    }

    /// <summary>The borderless icon button an item card and a group card both carry, for delete.</summary>
    private static Button BuildRowIconButton(
        int glyph, string tooltip, string automationName, string? fontFamily = null)
    {
        var icon = new FontIcon { Glyph = char.ConvertFromUtf32(glyph), FontSize = 14 };
        if (fontFamily is not null)
            icon.FontFamily = new FontFamily(fontFamily);

        var button = new Button
        {
            Content = icon,
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
    /// The square an Elements or a Tools card draws its icon in, and the two glyph sizes drawn
    /// inside it. Shared by <see cref="BuildItemIcon"/> and <see cref="BuildToolCard"/> rather than
    /// written out twice: the two screens build their cards from separate code but the cards are
    /// meant to read as one family, and an icon a few pixels off between them is exactly the kind
    /// of drift that is invisible while editing one screen and obvious with both open.
    /// <para>
    /// <see cref="CardTextGlyphSize"/> is for the glyphs that are not Fluent marks at all —
    /// "B64" and friends, which <see cref="GlyphFonts.IsTextGlyph"/> picks out and which render in
    /// Segoe UI. Those are measured in characters rather than in one em-wide mark, so at the size
    /// a single-mark glyph wants they would run straight out of the box and past the title beside
    /// them.
    /// </para>
    /// </summary>
    private const double CardIconSize = 36;
    private const double CardGlyphSize = 28;
    private const double CardTextGlyphSize = 18;

    /// <summary>
    /// The icon at the head of a card: the item's own glyph to begin with, replaced by its real
    /// shell / folder / favicon icon if <see cref="IconService"/> resolves one. The same
    /// image-over-glyph pair the orbit itself draws, just built imperatively here rather than bound.
    /// A group resolves nothing (it has no target), so it simply keeps its group glyph.
    /// <para>
    /// Both sit straight on the card's own ground. They were inset in a ⌀44 medallion until now — a
    /// <c>ShellMedallionBrush</c> fill rimmed in <c>ShellCardBorderBrush</c>, borrowed from the
    /// Home screen's nav boxes — but a nav box is a flat panel and a card is already a raised
    /// surface, so the same plate that gave the mark weight there read as a second card inside the
    /// first here, and the app icons it framed are artwork with their own edges that gain nothing
    /// from being circled. With the plate gone the mark is the only thing left to carry that
    /// weight, which is why it is drawn a good deal larger than the 20pt it could get away with
    /// while a 44px disc was doing the work: at the old size on bare ground it read as a stray
    /// character adrift in the corner of the card.
    /// </para>
    /// <para>
    /// The box the glyph and the picture share keeps a fixed size even though nothing paints it.
    /// That is what holds every card's title at the same height whether its icon resolved to a
    /// picture or stayed a glyph, and whether that glyph is one Fluent mark or the three characters
    /// of "B64".
    /// </para>
    /// </summary>
    private FrameworkElement BuildItemIcon(OrbitItem item)
    {
        var glyph = new FontIcon
        {
            Glyph = item.Glyph,
            FontSize = GlyphFonts.IsTextGlyph(item.Glyph) ? CardTextGlyphSize : CardGlyphSize,
            Foreground = IconColors.BrushFor(item, ThemeHelper.Brush(Root, "SwiftXForegroundBrush")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Same non-Fluent-glyph handling the orbit and the preview do — "B64" and friends are tofu
        // boxes in the icon font.
        if (GlyphFonts.IsTextGlyph(item.Glyph))
            glyph.FontFamily = new FontFamily("Segoe UI");

        var image = new Image
        {
            Width = CardIconSize,
            Height = CardIconSize,
            Stretch = Stretch.Uniform,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var content = new Grid
        {
            Width = CardIconSize,
            Height = CardIconSize,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(glyph);
        content.Children.Add(image);

        LoadRowIcon(item, image, glyph);

        return content;
    }

    /// <summary>
    /// Fire-and-forget icon resolution for one row. Started on the UI thread, so the continuation
    /// comes back to it and can touch the two elements directly. A null result means "nothing
    /// resolved, or the user picked a glyph" — the glyph already on screen is then simply left
    /// alone.
    /// </summary>
    private static async void LoadRowIcon(OrbitItem item, Image image, FontIcon glyph)
    {
        // async void, no caller: this list is rebuilt (RebuildItemsList) whenever the Elements
        // list changes while icon loads for dozens of rows are still in flight, and the window
        // itself can close during that time too — either way this resumes to touch two elements
        // that may no longer be in a live visual tree.
        try
        {
            var source = await IconService.LoadIconAsync(item);
            if (source is null) return;
            image.Source = source;
            image.Visibility = Visibility.Visible;
            glyph.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Diag.Log("ShellWindow.LoadRowIcon failed: " + ex);
        }
    }

    /// <summary>
    /// <paramref name="Tags"/> are the short chips the card grid shows under a tool's
    /// description (mockup: docs/images/6-swiftx-tools.svg) — purely cosmetic, no existing
    /// data-model field backed them before, so they live here alongside the rest of a row's static
    /// content. The first tag is the tool's primary category (the one badge a card used to
    /// carry); the rest name a format it handles or something it does, and each has to stay true
    /// to the tool page behind it, because a chip on a card reads as a promise about that page.
    /// </summary>
    private sealed record ToolRow(
        string Target, ToolKind Kind, string Title, string Description, IReadOnlyList<string> Tags, string Glyph,
        bool TextGlyph, Func<AppConfig, bool> GetShown, Action<AppConfig, bool> SetShown);

    // Built from the codepoint, not a literal character in source, so the glyph can't be
    // silently swapped for a different (or invisible) private-use character by a stray
    // copy/paste. See the memory note on this exact failure mode.
    private static readonly string DataGlyph = char.ConvertFromUtf32(0xE943);
    private static readonly string QuickFormatGlyph = char.ConvertFromUtf32(0xE917);
    private static readonly string SecureValuesGlyph = char.ConvertFromUtf32(0xEA18);
    private static readonly string CertificateGlyph = char.ConvertFromUtf32(0xEB95);

    private static readonly ToolRow[] ToolRows =
    [
        new("Data", ToolKind.Data, "Data formatter", "Auto-detect JSON or XML and format it.", ["Formatter", "JSON", "XML", "Minify"], DataGlyph, false,
            c => c.ShowDataTool, (c, v) => c.ShowDataTool = v),
        new("QuickFormat", ToolKind.QuickFormat, "Quick Format", "Auto-format your last copied JSON, XML, YAML or CSV.", ["Clipboard", "Auto-detect", "YAML", "CSV"], QuickFormatGlyph, false,
            c => c.ShowQuickFormatTool, (c, v) => c.ShowQuickFormatTool = v),
        new("SecureValues", ToolKind.SecureValues, "Secure values", "Local key/secret store with masked values.", ["Vault", "DPAPI", "Masked", "Local only"], SecureValuesGlyph, false,
            c => c.ShowSecureValuesTool, (c, v) => c.ShowSecureValuesTool = v),
        new("Base64", ToolKind.Base64, "Base64", "Encode and decode Base64 text.", ["Codec", "URL-safe", "UTF-8"], "B64", true,
            c => c.ShowBase64Tool, (c, v) => c.ShowBase64Tool = v),
        new("Certificate", ToolKind.Certificate, "Certificate", "Inspect certificate, PKCS#12, and Java KeyStore files.", ["Inspector", "PEM", "PKCS#12", "JKS"], CertificateGlyph, false,
            c => c.ShowCertificateTool, (c, v) => c.ShowCertificateTool = v),
    ];

    private void RebuildToolsList()
    {
        ToolsGrid.ColumnDefinitions.Clear();
        ToolsGrid.Children.Clear();
        ToolsGrid.ColumnSpacing = 16;
        for (int i = 0; i < ToolRows.Length; i++)
            ToolsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < ToolRows.Length; i++)
        {
            var card = BuildToolCard(ToolRows[i]);
            Grid.SetColumn(card, i);
            ToolsGrid.Children.Add(card);
        }
    }

    /// <summary>
    /// One tool's card in the single row <see cref="ToolsGrid"/> lays out —
    /// built by <see cref="BuildToolCardShell"/>: the icon, title, description, tag chips and OPEN
    /// footer all centred on the card's own centre line. That is a layout of its own rather than a
    /// call to <see cref="BuildCardShell"/> (the left-aligned shape the Elements screen's cards
    /// share), because a tool card has no delete button or visibility toggle to seat beside its
    /// icon — a built-in tool is opened from
    /// this page, not shown, hidden, or removed. <see cref="Root"/>'s "Card" style wraps the
    /// result and carries <c>row.Target</c> as its <c>Tag</c>; the Tag outlived the drag-reorder
    /// that used to read it back, and is left in place because it is the only thing identifying
    /// which row a card came from.
    /// </summary>
    private Border BuildToolCard(ToolRow row)
    {
        // A tool has no resolvable app/file/folder icon the way an Elements item does — just its own
        // fixed Fluent mark, or for Base64 the literal "B64" text row.TextGlyph flags. Sized and
        // boxed to match BuildItemIcon's exactly (CardIconSize and its two glyph sizes), because the
        // two screens build their cards from separate code and are meant to read as one family.
        FrameworkElement glyph = row.TextGlyph
            ? new TextBlock
            {
                Text = row.Glyph,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = CardTextGlyphSize,
                Foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush"),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
            : new FontIcon
            {
                Glyph = row.Glyph,
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                FontSize = CardGlyphSize,
                Foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        // The box is kept, the plate is not — see BuildItemIcon, which lost the same ⌀44 medallion
        // for the same reason. Sizing it holds every card's title at one height regardless of
        // whether the mark inside is one Fluent glyph or three characters of Segoe UI.
        var icon = new Grid
        {
            Width = CardIconSize,
            Height = CardIconSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { glyph },
        };

        var kind = row.Kind;
        var card = BuildToolCardShell(
            icon,
            row.Title,
            row.Description,
            row.Tags,
            footerAutomationName: "Open " + row.Title,
            onFooter: () => _manager.OpenTool(kind));

        return new Border { Style = (Style)Root.Resources["Card"], Child = card, Tag = row.Target };
    }

    /// <summary>
    /// A tool card's line-height for its description, and the chip row's reserved height: two
    /// lines each, the most either is ever allowed to take (the description is capped at two
    /// lines, and each tool's tags in <see cref="ToolRows"/> are kept to what fits two rows).
    /// Reserving the full two whether or not a tool needs them is what puts every card's chips,
    /// divider and OPEN footer at the same offsets, so a card whose description or tags happen to
    /// fit on one line does not come out shorter than the rest of its row. A chip is a 16px
    /// caption line with 3px of padding above and below (see <see cref="BuildKindBadge"/>), so
    /// 22px; two of them and the 6px gap are 50px, with 2px in hand for the caption's real line
    /// box.
    /// </summary>
    private const double ToolCardCaptionLineHeight = 16;
    private const double ToolCardChipRowMinHeight = 52;
    private const double ToolCardChipGap = 6;

    /// <summary>
    /// The layout of one tool card. Everything sits on the card's centre line, where
    /// <see cref="BuildCardShell"/> hangs its content from the left edge under an icon-and-actions
    /// header: the icon (a tool has nothing to delete or toggle, so the header is only the icon),
    /// a centred title and description, a centred wrapping row of tag chips, then the divider and
    /// the OPEN footer.
    /// <para>
    /// The chips, divider and footer are the same shared pieces the Elements cards use
    /// (<see cref="BuildKindBadge"/> once per tag, <see cref="BuildCardDivider"/>,
    /// <see cref="BuildCardFooterButton"/>), so the two screens' cards still read as one family.
    /// </para>
    /// <para>
    /// The chip row is the one star-sized row and the description and chip rows reserve two lines
    /// each (see <see cref="ToolCardCaptionLineHeight"/>). Between them a card's height does not
    /// depend on how its own text wraps, and if the grid ever arranges a card taller than its
    /// content (a title wrapping to a second line in another card of its row), the surplus lands
    /// under its chips and the footer stays pinned to the bottom edge.
    /// </para>
    /// </summary>
    private Grid BuildToolCardShell(
        FrameworkElement icon, string title, string description, IReadOnlyList<string> tags,
        string footerAutomationName, Action onFooter)
    {
        var card = new Grid { RowSpacing = 8 };
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
            MinHeight = 2 * ToolCardCaptionLineHeight,
        });
        card.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
            MinHeight = ToolCardChipRowMinHeight,
        });
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(icon, 0);
        card.Children.Add(icon);

        var titleBlock = new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            FontSize = 18,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
        };
        Grid.SetRow(titleBlock, 1);
        card.Children.Add(titleBlock);

        var descriptionBlock = new TextBlock
        {
            Text = description,
            Style = SecondaryCaptionStyle,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            LineHeight = ToolCardCaptionLineHeight,
        };
        Grid.SetRow(descriptionBlock, 2);
        card.Children.Add(descriptionBlock);

        var chips = new CenteredWrapPanel
        {
            ItemSpacing = ToolCardChipGap,
            LineSpacing = ToolCardChipGap,
            VerticalAlignment = VerticalAlignment.Top,
        };
        foreach (var tag in tags)
            chips.Children.Add(BuildKindBadge(tag));
        Grid.SetRow(chips, 3);
        card.Children.Add(chips);

        var divider = BuildCardDivider();
        Grid.SetRow(divider, 4);
        card.Children.Add(divider);

        var footer = BuildCardFooterButton("OPEN", footerAutomationName);
        footer.Click += (_, _) => onFooter();
        Grid.SetRow(footer, 5);
        card.Children.Add(footer);

        return card;
    }
}
