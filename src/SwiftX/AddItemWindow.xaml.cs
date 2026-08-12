using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SwiftX.Controls;
using SwiftX.Models;
using SwiftX.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SwiftX;

/// <summary>
/// Add-item dialog patterned after Anchor's AddNewWindow: type tiles, target/name form,
/// solid theme chrome (no Mica).
/// </summary>
public sealed partial class AddItemWindow : Window
{
    private enum AddKind { App, File, Folder, Link, Shortcut, Group, Tool }

    private readonly RingManager _manager;
    private readonly Action<RingItem>? _onAdded;
    private readonly List<RingItem> _targetList;
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private AddKind _kind = AddKind.App;
    private bool _nameEdited;

    public AddItemWindow(RingManager manager, Action<RingItem>? onAdded, List<RingItem> targetList)
    {
        _manager = manager;
        _onAdded = onAdded;
        _targetList = targetList;
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));
        Title = "Add to ring";

        var titleBar = new AppTitleBar("", "Add to ring");
        Grid.SetRow(titleBar, 0);
        Root.Children.Insert(0, titleBar);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);

        ApplyTheme();
        Root.ActualThemeChanged += (_, _) => ApplyChromeTheme();
        Activated += (_, _) => ApplyChromeTheme();

        if (_appWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
        }
        _appWindow.IsShownInSwitchers = true;

        WindowChrome.SetClientSizeDip(_appWindow, _hwnd, 760, 560);
        WindowChrome.SetRoundedCorners(_hwnd);
        WindowChrome.CenterOnCursor(_appWindow, _appWindow.Id);

        NameBox.TextChanged += (_, _) =>
        {
            if (NameBox.FocusState != FocusState.Unfocused)
                _nameEdited = !string.IsNullOrEmpty(NameBox.Text);
        };

        SelectType(AddKind.App);
    }

    public void ApplyTheme()
    {
        Root.RequestedTheme = ThemeHelper.Resolve(_manager.Config.Theme);
        ApplyChromeTheme();
    }

    private void ApplyChromeTheme()
    {
        bool dark = ThemeHelper.IsDark(Root);
        WindowChrome.SetTitleBarTheme(_appWindow, dark);
        WindowChrome.HideWindowBorder(_hwnd, dark);
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
                "A group holds other ring items in a nested ring."),
            AddKind.Tool => ("", "",
                "Built-in formatter tool (JSON or XML)."),
            _ => ("Target", "Path or URL", ""),
        };

        if (kind == AddKind.Group && !_nameEdited && string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = "Group";

        HideError();
    }

    private void TargetBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_nameEdited || string.IsNullOrWhiteSpace(TargetBox.Text)) return;
        NameBox.Text = RingItemFactory.SuggestName(TargetBox.Text.Trim());
    }

    private void SetTarget(string path)
    {
        TargetBox.Text = path;
        if (!_nameEdited)
            NameBox.Text = RingItemFactory.SuggestName(path);
        HideError();
    }

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

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var item = new RingItem();

        if (_kind == AddKind.Tool)
        {
            var tool = (ToolBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Data";
            var def = ToolCatalog.GetByName(tool);
            item.Kind = RingItemKind.Tool;
            item.Target = tool;
            item.DisplayName = string.IsNullOrWhiteSpace(NameBox.Text)
                ? (def?.DisplayName ?? tool)
                : NameBox.Text.Trim();
            item.CustomGlyph = def?.Glyph ?? "\uE8A5";
        }
        else if (_kind == AddKind.Group)
        {
            item.Kind = RingItemKind.Group;
            item.DisplayName = string.IsNullOrWhiteSpace(NameBox.Text) ? "Group" : NameBox.Text.Trim();
            item.CustomGlyph = "\uE838";
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
            // hand-typed duplicate is indistinguishable from the real one to RingManager's
            // dispatch, and could hijack or shadow it.
            if (target.StartsWith("swiftx:", StringComparison.OrdinalIgnoreCase))
            {
                ShowError("That target is reserved for a built-in item.");
                return;
            }

            item.Kind = _kind switch
            {
                AddKind.File => RingItemKind.File,
                AddKind.Folder => RingItemKind.Folder,
                AddKind.Link => RingItemKind.WebLink,
                AddKind.Shortcut => RingItemKind.Shortcut,
                _ => RingItemKind.Application,
            };
            item.Target = target;
            item.DisplayName = string.IsNullOrWhiteSpace(NameBox.Text)
                ? RingItemFactory.SuggestName(target)
                : NameBox.Text.Trim();
        }

        if (_onAdded is not null)
            _onAdded(item);
        else
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
    }

    private void HideError()
    {
        ErrorBar.IsOpen = false;
        ErrorBar.Message = string.Empty;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
