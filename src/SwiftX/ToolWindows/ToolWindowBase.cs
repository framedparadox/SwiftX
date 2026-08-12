using SwiftX.Controls;
using SwiftX.Interop;
using SwiftX.Models;
using SwiftX.Services;
using SwiftX.ToolPages;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SwiftX.ToolWindows;

/// <summary>Hosts one tool page in a normal resizable window with solid theme (no acrylic/Mica).</summary>
public sealed class ToolWindowBase : Window
{
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private readonly RingManager _manager;
    private readonly Grid _root = new();
    private bool _forceClose;
    private bool _askingToDiscard;

    public ToolPage Page { get; }
    public ToolKind Kind { get; }

    public ToolWindowBase(RingManager manager, ToolPage page, ToolDefinition definition)
    {
        _manager = manager;
        Page = page;
        Kind = definition.Kind;

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        page.HostHwnd = _hwnd;

        Title = definition.DisplayName;
        ExtendsContentIntoTitleBar = true;

        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.Background = ThemeHelper.Brush(_root, "SwiftXSurfaceBrush");

        var titleBar = new AppTitleBar(definition.Glyph, definition.DisplayName, page.HeaderActions);
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(page, 1);
        _root.Children.Add(titleBar);
        _root.Children.Add(page);
        Content = _root;
        SetTitleBar(titleBar);

        ApplyTheme();

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
        }
        _appWindow.IsShownInSwitchers = true;

        WindowChrome.SetRoundedCorners(_hwnd);
        WindowChrome.SetClientSizeDip(_appWindow, _hwnd, 900, 640);
        WindowChrome.CenterOnCursor(_appWindow, windowId);

        Activated += (_, _) => ApplyChromeTheme();
        _root.ActualThemeChanged += (_, _) => ApplyChromeTheme();
        ApplyChromeTheme();

        _appWindow.Closing += OnClosing;
        Closed += (_, _) =>
        {
            Page.NotifyClosing();
            _manager.ToolWindows.Unregister(this);
        };

        manager.ToolWindows.Register(this);
    }

    public void ApplyTheme()
    {
        _root.RequestedTheme = ThemeHelper.Resolve(_manager.Config.Theme);
        _root.Background = ThemeHelper.Brush(_root, "SwiftXSurfaceBrush");
        ApplyChromeTheme();
    }

    private void ApplyChromeTheme()
    {
        bool dark = ThemeHelper.IsDark(_root);
        _root.Background = ThemeHelper.Brush(_root, "SwiftXSurfaceBrush");
        WindowChrome.SetTitleBarTheme(_appWindow, dark);
        WindowChrome.HideWindowBorder(_hwnd, dark);
    }

    public void BringToFront()
    {
        try
        {
            if (_appWindow.IsVisible)
                _appWindow.Show(activateWindow: false);
            NativeMethods.SetForegroundWindow(_hwnd);
            Activate();
        }
        catch (Exception ex)
        {
            Diag.Log("ToolWindowBase.BringToFront failed: " + ex);
        }
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_forceClose || !Page.IsDirty)
            return;

        args.Cancel = true;
        if (_askingToDiscard)
            return;

        _askingToDiscard = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = _root.XamlRoot,
                Title = Loc.Get("Tool.DiscardTitle"),
                Content = new TextBlock { Text = Loc.Get("Tool.DiscardBody"), TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = Loc.Get("Tool.Discard"),
                CloseButtonText = Loc.Get("Common.Cancel"),
                DefaultButton = ContentDialogButton.Close,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _forceClose = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            Diag.Log("ToolWindowBase.OnClosing: " + ex);
        }
        finally
        {
            _askingToDiscard = false;
        }
    }
}
