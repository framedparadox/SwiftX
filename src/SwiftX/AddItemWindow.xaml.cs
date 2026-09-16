using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SwiftX.Controls;
using SwiftX.Models;
using SwiftX.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace SwiftX;

/// <summary>
/// Add-item dialog: type tiles, target/name form, solid theme chrome (no Mica).
/// <para>
/// Two things here are not stock WinUI. The accented surfaces — the commit button and whichever
/// type tile is checked — are repainted in SwiftX's own accent rather than the Windows system one
/// (see <see cref="ApplyAccentColorOverride"/>), and the whole window is a drop target, so a file,
/// folder, shortcut or link can be dragged onto it instead of hunting for it through Browse (see
/// <see cref="Root_Drop"/>).
/// </para>
/// </summary>
public sealed partial class AddItemWindow : Window
{
    private enum AddKind { App, File, Folder, Link, Shortcut, Group, Tool }

    private readonly OrbitManager _manager;
    private readonly Action<OrbitItem>? _onAdded;
    private readonly List<OrbitItem> _targetList;
    private readonly OrbitItem? _existing;
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private AddKind _kind = AddKind.App;
    private bool _nameEdited;

    /// <summary>
    /// Same dialog, two names: every "Add new" reached from the orbit, the tray icon, or the orbit
    /// preview itself is still adding "to the orbit", but the "Elements" tab's own "Add item…" button
    /// is not adding to any specific orbit slot, so it gets the more generic title — see the
    /// callers of <see cref="OrbitManager.OpenAddItem"/>.
    /// </summary>
    /// <param name="allowGroups">
    /// False when the target list is a group's own children. A group inside a group would need a
    /// third orbit to land in and the orbit has none (see <c>OrbitWindow.OnSelectAsync</c>), so the
    /// tile that would create one is taken away rather than left to add something the orbit can
    /// never expand.
    /// </param>
    /// <param name="heading">
    /// Overrides the title/heading the two flows above would pick. Settings' "Replace…" reuses
    /// this same form to swap what is already in an orbit slot or a corner place, which is neither
    /// of them.
    /// </param>
    /// <param name="confirmLabel">The commit button's label, for the same reason.</param>
    /// <param name="existing">
    /// The item being edited, when this is an edit rather than an add. Everything below that
    /// branches on it exists because this dialog had no way of being told about one: the Elements
    /// screen's "EDIT" already reached the same form, but with nothing to prefill from it opened
    /// blank — no type picked, no target, no name — and its Save wrote a brand new item over the
    /// old one, so an edit silently discarded whatever the user had not retyped from memory.
    /// Passing the item makes the form open on what it already is, and makes the commit carry the
    /// item's identity and its off-form fields across (see <see cref="PrefillFrom"/> and
    /// <see cref="Add_Click"/>). Null for every add, which is every other caller.
    /// </param>
    public AddItemWindow(
        OrbitManager manager, Action<OrbitItem>? onAdded, List<OrbitItem> targetList,
        bool isGenericAddItem = false, bool allowGroups = true,
        string? heading = null, string? confirmLabel = null, OrbitItem? existing = null)
    {
        _manager = manager;
        _onAdded = onAdded;
        _targetList = targetList;
        _existing = existing;
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));
        heading ??= isGenericAddItem ? "Add item" : "Add to orbit";
        Title = heading;
        HeadingText.Text = heading;
        if (!string.IsNullOrEmpty(confirmLabel))
            ConfirmButton.Content = confirmLabel;

        // Adding a plain item (App/File/Folder/Link/Shortcut/Group) is the generic flow's whole
        // job; a built-in tool already has its own home on the Tools page, so that tile only makes
        // sense from an orbit/corner slot that can actually hold one.
        if (isGenericAddItem)
            ToolType.Visibility = Visibility.Collapsed;

        if (!allowGroups)
            GroupType.Visibility = Visibility.Collapsed;

        var titleBar = AppTitleBar.WithAppMark();
        Grid.SetRow(titleBar, 0);
        Root.Children.Insert(0, titleBar);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);

        ApplyTheme();
        Root.ActualThemeChanged += (_, _) =>
        {
            // Fires on an OS theme flip, which only AppTheme.System can follow. The accent has to
            // be rewritten for it because the built-in default is theme-dependent (see
            // AccentPalette's two default constants) — a no-op when the user has picked their own
            // colour, but the colour they get by default is a different teal either side of the
            // flip. Same reasoning, same call, as ShellWindow's own handler.
            ApplyAccentColorOverride();
            ApplyChromeTheme();
        };
        Activated += (_, _) => ApplyChromeTheme();

        if (_appWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
        }
        _appWindow.IsShownInSwitchers = true;

        // A starting height only. The real one is measured from the content once it has been laid
        // out for the first time (see FitWindowToContent), because a hard-coded number cannot stay
        // right: the hint text under the target box wraps to a second or third line in the longer
        // languages, the Tool and Group types show a different set of fields, and the error bar
        // adds a row when it opens. Each of those moves the figure, and guessing it too low puts
        // the Cancel/Add buttons off the bottom of a window the user cannot resize.
        WindowChrome.SetClientSizeDip(_appWindow, _hwnd, ClientWidthDip, InitialClientHeightDip);
        WindowChrome.SetRoundedCorners(_hwnd);
        WindowChrome.CenterOnCursor(_appWindow, _appWindow.Id);

        // Loaded, not the constructor: DesiredSize is meaningless until the tree has been through
        // a layout pass with its real styles and font metrics resolved.
        Root.Loaded += (_, _) => FitWindowToContent(recentre: true);

        NameBox.TextChanged += (_, _) =>
        {
            if (NameBox.FocusState != FocusState.Unfocused)
                _nameEdited = !string.IsNullOrEmpty(NameBox.Text);
        };

        // An add starts on the first tile; an edit starts on whatever the item already is. Both
        // go through SelectType, so the form is in the same consistent state either way — it is
        // what shows and hides the target row and the tool picker, and nothing else does.
        if (existing is null)
            SelectType(AddKind.App);
        else
            PrefillFrom(existing);
    }

    public void ApplyTheme()
    {
        ApplyBlackOverrides();
        // Both resource passes have to come before the refresh below, which is the thing that
        // makes every {ThemeResource} under Root re-resolve and actually notice them.
        ApplyAccentColorOverride();
        ForceThemeResourceRefresh(ThemeHelper.Resolve(_manager.Config.Theme));
        ApplyChromeTheme();
    }

    /// <summary>
    /// Re-reads just the accent colour, for a live change made while this window is open — Settings'
    /// colour picker is a sibling window, and the two are routinely on screen together.
    /// <para>
    /// Deliberately not <see cref="ApplyTheme"/>: the picker raises its ColorChanged continuously
    /// as the user drags, and ApplyTheme's <see cref="ForceThemeResourceRefresh"/> re-walks every
    /// <c>{ThemeResource}</c> under Root twice per call — far too heavy to run per frame of a drag,
    /// and pointless when nothing but these brushes has changed. Assigning into
    /// <c>Root.Resources</c> repaints the controls reading those keys on its own.
    /// </para>
    /// </summary>
    public void ApplyAccent() => ApplyAccentColorOverride();

    /// <summary>
    /// Midnight rides on Dark's own <see cref="ElementTheme"/> (see <see cref="ThemeHelper.Resolve"/>),
    /// so selecting it never changes <c>RequestedTheme</c>'s value and WinUI never re-walks the
    /// <c>{ThemeResource}</c> lookups that <see cref="ApplyBlackOverrides"/> has just shadowed.
    /// Nudging the property to the other theme and straight back is what forces that re-walk —
    /// without it, switching between Dark and Midnight looked like it did nothing at all. Same
    /// trick, and the same reason, as ShellWindow's.
    /// </summary>
    private void ForceThemeResourceRefresh(ElementTheme target)
    {
        if (Root.RequestedTheme == target)
            Root.RequestedTheme = target == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        Root.RequestedTheme = target;
    }

    /// <summary>
    /// Dark's surface is now Dark's own <c>#202020</c> (<c>SwiftXDialogSurfaceBrush</c> in
    /// <c>Themes/Colors.xaml</c>) rather than the flat black it used to borrow from
    /// <c>SwiftXSurfaceBrush</c> — which is what made this dialog show Midnight's chrome while
    /// Dark was selected. Midnight itself is not a XAML theme and cannot be told apart through a
    /// theme dictionary, so when it is selected the key is shadowed here with a local entry in
    /// <c>Root.Resources</c>: a nearer resource wins, so every <c>{ThemeResource}</c> under Root
    /// resolves to this instead. Removed again the moment Midnight is not selected.
    /// </summary>
    private void ApplyBlackOverrides()
    {
        if (_manager.Config.Theme != AppTheme.Black)
        {
            Root.Resources.Remove("SwiftXDialogSurfaceBrush");
            return;
        }

        Root.Resources["SwiftXDialogSurfaceBrush"] =
            new SolidColorBrush(Microsoft.UI.Colors.Black);
    }

    /// <summary>
    /// Repaints this dialog's accented surfaces — the commit button and the checked type tile — in
    /// SwiftX's accent colour, whether that is the user's own pick or the built-in teal.
    /// <para>
    /// Both were stock WinUI and therefore neither was SwiftX's: the commit button carries
    /// <c>AccentButtonStyle</c>, and a checked <c>ToggleButton</c> uses the framework's default
    /// template, and both read their fills from keys the framework's own dictionary defines as
    /// <c>&lt;StaticResource ResourceKey="AccentFillColorDefaultBrush"/&gt;</c> — the <em>Windows</em>
    /// accent, blue on a default install. Overriding <c>AccentFillColorDefaultBrush</c> instead
    /// would change nothing, because a StaticResource alias is resolved once, when the framework
    /// dictionary is parsed, and never looked up again; the leaf keys are the only reachable
    /// handle. So they are shadowed here in <see cref="Root"/>'s own Resources, where a nearer
    /// dictionary wins — the same mechanism <see cref="ApplyBlackOverrides"/> uses, and the same
    /// list ShellWindow keeps for the controls Settings happens to use.
    /// </para>
    /// <para>
    /// Setting <c>Foreground</c> or <c>Background</c> on the controls themselves is not an
    /// alternative: the templates' own visual states assign those properties on hover and press,
    /// and a locally set value is what they overwrite.
    /// </para>
    /// <para>
    /// Disabled variants are deliberately left alone — a disabled control should read as disabled,
    /// not as a washed-out accent.
    /// </para>
    /// </summary>
    private void ApplyAccentColorOverride()
    {
        var accent = AccentPalette.Resolve(
            _manager.Config, AccentPalette.IsDark(_manager.Config, Root));
        var hoverColor = AccentPalette.PointerOver(accent);
        var pressedColor = AccentPalette.Pressed(accent);
        // The accent is the user's to choose and may be pale, so what gets drawn on top of it is
        // computed from its luminance rather than assumed to be white (which is all WinUI's own
        // TextOnAccentFillColorPrimaryBrush amounts to). One foreground for all three states:
        // see AccentPalette.OnAccent for why it is not decided per state.
        var onAccentColor = AccentPalette.OnAccent(accent, hoverColor, pressedColor);

        var normal = new SolidColorBrush(accent);
        var hover = new SolidColorBrush(hoverColor);
        var pressed = new SolidColorBrush(pressedColor);
        var onAccent = new SolidColorBrush(onAccentColor);

        ApplyDropOverlayAccent(accent, onAccentColor);

        // Key names verified against the WinUI dictionary this project actually resolves to
        // (Microsoft.WindowsAppSDK 2.4.0 depends on Microsoft.WindowsAppSDK.WinUI 2.3.6; its
        // Themes/generic.xaml is the file below): AccentButtonStyle sets Foreground/Background/
        // BorderBrush from the AccentButton* keys and swaps in the PointerOver/Pressed variants
        // from its own visual states, and the implicit ToggleButton style (DefaultToggleButtonStyle,
        // which the bare <ToggleButton>s in the XAML pick up) reads the ToggleButton*Checked*
        // keys from its Checked / CheckedPointerOver / CheckedPressed states. Border brushes get
        // the same triple as the fills, which flattens the framework's subtle elevation gradient
        // into one colour — the accent reading as a single deliberate colour matters more here
        // than that gradient does.
        var overrides = new (string Key, Brush Brush)[]
        {
            ("AccentButtonBackground", normal),
            ("AccentButtonBackgroundPointerOver", hover),
            ("AccentButtonBackgroundPressed", pressed),
            ("AccentButtonBorderBrush", normal),
            ("AccentButtonBorderBrushPointerOver", hover),
            ("AccentButtonBorderBrushPressed", pressed),
            ("AccentButtonForeground", onAccent),
            ("AccentButtonForegroundPointerOver", onAccent),
            ("AccentButtonForegroundPressed", onAccent),
            ("ToggleButtonBackgroundChecked", normal),
            ("ToggleButtonBackgroundCheckedPointerOver", hover),
            ("ToggleButtonBackgroundCheckedPressed", pressed),
            ("ToggleButtonBorderBrushChecked", normal),
            ("ToggleButtonBorderBrushCheckedPointerOver", hover),
            ("ToggleButtonBorderBrushCheckedPressed", pressed),
            ("ToggleButtonForegroundChecked", onAccent),
            ("ToggleButtonForegroundCheckedPointerOver", onAccent),
            ("ToggleButtonForegroundCheckedPressed", onAccent),
        };

        // High Contrast always wins (design doc §13.3). Those theme dictionaries aim these very
        // same keys at the OS's guaranteed-contrast system brushes, so a custom accent painted
        // over them would undo precisely the contrast the mode exists to provide. Removing rather
        // than skipping, so turning High Contrast on while this window is open still lets the
        // shadowed keys fall back through to the framework's.
        bool highContrast = HighContrast.IsActive();
        foreach (var (key, brush) in overrides)
        {
            if (highContrast)
                Root.Resources.Remove(key);
            else
                Root.Resources[key] = brush;
        }
    }

    /// <summary>
    /// Colours the drag-over affordance (see the XAML) from the same accent everything else here
    /// now uses. Painted directly rather than through a shadowed resource key because it is
    /// SwiftX's own chrome, not a framework control template — and it stays accent-coloured even
    /// under High Contrast, where its legibility is not a gamble: the pill's label is picked
    /// against the pill's own fill by luminance, and the wash beneath is faint enough to leave the
    /// form under it readable.
    /// </summary>
    private void ApplyDropOverlayAccent(Color accent, Color onAccent)
    {
        // The Drag 'n Drop tile beside the type tiles: accent glyph and label, a dimmed rim and a
        // faint wash of the same colour. Faint deliberately — it stands next to seven ToggleButtons
        // whose *checked* state is this exact accent at full strength, and a tile that read as
        // strongly as a selected one would look like an eighth type that was already picked.
        // Assigned rather than bound for the same reason as the overlay below: the accent is a
        // runtime colour with no ThemeResource behind it.
        DragDropTile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x8C, accent.R, accent.G, accent.B));
        DragDropTile.Background = new SolidColorBrush(Color.FromArgb(0x14, accent.R, accent.G, accent.B));
        var accentBrush = new SolidColorBrush(accent);
        DragDropTileGlyph.Foreground = accentBrush;
        DragDropTileText.Foreground = accentBrush;

        DropOverlay.BorderBrush = new SolidColorBrush(accent);
        DropOverlay.Background =
            new SolidColorBrush(Color.FromArgb(0x24, accent.R, accent.G, accent.B));
        DropPill.Background = new SolidColorBrush(accent);

        var label = new SolidColorBrush(onAccent);
        DropPillGlyph.Foreground = label;
        DropPillText.Foreground = label;
    }

    private void ApplyChromeTheme()
    {
        bool dark = ThemeHelper.IsDark(Root);
        WindowChrome.SetTitleBarTheme(_appWindow, dark);
        // The rim has to match whatever the root actually paints, or it draws a hard edge around
        // the window instead of disappearing into it — Dark's surface is no longer black.
        int rim = _manager.Config.Theme == AppTheme.Black
            ? RimColorBlack
            : dark ? RimColorDark : RimColorLight;
        WindowChrome.HideWindowBorder(_hwnd, dark, rim);
    }

    /// <summary>
    /// Rim colors as COLORREFs (0x00BBGGRR), matching <c>SwiftXDialogSurfaceBrush</c> in
    /// <c>Themes/Colors.xaml</c> and <see cref="ApplyBlackOverrides"/>' own flat black. Neutral
    /// greys, so channel order is moot.
    /// </summary>
    private const int RimColorDark = 0x00222222;
    private const int RimColorLight = 0x00FFFFFF;
    private const int RimColorBlack = 0x00000000;

    /// <summary>Fixed dialog width. The seven type tiles sit in one unwrapped row, and that is what
    /// sets it — narrower and they spill.</summary>
    private const int ClientWidthDip = 760;

    /// <summary>
    /// The size the window is first created at: roughly what the default App type measures to, so
    /// the common case is already right before <see cref="FitWindowToContent"/> has run and the
    /// window does not visibly resize as it opens.
    /// </summary>
    private const int InitialClientHeightDip = 460;

    /// <summary>
    /// Floor for the measured height. Low enough to be out of the way in every normal state — the
    /// measurement is the thing that decides, not this — and there purely so a measurement that
    /// somehow came back at or near zero could not collapse the window to a sliver.
    /// </summary>
    private const int MinClientHeightDip = 320;

    /// <summary>Ceiling, before the display's own work area clamps it further.</summary>
    private const int MaxClientHeightDip = 900;

    private int _clientHeightDip = InitialClientHeightDip;

    /// <summary>
    /// Sizes the window to whatever its content actually needs, rather than to a constant that has
    /// to be re-guessed every time the form changes. Called once the tree is loaded and again after
    /// each type change, since the types do not show the same fields.
    /// <para>
    /// Shrinks as well as grows, now that every row is Auto. Nothing stretches any more, so spare
    /// height has nowhere to go but a gap above the buttons — the exact thing this is here to
    /// prevent — and the only way to have no gap in the short states (Group, Tool) as well as the
    /// tall ones is to hand the height back.
    /// </para>
    /// <para>
    /// <paramref name="recentre"/> is false for those later calls on purpose. Re-centring uses the
    /// *cursor*, which has moved by then — usually onto the type tile that triggered this — so
    /// re-centring on every type change would walk the window across the screen as the user
    /// clicked through the row. Resize alone keeps the top-left pinned, so only the bottom edge
    /// moves.
    /// </para>
    /// </summary>
    private void FitWindowToContent(bool recentre = false)
    {
        try
        {
            // Measured against the real width and an unbounded height, so what comes back is what
            // the content wants rather than what it has already been squeezed into.
            Body.Measure(new Windows.Foundation.Size(ClientWidthDip, double.PositiveInfinity));

            int target = (int)Math.Ceiling(AppTitleBar.HeightDip + Body.DesiredSize.Height);
            target = Math.Clamp(target, MinClientHeightDip, MaxHeightForDisplay());
            if (target == _clientHeightDip && !recentre)
                return;

            _clientHeightDip = target;
            WindowChrome.SetClientSizeDip(_appWindow, _hwnd, ClientWidthDip, target);
            if (recentre)
                WindowChrome.CenterOnCursor(_appWindow, _appWindow.Id);
        }
        catch (Exception ex)
        {
            // A dialog that cannot measure itself keeps the height it has, which is the floor above
            // — never a reason to fail opening it.
            Diag.Log("AddItemWindow.FitWindowToContent failed: " + ex.Message);
        }
    }

    /// <summary>
    /// The tallest this window may become on the display it is actually on, leaving a margin so it
    /// never runs edge to edge. Guards the case the clamp exists for: a low-resolution or heavily
    /// scaled display where the content's natural height is taller than the screen, and a window
    /// sized to it would put its own buttons off the bottom — the very failure the measuring is
    /// meant to prevent.
    /// </summary>
    private int MaxHeightForDisplay()
    {
        try
        {
            var work = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            double scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
            if (scale <= 0) scale = 1.0;
            int available = (int)(work.Height / scale) - 48;
            return Math.Clamp(available, MinClientHeightDip, MaxClientHeightDip);
        }
        catch
        {
            return MaxClientHeightDip;
        }
    }

    private void TypeSelected(object sender, RoutedEventArgs e)
    {
        AddKind kind = ReferenceEquals(sender, AppType) ? AddKind.App
                     : ReferenceEquals(sender, FileType) ? AddKind.File
                     : ReferenceEquals(sender, FolderType) ? AddKind.Folder
                     : ReferenceEquals(sender, LinkType) ? AddKind.Link
                     : ReferenceEquals(sender, GroupType) ? AddKind.Group
                     : ReferenceEquals(sender, ToolType) ? AddKind.Tool
                     : AddKind.Shortcut;
        SelectType(kind);
    }

    private void SelectType(AddKind kind)
    {
        _kind = kind;

        AppType.IsChecked = kind == AddKind.App;
        FileType.IsChecked = kind == AddKind.File;
        FolderType.IsChecked = kind == AddKind.Folder;
        LinkType.IsChecked = kind == AddKind.Link;
        ShortcutType.IsChecked = kind == AddKind.Shortcut;
        GroupType.IsChecked = kind == AddKind.Group;
        ToolType.IsChecked = kind == AddKind.Tool;

        bool hasTarget = kind is not (AddKind.Group or AddKind.Tool);
        TargetLabel.Visibility = hasTarget ? Visibility.Visible : Visibility.Collapsed;
        TargetRow.Visibility = hasTarget ? Visibility.Visible : Visibility.Collapsed;
        NamePanel.Visibility = Visibility.Visible;
        ToolPanel.Visibility = kind == AddKind.Tool ? Visibility.Visible : Visibility.Collapsed;

        bool canBrowse = kind is AddKind.App or AddKind.File or AddKind.Folder or AddKind.Shortcut;
        BrowseButton.Visibility = canBrowse ? Visibility.Visible : Visibility.Collapsed;

        (TargetLabel.Text, TargetBox.PlaceholderText, TargetHint.Text) = kind switch
        {
            AddKind.App => ("Application", "Path to an .exe or .lnk",
                "Pick an app or paste a path. Arguments are not required."),
            AddKind.File => ("File", "Path to a file",
                "Opens with the default associated app."),
            AddKind.Folder => ("Folder", "Path to a folder",
                "Opens in File Explorer."),
            AddKind.Link => ("URL", "https://…",
                "Opens in your default browser."),
            AddKind.Shortcut => ("Shortcut / command", "Path or command line",
                "Any launchable path or .lnk."),
            AddKind.Group => ("", "",
                "A group holds other orbit items in a nested orbit."),
            AddKind.Tool => ("", "",
                "Built-in formatter tool (JSON or XML)."),
            _ => ("Target", "Path or URL", ""),
        };

        if (kind == AddKind.Group && !_nameEdited && string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = "Group";

        HideError();

        // The types do not show the same fields — Tool swaps the target row for a picker, Group
        // drops it entirely — so the height that fit the last one is not necessarily enough for
        // this one.
        FitWindowToContent();
    }

    /// <summary>
    /// Puts an item that already exists back into the form, so an edit begins from what that item
    /// is rather than from a blank add.
    /// <para>
    /// The order of the three steps is the whole of it. <see cref="SelectType"/> has to run first
    /// because it is what decides which fields are even visible, and because it seeds "Group" into
    /// an empty name box. The target goes in next, which re-enters
    /// <see cref="TargetBox_TextChanged"/> and derives a name from the path. Only then is the real
    /// name written, with <see cref="_nameEdited"/> latched by hand afterwards: the item's own
    /// name is one the user already settled on, so it has to be treated as hand-typed and not
    /// quietly re-derived out from under them the moment they correct the path it sits beside.
    /// </para>
    /// <para>
    /// Arguments, a custom icon path and a custom glyph have no field on this form at all, so
    /// there is nothing here to prefill them into; they ride across the commit instead (see
    /// <see cref="Add_Click"/>), which is also the only way to keep them for the two kinds whose
    /// glyph the commit re-derives from the picker.
    /// </para>
    /// </summary>
    private void PrefillFrom(OrbitItem item)
    {
        SelectType(ToAddKind(item.Kind));

        if (item.Kind == OrbitItemKind.Tool)
            SelectTool(item.Target);
        else
            TargetBox.Text = item.Target;

        NameBox.Text = item.DisplayName;
        _nameEdited = !string.IsNullOrEmpty(item.DisplayName);
    }

    /// <summary>
    /// Points <see cref="ToolBox"/> at the tool a given <see cref="OrbitItem.Target"/> names.
    /// Matched on each entry's <c>Tag</c> rather than on its visible label, because the Tag is the
    /// exact string <see cref="Add_Click"/> reads back out of the same combo — the two disagree
    /// often enough to matter ("QuickFormat" the target against "Quick Format" the label), and
    /// matching the label would quietly leave the picker on Data for half the catalog.
    /// </summary>
    private void SelectTool(string target)
    {
        foreach (var entry in ToolBox.Items)
        {
            if (entry is ComboBoxItem candidate &&
                string.Equals(candidate.Tag as string, target, StringComparison.OrdinalIgnoreCase))
            {
                ToolBox.SelectedItem = candidate;
                return;
            }
        }
    }

    private void TargetBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_nameEdited || string.IsNullOrWhiteSpace(TargetBox.Text)) return;
        NameBox.Text = OrbitItemFactory.SuggestName(TargetBox.Text.Trim());
    }

    /// <summary>
    /// Fills the target box, and the name box with it.
    /// </summary>
    /// <param name="displayName">
    /// A name to force in rather than leaving <see cref="OrbitItemFactory.SuggestName"/> to derive
    /// one — and, unlike the derived name, it overwrites a name the user has already typed. Only
    /// the drop path passes it: a drop replaces the entire subject of the form, so a name left
    /// over from whatever was being described before it is stale, not precious. Browse and typing
    /// keep the old behaviour of never touching a hand-edited name.
    /// </param>
    private void SetTarget(string path, string? displayName = null)
    {
        TargetBox.Text = path;
        if (displayName is null)
        {
            if (!_nameEdited)
                NameBox.Text = OrbitItemFactory.SuggestName(path);
        }
        else
        {
            NameBox.Text = displayName;
            // Assigning NameBox.Text re-enters the TextChanged handler wired in the constructor,
            // which latches _nameEdited whenever the box happens to hold focus. Clear it after, so
            // the latch keeps meaning "the user typed this" rather than "a drop filled this in".
            _nameEdited = false;
        }
        HideError();
    }

    /// <summary>
    /// Whether the payload currently hovering the window can become an item, cached for the length
    /// of one drag by <see cref="Root_DragEnter"/>. Null means "not answered yet", which only
    /// happens for a text-only payload, whose usability cannot be decided without an async read.
    /// </summary>
    private bool? _dropUsable;

    /// <summary>
    /// Bumped every time a drag starts, leaves, or lands, so the async probe below can tell
    /// whether the drag it was asked about is still the one over the window by the time its answer
    /// comes back. Without it, a pointer that leaves again before the text read finishes would
    /// have the affordance quietly put back up behind it, with no drag left to dismiss it.
    /// </summary>
    private int _dragToken;

    /// <summary>
    /// Works out once, as the drag arrives, whether this payload is something the form can take,
    /// and puts the affordance up if it is.
    /// <para>
    /// Storage items and web links can be recognised from the format list alone. Plain text cannot:
    /// a dragged word out of a document offers text exactly as a dragged URL does, and the only way
    /// to tell them apart is to read it, which is async. Doing that read here rather than in
    /// <see cref="Root_DragOver"/> means it happens once per drag instead of on every mouse tick.
    /// </para>
    /// </summary>
    private async void Root_DragEnter(object sender, DragEventArgs e)
    {
        int token = ++_dragToken;
        try
        {
            var data = e.DataView;
            if (data.Contains(StandardDataFormats.StorageItems) ||
                data.Contains(StandardDataFormats.WebLink))
            {
                SetDropState(true, e);
                return;
            }

            if (!data.Contains(StandardDataFormats.Text))
            {
                SetDropState(false, e);
                return;
            }

            // Optimistic while the read is in flight: guessing "no" would flash a no-entry cursor
            // over every ordinary drag for the frame or two the read takes. Root_DragOver keeps
            // re-asserting whatever the answer turns out to be.
            SetDropState(null, e);
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
            // The args are spent by now, so only the affordance is updated here; the operation
            // itself is DragOver's to set from the cached answer.
            if (token == _dragToken)
                SetDropState(usable, null);
        }
        catch (Exception ex)
        {
            // async void: an exception escaping this is not an unhandled event, it is the process
            // going away. A drag hovering a dialog is not worth the app.
            Diag.Log("AddItemWindow.Root_DragEnter failed: " + ex);
            if (token == _dragToken)
                SetDropState(false, null);
        }
    }

    private void Root_DragOver(object sender, DragEventArgs e) => ApplyDropOperation(e);

    private void Root_DragLeave(object sender, DragEventArgs e)
    {
        _dragToken++;
        SetDropState(null, null);
    }

    /// <summary>
    /// Records what is known about the current payload, shows or hides the affordance to match,
    /// and — when there are still live args to say it to — tells the source.
    /// </summary>
    /// <param name="usable">True, false, or null for "not known yet / no drag in progress".</param>
    private void SetDropState(bool? usable, DragEventArgs? e)
    {
        _dropUsable = usable;
        DropOverlay.Visibility = usable == false || (usable is null && e is null)
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (e is not null)
            ApplyDropOperation(e);
    }

    /// <summary>
    /// Sets the operation the source sees, and the caption shown next to the pointer.
    /// </summary>
    private void ApplyDropOperation(DragEventArgs e)
    {
        // Unknown counts as acceptable — see Root_DragEnter.
        bool acceptable = _dropUsable != false;
        e.AcceptedOperation = acceptable ? DataPackageOperation.Copy : DataPackageOperation.None;
        if (!acceptable)
            return;

        // The caption is only drawn if the source's own DataPackage left the drag UI visible and
        // the caption enabled, and a source is free to arrive with either turned off, so both are
        // asserted here rather than assumed — setting Caption alone was silently doing nothing for
        // some sources.
        var ui = e.DragUIOverride;
        if (ui is null)
            return;
        ui.Caption = "Add to orbit";
        ui.IsCaptionVisible = true;
        ui.IsGlyphVisible = true;
        ui.IsContentVisible = true;
    }

    /// <summary>
    /// Lets a file, folder, app, .lnk shortcut (from Explorer, the desktop, the Start menu) or a
    /// web link (a browser's address bar or an anchor drag) be dropped straight onto this window
    /// instead of going through Browse. Fills the form only — type tile, target and name; the user
    /// still reviews it and presses Add.
    /// <para>
    /// The deferral is not optional and its absence is what made this silently do nothing.
    /// A <see cref="DragEventArgs"/>' data view is torn down the moment the handler hands control
    /// back to the framework, and an <c>async void</c> handler hands control back at its first
    /// <c>await</c> — so the old code was awaiting <c>GetStorageItemsAsync</c> against a view the
    /// framework had already released. <c>GetDeferral</c> is what tells the framework the drop is
    /// still in progress; completing it in a <c>finally</c> is what stops a failure from wedging
    /// the source's drag.
    /// </para>
    /// </summary>
    private async void Root_Drop(object sender, DragEventArgs e)
    {
        _dragToken++;
        SetDropState(null, null);

        var deferral = e.GetDeferral();
        try
        {
            var target = await OrbitItemFactory.ReadDroppedTargetAsync(e.DataView);
            if (string.IsNullOrEmpty(target))
            {
                // Nothing usable in there. Refuse it properly and leave the form exactly as the
                // user left it — a half-filled row from a drag they didn't mean to finish here is
                // worse than no reaction at all.
                e.AcceptedOperation = DataPackageOperation.None;
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Copy;
            SelectType(ToAddKind(OrbitItemFactory.Classify(target)));
            SetTarget(target, OrbitItemFactory.SuggestName(target));
        }
        catch (Exception ex)
        {
            // async void again: see Root_DragEnter. Anything thrown past here kills the app.
            Diag.Log("AddItemWindow.Root_Drop failed: " + ex);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// The tile that stands for a given item kind. Group and Tool are listed for
    /// <see cref="PrefillFrom"/>'s sake, not the drop path's:
    /// <see cref="OrbitItemFactory.Classify"/> only ever answers with the five kinds a dropped
    /// path can be, so <see cref="Root_Drop"/> cannot reach either arm and keeps the behaviour it
    /// had when they both fell through to App.
    /// </summary>
    private static AddKind ToAddKind(OrbitItemKind kind) => kind switch
    {
        OrbitItemKind.Folder => AddKind.Folder,
        OrbitItemKind.WebLink => AddKind.Link,
        OrbitItemKind.Shortcut => AddKind.Shortcut,
        OrbitItemKind.File => AddKind.File,
        OrbitItemKind.Group => AddKind.Group,
        OrbitItemKind.Tool => AddKind.Tool,
        _ => AddKind.App,
    };

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_kind == AddKind.Folder)
            {
                var folderPicker = new FolderPicker();
                InitializeWithWindow.Initialize(folderPicker, _hwnd);
                folderPicker.FileTypeFilter.Add("*");
                var folder = await folderPicker.PickSingleFolderAsync();
                if (folder is not null)
                    SetTarget(folder.Path);
                return;
            }

            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, _hwnd);
            if (_kind is AddKind.App or AddKind.Shortcut)
            {
                picker.FileTypeFilter.Add(".exe");
                picker.FileTypeFilter.Add(".lnk");
                picker.FileTypeFilter.Add(".bat");
                picker.FileTypeFilter.Add(".cmd");
            }
            else
            {
                picker.FileTypeFilter.Add("*");
            }
            var file = await picker.PickSingleFileAsync();
            if (file is not null)
                SetTarget(file.Path);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    /// <summary>
    /// Builds the item the form describes and hands it to whoever asked for one.
    /// <para>
    /// An edit builds a fresh <see cref="OrbitItem"/> too, rather than writing back into the one
    /// it came from, because the caller's callback is what decides where the result lands and a
    /// half-mutated original would already be on the orbit by the time the user pressed Cancel.
    /// What an edit does instead is carry the parts of the old item this form cannot describe onto
    /// the new one before the kind-specific branches run: its <see cref="OrbitItem.Id"/>, so
    /// anything keyed by identity still points at the same element; its hidden state, so editing a
    /// hidden item's name does not quietly put it back on the orbit; and the arguments, custom
    /// icon path and custom glyph the form has no field for. The two branches that re-derive a
    /// glyph then overwrite the carried one, which is correct for exactly those two — a tool's
    /// mark follows the tool the picker is on, and a group's is the group mark.
    /// </para>
    /// </summary>
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var item = new OrbitItem();

        if (_existing is not null)
        {
            item.Id = _existing.Id;
            item.Hidden = _existing.Hidden;
            item.Arguments = _existing.Arguments;
            item.CustomIconPath = _existing.CustomIconPath;
            item.CustomGlyph = _existing.CustomGlyph;
        }

        if (_kind == AddKind.Tool)
        {
            var tool = (ToolBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Data";
            var def = ToolCatalog.GetByName(tool);
            item.Kind = OrbitItemKind.Tool;
            item.Target = tool;
            item.DisplayName = string.IsNullOrWhiteSpace(NameBox.Text)
                ? (def?.DisplayName ?? tool)
                : NameBox.Text.Trim();
            item.CustomGlyph = def?.Glyph ?? "\uE8A5";
        }
        else if (_kind == AddKind.Group)
        {
            item.Kind = OrbitItemKind.Group;
            item.DisplayName = string.IsNullOrWhiteSpace(NameBox.Text) ? "Group" : NameBox.Text.Trim();
            item.CustomGlyph = "\uE838";
            // A group is the one kind with no target row and no field describing its contents, so
            // an edit of one has to bring both across itself. Without this, renaming a group would
            // empty it — which is the opposite of what anyone opens "Edit" on a group to do.
            if (_existing is not null && _existing.IsGroup)
            {
                item.Target = _existing.Target;
                item.Children.AddRange(_existing.Children);
            }
        }
        else
        {
            var target = TargetBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                ShowError("Enter a target path or URL.");
                return;
            }
            // "swiftx:" targets are reserved for built-in items (Settings, Close) — otherwise a
            // hand-typed duplicate is indistinguishable from the real one to OrbitManager's
            // dispatch, and could hijack or shadow it.
            if (target.StartsWith("swiftx:", StringComparison.OrdinalIgnoreCase))
            {
                ShowError("That target is reserved for a built-in item.");
                return;
            }

            item.Kind = _kind switch
            {
                AddKind.File => OrbitItemKind.File,
                AddKind.Folder => OrbitItemKind.Folder,
                AddKind.Link => OrbitItemKind.WebLink,
                AddKind.Shortcut => OrbitItemKind.Shortcut,
                _ => OrbitItemKind.Application,
            };
            item.Target = target;
            item.DisplayName = string.IsNullOrWhiteSpace(NameBox.Text)
                ? OrbitItemFactory.SuggestName(target)
                : NameBox.Text.Trim();
        }

        // An edit must never append. The caller's callback writes the result back over the item it
        // replaced, wherever in the tree that lives, so an added-to-the-list copy as well would
        // leave the orbit holding both the old edit and the new one. The callback is the only exit
        // an edit has: with none there is nowhere to write it back to, and dropping it is the only
        // honest answer — appending would be an add the user never asked for.
        if (_onAdded is not null)
            _onAdded(item);
        else if (_existing is null)
        {
            _targetList.Add(item);
            _manager.NotifyItemsChanged();
        }
        Close();
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
        // An InfoBar opening adds a row the window had not allowed for. Without this the error
        // explaining what went wrong can push the buttons that fix it off the bottom edge.
        FitWindowToContent();
    }

    private void HideError()
    {
        ErrorBar.IsOpen = false;
        ErrorBar.Message = string.Empty;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
