using SwiftX.Models;
using SwiftX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace SwiftX.ToolPages;

/// <summary>
/// Local key/secret store. Values are DPAPI-protected at rest and masked on screen; Copy places
/// the real secret on the clipboard. The list is two visually distinct pieces: a single pinned
/// input row on top (see <see cref="CreateInputRow"/>) for typing a new key/value pair, and a
/// plain table of already-saved entries below it (a column header from
/// <see cref="CreateTableHeaderRow"/> plus one row per entry from <see cref="CreateTableRow"/>,
/// no per-row card/background) with Copy/Edit/Delete. Save lives in the title bar (see
/// <see cref="HeaderActions"/>) as a manual alternative to pressing Enter in the input row —
/// nothing in this page ever commits on blur, only on an explicit Enter or button press.
/// </summary>
public sealed class SecureValuesPage : EditorToolPage
{
    // Icon glyphs are built from their codepoint, never as a string literal in source — a
    // literal here can silently become a different (or invisible) private-use character on a
    // stray copy/paste, with nothing visibly wrong to catch by eye. See the memory note on this
    // exact failure mode.
    private const int CopyGlyph = 0xE8C8;
    private const int EditGlyph = 0xE70F;
    private const int DeleteGlyph = 0xE74D;
    private const int RefreshGlyph = 0xE72C;
    private const int SaveGlyph = 0xE74E;
    private const int InfoGlyph = 0xE946;

    // Shared by CreateTableHeaderRow and CreateTableRow so the header always lines up with the
    // data underneath it — one source of truth instead of matching literals in two places.
    private const double KeyColumnWidth = 180;
    private const double ActionColumnWidth = 40;

    /// <summary>Already-saved entries only — the pinned input row's in-progress entry lives in
    /// <see cref="_inputEntry"/> instead, so it never needs special-casing here (no "is this the
    /// blank one" checks, no ordering games to keep it first).</summary>
    private readonly List<SecureValueEntry> _entries = [];
    private SecureValueEntry _inputEntry = new();

    // No inter-row spacing: table rows are separated by their own bottom hairline (see
    // CreateTableRow) rather than a gap, so they read as one continuous table instead of a stack
    // of separate blocks. The input row supplies its own bottom margin for breathing room above
    // the header, since it isn't part of the table.
    private readonly StackPanel _rows = new() { Spacing = 0 };

    /// <summary>Rows that can still be flushed by the header Save button. In practice this is
    /// just the pinned input row: table rows are born already-saved (<c>KeyLocked = true</c>)
    /// and SaveAll skips locked rows, so registering them here would be a permanently-skipped
    /// no-op — see <see cref="CreateTableRow"/>, which deliberately doesn't add itself.</summary>
    private readonly List<(RowState State, Func<bool> Commit)> _rowActions = [];
    private readonly FrameworkElement _headerActions;

    // Save and the info button are built once and never rebuilt (unlike the row buttons below),
    // so ApplyTheme/ApplyAccent repaint them directly rather than through a rebuild.
    private Button _saveButton = null!;
    private Button _infoButton = null!;

    // Every row-level button built with HoverAccent.Accent (Refresh on the input row, Edit on a
    // saved row) — Copy and Delete are fixed colours untouched by an accent change, so tracking
    // them here would just mean repainting them with the value they already have. Cleared and
    // repopulated by RebuildRows every time it tears down and rebuilds the row set, so this never
    // holds a reference to a control that has since been discarded.
    private readonly List<Button> _rowAccentButtons = [];

    private bool _isDirty;
    private bool _loading;

    public SecureValuesPage(OrbitManager manager) : base(manager)
    {
        var scroller = new ScrollViewer
        {
            Content = _rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(16, 12, 16, 16),
        };

        _headerActions = BuildHeaderActions();

        SetBody(Pane(scroller));
        SetFooter(rightExtra: BuildInfoButton());
        StatusBar.SetUntouched();
        InitializeChrome();
        LoadEntries();
    }

    /// <summary>
    /// Repaints the shared chrome via <see cref="EditorToolPage.ApplyTheme"/>, then rebuilds every
    /// row from scratch via <see cref="RebuildRows"/> — the same mechanism a commit or a delete
    /// already uses to redraw with fresh brushes, rather than a second parallel repaint path
    /// walking every row's <c>Border</c>/<c>TextBlock</c> by hand. This does mean a row that is
    /// mid-Edit loses its uncommitted keystrokes if the live theme changes while it is open — an
    /// edge case not worth threading a page-wide "is anything being edited" flag through to
    /// preserve, since nothing here commits that half-typed text on its own either way (see
    /// <see cref="CreateTableRow"/>'s own Commit-on-Enter-only contract).
    /// </summary>
    public override void ApplyTheme()
    {
        base.ApplyTheme();
        RebuildRows();
        ApplyHoverIconColor(_saveButton, HoverAccent.Accent);
        ApplyHoverIconColor(_infoButton, HoverAccent.Accent);
    }

