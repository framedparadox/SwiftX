using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
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
/// Settings shell: left NavigationView with Home (ring customize,
/// including the hot corner), General (the invoke shortcut and app-wide settings), Elements,
/// Tools, and — pinned to the footer — Feedback &amp; Support, About, and Privacy.
/// <para>
/// This window, and only this window, follows the Logi Options+ palette (see
/// <c>docs/logi_options_design_features.md</c> and the Settings-exclusive keys in
/// <c>Themes/Colors.xaml</c>); the ring, AddItemWindow, and the tool windows keep the app's shared
/// black/white identity, which is why the shared <c>SwiftX*</c> brushes are deliberately left
/// alone here.
/// </para>
/// <para>
/// Light is flat and fully opaque: a pure white canvas with cards a cool tint <em>darker</em>
/// (#F0F4F8) inset into it — the reverse of the white-cards-on-grey it used to be. Dark sits on a
/// real <see cref="MicaBackdrop"/> (attached below, regardless of theme — it only ever shows
/// through Dark's brushes) under the layered, alpha-carrying plates in the Dark dictionary of
/// <c>Themes/Colors.xaml</c> (nav pane, content canvas, cards, and the recessed "tray" areas each
/// a deliberately different shade), so the surfaces stay visually distinct from each other while
/// still letting Mica's own wallpaper tint read through every one of them. That alpha is why Dark
/// lands near Logi's flat hexes rather than exactly on them — a deliberate trade to keep the
/// frosted-glass look rather than making Mica a no-op.
/// </para>
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly RingManager _manager;
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private bool _loading = true;
    private Border? _dragBorder;
    private RingItem? _dragItem;
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
    private readonly Border?[] _slotBorders = new Border?[RingMetrics.SlotCount];
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

    // Whether the canvas is currently showing the hot corner's quadrant instead of the ring.
    // Purely a preview: toggled by the corner button RenderPreview draws, and never persisted —
    // what the corner actually summons is Config.HotCornerLayout.
    private bool _previewQuadrant;

    // Selection state for the read-only inspector (InfoBox) and the group children row (GroupRow).
    // Neither swaps out which list the ring renders anymore — a click just picks what, if anything,
    // is shown below the ring — so plain "currently selected / currently expanded" fields replace
    // the old _editing/_editList-swapping model entirely.
    private RingItem? _selectedItem;
    private RingItem? _expandedGroup;

    // Geometry + drag state for GroupRow's horizontal children strip, mirroring _slotBorders /
    // _dragBorder etc. above but for a 1-D layout instead of the ring's circular one.
    private const double GroupChildBubble = 44;
    private const double GroupChildGap = 14;
    private readonly List<Border> _groupSlotBorders = new();
    private Border? _groupDragBorder;
    private RingItem? _groupDragItem;
    private Point _groupDragStart;
    private bool _groupDragMoved;
    private int _groupDragIndex = -1;

    public SettingsWindow(RingManager manager)
    {
        _manager = manager;
        InitializeComponent();

        // Attached regardless of theme — see the class doc above for why that's fine: Light's own
        // brushes stay fully opaque, so Mica never has anything translucent to show through there.
        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };

        _toolCards.CollectionChanged += ToolCards_CollectionChanged;

        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));
        Title = "SwiftX Settings";

        var titleBar = AppTitleBar.WithAppMark("SwiftX Settings");
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
        AboutMark.Content = AppLogo.Create(48);

        PreviewCanvas.PointerPressed += Preview_PointerPressed;
        PreviewCanvas.PointerMoved += Preview_PointerMoved;
        PreviewCanvas.PointerReleased += Preview_PointerReleased;
        PreviewCanvas.PointerCaptureLost += (_, _) => EndPreviewDrag();

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
            ApplyBlackOverrides();
            // System follows Windows, so an OS flip changes which built-in teal is the right one.
            ApplyAccentColorOverride();
            SyncAccentControls();
            ApplyChromeTheme();
            RenderPreview();
        };
        Activated += (_, _) => ApplyChromeTheme();

        if (_appWindow.Presenter is OverlappedPresenter p)
            p.IsMaximizable = false;
        _appWindow.IsShownInSwitchers = true;

        // 1000 used to leave only a couple of px of margin for the Home tab's ring-preview card +
        // sidebar row (~998px of fixed content against a 1000px client width) — razor-thin enough
        // that ordinary font-metric/DPI variance could tip it into visible clipping. 1040 restores
        // a real margin; the Home tab's sidebar column is also now Grid-adaptive (see
        // SettingsWindow.xaml) rather than a fixed width, so it degrades gracefully even if the
        // window is later resized narrower still.
        WindowChrome.SetClientSizeDip(_appWindow, _hwnd, 1040, 700);
        WindowChrome.SetRoundedCorners(_hwnd);
        WindowChrome.CenterOnCursor(_appWindow, _appWindow.Id);

        Root.Loaded += async (_, _) =>
        {
            _loading = true;
            SelectTheme(_manager.Config.Theme);
            PopulateLanguageBox();
            SelectHoverStyle(_manager.Config.HoverStyle);
            SelectItemSize(_manager.Config.ItemSize);
            RadiusSlider.Value = _manager.Config.RingRadius ?? RingMetrics.DefaultRadius;
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
            VersionText.Text = "Version " + (typeof(App).Assembly.GetName().Version?.ToString() ?? "1.0.0");
            _loading = false;
            RenderPreview();
            RebuildItemsList();
            UpdateHotkeyStatus();
        };

        _manager.ItemsChanged += () =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                // An edit from elsewhere (e.g. a delete on the "Elements" tab) can remove the
                // item this tab currently has selected or expanded — drop stale state rather than
                // leave the inspector or children row pointing at something that no longer exists.
                if (_expandedGroup is not null && !ItemExists(_manager.Config.Primary.Items, _expandedGroup))
                    CollapseGroup();
                if (_selectedItem is not null && !ItemExists(_manager.Config.Primary.Items, _selectedItem))
                    HideInfo();
                RenderPreview();
                if (_expandedGroup is not null) RenderGroupRow();
                RebuildItemsList();
            });
        };
    }

    private static bool ItemExists(List<RingItem> items, RingItem target)
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
        ApplyBlackOverrides();
        ApplyAccentColorOverride();
        ForceThemeResourceRefresh(ThemeHelper.Resolve(_manager.Config.Theme));
        ApplyChromeTheme();
        RenderPreview();
    }

    /// <summary>
    /// Assigns <see cref="Root"/>'s RequestedTheme, forcing every <c>{ThemeResource}</c> binding
    /// under it to re-resolve even when <paramref name="target"/> is the value it's already at.
    /// <para>
    /// WinUI only re-walks <c>{ThemeResource}</c> lookups when RequestedTheme/ActualTheme actually
    /// changes value — a plain edit to <see cref="Root"/>'s own Resources (see
    /// <see cref="ApplyBlackOverrides"/>) is invisible to bindings that already resolved earlier.
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
    /// dictionary's for every Card, NavigationView pane, and canvas Grid under Root — exactly the
    /// same "closer resource wins" behavior SecondaryCaptionStyle's own {ThemeResource} already
    /// relies on (see its comment). Removed again the moment Black is no longer selected, so
    /// switching back to Dark/Light falls straight through to the normal dictionary.
    /// </summary>
    private static readonly string[] BlackOverrideKeys =
    {
        "SettingsSurfaceBrush", "SwiftXCanvasBrush", "SwiftXNavPaneBrush",
        "SwiftXCardBackgroundBrush", "SwiftXRingCanvasBrush",
        "NavigationViewDefaultPaneBackground", "NavigationViewExpandedPaneBackground",
        "NavigationViewContentBackground",
    };

    private void ApplyBlackOverrides()
    {
        if (_manager.Config.Theme != AppTheme.Black)
        {
            foreach (var key in BlackOverrideKeys)
                Root.Resources.Remove(key);
            return;
        }

        // One flat, higher-opacity black shared by every surface — no graduated raised/recessed
        // steps the way Dark has them, since there is no lighter tone left to graduate through
        // once the anchor color is already pure black. Card edges (SwiftXCardStrokeBrush, left
        // untouched) carry the structure instead.
        var surface = new SolidColorBrush(Color.FromArgb(0xE6, 0, 0, 0));
        foreach (var key in BlackOverrideKeys)
            Root.Resources[key] = surface;
    }

    /// <summary>
    /// Paints every WinUI brush key, across the controls Settings actually uses, that a real
    /// "accent colour" has to reach — always, whether the colour is the user's own pick or the
    /// built-in teal default. Shadows them in <see cref="Root"/>'s own Resources, the same "closer
    /// resource wins" mechanism <see cref="ApplyBlackOverrides"/> uses.
    /// <para>
    /// It applies the default too, rather than removing the shadow and letting the keys fall
    /// through, because falling through does not reach SwiftX's teal at all: unset, these keys
    /// resolve to WinUI's own <c>AccentFillColorDefaultBrush</c>, i.e. the Windows system accent
    /// (measured at #4CC2FF on this machine). Only the hyperlinks were ever teal by default —
    /// the toggles, radio buttons, Radius slider, rating stars, and nav selection indicator all
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
        var hover = new SolidColorBrush(Blend(accent, Colors.White, 0.18));
        var pressed = new SolidColorBrush(Blend(accent, Colors.Black, 0.18));

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
        Root.Resources["NavigationViewSelectionIndicatorForeground"] = normal;

        // The one accented thing a resource override cannot reach on its own — see the comment
        // on ReadPrivacyLink in SettingsWindow.xaml.
        ReadPrivacyLink.Foreground = normal;
    }

    /// <summary>The built-in default, per theme — must track SettingsAccentBrush/
    /// SettingsAccentHoverBrush in Themes/Colors.xaml, the same duplication-for-a-reason
    /// SettingsRimColorDark/Light already carries a few lines down (see that comment).</summary>
    private const string DefaultAccentHexDark = "#3DDCD3";
    private const string DefaultAccentHexLight = "#0B7C7C";

    /// <summary>
    /// What the accent colour actually resolves to right now: the user's own pick when they have
    /// made one, otherwise the built-in teal for the theme in play. Drives both the brushes
    /// <see cref="ApplyAccentColorOverride"/> paints and the swatch/picker that report them.
    /// </summary>
    private Color CurrentAccentColor()
    {
        var hex = _manager.Config.AccentColorHex;
        if (!string.IsNullOrEmpty(hex) && TryParseHexColor(hex, out var custom))
            return custom;
        TryParseHexColor(ResolveIsDark() ? DefaultAccentHexDark : DefaultAccentHexLight, out var fallback);
        return fallback;
    }

    /// <summary>
    /// Whether the theme now being applied is a dark one, read from the configured
    /// <see cref="AppTheme"/> rather than from <c>Root.ActualTheme</c>.
    /// <para>
    /// The distinction matters for ordering: <see cref="ApplyTheme"/> has to write the accent
    /// brushes <em>before</em> <see cref="ForceThemeResourceRefresh"/>, since that refresh is what
    /// makes every <c>{ThemeResource}</c> under Root re-resolve and pick the new brushes up —
    /// but at that moment Root.RequestedTheme has not been assigned yet, so ActualTheme can still
    /// be reporting the previous theme. Config is already correct at that point. Only System, which
    /// has no fixed answer of its own, defers to what the element actually resolved to.
    /// </para>
    /// </summary>
    private bool ResolveIsDark() => ThemeHelper.Resolve(_manager.Config.Theme) switch
    {
        ElementTheme.Dark => true,
        ElementTheme.Light => false,
        _ => ThemeHelper.IsDark(Root),
    };

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

    /// <summary>Parses "#RRGGBB" (the only form <see cref="AppConfig.AccentColorHex"/> is ever
    /// written in). Never throws — an unparsable stored value just falls back like an unset
    /// one.</summary>
    private static bool TryParseHexColor(string hex, out Color color)
    {
        color = default;
        var s = hex.TrimStart('#');
        if (s.Length != 6)
            return false;
        try
        {
            byte r = Convert.ToByte(s.Substring(0, 2), 16);
            byte g = Convert.ToByte(s.Substring(2, 2), 16);
            byte b = Convert.ToByte(s.Substring(4, 2), 16);
            color = Color.FromArgb(255, r, g, b);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ToHexColor(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Blends <paramref name="c"/> toward <paramref name="target"/> by
    /// <paramref name="amount"/> (0-1) — the hover/pressed shades <see cref="ApplyAccentColorOverride"/>
    /// derives from one stored colour.</summary>
    private static Color Blend(Color c, Color target, double amount)
    {
        byte L(byte a, byte b) => (byte)Math.Clamp(a + (b - a) * amount, 0, 255);
        return Color.FromArgb(255, L(c.R, target.R), L(c.G, target.G), L(c.B, target.B));
    }

    private void AccentColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_loading) return;
        _manager.Config.AccentColorHex = ToHexColor(args.NewColor);
        _manager.Save();
        ApplyAccentColorOverride();
        AccentColorSwatch.Background = new SolidColorBrush(args.NewColor);
    }

    private void ResetAccentColor_Click(object sender, RoutedEventArgs e)
    {
        _manager.Config.AccentColorHex = null;
        _manager.Save();
        ApplyAccentColorOverride();
        SyncAccentControls();
        AccentColorSwatch.Flyout?.Hide();
    }

    private void ApplyChromeTheme()
    {
        bool dark = ThemeHelper.IsDark(Root);
        WindowChrome.SetTitleBarTheme(_appWindow, dark);
        int rim = _manager.Config.Theme == AppTheme.Black
            ? SettingsRimColorBlack
            : dark ? SettingsRimColorDark : SettingsRimColorLight;
        WindowChrome.HideWindowBorder(_hwnd, dark, rim);
    }

    /// <summary>
    /// Rim colors for Settings, as COLORREFs (0x00BBGGRR) — these must track
    /// <c>SettingsSurfaceColor</c>/<c>SwiftXCanvasBrush</c> in <c>Themes/Colors.xaml</c> (now the
    /// Logi Options+ canvas values, #FFFFFF and #222222), not the shared black/white every other
    /// window's rim still uses. Leave one stale after a palette change and the window grows a
    /// visible hairline where the rim stops matching the surface behind it. Both are neutral greys,
    /// so channel order is moot; the cool-tinted card/pane colors never reach the rim.
    /// SettingsRimColorBlack matches ApplyBlackOverrides' own flat black rather than Dark's canvas.
    /// </summary>
    private const int SettingsRimColorDark = 0x00222222;
    private const int SettingsRimColorLight = 0x00FFFFFF;
    private const int SettingsRimColorBlack = 0x00000000;

    private Style SecondaryCaptionStyle => (Style)Root.Resources["SecondaryCaption"];

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "home";
        HomePanel.Visibility = tag == "home" ? Visibility.Visible : Visibility.Collapsed;
        GeneralPanel.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        AppsPanel.Visibility = tag == "apps" ? Visibility.Visible : Visibility.Collapsed;
        ToolsPanel.Visibility = tag == "tools" ? Visibility.Visible : Visibility.Collapsed;
        PrivacyPanel.Visibility = tag == "privacy" ? Visibility.Visible : Visibility.Collapsed;
        FeedbackPanel.Visibility = tag == "feedback" ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "home") RenderPreview();
        if (tag == "apps") RebuildItemsList();
        if (tag == "tools") RebuildToolsList();
    }

    private void RenderPreview()
    {
        if (PreviewCanvas is null) return;
        PreviewCanvas.Children.Clear();
        const double size = 360;
        // Nudged up from dead center: the canvas now carries the Add item / Reset defaults row
        // along its own bottom edge, so the ring sits a little high of true center to keep clear
        // breathing room under it instead of crowding those buttons.
        const double verticalNudge = 24;
        double cx = size / 2, cy = size / 2 - verticalNudge;

        if (_previewQuadrant && _manager.Config.HotCornerEnabled)
        {
            // The ring's own slots are not on screen, so drop them: a press must not reach a
            // bubble that is no longer drawn. The two ring-wide buttons go with them — they act on
            // the ring, and a bottom corner's fan would run straight through where they sit.
            Array.Clear(_slotBorders);
            CanvasActions.Visibility = Visibility.Collapsed;
            RenderQuadrantPreview(size);
            AddHotCornerButton(size);
            return;
        }

        CanvasActions.Visibility = Visibility.Visible;

        // Scale the live ring geometry into the preview canvas rather than drawing one fixed
        // picture of it, so Ring size / Item size / Ring radius visibly change what this shows.
        double scale = size * 0.74 / (2 * (RingMetrics.Radius + RingMetrics.Bubble / 2));
        double radius = RingMetrics.Radius * scale;
        double bubble = RingMetrics.Bubble * scale;
        double glyphSize = Math.Max(10, RingMetrics.Glyph * scale);

        _previewCenter = new Point(cx, cy);
        _previewRadius = radius;
        _previewBubble = bubble;
        Array.Clear(_slotBorders);

        // Resolve against this window's theme — not Application.Current.Resources.
        var borderBrush = ThemeHelper.Brush(Root, "SwiftXBorderBrush");
        var muted = ThemeHelper.Brush(Root, "SwiftXMutedBrush");
        var foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush");

        // Match the ring: the center is the app mark alone, painted on nothing — floating form and
        // all, so the preview shows the same shadow the real hub has. A filled plate here read as a
        // big empty hole that does not exist on the real thing.
        double logo = RingMetrics.HubLogo * scale;
        var hub = AppLogo.CreateFloating(logo);
        Canvas.SetLeft(hub, cx - logo / 2);
        Canvas.SetTop(hub, cy - logo / 2);
        PreviewCanvas.Children.Add(hub);

        var items = _manager.Config.Primary.Items.Where(i => !i.Hidden).Take(RingMetrics.UserSlotCount).ToList();

        // A full ring has no empty slot left to add into, so the button that would open the add
        // dialog goes away rather than sitting there offering something the ring cannot take.
        // Reset defaults stays: it is still meaningful on a full ring.
        AddItemButton.Visibility = items.Count >= RingMetrics.UserSlotCount
            ? Visibility.Collapsed
            : Visibility.Visible;

        for (int i = 0; i < RingMetrics.SlotCount; i++)
        {
            double angle = (-90 + i * 45) * Math.PI / 180.0;
            double x = cx + radius * Math.Cos(angle) - bubble / 2;
            double y = cy + radius * Math.Sin(angle) - bubble / 2;

            // The ring synthesizes Settings and Close into the last two slots, so the preview does
            // the same rather than reading them out of the profile — they are not in it.
            bool pinned = i >= RingMetrics.UserSlotCount;
            RingItem? item = pinned
                ? (i == RingMetrics.UserSlotCount ? SystemRingItems.CreateSettings() : SystemRingItems.CreateClose())
                : i < items.Count ? items[i] : null;

            var border = new Border
            {
                Width = bubble,
                Height = bubble,
                CornerRadius = new CornerRadius(bubble / 2),
                Background = ThemeHelper.Brush(Root, _manager.Config.Theme == AppTheme.Black, "RingBubbleGlassTint"),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Tag = item,
            };

            if (pinned)
            {
                border.Child = PreviewGlyph(item!.Glyph, glyphSize, muted);
                ToolTipService.SetToolTip(border, $"{item.DisplayName} — always on the ring");
            }
            else if (item is null)
            {
                border.Child = PreviewGlyph("\uE710", glyphSize, muted);
            }
            else
            {
                border.Child = PreviewGlyph(item.Glyph, glyphSize, foreground);
                _slotBorders[i] = border;
            }

            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);
            PreviewCanvas.Children.Add(border);
        }

        AddHotCornerButton(size);
    }

    /// <summary>
    /// The quarter circle a hot corner set to <see cref="HotCornerLayout.Quadrant"/> summons,
    /// drawn to the same rules the real one uses (<see cref="QuadrantLayout"/>) and scaled into
    /// the canvas — centered on the canvas corner that matches the chosen screen corner, so the
    /// picture says which corner as much as what.
    /// <para>
    /// Editable, like the ring view beside it: one more place than the corner holds is drawn (up
    /// to the four it can hold) and offers a +, each filled place carries a remove badge, and any
    /// of them can be dragged onto another to reorder. The first such edit turns the corner's
    /// default pick into an arrangement of the user's own — see <see cref="HotCornerItems"/>.
    /// </para>
    /// </summary>
    private void RenderQuadrantPreview(double size)
    {
        var corner = _manager.Config.HotCorner;
        var items = HotCornerItems.Resolve(_manager.Config, SystemRingItems.CreateSettings());

        double bubble = RingMetrics.Bubble;
        // The places the corner will actually use, plus one empty place to add into while there is
        // room for it. Laying out for what is really there is what keeps this a picture of the
        // corner rather than an approximation of it — a corner holding all four is drawn exactly
        // as the corner draws it.
        int slots = Math.Min(QuadrantLayout.ItemCount, items.Count + 1);
        double hoverBubble = bubble * RingMetrics.HoverScale;
        double radius = QuadrantLayout.Radius(slots, hoverBubble, RingMetrics.Radius);

        // Same scale the ring preview uses, so switching between the two views changes the shape
        // and not the size of everything in it — only clamped smaller if a large ring radius
        // would push the fan off the canvas.
        double scale = Math.Min(
            size * 0.74 / (2 * (RingMetrics.Radius + bubble / 2)),
            size * 0.96 / (radius + bubble / 2));
        double previewRadius = radius * scale;
        double previewBubble = bubble * scale;
        double glyphSize = Math.Max(10, RingMetrics.Glyph * scale);

        var (cx, cy) = QuadrantLayout.Center(corner, size, size);
        _quadrantCenter = new Point(cx, cy);
        _quadrantRadius = previewRadius;
        _quadrantBubble = previewBubble;
        _quadrantSlotCount = slots;
        Array.Clear(_quadrantSlotBorders);

        var borderBrush = ThemeHelper.Brush(Root, "SwiftXBorderBrush");
        var muted = ThemeHelper.Brush(Root, "SwiftXMutedBrush");
        var foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush");

        // The arc the items sit on, so the shape reads as a quarter of the ring rather than four
        // loose bubbles that happen to be curved. One place has no arc to speak of, and an arc
        // whose two ends are the same point is ambiguous to draw, so it goes without.
        if (slots > 1)
        {
            PreviewCanvas.Children.Add(BuildQuadrantArc(
                corner, cx, cy, previewRadius, slots, hoverBubble, radius, muted));
        }

        var badges = new List<UIElement>();
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
                Background = ThemeHelper.Brush(Root, _manager.Config.Theme == AppTheme.Black, "RingBubbleGlassTint"),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Tag = item,
                Child = PreviewGlyph(
                    item?.Glyph ?? "\uE710", glyphSize, item is null || item.IsSystem ? muted : foreground),
            };
            ToolTipService.SetToolTip(border, item?.DisplayName ?? "Add to this corner");
            Canvas.SetLeft(border, left);
            Canvas.SetTop(border, top);
            PreviewCanvas.Children.Add(border);
            _quadrantSlotBorders[i] = border;

            // The last item stays: a corner with nothing on it would summon an empty screen.
            if (item is not null && items.Count > 1)
                badges.Add(BuildQuadrantRemoveBadge(i, item, left, top, previewBubble));
        }

        // Badges go on last so a bubble drawn after its neighbour can't cover one.
        foreach (var badge in badges)
            PreviewCanvas.Children.Add(badge);
    }

    /// <summary>The small × that takes one item back off the corner.</summary>
    private Button BuildQuadrantRemoveBadge(int slot, RingItem item, double left, double top, double bubble)
    {
        const double badge = 18;
        var button = new Button
        {
            Width = badge,
            Height = badge,
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            CornerRadius = new CornerRadius(badge / 2),
            Content = new FontIcon { Glyph = char.ConvertFromUtf32(0xE711), FontSize = 9 },
        };
        ToolTipService.SetToolTip(button, $"Remove {item.DisplayName} from this corner");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            button, $"Remove {item.DisplayName} from this corner");
        button.Click += (_, _) => RemoveQuadrantItem(slot);

        // Riding the bubble's upper-right shoulder, half on and half off it.
        Canvas.SetLeft(button, left + bubble - badge * 0.75);
        Canvas.SetTop(button, top - badge * 0.25);
        return button;
    }

    /// <summary>The thin guide arc the quadrant's items are strung along.</summary>
    private static Microsoft.UI.Xaml.Shapes.Path BuildQuadrantArc(
        HotCorner corner, double cx, double cy, double previewRadius,
        int count, double hoverBubble, double radius, Brush stroke)
    {
        double firstLocal = QuadrantLayout.LocalAngleRad(0, count, hoverBubble, radius);
        double lastLocal = QuadrantLayout.LocalAngleRad(count - 1, count, hoverBubble, radius);
        double first = QuadrantLayout.ScreenAngleRad(corner, firstLocal);
        double last = QuadrantLayout.ScreenAngleRad(corner, lastLocal);

        var start = new Point(cx + previewRadius * Math.Cos(first), cy + previewRadius * Math.Sin(first));
        var end = new Point(cx + previewRadius * Math.Cos(last), cy + previewRadius * Math.Sin(last));

        // Which way round the arc is drawn flips with the corner, because two of the four mirror
        // the sweep; comparing the canvas angles says which without special-casing each corner.
        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(previewRadius, previewRadius),
            SweepDirection = last > first ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
            IsLargeArc = false,
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry,
            Stroke = stroke,
            StrokeThickness = 1,
            Opacity = 0.5,
            IsHitTestVisible = false,
        };
    }

    /// <summary>
    /// The corner button: present on the canvas whenever the hot corner is on, sitting in the
    /// canvas corner that matches the chosen screen one, and switching the canvas between the
    /// ring and what that corner would actually show.
    /// <para>
    /// The icon is the same corner-pointing triangle as the Hot corner row above (see
    /// <see cref="CornerTriangleGeometry"/>/<see cref="CornerTriangleAngle"/>), always rotated to
    /// the chosen corner regardless of which of the two states below the button is currently in —
    /// this button sits physically in that corner of the canvas, so pointing at it is what marks
    /// the button as "this corner" rather than any other one on the canvas. The two states (ring
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
        // it read as one more bubble sitting in the ring rather than as the canvas's own corner.
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
            ? "Back to the full ring"
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
        var point = e.GetCurrentPoint(PreviewCanvas).Position;
        _dragSlot = _previewQuadrant ? QuadrantSlotAt(point) : PreviewSlotAt(point);
        _dragBorder = _dragSlot >= 0
            ? (_previewQuadrant ? _quadrantSlotBorders[_dragSlot] : _slotBorders[_dragSlot])
            : null;
        _dragItem = _dragBorder?.Tag as RingItem;
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
            _dragMoved = true;
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
    /// which has the side effect of turning a corner that was still following the ring into a
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

            var list = HotCornerItems.Resolve(_manager.Config, SystemRingItems.CreateSettings());
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
        if (targetSlot < 0 || targetSlot >= RingMetrics.UserSlotCount) return;

        var visible = _manager.Config.Primary.Items.Where(i => !i.Hidden).Take(RingMetrics.UserSlotCount).ToList();
        int vFrom = visible.IndexOf(_dragItem);
        if (vFrom < 0) return;
        int vTo = Math.Min(targetSlot, visible.Count - 1);
        if (vTo != vFrom)
        {
            visible.RemoveAt(vFrom);
            visible.Insert(vTo, _dragItem);
        }
        ReflowRingBorders(visible);
    }

    /// <summary>
    /// Glides every bubble in <paramref name="tentativeOrder"/> other than the one actually being
    /// dragged (that one follows the pointer via <c>RenderTransform</c> instead) to the ring
    /// position its index in that order implies. Pinned slots (Settings/Close) are never part of
    /// <paramref name="tentativeOrder"/> to begin with — it is built the same way
    /// <see cref="ReorderItem"/>'s own <c>visible</c> list is — so they can never be moved here.
    /// </summary>
    private void ReflowRingBorders(List<RingItem> tentativeOrder)
    {
        for (int i = 0; i < tentativeOrder.Count; i++)
        {
            var item = tentativeOrder[i];
            if (ReferenceEquals(item, _dragItem)) continue;
            var border = FindSlotBorder(_slotBorders, item);
            if (border is null) continue;
            double angle = RingMetrics.SlotAngleRad(i);
            double x = _previewCenter.X + _previewRadius * Math.Cos(angle) - _previewBubble / 2;
            double y = _previewCenter.Y + _previewRadius * Math.Sin(angle) - _previewBubble / 2;
            AnimateBubbleTo(border, x, y);
        }
    }

    /// <summary>Quadrant counterpart of <see cref="ReflowRingBorders"/>.</summary>
    private void ReflowQuadrantBorders(List<RingItem> tentativeOrder)
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

    private static Border? FindSlotBorder(Border?[] borders, RingItem item)
    {
        foreach (var border in borders)
        {
            if (border?.Tag is RingItem tagged && ReferenceEquals(tagged, item))
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
        else if (slot < RingMetrics.UserSlotCount)
        {
            _manager.OpenAddItem(added =>
            {
                _manager.Config.Primary.Items.Add(added);
                _manager.NotifyItemsChanged();
                RenderPreview();
            }, _manager.Config.Primary.Items);
        }
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
    /// the ring doesn't pick one up.
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
        var list = HotCornerItems.Edit(_manager.Config, SystemRingItems.CreateSettings());
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

    /// <summary>Takes one item off the corner. The ring itself keeps it.</summary>
    private void RemoveQuadrantItem(int slot)
    {
        var list = HotCornerItems.Edit(_manager.Config, SystemRingItems.CreateSettings());
        if (slot < 0 || slot >= list.Count || list.Count <= 1)
            return;

        list.RemoveAt(slot);
        _manager.Save();
        HideInfo();
        RenderPreview();
    }

    /// <summary>
    /// Fills an empty place on the corner. The item belongs to the corner rather than to the ring
    /// — this is the corner's own arrangement — so it goes into that list and nowhere else.
    /// </summary>
    private void AddQuadrantItem()
    {
        var settings = SystemRingItems.CreateSettings();
        if (HotCornerItems.Resolve(_manager.Config, settings).Count >= QuadrantLayout.ItemCount)
            return;

        _manager.OpenAddItem(added =>
        {
            var list = HotCornerItems.Edit(_manager.Config, SystemRingItems.CreateSettings());
            if (list.Count >= QuadrantLayout.ItemCount)
                return;
            list.Add(added);
            _manager.Save();
            RenderPreview();
        }, _manager.Config.HotCornerRing);
    }

    private int PreviewSlotAt(Point point, bool requireHit = true)
    {
        double dx = point.X - _previewCenter.X;
        double dy = point.Y - _previewCenter.Y;
        int slot = RingMetrics.NearestSlotIndex(dx, dy);
        if (!requireHit)
            return slot;

        double angle = RingMetrics.SlotAngleRad(slot);
        double sx = _previewRadius * Math.Cos(angle);
        double sy = _previewRadius * Math.Sin(angle);
        double gap = Math.Sqrt((dx - sx) * (dx - sx) + (dy - sy) * (dy - sy));
        return gap <= _previewBubble / 2 + 4 ? slot : -1;
    }

    /// <summary>
    /// A preview slot's glyph. Mirrors the ring's own handling of non-Fluent glyphs such as
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

    private void ReorderItem(RingItem dragged, int targetSlot)
    {
        // Slots past the user range hold the pinned Settings/Close bubbles; a drop there is
        // ignored rather than clamped, so the item stays where the user last put it.
        if (targetSlot < 0 || targetSlot >= RingMetrics.UserSlotCount) return;

        var visible = _manager.Config.Primary.Items.Where(i => !i.Hidden).Take(RingMetrics.UserSlotCount).ToList();
        int from = visible.IndexOf(dragged);
        if (from < 0) return;

        // Clamp onto the last occupied slot: dropping past the end of a part-filled ring means
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
    /// dedicated button. This is display-only: real edits happen on the "Elements" tab.
    /// <para>
    /// A click on a plain main-ring item is unrelated to whatever group row is currently expanded
    /// (if any), so that row is collapsed first — otherwise it kept sitting there showing a group
    /// that has nothing to do with the newly-selected item. A click on a child <em>inside</em> the
    /// expanded group's own row is the one case that should leave the row open alongside the info
    /// box, since the row is exactly what the user clicked into.
    /// </para>
    /// </summary>
    private void ShowItemInfo(RingItem item)
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

    private bool IsChildOfExpandedGroup(RingItem item) =>
        _expandedGroup is not null && _expandedGroup.Children.Any(c => ReferenceEquals(c, item));

    /// <summary>
    /// Expands (or, on a second click of the same group, collapses) <c>GroupRow</c> — the
    /// horizontal children strip that replaces the old full-ring drill-down.
    /// <para>
    /// Switching to a different group closes whatever was open first (via <see cref="CollapseGroup"/>)
    /// rather than just overwriting <c>_expandedGroup</c> in place, and any info box left showing
    /// from before is cleared too — both would otherwise linger, showing state for an item or
    /// group the newly-opened row has nothing to do with.
    /// </para>
    /// </summary>
    private void ToggleGroup(RingItem group)
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
    /// helper) at a smaller size. Hidden children are skipped, same as the main ring.
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
                Background = ThemeHelper.Brush(Root, _manager.Config.Theme == AppTheme.Black, "RingBubbleGlassTint"),
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
    /// of the ring's circular one.
    /// </summary>
    private void GroupRow_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(GroupChildrenCanvas).Position;
        _groupDragIndex = GroupChildIndexAt(point);
        _groupDragBorder = _groupDragIndex >= 0 ? _groupSlotBorders[_groupDragIndex] : null;
        _groupDragItem = _groupDragBorder?.Tag as RingItem;
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
    /// position-preserving move <see cref="ReorderItem"/> does for the main ring so hidden
    /// siblings keep their place instead of being shuffled by an index from a filtered view.
    /// </summary>
    private void ReorderGroupChild(RingItem dragged, int targetIndex)
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

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        // Every item the selection/expansion state could be pointing at is about to be replaced.
        HideInfo();
        CollapseGroup();
        // The corner's own arrangement was built out of the items being replaced, so it goes back
        // to following the ring rather than holding four copies of the old one.
        HotCornerItems.Reset(_manager.Config);
        _manager.SeedDefaults(_manager.Config.Primary);
        _manager.NotifyItemsChanged();
        RenderPreview();
        RebuildItemsList();
    }

    private void SelectTheme(AppTheme theme)
    {
        foreach (ComboBoxItem item in ThemeBox.Items)
        {
            if ((item.Tag as string) == theme.ToString())
            {
                ThemeBox.SelectedItem = item;
                break;
            }
        }
    }

    /// <summary>
    /// Fills <c>LanguageBox</c> from <see cref="LanguageCatalog.Options"/> and selects whichever
    /// entry matches <see cref="AppConfig.Language"/>. Built in code-behind rather than as static
    /// ComboBoxItems in XAML (unlike ThemeBox/HoverStyleBox) because each entry's Content is that
    /// language's own native name — data, not markup.
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

    private void SelectHoverStyle(RingHoverStyle style)
    {
        foreach (ComboBoxItem item in HoverStyleBox.Items)
        {
            if ((item.Tag as string) == style.ToString())
            {
                HoverStyleBox.SelectedItem = item;
                break;
            }
        }
    }

    private void SelectItemSize(RingItemSize size)
    {
        foreach (ComboBoxItem item in ItemSizeBox.Items)
        {
            if ((item.Tag as string) == size.ToString())
            {
                ItemSizeBox.SelectedItem = item;
                break;
            }
        }
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is not ComboBoxItem item) return;
        if (Enum.TryParse<AppTheme>(item.Tag as string, out var theme))
        {
            _manager.Config.Theme = theme;
            _manager.Save();
            _manager.ApplyTheme();
            ApplyTheme();
            // Light and Dark carry different built-in teals; the swatch has to follow.
            SyncAccentControls();
        }
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedItem is not ComboBoxItem item) return;
        if (Enum.TryParse<AppLanguage>(item.Tag as string, out var language))
        {
            _manager.Config.Language = language;
            _manager.Save();
            _manager.ApplyLanguage();
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
    private const string TermsUrl = GitHubRepoUrl + "/blob/main/docs/TERMS.md";

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
    /// Moves to the Privacy page instead of opening anything — it is a page of this window, not a
    /// page on the web. Selecting the NavigationViewItem rather than flipping panel Visibility
    /// directly, so Nav_SelectionChanged stays the single place that knows which panel belongs to
    /// which tag, and the nav pane's own selection follows along the way it would if the user had
    /// clicked Privacy themselves.
    /// </summary>
    private void GoToPrivacyPage()
    {
        foreach (var item in Nav.FooterMenuItems)
        {
            if (item is NavigationViewItem nvi && (nvi.Tag as string) == "privacy")
            {
                Nav.SelectedItem = nvi;
                return;
            }
        }
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


    private void HoverStyleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || HoverStyleBox.SelectedItem is not ComboBoxItem item) return;
        if (Enum.TryParse<RingHoverStyle>(item.Tag as string, out var style))
        {
            _manager.Config.HoverStyle = style;
            _manager.Save();
        }
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        await StartupService.SetEnabledAsync(StartupToggle.IsOn);
        _manager.Config.LaunchAtStartup = StartupToggle.IsOn;
        _manager.Save();
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.SuggestedFileName = "ring";
        picker.FileTypeChoices.Add("JSON", [".json"]);
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
            RingStore.ExportTo(_manager.Config, file.Path);
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        var imported = RingStore.ImportFrom(file.Path);
        if (imported is null) return;
        // The whole Rings collection is being replaced, so nothing the selection/expansion state
        // could be pointing at survives.
        HideInfo();
        CollapseGroup();
        _manager.Config.CopyAppSettingsFrom(imported);
        _manager.Config.Rings.Clear();
        _manager.Config.Rings.AddRange(imported.Rings);
        _manager.Config.EnsurePrimary();
        _manager.NotifyItemsChanged();
        _manager.ApplyTheme();
        _manager.ApplyLanguage();
        _manager.ApplyHotCorner();
        ApplyTheme();
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
        // SeedDefaults below replaces every ring item, so drop anything the selection/expansion
        // state was pointing at first.
        HideInfo();
        CollapseGroup();
        _manager.Config.Theme = AppTheme.Dark;
        _manager.Config.HoverStyle = RingHoverStyle.Invert;
        _manager.Config.Hotkey = HotkeyGesture.Default.ToString();
        _manager.Config.HotkeyEnabled = true;
        _manager.Config.HotCornerEnabled = true;
        _manager.Config.HotCorner = HotCorner.TopLeft;
        _manager.Config.HotCornerLayout = HotCornerLayout.Ring;
        HotCornerItems.Reset(_manager.Config);
        _manager.Config.ShowDataTool = true;
        _manager.Config.ShowSecureValuesTool = true;
        _manager.Config.ShowBase64Tool = true;
        _manager.Config.ShowCertificateTool = true;
        _manager.Config.FeedbackRating = 0;
        _manager.Config.AccentColorHex = null;
        _manager.SeedDefaults(_manager.Config.Primary);
        // Through the manager, not a raw Config assignment: RingMetrics holds the geometry the
        // ring actually draws with.
        _manager.SetItemSize(RingItemSize.Medium);
        _manager.SetRadius(null);
        _manager.NotifyItemsChanged();
        _manager.ApplyTheme();
        _manager.ApplyHotkey();
        _manager.ApplyHotCorner();
        ApplyTheme();
        _loading = true;
        SelectTheme(AppTheme.Dark);
        SelectHoverStyle(RingHoverStyle.Invert);
        SelectItemSize(RingItemSize.Medium);
        RadiusSlider.Value = RingMetrics.DefaultRadius;
        HotkeyEnabledToggle.IsOn = true;
        HotkeyCapture.Gesture = HotkeyGesture.Default;
        HotCornerEnabledToggle.IsOn = true;
        ExperienceRating.Value = -1;
        UpdateRatingThanks();
        SyncAccentControls();
        SelectHotCorner(HotCorner.TopLeft);
        SelectHotCornerLayout(HotCornerLayout.Ring);
        SyncHotCornerControls();
        RebuildToolsList();
        _loading = false;
        RenderPreview();
        RebuildItemsList();
        UpdateHotkeyStatus();
    }

    private void ItemSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ItemSizeBox.SelectedItem is not ComboBoxItem item) return;
        if (Enum.TryParse<RingItemSize>(item.Tag as string, out var size))
            _manager.SetItemSize(size);
    }

    private void RadiusSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
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
    /// SettingsWindow.xaml is authored with its right angle at the top left, so each quarter turn
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
    /// XAML-declared PathIcon on the Home page's Hot corner row — and
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
    /// coordinates as the XAML-declared PathIcon's <c>Data</c> in SettingsWindow.xaml, so the two
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
        HotCornerLayoutRing.IsChecked = layout == HotCornerLayout.Ring;
        HotCornerLayoutQuadrant.IsChecked = layout == HotCornerLayout.Quadrant;
    }

    /// <summary>
    /// Brings the hot corner's second row in line with the switch above it — which corner, and how
    /// much of the ring it opens, mean nothing while the corner is off — and drops any quadrant
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
            : HotCornerLayout.Ring;
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
    /// Builds the "Elements" page: every top-level item and every top-level group in one flat
    /// two-column card grid, all cards the same width — a group's own card immediately followed by
    /// a card for each of its children, in ring order.
    /// <para>
    /// A group used to be skipped entirely here — only its children were listed, folded into the
    /// flat grid under the group's name — which meant a group with nothing in it yet (exactly what
    /// "Add item… › Group" produces) appeared nowhere at all and read as a broken add. A later
    /// design gave each group its own full-width section (a header plus a nested sub-grid of
    /// children), which fixed that but made a group's own card noticeably wider than every other
    /// element's. This is the version that keeps the first fix without the second problem: a
    /// group gets exactly one card — same shape and width as <see cref="BuildItemRow"/>, via
    /// <see cref="BuildGroupRow"/> — and its children are just more cards in the same shared grid
    /// right after it, distinguished from a top-level item only by the group's name already
    /// appearing in their own caption (<c>BuildItemRow</c>'s <c>ring</c> parameter).
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
            // Tools are managed from the Tools tab's own toggles, not listed here too.
            if (item.IsTool)
                continue;

            // Nor is the built-in Tools group: it is wholly owned by the dedicated Tools tab, so
            // showing it here too would duplicate that tab instead of listing something new.
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
                        Child = BuildItemRow(child, item.DisplayName),
                    });
            }
            else
            {
                cards.Add(new Border
                {
                    Style = (Style)Root.Resources["Card"],
                    Child = BuildItemRow(item, "Home"),
                });
            }
        }

        int rowCount = (cards.Count + 1) / 2;
        for (int r = 0; r < rowCount; r++)
            ItemsList.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < cards.Count; i++)
        {
            Grid.SetRow(cards[i], i / 2);
            Grid.SetColumn(cards[i], i % 2);
            ItemsList.Children.Add(cards[i]);
        }
    }

    /// <summary>
    /// A top-level group's own row: the same shape <see cref="BuildItemRow"/> uses — icon, name,
    /// a caption, a show/hide switch — so a group's card is never wider than an ungrouped item's.
    /// Two icon buttons stand in for <c>BuildItemRow</c>'s one, an "Add item…" scoped to this
    /// group's own children alongside the same delete every row carries — icon-only rather than a
    /// labeled button, since a labeled "Add item…" button is what used to force this row onto its
    /// own full-width section; a delete-sized icon button fits the same half-width card everything
    /// else uses.
    /// </summary>
    private Grid BuildGroupRow(RingItem group)
    {
        var children = group.Children.Where(c => !c.IsTool).ToList();

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = BuildItemIcon(group);
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock
        {
            Text = group.DisplayName,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        labels.Children.Add(new TextBlock
        {
            Text = children.Count switch
            {
                0 => "No items yet",
                1 => "Group · 1 item",
                _ => $"Group · {children.Count} items",
            },
            Style = SecondaryCaptionStyle,
        });
        Grid.SetColumn(labels, 1);
        row.Children.Add(labels);

        // Same show/hide switch every other element's row carries — a group is hidden from the
        // ring the same way a plain item is, so it needs the same control, not just its children's.
        var toggle = new ToggleSwitch
        {
            IsOn = !group.Hidden,
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggle.Toggled += (_, _) =>
        {
            group.Hidden = !toggle.IsOn;
            _manager.NotifyItemsChanged();
            RenderPreview();
        };
        Grid.SetColumn(toggle, 2);
        row.Children.Add(toggle);

        // Into this group's own children, not the top-level ring — and with the Group tile taken
        // away, since the ring has no third ring to expand a group inside a group into.
        var add = BuildRowIconButton(0xE710, "Add item…", "Add item to " + group.DisplayName);
        add.Click += (_, _) => _manager.OpenAddItem(added =>
        {
            group.Children.Add(added);
            _manager.NotifyItemsChanged();
            RenderPreview();
            RebuildItemsList();
        }, group.Children, isGenericAddItem: true, allowGroups: false);
        Grid.SetColumn(add, 3);
        row.Children.Add(add);

        var remove = BuildRowIconButton(0xE74D, "Delete", "Delete " + group.DisplayName);
        remove.Click += (_, _) =>
        {
            // The whole group goes, children and all — they only exist inside it.
            _manager.Config.Primary.Items.Remove(group);
            _manager.NotifyItemsChanged();
            RebuildItemsList();
            RenderPreview();
        };
        Grid.SetColumn(remove, 4);
        row.Children.Add(remove);

        return row;
    }

    /// <summary>
    /// One item's row — icon, name, where it lives and what kind it is, a show/hide switch, and a
    /// delete. Shared by the top-level cards and by a group section's children, so both look and
    /// behave the same.
    /// </summary>
    private Grid BuildItemRow(RingItem item, string ring)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = BuildItemIcon(item);
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock
        {
            Text = item.DisplayName,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        labels.Children.Add(new TextBlock
        {
            Text = $"{ring} · {item.Kind}",
            Style = SecondaryCaptionStyle,
        });
        Grid.SetColumn(labels, 1);
        row.Children.Add(labels);

        var toggle = new ToggleSwitch
        {
            IsOn = !item.Hidden,
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggle.Toggled += (_, _) =>
        {
            item.Hidden = !toggle.IsOn;
            _manager.NotifyItemsChanged();
            RenderPreview();
        };
        Grid.SetColumn(toggle, 2);
        row.Children.Add(toggle);

        var remove = BuildRowIconButton(0xE74D, "Delete", "Delete " + item.DisplayName);
        remove.Click += (_, _) =>
        {
            // Top level and inside a group both, since the same row builds for either.
            _manager.Config.Primary.Items.Remove(item);
            foreach (var group in _manager.Config.Primary.Items.Where(i => i.IsGroup))
                group.Children.Remove(item);
            _manager.NotifyItemsChanged();
            RebuildItemsList();
            RenderPreview();
        };
        Grid.SetColumn(remove, 3);
        row.Children.Add(remove);

        return row;
    }

    /// <summary>The borderless icon button an item row and a group row both carry — trash for
    /// delete, "+" for a group's own scoped add.</summary>
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
    /// The small plate at the head of an item row: the item's own glyph to begin with, replaced by
    /// its real shell / folder / favicon icon if <see cref="IconService"/> resolves one. The same
    /// image-over-glyph pair the ring itself draws, just built imperatively here rather than bound.
    /// A group resolves nothing (it has no target), so it simply keeps its group glyph.
    /// </summary>
    private FrameworkElement BuildItemIcon(RingItem item)
    {
        const double plate = 28;

        var glyph = new FontIcon
        {
            Glyph = item.Glyph,
            FontSize = 15,
            Foreground = ThemeHelper.Brush(Root, "SwiftXForegroundBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Same non-Fluent-glyph handling the ring and the preview do — "B64" and friends are tofu
        // boxes in the icon font.
        if (GlyphFonts.IsTextGlyph(item.Glyph))
            glyph.FontFamily = new FontFamily("Segoe UI");

        var image = new Image
        {
            Width = plate - 6,
            Height = plate - 6,
            Stretch = Stretch.Uniform,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var content = new Grid();
        content.Children.Add(glyph);
        content.Children.Add(image);

        LoadRowIcon(item, image, glyph);

        // Transparent, not a plate of its own: this row already sits inside a Card (background
        // SwiftXCardBackgroundBrush), so the card's own fill shows straight through and the glyph
        // just reads as part of the row. It used to paint RingBubbleBackground here — the ring
        // dock's own opaque bubble color (solid black in Dark) — which was a copy/paste of the
        // preview bubble styling and had nothing to do with this list; sitting on top of the
        // translucent acrylic card it read as a stray solid-black square on every row.
        return new Border
        {
            Width = plate,
            Height = plate,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
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
    private static async void LoadRowIcon(RingItem item, Image image, FontIcon glyph)
    {
        var source = await IconService.LoadIconAsync(item);
        if (source is null) return;
        image.Source = source;
        image.Visibility = Visibility.Visible;
        glyph.Visibility = Visibility.Collapsed;
    }

    private sealed record ToolRow(
        string Target, ToolKind Kind, string Title, string Description, string Glyph, bool TextGlyph,
        Func<AppConfig, bool> GetShown, Action<AppConfig, bool> SetShown);

    // Built from the codepoint, not a literal character in source, so the glyph can't be
    // silently swapped for a different (or invisible) private-use character by a stray
    // copy/paste. See the memory note on this exact failure mode.
    private static readonly string DataGlyph = char.ConvertFromUtf32(0xE943);
    private static readonly string SecureValuesGlyph = char.ConvertFromUtf32(0xE72E);
    private static readonly string CertificateGlyph = char.ConvertFromUtf32(0xEB95);

    private static readonly ToolRow[] ToolRows =
    [
        new("Data", ToolKind.Data, "Data formatter", "Auto-detect JSON or XML and format it.", DataGlyph, false,
            c => c.ShowDataTool, (c, v) => c.ShowDataTool = v),
        new("SecureValues", ToolKind.SecureValues, "Secure values", "Local key/secret store with masked values.", SecureValuesGlyph, false,
            c => c.ShowSecureValuesTool, (c, v) => c.ShowSecureValuesTool = v),
        new("Base64", ToolKind.Base64, "Base64", "Encode and decode Base64 text.", "B64", true,
            c => c.ShowBase64Tool, (c, v) => c.ShowBase64Tool = v),
        new("Certificate", ToolKind.Certificate, "Certificate", "Inspect certificate, PKCS#12, and Java KeyStore files.", CertificateGlyph, false,
            c => c.ShowCertificateTool, (c, v) => c.ShowCertificateTool = v),
    ];

    // Bound as ToolsList's ItemsSource: WinUI's CanReorderItems drag pipeline mutates whichever
    // IList backs the list, so this is what actually moves when the user drags a card — observing
    // it (see ToolCards_CollectionChanged, wired in the constructor) is how a drag becomes a ring
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

    private Border BuildToolCard(ToolRow row)
    {
        // The drag affordance — grabbing anywhere on the card works (this is WinUI's own
        // CanReorderItems drag, not a hand-rolled one), but a dedicated grip at the leading edge
        // is what tells the user this row moves at all, replacing the old per-row up/down
        // buttons. Six dots rather than a font glyph: no Segoe Fluent Icons codepoint draws that
        // pattern, and hand-placed dots can't silently turn into a different (or invisible)
        // private-use character the way a glyph literal could (see the memory note on that
        // failure mode).
        var grip = BuildDragGrip(row.Title, ThemeHelper.Brush(Root, "SwiftXForegroundBrush"));
        Grid.SetColumn(grip, 0);

        FrameworkElement icon = row.TextGlyph
            ? new TextBlock
            {
                Text = row.Glyph,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                Width = 28,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
            : new FontIcon
            {
                Glyph = row.Glyph,
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
            };
        Grid.SetColumn(icon, 1);

        var labels = new StackPanel();
        labels.Children.Add(new TextBlock
        {
            Text = row.Title,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        labels.Children.Add(new TextBlock
        {
            Text = row.Description,
            Style = SecondaryCaptionStyle,
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(labels, 2);

        var openButton = new Button
        {
            Content = new FontIcon { Glyph = char.ConvertFromUtf32(0xE8A7), FontSize = 14 },
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(openButton, "Open");
        var kind = row.Kind;
        openButton.Click += (_, _) => _manager.OpenTool(kind);
        Grid.SetColumn(openButton, 3);

        var toggle = new ToggleSwitch
        {
            IsOn = row.GetShown(_manager.Config),
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        toggle.Toggled += (_, _) =>
        {
            if (_loading) return;
            row.SetShown(_manager.Config, toggle.IsOn);
            ApplyToolVisibility();
            _manager.NotifyItemsChanged();
            RenderPreview();
        };
        Grid.SetColumn(toggle, 4);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(grip);
        grid.Children.Add(icon);
        grid.Children.Add(labels);
        grid.Children.Add(openButton);
        grid.Children.Add(toggle);

        return new Border { Style = (Style)Root.Resources["Card"], Child = grid, Tag = row.Target };
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
    /// other item in the list keeps its position. A tool kind can have more than one ring instance
    /// at once (the default seed puts Data both inside "Dev tools" and at the top level, see
    /// <see cref="SeedDefaults"/>), so this walks the whole tree rather than assuming one.
    /// </summary>
    private void ApplyToolOrder(List<string> orderedTargets)
    {
        void Walk(List<RingItem> list)
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
