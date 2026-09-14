using SwiftX.Controls;
using SwiftX.Models;
using SwiftX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace SwiftX.ToolPages;

/// <summary>
/// One of the two page archetypes (design doc §9.2): one or two text panes, an optional auxiliary
/// panel, a command bar and a status bar. Concrete pages assemble their own pane layout (they
/// differ in shape — JWT's three decoded sections, Regex's pattern+subject+groups table, Text
/// Diff's two inputs — so this base supplies the shared chrome rather than a single rigid pane
/// grid) via <see cref="SetBody"/>, then call <see cref="InitializeChrome"/> once from the end of
/// their constructor.
/// </summary>
public abstract class EditorToolPage : ToolPage
{
    /// <summary>Width of a label-less command button — enough for the 20px glyph plus a normal
    /// touch-friendly margin, against the 68px an AppBarButton reserves for a label.</summary>
    private const double IconOnlyButtonWidth = 40;

    protected ToolStatusBar StatusBar { get; } = new();

    /// <summary>
    /// Exposed read-only so a page can measure against the bar's actual rendered layout (see
    /// <c>RegexPage</c>'s pattern field, which has to know exactly where the real Clear button
    /// ends up rather than guess a constant — the stock <c>CommandBar</c> template doesn't let its
    /// <see cref="SetOptions"/> content stretch to fill the row on its own).
    /// </summary>
    protected CommandBar CommandBar => _commandBar;

