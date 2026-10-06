using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SwiftX.Controls;
using SwiftX.Interop;
using SwiftX.Models;
using SwiftX.Services;
using Windows.UI;
using WinRT.Interop;

namespace SwiftX;

/// <summary>
/// The first-run guide. Three steps: a welcome, a picker for what goes on the orbit, and the keys
/// and corner that summon it.
/// <para>
/// It exists because SwiftX's first run used to be silent — the orbit was seeded with six items
/// nobody chose and a hotkey nobody was told about, on an app that puts nothing on the taskbar. The
/// guide asks the two questions that decide whether the app is useful at all, and does it before
/// the user has to go looking for a Settings screen.
/// </para>
/// <para>
/// Nothing is written until <b>Finish</b>. Dismissing the window at any point leaves the config
/// exactly as <see cref="OrbitManager.Start"/> left it — which is the ordinary seeded default, not
/// an empty orbit (see <see cref="OrbitManager.Start"/>'s own comment on why the seed runs first
/// and the guide only ever replaces it).
/// </para>
/// <para>
/// Styled from the same <c>Shell*</c> tokens as <see cref="ShellWindow"/>, so the first thing a new
/// user sees is the app they are about to use rather than a separate installer-looking dialog.
/// </para>
/// </summary>
public sealed partial class OnboardingWindow : Window
{
    private readonly OrbitManager _manager;
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private bool _loading = true;

    /// <summary>
    /// What the picker is offering, in display order — the machine-checked catalog plus anything
    /// the user added themselves through "Add your own…", appended as they add it.
    /// </summary>
    private readonly List<OnboardingSuggestion> _suggestions = new();

    /// <summary>Keys of the ticked tiles. A set rather than a flag on the suggestion so a rebuild
    /// of the grid (which happens on every tick, to re-evaluate the slot cap) cannot lose it.</summary>
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

    /// <summary>
    /// The gesture the user has chosen, or null until they choose one. Held here rather than
    /// written straight to the config because nothing this window collects is persisted before
    /// Finish.
    /// </summary>
    private HotkeyGesture? _gesture;
    private HotCorner _hotCorner = HotCorner.TopLeft;
    private bool _hotCornerEnabled = true;
    private bool _hotkeyEnabled = true;

    private int _step;
    private const int StepCount = 3;

    /// <summary>
    /// Raised once the window has closed, with <see langword="true"/> if the user finished the
    /// guide and <see langword="false"/> if they dismissed it. <see cref="OrbitManager"/> opens the
    /// app window either way — someone who just launched SwiftX should end up looking at it — but
    /// the desktop-shortcut prompt is skipped after a finish, because the guide already asked.
    /// </summary>
    public event Action<bool>? Completed;

