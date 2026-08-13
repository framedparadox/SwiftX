using SwiftX.Models;
using SwiftX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace SwiftX.ToolPages;

/// <summary>
/// Local key/secret store. Values are DPAPI-protected at rest and masked on screen; Copy places
/// the real secret on the clipboard. Add/Save live in the title bar (see
/// <see cref="HeaderActions"/>); a blank row is always present so there's somewhere to type
/// without reaching for a button first.
/// </summary>
public sealed class SecureValuesPage : EditorToolPage
{
    // Icon glyphs are built from their codepoint, never as a string literal in source — a
    // literal here can silently become a different (or invisible) private-use character on a
    // stray copy/paste, with nothing visibly wrong to catch by eye. See the memory note on this
    // exact failure mode.
    private const int CopyGlyph = 0xE8C8;
    private const int DeleteGlyph = 0xE74D;
    private const int AddGlyph = 0xE710;
    private const int SaveGlyph = 0xE74E;
    private const int InfoGlyph = 0xE946;

    private readonly List<SecureValueEntry> _entries = [];
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly List<(RowState State, Func<bool> Commit)> _rowActions = [];
    private readonly FrameworkElement _headerActions;
    private bool _isDirty;
    private bool _loading;

    public SecureValuesPage()
    {
        var header = new TextBlock
        {
            Text = Loc.Get("SecureValues.Heading"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var scroller = new ScrollViewer
        {
            Content = _rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(16, 12, 16, 16),
        };

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var headerBorder = new Border
        {
            Child = header,
            Padding = new Thickness(16, 12, 16, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Grid.SetRow(headerBorder, 0);
        Grid.SetRow(scroller, 1);
        body.Children.Add(headerBorder);
        body.Children.Add(scroller);

        _headerActions = BuildHeaderActions();

        SetBody(Pane(body));
        SetFooter(rightExtra: BuildInfoButton());
        StatusBar.SetUntouched();
        InitializeChrome();
        LoadEntries();
    }

    /// <summary>
    /// The blurb that used to sit above the list, now behind an info button in the footer — a
    /// small centered heading (see <c>header</c> above) reads as a page title rather than a
    /// sentence explaining DPAPI, so the explanation moved to a bubble popup styled like the
    /// privacy indicator's own, but click-only rather than hover-only: a plain
    /// <see cref="ToolTip"/> only works when the framework opens and closes it via a hover
    /// trigger it owns (see <c>EditorToolPage.BuildBubbleToolTip</c>'s doc comment — a manually
    /// toggled <c>IsOpen</c> on one crashes), so the on-click case uses
    /// <c>EditorToolPage.BuildBubbleFlyout</c> instead, the same rounded speech-bubble styling
    /// built on a control that's actually meant to be shown on demand.
    /// </summary>
    private FrameworkElement BuildInfoButton()
    {
        var button = new Button
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
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, Loc.Get("SecureValues.InfoLabel"));
        button.Click += (_, _) => flyout.ShowAt(button);
        return button;
    }

    public override ToolKind Kind => ToolKind.SecureValues;
    public override bool IsDirty => _isDirty;

    /// <summary>No command-bar commands — Add/Save live in the title bar instead (see
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
            EnsureBlankRow();
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

    /// <summary>Appends one blank, unsaved entry — but only when the list doesn't already end
    /// in one — so there's always a row ready to type into. With no saved entries this is the
    /// only (and therefore topmost) row; with saved entries it lands after them.</summary>
    private void EnsureBlankRow()
    {
        if (_entries.Any(e => string.IsNullOrEmpty(e.Key)))
            return;
        _entries.Add(new SecureValueEntry());
    }

    private void AddRow()
    {
        var entry = new SecureValueEntry();
        _entries.Add(entry);
        _rows.Children.Add(CreateRow(entry, keyLocked: false));
        _isDirty = true;
        StatusBar.SetMessage(Loc.Format("SecureValues.Count", SavedCount), isError: false);
    }

    /// <summary>Commits every row that hasn't been saved yet (the always-present blank row, and
    /// any others added since), in one pass, then persists once — not once per row.</summary>
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
            Persist();
    }

    private void RebuildRows()
    {
        _rows.Children.Clear();
        _rowActions.Clear();
        foreach (var entry in _entries)
            _rows.Children.Add(CreateRow(entry, keyLocked: !string.IsNullOrWhiteSpace(entry.Key)));
    }

    private FrameworkElement CreateRow(SecureValueEntry entry, bool keyLocked)
    {
        var state = new RowState { KeyLocked = keyLocked, EditingValue = !keyLocked };

        var keyBox = new TextBox
        {
            PlaceholderText = Loc.Get("SecureValues.KeyPlaceholder"),
            Text = entry.Key,
            Width = 180,
            IsReadOnly = keyLocked,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(keyBox, Loc.Get("SecureValues.Key"));

        var valueBox = new TextBox
        {
            PlaceholderText = Loc.Get("SecureValues.ValuePlaceholder"),
            Text = keyLocked ? ValueMasking.Mask(entry.Value) : entry.Value,
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(valueBox, Loc.Get("SecureValues.Value"));

        var copyButton = IconButton(CopyGlyph, Loc.Get("SecureValues.Copy"));
        copyButton.IsEnabled = !string.IsNullOrEmpty(entry.Value);

        var deleteButton = IconButton(DeleteGlyph, Loc.Get("SecureValues.Delete"));

        void ShowMasked()
        {
            if (state.EditingValue)
                return;
            valueBox.Text = ValueMasking.Mask(entry.Value);
        }

        bool Commit()
        {
            var key = keyBox.Text.Trim();
            if (string.IsNullOrEmpty(key))
            {
                StatusBar.SetMessage(Loc.Get("SecureValues.KeyRequired"), isError: true);
                return false;
            }

            if (state.EditingValue || !state.KeyLocked)
                entry.Value = valueBox.Text;

            entry.Key = key;
            keyBox.Text = key;
            keyBox.IsReadOnly = true;
            state.KeyLocked = true;
            state.EditingValue = false;
            ShowMasked();
            copyButton.IsEnabled = !string.IsNullOrEmpty(entry.Value);
            _isDirty = true;
            return true;
        }

        _rowActions.Add((state, Commit));

        keyBox.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (Commit())
                    Persist();
                e.Handled = true;
            }
        };

        valueBox.GotFocus += (_, _) =>
        {
            state.EditingValue = true;
            valueBox.Text = entry.Value;
        };

        valueBox.LostFocus += (_, _) =>
        {
            if (!state.KeyLocked)
                return;
            entry.Value = valueBox.Text;
            state.EditingValue = false;
            ShowMasked();
            copyButton.IsEnabled = !string.IsNullOrEmpty(entry.Value);
            _isDirty = true;
            Persist();
        };

        valueBox.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (Commit())
                    Persist();
                e.Handled = true;
            }
        };

        copyButton.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(entry.Value))
                return;
            var package = new DataPackage();
            package.SetText(entry.Value);
            Clipboard.SetContent(package);
            StatusBar.SetMessage(Loc.Get("SecureValues.Copied"), isError: false);
        };

        deleteButton.Click += (_, _) =>
        {
            _entries.RemoveAll(e => e.Id == entry.Id);
            EnsureBlankRow();
            Persist();
            RebuildRows();
            StatusBar.SetMessage(Loc.Format("SecureValues.Count", SavedCount), isError: false);
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(keyBox, 0);
        Grid.SetColumn(valueBox, 1);
        Grid.SetColumn(copyButton, 2);
        Grid.SetColumn(deleteButton, 3);
        grid.Children.Add(keyBox);
        grid.Children.Add(valueBox);
        grid.Children.Add(copyButton);
        grid.Children.Add(deleteButton);

        return new Border
        {
            Child = grid,
            Padding = new Thickness(12, 10, 12, 10),
            Background = ThemeBrush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
        };
    }

    private FrameworkElement BuildHeaderActions()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        panel.Children.Add(HeaderIconButton(AddGlyph, Loc.Get("SecureValues.Add"), AddRow));
        panel.Children.Add(HeaderIconButton(SaveGlyph, Loc.Get("SecureValues.Save"), SaveAll));
        return panel;
    }

    private static Button HeaderIconButton(int codepoint, string label, Action action)
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
        return button;
    }

    private static Button IconButton(int codepoint, string label)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = char.ConvertFromUtf32(codepoint), FontSize = 14 },
            Width = 40,
            Height = 32,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(button, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        return button;
    }

    private static Brush? ThemeBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value)
            ? value as Brush
            : null;

    private sealed class RowState
    {
        public bool KeyLocked;
        public bool EditingValue;
    }
}