    private readonly CommandBar _commandBar = new()
    {
        DefaultLabelPosition = CommandBarDefaultLabelPosition.Right,
        IsOpen = false,
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
        HorizontalContentAlignment = HorizontalAlignment.Left,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// The status/privacy footer row — collapsed (zero height) until a page opts in via
    /// <see cref="SetFooter"/>, so pages that don't call it look exactly as before. Counts and
    /// privacy live in this one row rather than two stacked rows: the counts (<see cref="StatusBar"/>)
    /// sit on the left, an optional page-specific control (Base64's URL-safe checkbox) sits right
    /// after them, an optional second control (Secure values' info button) sits immediately before
    /// the privacy indicator, and the privacy indicator itself is pinned to the right edge.
    /// </summary>
    private readonly Grid _footerRow = new()
    {
        Visibility = Visibility.Collapsed,
        Padding = new Thickness(0, 0, 12, 0),
    };

    private readonly ContentPresenter _footerLeftExtra = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly ContentPresenter _footerRightExtra = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly FrameworkElement _privacyIndicator;
    private readonly FontIcon _privacyIcon = new()
    {
        // Built from the codepoint, not a literal character in source, so the glyph can't be
        // silently swapped for a different (or invisible) private-use character by a stray
        // copy/paste — see the memory note on this exact failure mode.
        Glyph = char.ConvertFromUtf32(0xEA18),
        FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
        FontSize = 14,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Grid _root = new();

    /// <summary>
    /// This page's own live theme — <see cref="ToolPage.HostManager"/>'s config, read fresh on
    /// every call rather than captured once, so a value read from here is never one step behind a
    /// change made in Settings while this window is open.
    /// </summary>
    protected AppTheme Theme => HostManager.Config.Theme;

    /// <summary>
    /// A SwiftX theme brush, resolved against this page's own <c>ActualTheme</c> and live
    /// <see cref="Theme"/> — never <c>Application.Current.Resources</c>, which resolves against
    /// the application's ambient theme and silently disagrees with what this specific window is
    /// actually showing the moment a SwiftX theme pick and the OS/App theme diverge (see
    /// <see cref="ThemeHelper"/>'s own warning on exactly this). Every brush this class and its
    /// subclasses paint goes through here now, in place of the <c>Application.Current.Resources</c>
    /// lookups (mostly stock WinUI system keys — <c>CardBackgroundFillColorDefaultBrush</c> and
    /// its neighbours) each used to carry.
    /// </summary>
    protected Microsoft.UI.Xaml.Media.Brush ThemeBrush(string key) => ThemeHelper.Brush(this, Theme, key);

    /// <summary>
    /// The live accent colour: the user's own pick, or the theme's own built-in default when they
    /// have not made one — computed fresh each call, the same way <c>ShellWindow</c>'s and
    /// <c>AddItemWindow</c>'s own <c>CurrentAccentColor</c> are, so a colour read from here during
    /// a live change in Settings is never stale. This is what the tool pages had no concept of at
    /// all before this pass — the drag-over border and the "everything but Copy/Delete" hover tint
    /// both used to read WinUI's own <c>AccentFillColorDefaultBrush</c>, the Windows system accent,
    /// which is a different colour from SwiftX's own the moment a user picks one.
    /// </summary>
    protected Windows.UI.Color CurrentAccentColor() =>
        AccentPalette.Resolve(HostManager.Config, AccentPalette.IsDark(HostManager.Config, this));

    protected EditorToolPage(OrbitManager manager) : base(manager)
    {
        _privacyIndicator = BuildPrivacyIndicator();

        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_commandBar, 0);
        Grid.SetRow(_footerRow, 2);

        _footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(StatusBar, 0);
        Grid.SetColumn(_footerLeftExtra, 1);
        Grid.SetColumn(_footerRightExtra, 3);
        Grid.SetColumn(_privacyIndicator, 4);
        _footerRow.Children.Add(StatusBar);
        _footerRow.Children.Add(_footerLeftExtra);
        _footerRow.Children.Add(_footerRightExtra);
        _footerRow.Children.Add(_privacyIndicator);

        _root.Children.Add(_commandBar);
        _root.Children.Add(_footerRow);
        base.Content = _root;
    }

    /// <summary>
    /// Puts a page's own option controls — encode/decode, ignore-whitespace, mask profile —
    /// on the <em>left</em> of the command bar, sharing the one toolbar row with the commands,
    /// which stay right-aligned. A tool's settings and its actions belong on the same line; a
    /// second options strip above the editor only ate vertical space and read as a separate thing.
    /// Call once, from the derived constructor.
    /// </summary>
    protected void SetOptions(FrameworkElement options)
    {
        options.HorizontalAlignment = HorizontalAlignment.Left;
        _commandBar.Content = options;
    }

    /// <summary>An options strip laid out the way <see cref="SetOptions"/> expects.</summary>
    protected static StackPanel OptionsBar() => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12, 0, 8, 0),
    };

    /// <summary>A caption sitting beside a control in an <see cref="OptionsBar"/>.</summary>
    protected static TextBlock OptionLabel(string text) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>
    /// A caption sitting beside a control in an <see cref="OptionsBar"/>, the same as
    /// <see cref="OptionLabel"/> — except it also sets <paramref name="control"/>'s
    /// <c>AutomationProperties.Name</c> to the same text, the toolbar-row equivalent of
    /// <see cref="FormToolPage.LabelledRow"/>. A <c>Header</c> is the right fix for a vertical form
    /// row (<see cref="FormToolPage"/>); it is the wrong one here, since it stacks the caption
    /// above the control and breaks the toolbar's single-line layout, so this pairs the same
    /// <see cref="TextBlock"/> caption with an explicit automation name instead of a visual
    /// <c>Header</c> — the visible label was never wired to the control it sits beside, so Narrator
    /// read the control with no name at all.
    /// </summary>
    protected static TextBlock OptionLabelFor(FrameworkElement control, string text)
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, text);
        return OptionLabel(text);
    }

    /// <summary>Every pane this page has built, paired with which side it is — so a theme change
    /// can repaint each one without either the base class or the page having to keep any other
    /// record of them.</summary>
    private readonly List<(Border Border, bool Secondary)> _panes = new();

    /// <summary>
    /// Wraps a pane in its own surface. Input and output sit on slightly different backgrounds so
    /// the eye can tell at a glance which half it is reading — a one-step tint apart, not a
    /// contrast jump, since both are still just text on the window's glass. The two backgrounds are
    /// the app's own <c>ShellCardBackgroundBrush</c>/<c>ShellPillBrush</c> pair (the same one-step
    /// relationship the Home screen's nav-box card and its inset pill already carry) rather than
    /// WinUI's <c>CardBackgroundFillColorDefault/SecondaryBrush</c>, which is what these panes
    /// actually were until this pass and is precisely the mechanism <see cref="ThemeBrush"/>'s own
    /// remarks warn about.
    /// </summary>
    /// <param name="secondary">True for the output/derived side of a two-pane tool.</param>
    protected Border Pane(FrameworkElement content, bool secondary = false)
    {
        var pane = new Border
        {
            Child = content,
            BorderThickness = secondary ? new Thickness(1, 0, 0, 0) : new Thickness(0),
        };
        _panes.Add((pane, secondary));
        PaintPane(pane, secondary);
        return pane;
    }

    private void PaintPane(Border pane, bool secondary)
    {
        pane.Background = ThemeBrush(secondary ? "ShellPillBrush" : "ShellCardBackgroundBrush");
        pane.BorderBrush = ThemeBrush("ShellCardBorderBrush");
    }

    /// <summary>
    /// Which hover tint a button's icon takes on pointer-over/pressed — shared across every tool
    /// page (design doc: one hover language everywhere the four built-in tools show a Copy or a
    /// Delete/Clear) so Copy is always blue and Delete/Clear always red wherever either appears,
    /// and every other action (Format, Validate, Encode/Decode, Open file, Edit, Refresh, Save,
    /// Unlock…) gets the app's own accent instead of WinUI's default neutral-grey hover. Icon
    /// colour only — see <see cref="ApplyHoverIconColor(Button, HoverAccent)"/>, which never
    /// touches Background.
    /// </summary>
    protected enum HoverAccent { Accent, Copy, Delete }

    /// <summary>Maps a <see cref="ToolCommand.Id"/> to the hover tint its command-bar button
    /// should take. <see cref="ToolCommand.Ids.Copy"/> is always blue, <see cref="ToolCommand.Ids.Clear"/>
    /// always red; every other id — including a page-specific one like Base64's "base64-encode" —
    /// falls through to the accent default, so a page never has to opt in by hand.</summary>
    private static HoverAccent HoverAccentFor(string commandId) => commandId switch
    {
        ToolCommand.Ids.Copy => HoverAccent.Copy,
        ToolCommand.Ids.Clear => HoverAccent.Delete,
        _ => HoverAccent.Accent,
    };

    /// <summary>
    /// Shadows the framework key a plain <see cref="Button"/>'s stock style reads its pointer-over
    /// and pressed icon colour from (<c>ButtonForegroundPointerOver</c>/<c>...Pressed</c>) on this
    /// one button instance — the same "closer resource wins" override <c>ShellWindow</c>'s own
    /// accent-colour and hot-corner buttons already rely on
    /// (<c>ShellWindow.xaml.cs</c>'s <c>ApplyAccentColorOverride</c>/<c>AddHotCornerButton</c>),
    /// rather than replacing the whole control template. Deliberately leaves Background alone —
    /// the ask is a coloured icon on hover, not a filled hover chip. A <see cref="FontIcon"/>
    /// content has no <c>Foreground</c> of its own set, so it inherits the Button's, the same way
    /// it already inherits the icon colour these buttons show at rest.
    /// <para>
    /// Instance, not static, now that the accent tint needs <see cref="CurrentAccentColor"/> —
    /// re-run this against an already-built button (a concrete page's own <c>ApplyTheme</c>
    /// override, or one it built once at construction and never rebuilds) to repaint it after a
    /// live theme or accent change.
    /// </para>
    /// </summary>
    protected void ApplyHoverIconColor(Button button, HoverAccent accent)
    {
        var brush = HoverAccentBrush(accent);
        button.Resources["ButtonForegroundPointerOver"] = brush;
        button.Resources["ButtonForegroundPressed"] = brush;
    }

    /// <summary>Same idea as the <see cref="Button"/> overload, for the <see cref="AppBarButton"/>s
    /// <see cref="InitializeChrome"/> builds from <see cref="ToolPage.Commands"/> — a different
    /// control with its own template and its own key names. Its stock template ties the icon and
    /// any visible label to this same key, so a command whose label happens to be visible (Format,
    /// Encode/Decode — anything not <see cref="ToolCommand.IconOnly"/>) takes the tint on both,
    /// which reads as one coherent "this is now highlighted" rather than a mismatch.</summary>
    private void ApplyHoverIconColor(AppBarButton button, HoverAccent accent)
    {
        var brush = HoverAccentBrush(accent);
        button.Resources["AppBarButtonForegroundPointerOver"] = brush;
        button.Resources["AppBarButtonForegroundPressed"] = brush;
    }

    /// <summary>
    /// Copy reads from SwiftX's own <c>SwiftXCopyHoverBrush</c> (Themes/Colors.xaml), not WinUI's
    /// <c>SystemFillColorAttentionBrush</c> — that framework key is itself just a tint of the
    /// system accent colour, so on a machine whose Windows accent isn't blue, Copy's hover would
    /// have silently matched every other button's instead of standing apart from Delete's fixed
    /// red. Delete reads <c>ToolCriticalBrush</c> — <see cref="ThemeHelper"/>'s own copy of Fluent's
    /// critical red, resolved against this page rather than the app-ambient theme (see
    /// <see cref="ThemeBrush"/>) — and every other action takes <see cref="CurrentAccentColor"/>,
    /// the app's own accent, in place of the Windows system accent
    /// <c>AccentFillColorDefaultBrush</c> used to hand back.
    /// </summary>
    private Microsoft.UI.Xaml.Media.Brush HoverAccentBrush(HoverAccent accent) => accent switch
    {
        HoverAccent.Delete => ThemeBrush("ToolCriticalBrush"),
        HoverAccent.Copy => ThemeBrush("SwiftXCopyHoverBrush"),
        _ => new Microsoft.UI.Xaml.Media.SolidColorBrush(CurrentAccentColor()),
    };

    /// <summary>Sets the page's own pane layout. Call once, from the derived constructor.</summary>
    protected void SetBody(FrameworkElement body)
    {
        Grid.SetRow(body, 1);
        _root.Children.Add(body);
    }

    /// <summary>
    /// Shows the footer row — counts (<see cref="StatusBar"/>) on the left, the privacy
    /// indicator pinned to the right, both in one row. Call once, from the derived constructor.
    /// </summary>
    /// <param name="leftExtra">A control that belongs beside the counts, such as Base64's
    /// URL-safe checkbox.</param>
    /// <param name="rightExtra">A control that belongs immediately before the privacy indicator,
    /// such as Secure values' info button.</param>
    protected void SetFooter(FrameworkElement? leftExtra = null, FrameworkElement? rightExtra = null)
    {
        if (leftExtra is not null)
        {
            leftExtra.VerticalAlignment = VerticalAlignment.Center;
            _footerLeftExtra.Margin = new Thickness(16, 0, 0, 0);
            _footerLeftExtra.Content = leftExtra;
        }
        if (rightExtra is not null)
        {
            rightExtra.VerticalAlignment = VerticalAlignment.Center;
            _footerRightExtra.Content = rightExtra;
        }
        _footerRow.Visibility = Visibility.Visible;
    }

    /// <summary>The rounded corner every "chat bubble" hover tip and info popup shares, so the
    /// privacy tooltip and Secure values' info flyout read as the same affordance.</summary>
    private static readonly CornerRadius BubbleCornerRadius = new(12);

    /// <summary>Shared with <see cref="BuildBubbleFlyout"/> so its content wraps at the exact
    /// same width as <see cref="BuildBubbleToolTip"/>'s — two different controls rendering one
    /// visual affordance only reads as the same bubble if nothing about its size gives it away.</summary>
    private const double BubbleMaxWidth = 260;

    /// <summary>Shared with <see cref="BuildBubbleFlyout"/>'s presenter style for the same reason
    /// as <see cref="BubbleMaxWidth"/> — identical padding, not just identical corner radius and
    /// colours.</summary>
    private static readonly Thickness BubblePadding = new(10, 8, 10, 8);

    /// <summary>The app's own card surface, not the OS's tooltip chrome — a hover bubble that
    /// used the system tooltip background could land on a colour that doesn't track SwiftX's own
    /// light/dark switch (Settings ▸ General), since that background is a Windows setting, not
    /// this app's theme.</summary>
    private Microsoft.UI.Xaml.Media.Brush BubbleBackground => ThemeBrush("ShellCardBackgroundBrush");
    private Microsoft.UI.Xaml.Media.Brush BubbleBorder => ThemeBrush("ShellCardBorderBrush");

    /// <summary>
    /// A hover-only tip styled as a rounded speech bubble in the app's own theme colours, rather
    /// than the OS's plain rectangular tooltip <em>or</em> a manually driven one — a
    /// <see cref="ToolTip"/> not attached via <see cref="ToolTipService.SetToolTip"/> has no
    /// framework-managed connection for its <see cref="ToolTip.IsOpen"/> setter to work against;
    /// setting <c>IsOpen</c> on one throws a native <see cref="NullReferenceException"/> straight
    /// out of the WinRT ABI. So this is hover-triggered only, full stop — see
    /// <see cref="BuildBubbleFlyout"/> for the click-triggered counterpart, which is a different
    /// control precisely because programmatic open/close needs one, styled to be visually
    /// indistinguishable from this one.
    /// </summary>
    protected ToolTip BuildBubbleToolTip(string text) => new()
    {
        Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = BubbleMaxWidth },
        Background = BubbleBackground,
        BorderBrush = BubbleBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = BubbleCornerRadius,
        Padding = BubblePadding,
    };

    /// <summary>
    /// A click-to-open popup styled as the same speech bubble as <see cref="BuildBubbleToolTip"/>
    /// — same corner radius, colours, padding and max width — for the bubbles that must never
    /// appear on hover (Secure values' info button). A <see cref="Flyout"/> rather than a
    /// manually toggled <see cref="ToolTip"/> because it is actually built for on-demand
    /// open/close — light-dismiss (closing on the next click anywhere else) comes for free
    /// instead of needing to be reimplemented by hand. <see cref="FlyoutPresenter.IsDefaultShadowEnabledProperty"/>
    /// is off: a Flyout's default drop shadow is what would otherwise give it away as a different
    /// kind of popup than the flat tooltip bubble.
    /// </summary>
    protected Flyout BuildBubbleFlyout(string text)
    {
        var presenterStyle = new Style(typeof(FlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(Control.BackgroundProperty, BubbleBackground));
        presenterStyle.Setters.Add(new Setter(Control.BorderBrushProperty, BubbleBorder));
        presenterStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        presenterStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, BubbleCornerRadius));
        presenterStyle.Setters.Add(new Setter(Control.PaddingProperty, BubblePadding));
        presenterStyle.Setters.Add(new Setter(FlyoutPresenter.IsDefaultShadowEnabledProperty, false));

        return new Flyout
        {
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = BubbleMaxWidth },
            FlyoutPresenterStyle = presenterStyle,
            Placement = FlyoutPlacementMode.Top,
        };
    }

    /// <summary>
    /// The "your data stays on this PC" indicator every local-only tool's footer ends with: a
    /// green shield glyph alone — the caption is the bubble tooltip, shown on hover only.
    /// (Secure values' info button is the click-only counterpart — see
    /// <c>SecureValuesPage.BuildInfoButton</c> — deliberately not the same trigger, since a
    /// hover-only tip on a hand-cursor-less info glyph would never be discovered, and a
    /// click-only privacy indicator sitting in every tool's footer would take a click just to
    /// read a line that's the same on every page.)
    /// </summary>
    private FrameworkElement BuildPrivacyIndicator()
    {
        var indicator = new Border
        {
            Child = _privacyIcon,
            Padding = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var text = Loc.Get("Tool.PrivacyFooter");
        ToolTipService.SetToolTip(indicator, BuildBubbleToolTip(text));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(indicator, text);
        PaintPrivacyIndicator();
        return indicator;
    }

    private void PaintPrivacyIndicator()
    {
        _privacyIcon.Foreground = ThemeBrush("ToolSuccessBrush");
        // The tooltip is a whole separate popup control, built once and handed to
        // ToolTipService — rebuilding it here is what carries the new theme onto its own
        // background/border, which a Foreground repaint on the icon above cannot reach.
        ToolTipService.SetToolTip(_privacyIndicator, BuildBubbleToolTip(Loc.Get("Tool.PrivacyFooter")));
    }

    /// <summary>
    /// The commands this page was built with, captured by <see cref="InitializeChrome"/>. Most
    /// pages spell <c>Commands</c> as a collection expression, so every read of it allocates a
    /// fresh set of <see cref="ToolCommand"/> objects and re-runs their <c>Loc.Get</c> lookups —
    /// and the tunnelling key handler below reads it on <em>every keystroke</em>. Capturing once
    /// also means the buttons on the bar and the shortcuts that fire them are the same objects
    /// rather than two parallel sets that merely happen to close over the same lambdas.
    /// </summary>
    private IReadOnlyList<ToolCommand> _boundCommands = [];

    /// <summary>The bound commands paired with the button each landed in the bar — only entries
    /// whose <see cref="ToolCommand.IsVisible"/> is non-null are worth tracking, but every command
    /// with a button is kept here regardless, so <see cref="RefreshCommandVisibility"/> is one
    /// simple loop rather than needing a second pass over <see cref="ToolPage.Commands"/>.</summary>
    private readonly List<(ToolCommand Command, AppBarButton Button)> _commandButtons = new();

    /// <summary>Builds the command bar and keyboard accelerators from <see cref="ToolPage.Commands"/>.
    /// Call once, at the end of the derived constructor, after every field a command's lambda
    /// closes over has been assigned.</summary>
    protected void InitializeChrome()
    {
        _boundCommands = Commands;

        // A page that moved every command into the title bar (see ToolPage.HeaderActions)
        // has nothing left for this row to show — an empty CommandBar still reserves its full
        // height, which would leave a dead strip under the title.
        _commandBar.Visibility = _boundCommands.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        // Commands are matched on the way *down* to the focused control, not on the way back up.
        // A KeyboardAccelerator alone is not enough once the focused control is a text editor: the
        // editor handles the key first (Ctrl+Enter inserts a break in a rich-edit document) and the
        // accelerator never runs. Tunnelling gets the tool's own shortcuts in first; the
        // accelerators below stay because they are what renders the "Ctrl+Enter" hint on a button.
        PreviewKeyDown += OnPreviewKeyDown;

        _commandBar.PrimaryCommands.Clear();
        _commandButtons.Clear();
        foreach (var command in _boundCommands)
        {
            // A command carrying a control rather than an action (the formatter's indent spinner)
            // rides in the same row as its neighbours instead of being exiled to the left strip,
            // so an option that belongs next to a specific button can sit next to it.
            if (command.Content is { } inline)
            {
                _commandBar.PrimaryCommands.Add(new AppBarElementContainer
                {
                    Content = inline,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 4, 0),
                });
                continue;
            }

            var button = new AppBarButton
            {
                Label = command.Label,
                Icon = new FontIcon { Glyph = command.Glyph },
            };

            // The command's stable id, not its translated label, is what UI automation addresses
            // this button by. See ToolCommand.Id.
            if (command.Id.Length > 0)
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, command.Id);

            // An icon-only command shows its glyph alone; the label survives as the tooltip and
            // as the automation name, so the button stays discoverable and readable to Narrator.
            if (command.IconOnly)
            {
                button.LabelPosition = CommandBarLabelPosition.Collapsed;
                // An AppBarButton is 68px wide so its label has room. With no label that is mostly
                // empty space, and five of them in a row read as unrelated buttons rather than one
                // group of actions.
                button.Width = IconOnlyButtonWidth;
                ToolTipService.SetToolTip(button, command.Label);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, command.Label);
            }

            if (command.IsVisible is { } isVisible)
                button.Visibility = isVisible() ? Visibility.Visible : Visibility.Collapsed;

            button.Click += (_, _) => command.Invoke();
            ApplyHoverIconColor(button, HoverAccentFor(command.Id));
            _commandBar.PrimaryCommands.Add(button);
            _commandButtons.Add((command, button));

            if (command.Key is { } key)
            {
                var accelerator = new KeyboardAccelerator { Key = key, Modifiers = command.Modifiers };
                accelerator.Invoked += (_, e) =>
                {
                    command.Invoke();
                    e.Handled = true;
                };
                KeyboardAccelerators.Add(accelerator);
            }
        }
    }

    /// <summary>
    /// Re-evaluates every bound command's <see cref="ToolCommand.IsVisible"/> and shows or hides
    /// its button to match. A command with no <see cref="ToolCommand.IsVisible"/> is skipped —
    /// its button's visibility was set once in <see cref="InitializeChrome"/> and never needs
    /// touching again. Call whenever page state a command's visibility depends on changes.
    /// </summary>
    protected void RefreshCommandVisibility()
    {
        foreach (var (command, button) in _commandButtons)
        {
            if (command.IsVisible is { } isVisible)
                button.Visibility = isVisible() ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Repaints every piece of shared chrome this class owns a reference to: every pane
    /// <see cref="Pane"/> ever built, every command-bar button's hover tint, and the privacy
    /// indicator. Called by <c>ToolWindowBase.ApplyTheme</c> after it repaints its own window
    /// chrome — and, just as importantly, once synchronously during this page's own construction
    /// path (<c>ToolWindowBase</c>'s constructor calls it before the window is ever shown), which
    /// is what makes the very first paint correct: a page's own constructor runs before it has a
    /// <c>XamlRoot</c> at all, so anything computed from <c>ActualTheme</c> during that constructor
    /// is a guess, not an answer — this is the first point at which it is safe to trust.
    /// <para>
    /// A page whose own content needs repainting too (<c>CertificatePage</c>'s cached certificate,
    /// <c>SecureValuesPage</c>'s row list) overrides this, calls <c>base.ApplyTheme()</c>, and then
    /// re-runs whatever already rebuilds that content from scratch — the same rebuild a data change
    /// already triggers, not a second parallel repaint path.
    /// </para>
    /// </summary>
    public override void ApplyTheme()
    {
        foreach (var (border, secondary) in _panes)
            PaintPane(border, secondary);

        foreach (var (command, button) in _commandButtons)
            ApplyHoverIconColor(button, HoverAccentFor(command.Id));

        PaintPrivacyIndicator();
    }

    /// <summary>
    /// The lighter counterpart <see cref="ToolPage.ApplyAccent"/> asks for: only the command-bar
    /// buttons whose hover tint actually <em>is</em> the accent (Copy and Delete/Clear are fixed
    /// colours, untouched by an accent change) get repainted, and nothing here re-renders a page's
    /// own content the way <see cref="ApplyTheme"/>'s overrides do — see that method's base class
    /// remarks, and <c>ToolPage.ApplyAccent</c>'s, for why a per-frame cost during a colour-picker
    /// drag is the thing this split exists to avoid.
    /// </summary>
    public override void ApplyAccent()
    {
        foreach (var (command, button) in _commandButtons)
        {
            if (HoverAccentFor(command.Id) == HoverAccent.Accent)
                ApplyHoverIconColor(button, HoverAccent.Accent);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled)
            return;

        var modifiers = CurrentModifiers();
        foreach (var command in _boundCommands)
        {
            if (command.Content is not null || command.Key != e.Key || command.Modifiers != modifiers)
                continue;
            command.Invoke();
            e.Handled = true;
            return;
        }
    }

    /// <summary>The modifier keys held right now. Read from the input system rather than from the
    /// event, which carries no modifier state of its own.</summary>
    private static Windows.System.VirtualKeyModifiers CurrentModifiers()
    {
        var modifiers = Windows.System.VirtualKeyModifiers.None;
        if (IsDown(Windows.System.VirtualKey.Control))
            modifiers |= Windows.System.VirtualKeyModifiers.Control;
        if (IsDown(Windows.System.VirtualKey.Shift))
            modifiers |= Windows.System.VirtualKeyModifiers.Shift;
        if (IsDown(Windows.System.VirtualKey.Menu))
            modifiers |= Windows.System.VirtualKeyModifiers.Menu;
        return modifiers;

        static bool IsDown(Windows.System.VirtualKey key) =>
            Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(key)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }
}