    private bool _finished;

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args) => ApplyChromeTheme();

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnWindowClosed;
        Activated -= OnWindowActivated;
        Completed?.Invoke(_finished);
    }

    public OnboardingWindow(OrbitManager manager)
    {
        _manager = manager;
        InitializeComponent();

        // Left to ApplyBackdrop (via ApplyTheme below), which picks Mica or acrylic by theme.

        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));
        Title = "Set up SwiftX";

        var titleBar = AppTitleBar.WithAppMark();
        Grid.SetRow(titleBar, 0);
        Root.Children.Insert(0, titleBar);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);

        WelcomeMark.Content = AppLogo.Create(56);

        if (_appWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsMaximizable = false;
        _appWindow.IsShownInSwitchers = true;

        WindowChrome.SetClientSizeDip(_appWindow, _hwnd, 880, 700);
        WindowChrome.SetRoundedCorners(_hwnd);
        WindowChrome.CenterOnCursor(_appWindow, _appWindow.Id);

        ApplyTheme();
        Root.ActualThemeChanged += (_, _) =>
        {
            ApplySurfaceOverrides();
            ApplyAccentColorOverride();
            ApplyChromeTheme();
            UpdateStepDots();
        };
        // Named, and removed again in OnWindowClosed: a Window that leaves a handler of its own
        // method on its own Activated or Closed is never garbage collected — see
        // ShellWindow.OnWindowClosed for the measurement.
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;

        Root.Loaded += async (_, _) =>
        {
            _loading = true;

            foreach (var suggestion in OnboardingCatalog.Available())
            {
                _suggestions.Add(suggestion);
                if (suggestion.DefaultOn)
                    _selected.Add(suggestion.Key);
            }

            _gesture = HotkeyGesture.TryParse(_manager.Config.Hotkey, out var parsed)
                ? parsed
                : HotkeyGesture.Default;
            HotkeyCapture.Gesture = _gesture;
            HotkeyCapture.Assigned += OnHotkeyAssigned;
            HotkeyCapture.NeedsModifier += () =>
                HotkeyStatus.Text = "Hold Ctrl, Alt, or Win with that key.";

            _hotkeyEnabled = _manager.Config.HotkeyEnabled;
            _hotCornerEnabled = _manager.Config.HotCornerEnabled;
            _hotCorner = _manager.Config.HotCorner;
            HotkeyEnabledToggle.IsOn = _hotkeyEnabled;
            HotCornerEnabledToggle.IsOn = _hotCornerEnabled;
            SelectHotCorner(_hotCorner);
            SyncHotCornerControls();

            StartupToggle.IsOn = await StartupService.IsEnabledAsync();
            DesktopShortcutToggle.IsOn = DesktopShortcutService.Exists();

            _loading = false;
            RebuildSuggestions();
            ShowStep(0);
        };
    }

    // ===================== theme =====================
    // The same three-part pass ShellWindow runs, for the same reasons — see its own doc comments.
    // Kept here rather than shared because the two windows override different key sets: this one
    // has no orbit canvas, no nav rail and no theme cards.

    public void ApplyTheme()
    {
        ApplyBackdrop();
        ApplySurfaceOverrides();
        ApplyAccentColorOverride();
        ForceThemeResourceRefresh(ThemeHelper.Resolve(_manager.Config.Theme));
        ApplyChromeTheme();
        UpdateStepDots();
    }

    /// <summary>
    /// Assigns <see cref="Root"/>'s RequestedTheme, forcing every <c>{ThemeResource}</c> under it to
    /// re-resolve even when it is already at <paramref name="target"/> — Midnight rides on Dark's
    /// own ElementTheme and is told apart only by the resource override below, so without the nudge
    /// the override is invisible to bindings that already resolved. Same trick, same reason, as
    /// <see cref="ShellWindow"/>'s.
    /// </summary>
    private void ForceThemeResourceRefresh(ElementTheme target)
    {
        if (Root.RequestedTheme == target)
            Root.RequestedTheme = target == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        Root.RequestedTheme = target;
    }

    private static readonly (string Key, byte A, byte R, byte G, byte B)[] BlackOverrides =
    {
        ("ShellBackgroundBrush", 0xE6, 0x00, 0x00, 0x00),
        ("ShellCardBackgroundBrush", 0xE6, 0x12, 0x12, 0x12),
        ("ShellPillBrush", 0xE6, 0x1A, 0x1A, 0x1A),
        ("ShellCardBorderBrush", 0xFF, 0x2A, 0x2A, 0x2A),
    };

    /// <summary>
    /// The guide's own copy of <c>ShellWindow.ApplySurfaceOverrides</c>, over the four surfaces this
    /// window actually has. Both palettes ride Dark's ElementTheme and so cannot be told apart by a
    /// theme dictionary — see that method, and <see cref="ThemeHelper.Resolve"/>, for why.
    /// </summary>
    private void ApplySurfaceOverrides()
    {
        foreach (var (key, _, _, _, _) in BlackOverrides)
            Root.Resources.Remove(key);

        switch (_manager.Config.Theme)
        {
            case AppTheme.Black:
                foreach (var (key, a, r, g, b) in BlackOverrides)
                    Root.Resources[key] = new SolidColorBrush(Color.FromArgb(a, r, g, b));
                break;

            case AppTheme.FrostedGlass:
                // Only the keys this window has, asked for one at a time rather than by walking
                // FrostedGlassPalette.Surfaces: the palette covers the whole app's surfaces and
                // most of them have no element here to reach.
                foreach (var (key, _, _, _, _) in BlackOverrides)
                {
                    if (FrostedGlassPalette.TryColorFor(key, out var frosted))
                        Root.Resources[key] = new SolidColorBrush(frosted);
                }
                break;
        }
    }

    /// <summary>
    /// Frosted glass wants a real thin acrylic where every other theme wants Mica. Guarded on the
    /// attached type so a re-apply does not tear the composition target down and rebuild it.
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
    /// Paints the WinUI brush keys the controls on this window actually read, so the accent the user
    /// has stored reaches the Next button, the toggles and the step dots rather than leaving them on
    /// the Windows system accent. A strict subset of <see cref="ShellWindow"/>'s list — this window
    /// has no slider, no rating control and no hyperlinks.
    /// </summary>
    private void ApplyAccentColorOverride()
    {
        var accent = AccentPalette.Resolve(_manager.Config, AccentPalette.IsDark(_manager.Config, Root));
        var normal = new SolidColorBrush(accent);
        var hover = new SolidColorBrush(AccentPalette.PointerOver(accent));
        var pressed = new SolidColorBrush(AccentPalette.Pressed(accent));
        var onAccent = new SolidColorBrush(AccentPalette.OnAccent(
            accent, AccentPalette.PointerOver(accent), AccentPalette.Pressed(accent)));

        Root.Resources["SettingsAccentBrush"] = normal;
        Root.Resources["ToggleSwitchFillOn"] = normal;
        Root.Resources["ToggleSwitchFillOnPointerOver"] = hover;
        Root.Resources["ToggleSwitchFillOnPressed"] = pressed;
        Root.Resources["ToggleSwitchStrokeOn"] = normal;
        Root.Resources["ToggleSwitchStrokeOnPointerOver"] = hover;
        Root.Resources["ToggleSwitchStrokeOnPressed"] = pressed;
        Root.Resources["CheckBoxCheckBackgroundFillChecked"] = normal;
        Root.Resources["CheckBoxCheckBackgroundFillCheckedPointerOver"] = hover;
        Root.Resources["CheckBoxCheckBackgroundFillCheckedPressed"] = pressed;
        Root.Resources["CheckBoxCheckBackgroundStrokeChecked"] = normal;
        Root.Resources["CheckBoxCheckBackgroundStrokeCheckedPointerOver"] = hover;
        Root.Resources["CheckBoxCheckBackgroundStrokeCheckedPressed"] = pressed;
        // The commit button is an AccentButtonStyle, which reads these three rather than the
        // ToggleSwitch keys above. OnAccent is chosen once for all three fills so the label cannot
        // flip from white to black as the pointer crosses the button.
        Root.Resources["AccentButtonBackground"] = normal;
        Root.Resources["AccentButtonBackgroundPointerOver"] = hover;
        Root.Resources["AccentButtonBackgroundPressed"] = pressed;
        Root.Resources["AccentButtonForeground"] = onAccent;
        Root.Resources["AccentButtonForegroundPointerOver"] = onAccent;
        Root.Resources["AccentButtonForegroundPressed"] = onAccent;
    }

    private void ApplyChromeTheme()
    {
        bool dark = ThemeHelper.IsDark(Root);
        int rim = _manager.Config.Theme switch
        {
            AppTheme.Black => 0x00000000,
            AppTheme.FrostedGlass => 0x00161010,
            _ => dark ? 0x002B2B2B : 0x00F2F2F2,
        };
        WindowChrome.ApplyShellFrame(
            _appWindow, _hwnd, dark,
            frosted: _manager.Config.Theme == AppTheme.FrostedGlass,
            rim);
    }

    // ===================== steps =====================

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, StepCount - 1);

        WelcomeStep.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        ItemsStep.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        ShortcutStep.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;

        StepText.Text = $"STEP {_step + 1} OF {StepCount}";
        (StepTitle.Text, StepSubtitle.Text) = _step switch
        {
            0 => ("Welcome to SwiftX",
                  "A quick-access orbit for the apps, folders, links and tools you reach for most."),
            1 => ("What goes on the orbit?",
                  $"Tick what you want within reach. The orbit holds {OrbitRingLayout.MaxUserCount} of your own items — everything here can be changed later from Orbit Settings."),
            _ => ("How should it appear?",
                  "Pick the keys that summon the orbit, and whether a screen corner should do it too."),
        };

        BackButton.Visibility = _step == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = _step == StepCount - 1 ? "Finish" : "Next";

        UpdateStepDots();
        if (_step == 2)
            UpdateHotkeyStatus();
    }

    /// <summary>
    /// The three footer dots: the current step in the accent, the rest in the card border colour.
    /// Filled from code rather than bound because the accent is the user's to choose, and
    /// <see cref="ThemeHelper.ColorFor(bool, AppTheme, string)"/> is the only element-scoped way to read
    /// the resting colour back (see that class's own warning about app-level lookups).
    /// </summary>
    private void UpdateStepDots()
    {
        var on = new SolidColorBrush(
            AccentPalette.Resolve(_manager.Config, AccentPalette.IsDark(_manager.Config, Root)));
        var off = new SolidColorBrush(ThemeHelper.ColorFor(
            ThemeHelper.IsDark(Root), _manager.Config.Theme, "ShellCardBorderBrush"));

        Dot1.Fill = _step >= 0 ? on : off;
        Dot2.Fill = _step >= 1 ? on : off;
        Dot3.Fill = _step >= 2 ? on : off;
    }

    private void BackStep_Click(object sender, RoutedEventArgs e) => ShowStep(_step - 1);

    private void NextStep_Click(object sender, RoutedEventArgs e)
    {
        if (_step < StepCount - 1)
        {
            ShowStep(_step + 1);
            return;
        }

        Finish();
    }

    // ===================== step 2: the picker =====================

    /// <summary>
    /// Rebuilds the whole tile grid. Cheap enough to do on every tick, and doing it that way is what
    /// keeps the slot cap honest: whether an unticked tile is available depends on how many others
    /// are ticked, so every tile's enabled state can change when any one of them does.
    /// </summary>
    private void RebuildSuggestions()
    {
        SuggestionGrid.Children.Clear();
        SuggestionGrid.RowDefinitions.Clear();

        int used = _selected.Count;
        bool full = used >= OrbitRingLayout.MaxUserCount;

        SlotCountText.Text =
            $"{used} of {OrbitRingLayout.MaxUserCount} orbit slots used" + (full ? " · orbit full" : "");

        int rows = (_suggestions.Count + 1) / 2;
        for (int r = 0; r < rows; r++)
            SuggestionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < _suggestions.Count; i++)
        {
            var card = BuildSuggestionTile(_suggestions[i], full);
            Grid.SetRow(card, i / 2);
            Grid.SetColumn(card, i % 2);
            SuggestionGrid.Children.Add(card);
        }
    }

    /// <summary>
    /// One tile: the item's own icon, its name and subtitle, and a checkbox. The icon is the same
    /// glyph-then-real-icon pair the orbit itself draws, so a tile shows the user what they will
    /// actually see on the orbit rather than a generic placeholder.
    /// </summary>
    /// <param name="full">
    /// Whether the orbit's user slots are already spoken for. An unticked tile is disabled when they
    /// are — disabled rather than hidden, so the reason the tile cannot be ticked stays visible next
    /// to the "orbit full" count that explains it.
    /// </param>
    private Border BuildSuggestionTile(OnboardingSuggestion suggestion, bool full)
    {
        bool ticked = _selected.Contains(suggestion.Key);

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = BuildTileIcon(suggestion.Item);
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock
        {
            Text = suggestion.Item.DisplayName,
            Style = (Style)Root.Resources["SettingTitle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        labels.Children.Add(new TextBlock
        {
            Text = suggestion.Subtitle,
            Style = (Style)Root.Resources["SettingBody"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        });
        Grid.SetColumn(labels, 1);
        row.Children.Add(labels);

        var check = new CheckBox
        {
            IsChecked = ticked,
            IsEnabled = ticked || !full,
            MinWidth = 0,
            Content = null,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            check, "Add " + suggestion.Item.DisplayName + " to the orbit");
        var key = suggestion.Key;
        check.Checked += (_, _) =>
        {
            if (_loading) return;
            _selected.Add(key);
            RebuildSuggestions();
        };
        check.Unchecked += (_, _) =>
        {
            if (_loading) return;
            _selected.Remove(key);
            RebuildSuggestions();
        };
        Grid.SetColumn(check, 2);
        row.Children.Add(check);

        return new Border { Style = (Style)Root.Resources["Card"], Child = row };
    }

    /// <summary>
    /// A tile's 28px icon plate: the item's glyph to begin with, replaced by its real shell / folder
    /// / favicon icon if <see cref="IconService"/> resolves one. Same image-over-glyph pair the
    /// orbit and the Elements list draw.
    /// </summary>
    private FrameworkElement BuildTileIcon(OrbitItem item)
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
        // Same non-Fluent-glyph handling the orbit does — "B64" and friends are tofu boxes in the
        // icon font.
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
        LoadTileIcon(item, image, glyph);

        return new Grid
        {
            Width = plate,
            Height = plate,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { content },
        };
    }

    /// <summary>
    /// Fire-and-forget icon resolution for one tile. Started on the UI thread, so the continuation
    /// comes back to it and can touch the two elements directly. A null result means nothing
    /// resolved — the glyph already on screen is then simply left alone.
    /// </summary>
    private static async void LoadTileIcon(OrbitItem item, Image image, FontIcon glyph)
    {
        // async void, no caller: this window closes (Finish, or the title bar) while dozens of
        // these tile icon loads can still be in flight — same race as ShellWindow.LoadRowIcon.
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
            Diag.Log("OnboardingWindow.LoadTileIcon failed: " + ex);
        }
    }

    /// <summary>
    /// Hands the user the ordinary Add-item dialog so the guide can cover anything the catalog
    /// cannot guess at — a shortcut, a file, an app that is not on the list. Whatever comes back is
    /// appended as a tile of its own, already ticked, rather than silently added: the slot count has
    /// to stay the truth about what Finish will write.
    /// </summary>
    private void AddOwn_Click(object sender, RoutedEventArgs e)
    {
        // A callback, so AddItemWindow hands the item back instead of committing it to the orbit
        // itself (see its Add_Click) — nothing this window collects is persisted before Finish.
        // The target list it is given is what is staged here, so its own duplicate checks compare
        // against the orbit the user is actually building.
        _manager.OpenAddItem(
            added =>
            {
                var key = "custom:" + added.Id;
                _suggestions.Add(new OnboardingSuggestion(key, KindLabel(added), added, DefaultOn: true));
                if (_selected.Count < OrbitRingLayout.MaxUserCount)
                    _selected.Add(key);
                RebuildSuggestions();
            },
            targetList: SelectedItems(),
            isGenericAddItem: true,
            allowGroups: false,
            heading: "Add to the orbit",
            confirmLabel: "Add");
    }

    /// <summary>A custom item's subtitle: what kind of thing it is, and where it points.</summary>
    private static string KindLabel(OrbitItem item)
    {
        var kind = item.Kind switch
        {
            OrbitItemKind.Application => "App",
            OrbitItemKind.File => "File",
            OrbitItemKind.Folder => "Folder",
            OrbitItemKind.WebLink => "Link",
            OrbitItemKind.Shortcut => "Shortcut",
            OrbitItemKind.Group => "Group",
            _ => "Tool",
        };
        return string.IsNullOrEmpty(item.Target) ? kind : kind + " · " + item.Target;
    }

    /// <summary>
    /// The ticked items, in catalog order, capped at the orbit's own user-slot count. The cap is
    /// belt and braces — the picker disables further ticks once it is reached — but it is the one
    /// guarantee that matters, because an orbit handed more items than it has slots simply drops
    /// the overflow without saying so.
    /// </summary>
    private List<OrbitItem> SelectedItems() =>
        _suggestions
            .Where(s => _selected.Contains(s.Key))
            .Select(s => s.Item)
            .Take(OrbitRingLayout.MaxUserCount)
            .ToList();

    // ===================== step 3: summoning =====================

    private void OnHotkeyAssigned(HotkeyGesture? gesture)
    {
        if (_loading || gesture is null) return;
        _gesture = gesture;
        UpdateHotkeyStatus();
    }

    private void HotkeyEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _hotkeyEnabled = HotkeyEnabledToggle.IsOn;
        UpdateHotkeyStatus();
    }

    private void HotCornerEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _hotCornerEnabled = HotCornerEnabledToggle.IsOn;
        SyncHotCornerControls();
    }

    private void HotCornerBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || HotCornerBox.SelectedItem is not ComboBoxItem item) return;
        if (Enum.TryParse<HotCorner>(item.Tag as string, out var corner))
            _hotCorner = corner;
    }

    private void SelectHotCorner(HotCorner corner)
    {
        foreach (ComboBoxItem item in HotCornerBox.Items)
        {
            if ((item.Tag as string) == corner.ToString())
            {
                HotCornerBox.SelectedItem = item;
                return;
            }
        }
    }

    private void SyncHotCornerControls() =>
        HotCornerOptionsRow.Visibility = HotCornerEnabledToggle.IsOn
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>
    /// Says whether the chosen combination can actually be registered — without registering it.
    /// The guide has not committed anything yet, and taking the hotkey here would leave a dismissed
    /// guide having changed the very setting it promised not to touch. The real registration
    /// happens in <see cref="Finish"/>, through <see cref="OrbitManager.ApplyHotkey"/>.
    /// </summary>
    private void UpdateHotkeyStatus()
    {
        if (!_hotkeyEnabled)
        {
            HotkeyStatus.Text = "Off — the tray icon and the hot corner still work.";
            return;
        }
        HotkeyStatus.Text = _gesture is null
            ? ""
            : $"Will use {_gesture}.";
    }

    // ===================== commit =====================

    /// <summary>
    /// The one place this window writes anything. Everything the three steps collected lands in the
    /// config together, then goes out through the manager so the orbit, the tray and the hotkey
    /// registration all pick it up in the same pass.
    /// </summary>
    private async void Finish()
    {
        // async void, no caller: nothing here used to be guarded, and NotifyItemsChanged alone
        // fans out to every ItemsChanged subscriber (ShellWindow, the live OrbitWindow) plus
        // OrbitWindow.ReloadItems directly — any one of which throwing would otherwise take the
        // whole app down on the one screen every first run passes through.
        try
        {
            var chosen = SelectedItems();

            // An orbit with nothing on it is not a choice anyone means to make — it leaves the app with
            // no reason to be summoned. Keep what Start already seeded in that case, and say nothing:
            // the user can empty the orbit deliberately from Elements, where the consequence is visible.
            if (chosen.Count > 0)
            {
                _manager.Config.Primary.Items.Clear();
                _manager.Config.Primary.Items.AddRange(chosen);
                // The tools group has to be the shape the rest of the app expects wherever it came from
                // — and if the user unticked it, this is what puts the built-in tools back out of the
                // way rather than leaving orbit items pointing at tools that are nowhere.
                ToolCatalog.EnsureToolsGroup(_manager.Config.Primary.Items);
                ToolCatalog.ApplyVisibility(_manager.Config.Primary.Items, _manager.Config);
            }

            if (_gesture is not null)
                _manager.Config.Hotkey = _gesture.ToString();
            _manager.Config.HotkeyEnabled = _hotkeyEnabled;
            _manager.Config.HotCornerEnabled = _hotCornerEnabled;
            _manager.Config.HotCorner = _hotCorner;

            _manager.NotifyItemsChanged();
            _manager.ApplyHotkey();
            _manager.ApplyHotCorner();

            // Both of these touch the OS rather than the config, and both can fail (a policy-locked
            // Run key, a read-only desktop). Awaited before the window closes so a failure is not
            // racing a teardown, and deliberately not reported here — the same switches on Settings ›
            // General show what actually took effect.
            await StartupService.SetEnabledAsync(StartupToggle.IsOn);
            _manager.Config.LaunchAtStartup = StartupToggle.IsOn;

            // Apply reports what is actually on the desktop afterwards, which is not always what was
            // asked for (a read-only or redirected desktop). Persist that, not the request — Settings'
            // own switch reads the same truth back and would otherwise disagree with the config.
            _manager.Config.DesktopShortcut = DesktopShortcutService.Apply(DesktopShortcutToggle.IsOn);

            _manager.Save();

            _finished = true;
            Close();
        }
        catch (Exception ex)
        {
            Diag.Log("OnboardingWindow.Finish failed: " + ex);
        }
    }
}
