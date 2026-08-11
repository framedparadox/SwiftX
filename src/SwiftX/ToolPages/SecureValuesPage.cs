using SwiftX.Models;
using SwiftX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace SwiftX.ToolPages;

/// <summary>
/// Local key/secret store. Values are DPAPI-protected at rest and masked on screen; Copy places
/// the real secret on the clipboard.
/// </summary>
public sealed class SecureValuesPage : EditorToolPage
{
    private readonly List<SecureValueEntry> _entries = [];
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private IReadOnlyList<ToolCommand>? _commands;
    private bool _isDirty;
    private bool _loading;

    public SecureValuesPage()
    {
        var header = new TextBlock
        {
            Text = Loc.Get("SecureValues.Blurb"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            Margin = new Thickness(0, 0, 0, 4),
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
        };
        Grid.SetRow(headerBorder, 0);
        Grid.SetRow(scroller, 1);
        body.Children.Add(headerBorder);
        body.Children.Add(scroller);

        SetBody(Pane(body));
        StatusBar.SetUntouched();
        InitializeChrome();
        LoadEntries();
    }

    public override ToolKind Kind => ToolKind.SecureValues;
    public override bool IsDirty => _isDirty;

    public override IReadOnlyList<ToolCommand> Commands => _commands ??=
    [
        new ToolCommand(Loc.Get("SecureValues.Add"), "\uE710", AddRow, id: "secure-add"),
        ToolCommand.Clear(ClearAll),
    ];

    private void LoadEntries()
    {
        _loading = true;
        try
        {
            _entries.Clear();
            _entries.AddRange(SecureValueStore.Load());
            RebuildRows();
            StatusBar.SetMessage(Loc.Format("SecureValues.Count", _entries.Count), isError: false);
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

    private void AddRow()
    {
        var entry = new SecureValueEntry();
        _entries.Add(entry);
        _rows.Children.Add(CreateRow(entry, keyLocked: false));
        _isDirty = true;
        StatusBar.SetMessage(Loc.Format("SecureValues.Count", _entries.Count), isError: false);
    }

    private void ClearAll()
    {
        _entries.Clear();
        _rows.Children.Clear();
        Persist();
        StatusBar.SetMessage(Loc.Format("SecureValues.Count", 0), isError: false);
    }

    private void RebuildRows()
    {
        _rows.Children.Clear();
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

        var copyButton = IconButton("\uE8C8", Loc.Get("SecureValues.Copy"));
        copyButton.IsEnabled = !string.IsNullOrEmpty(entry.Value);

        var deleteButton = IconButton("\uE74D", Loc.Get("SecureValues.Delete"));

        var saveButton = new Button
        {
            Content = Loc.Get("SecureValues.Save"),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = keyLocked ? Visibility.Collapsed : Visibility.Visible,
        };

        void ShowMasked()
        {
            if (state.EditingValue)
                return;
            valueBox.Text = ValueMasking.Mask(entry.Value);
        }

        void Commit()
        {
            var key = keyBox.Text.Trim();
            if (string.IsNullOrEmpty(key))
            {
                StatusBar.SetMessage(Loc.Get("SecureValues.KeyRequired"), isError: true);
                return;
            }

            if (state.EditingValue || !state.KeyLocked)
                entry.Value = valueBox.Text;

            entry.Key = key;
            keyBox.Text = key;
            keyBox.IsReadOnly = true;
            state.KeyLocked = true;
            state.EditingValue = false;
            saveButton.Visibility = Visibility.Collapsed;
            ShowMasked();
            copyButton.IsEnabled = !string.IsNullOrEmpty(entry.Value);
            _isDirty = true;
            Persist();
        }

        keyBox.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                Commit();
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
                Commit();
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
            Persist();
            RebuildRows();
            StatusBar.SetMessage(Loc.Format("SecureValues.Count", _entries.Count), isError: false);
        };

        saveButton.Click += (_, _) => Commit();

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(keyBox, 0);
        Grid.SetColumn(valueBox, 1);
        Grid.SetColumn(saveButton, 2);
        Grid.SetColumn(copyButton, 3);
        Grid.SetColumn(deleteButton, 4);
        grid.Children.Add(keyBox);
        grid.Children.Add(valueBox);
        grid.Children.Add(saveButton);
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

    private static Button IconButton(string glyph, string label)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
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