    /// <summary>The cheap counterpart: repaints only the Accent-tinted buttons, without the full
    /// <see cref="RebuildRows"/> a live colour-picker drag would otherwise pay on every frame.</summary>
    public override void ApplyAccent()
    {
        ApplyHoverIconColor(_saveButton, HoverAccent.Accent);
        ApplyHoverIconColor(_infoButton, HoverAccent.Accent);
        foreach (var button in _rowAccentButtons)
            ApplyHoverIconColor(button, HoverAccent.Accent);
    }

    /// <summary>
    /// The blurb explaining DPAPI, behind an info button in the footer rather than sitting in the
    /// page as a sentence of always-visible text. Styled like the privacy indicator's own bubble
    /// popup, but click-only rather than hover-only: a plain
    /// <see cref="ToolTip"/> only works when the framework opens and closes it via a hover
    /// trigger it owns (see <c>EditorToolPage.BuildBubbleToolTip</c>'s doc comment — a manually
    /// toggled <c>IsOpen</c> on one crashes), so the on-click case uses
    /// <c>EditorToolPage.BuildBubbleFlyout</c> instead, the same rounded speech-bubble styling
    /// built on a control that's actually meant to be shown on demand.
    /// </summary>
    private FrameworkElement BuildInfoButton()
    {
        _infoButton = new Button
        {
            Content = new FontIcon { Glyph = char.ConvertFromUtf32(InfoGlyph), FontSize = 14 },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        var flyout = BuildBubbleFlyout(Loc.Get("SecureValues.Blurb"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_infoButton, Loc.Get("SecureValues.InfoLabel"));
        _infoButton.Click += (_, _) => flyout.ShowAt(_infoButton);
        ApplyHoverIconColor(_infoButton, HoverAccent.Accent);
        return _infoButton;
    }

    public override ToolKind Kind => ToolKind.SecureValues;
    public override bool IsDirty => _isDirty;

    /// <summary>No command-bar commands — Save lives in the title bar instead (see
    /// <see cref="HeaderActions"/>), which also lets the (now empty) command bar collapse.</summary>
    public override IReadOnlyList<ToolCommand> Commands => [];

    public override FrameworkElement? HeaderActions => _headerActions;

    private int SavedCount => _entries.Count(e => !string.IsNullOrWhiteSpace(e.Key));

    private void LoadEntries()
    {
        _loading = true;
        try
        {
            _entries.Clear();
            _entries.AddRange(SecureValueStore.Load());
            _inputEntry = new SecureValueEntry();
            RebuildRows();
            StatusBar.SetMessage(Loc.Format("SecureValues.Count", SavedCount), isError: false);
        }
        finally
        {
            _loading = false;
        }
    }

    private void Persist()
    {
        if (_loading)
            return;
        try
        {
            SecureValueStore.Save(_entries.Where(e => !string.IsNullOrWhiteSpace(e.Key)));
            _isDirty = false;
            StatusBar.SetMessage(Loc.Get("SecureValues.Saved"), isError: false);
        }
        catch (Exception ex)
        {
            StatusBar.SetMessage(ex.Message, isError: true);
        }
    }

    /// <summary>Flushes the pinned input row if it has an uncommitted key typed into it, then
    /// persists and rebuilds so the newly-saved entry moves into the table below.</summary>
    private void SaveAll()
    {
        bool any = false;
        foreach (var (state, commit) in _rowActions)
        {
            if (state.KeyLocked)
                continue;
            if (commit())
                any = true;
        }
        if (any)
        {
            Persist();
            RebuildRows();
        }
    }

    private void RebuildRows()
    {
        _rows.Children.Clear();
        _rowActions.Clear();
        _rowAccentButtons.Clear();
        _rows.Children.Add(CreateInputRow(_inputEntry));
        _rows.Children.Add(CreateInputDivider());
        if (_entries.Count > 0)
        {
            _rows.Children.Add(CreateTableHeaderRow());
            foreach (var entry in _entries)
                _rows.Children.Add(CreateTableRow(entry));
        }
    }

    /// <summary>Column titles for the saved-values table — shown once above the whole list rather
    /// than repeated per row. Column widths/spacing must match <see cref="CreateTableRow"/>
    /// exactly (both read from the same <see cref="KeyColumnWidth"/>/<see cref="ActionColumnWidth"/>
    /// constants) so the header lines up with the data underneath it.</summary>
    private FrameworkElement CreateTableHeaderRow()
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(KeyColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ActionColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ActionColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ActionColumnWidth) });

        var keyHeader = new TextBlock
        {
            Text = Loc.Get("SecureValues.Key"),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = ThemeBrush("SwiftXMutedBrush"),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var valueHeader = new TextBlock
        {
            Text = Loc.Get("SecureValues.ValueHeader"),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = ThemeBrush("SwiftXMutedBrush"),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Grid.SetColumn(keyHeader, 0);
        Grid.SetColumn(valueHeader, 1);
        grid.Children.Add(keyHeader);
        grid.Children.Add(valueHeader);

        return new Border
        {
            Child = grid,
            Padding = new Thickness(12, 0, 12, 6),
            Margin = new Thickness(0, 0, 0, 4),
            BorderBrush = ThemeBrush("ShellDividerBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    /// <summary>
    /// The single pinned row for typing a new key/value pair — always rendered first (see
    /// <see cref="RebuildRows"/>), so the "blank row" concept no longer needs to live inside
    /// <see cref="_entries"/> or fight for a position in it. Pressing Enter in either box is the
    /// only way to commit it (no blur/tab-away autosave): the entry moves into
    /// <see cref="_entries"/>, <see cref="_inputEntry"/> is replaced with a fresh blank one, and
    /// the page rebuilds so the newly-saved entry appears in the table below with its own
    /// Copy/Edit/Delete buttons — a different control set from this row's, so a full rebuild (not
    /// just a mask/lock toggle) is required to swap them in.
    /// </summary>
    private FrameworkElement CreateInputRow(SecureValueEntry entry)
    {
        var state = new RowState { KeyLocked = false };

        var keyBox = new TextBox
        {
            PlaceholderText = Loc.Get("SecureValues.KeyPlaceholder"),
            Text = entry.Key,
            Width = KeyColumnWidth,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(keyBox, Loc.Get("SecureValues.Key"));

        var valueBox = new TextBox
        {
            PlaceholderText = Loc.Get("SecureValues.ValuePlaceholder"),
            Text = entry.Value,
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(valueBox, Loc.Get("SecureValues.Value"));

        var refreshButton = IconButton(RefreshGlyph, Loc.Get("SecureValues.Refresh"));

        bool Commit()
        {
            var key = keyBox.Text.Trim();
            if (string.IsNullOrEmpty(key))
            {
                StatusBar.SetMessage(Loc.Get("SecureValues.KeyRequired"), isError: true);
                return false;
            }

            entry.Key = key;
            entry.Value = valueBox.Text;
            _entries.Add(entry);
            _inputEntry = new SecureValueEntry();
            _isDirty = true;
            return true;
        }

        _rowActions.Add((state, Commit));

        void CommitAndRefresh()
        {
            if (Commit())
            {
                Persist();
                RebuildRows();
            }
        }

        // Enter is the only way to commit this row — no blur/tab-away autosave, so moving focus
        // off a half-typed entry never surfaces a "key required" error or saves something the
        // user wasn't done typing.
        keyBox.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                CommitAndRefresh();
                e.Handled = true;
            }
        };

        valueBox.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                CommitAndRefresh();
                e.Handled = true;
            }
        };

        refreshButton.Click += (_, _) =>
        {
            // Clears only this row's fields — no persist, no delete, nothing else touched.
            keyBox.Text = "";
            valueBox.Text = "";
            entry.Key = "";
            entry.Value = "";
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(keyBox, 0);
        Grid.SetColumn(valueBox, 1);
        Grid.SetColumn(refreshButton, 2);
        grid.Children.Add(keyBox);
        grid.Children.Add(valueBox);
        grid.Children.Add(refreshButton);

        return new Border
        {
            Child = grid,
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 12),
        };
    }

    /// <summary>The rule marking the end of the input section, always present regardless of
    /// whether there are any saved entries yet — it demarcates the input row, not the table.</summary>
    private Border CreateInputDivider() => new()
    {
        Height = 1,
        Margin = new Thickness(0, 0, 0, 12),
        Background = ThemeBrush("ShellDividerBrush"),
    };

    /// <summary>
    /// One already-saved entry, rendered as a plain table row — no per-row card or background;
    /// <see cref="CreateTableHeaderRow"/> supplies the column titles once above the whole list
    /// instead of every entry getting its own boxed-off section. Both the key and the value are
    /// locked until Edit unlocks them together, and (like the input row above) committing only
    /// happens on Enter, never on blur: tabbing from the key field to the value field within the
    /// same row while editing would otherwise fire a premature blur-commit off the key box alone
    /// and re-lock the row out from under the value the user is about to type into.
    /// </summary>
    private FrameworkElement CreateTableRow(SecureValueEntry entry)
    {
        bool editing = false;

        var keyBox = new TextBox
        {
            PlaceholderText = Loc.Get("SecureValues.KeyPlaceholder"),
            Text = entry.Key,
            Width = KeyColumnWidth,
            IsReadOnly = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(keyBox, Loc.Get("SecureValues.Key"));

        var valueBox = new TextBox
        {
            PlaceholderText = Loc.Get("SecureValues.ValuePlaceholder"),
            Text = ValueMasking.Mask(entry.Value),
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            IsReadOnly = true,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(valueBox, Loc.Get("SecureValues.Value"));

        var copyButton = IconButton(CopyGlyph, Loc.Get("SecureValues.Copy"), HoverAccent.Copy);
        copyButton.IsEnabled = !string.IsNullOrEmpty(entry.Value);

        var editButton = IconButton(EditGlyph, Loc.Get("SecureValues.Edit"));
        var deleteButton = IconButton(DeleteGlyph, Loc.Get("SecureValues.Delete"), HoverAccent.Delete);

        bool Commit()
        {
            var key = keyBox.Text.Trim();
            if (string.IsNullOrEmpty(key))
            {
                StatusBar.SetMessage(Loc.Get("SecureValues.KeyRequired"), isError: true);
                return false;
            }

            entry.Key = key;
            entry.Value = valueBox.Text;
            keyBox.Text = key;
            keyBox.IsReadOnly = true;
            valueBox.IsReadOnly = true;
            editing = false;
            valueBox.Text = ValueMasking.Mask(entry.Value);
            copyButton.IsEnabled = !string.IsNullOrEmpty(entry.Value);
            _isDirty = true;
            return true;
        }

        editButton.Click += (_, _) =>
        {
            editing = true;
            keyBox.IsReadOnly = false;
            valueBox.IsReadOnly = false;
            valueBox.Text = entry.Value;
            keyBox.Focus(FocusState.Programmatic);
            keyBox.SelectAll();
        };

        void CommitOnEnter(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter || !editing)
                return;
            if (Commit())
                Persist();
            e.Handled = true;
        }

        keyBox.KeyDown += CommitOnEnter;
        valueBox.KeyDown += CommitOnEnter;

        copyButton.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(entry.Value))
                return;
            var package = new DataPackage();
            package.SetText(entry.Value);
            // Opted out of clipboard history and cloud roaming — see SecretClipboard for why a
            // value this app went to the trouble of DPAPI-protecting must not then be handed to
            // a feature that syncs it to the user's other devices.
            SecretClipboard.MarkSecret(package.Properties);
            Clipboard.SetContent(package);
            StatusBar.SetMessage(Loc.Get("SecureValues.Copied"), isError: false);
        };

        deleteButton.Click += (_, _) =>
        {
            _entries.RemoveAll(e => e.Id == entry.Id);
            Persist();
            RebuildRows();
            StatusBar.SetMessage(Loc.Format("SecureValues.Count", SavedCount), isError: false);
        };

        // Auto here still resolves to KeyColumnWidth/ActionColumnWidth in practice, since each of
        // those columns' child has that exact width — but the header row (which has no such
        // children to size from) pins the same constants explicitly, so both stay in lockstep.
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(keyBox, 0);
        Grid.SetColumn(valueBox, 1);
        Grid.SetColumn(copyButton, 2);
        Grid.SetColumn(editButton, 3);
        Grid.SetColumn(deleteButton, 4);
        grid.Children.Add(keyBox);
        grid.Children.Add(valueBox);
        grid.Children.Add(copyButton);
        grid.Children.Add(editButton);
        grid.Children.Add(deleteButton);

        // No background/corner radius/full border — a bottom hairline is the only separator, so
        // consecutive entries read as rows of one table instead of a stack of separate cards.
        return new Border
        {
            Child = grid,
            Padding = new Thickness(12, 8, 12, 8),
            BorderBrush = ThemeBrush("ShellDividerBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    private FrameworkElement BuildHeaderActions()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        _saveButton = HeaderIconButton(SaveGlyph, Loc.Get("SecureValues.Save"), SaveAll);
        panel.Children.Add(_saveButton);
        return panel;
    }

    private Button HeaderIconButton(int codepoint, string label, Action action)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = char.ConvertFromUtf32(codepoint), FontSize = 14 },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(button, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        ApplyHoverIconColor(button, HoverAccent.Accent);
        return button;
    }

    private Button IconButton(int codepoint, string label, HoverAccent hover = HoverAccent.Accent)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = char.ConvertFromUtf32(codepoint), FontSize = 14 },
            Width = ActionColumnWidth,
            Height = 32,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(button, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        ApplyHoverIconColor(button, hover);
        if (hover == HoverAccent.Accent)
            _rowAccentButtons.Add(button);
        return button;
    }

    private sealed class RowState
    {
        public bool KeyLocked;
    }
}
