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
                Content = new TextBlock { Text = Loc.Get("Tool.DiscardBody"), TextWrapping = TextWrapping.Wrap },
                DefaultButton = ContentDialogButton.Primary,
            };
            dialog.Title = BuildDiscardTitle(dialog);

            // Save, then Discard, left to right — Save only for a page that actually has
            // somewhere to save its content (Page.CanSave); a page with nothing file-shaped to
            // save (Certificate is read-only; Secure values persists per row, not as a document)
            // gets Discard alone. Cancel is the title-bar X built by BuildDiscardTitle, not a
            // third button here.
            bool canSave = Page.CanSave;
            if (canSave)
            {
                dialog.PrimaryButtonText = Loc.Get("Tool.Save");
                dialog.SecondaryButtonText = Loc.Get("Tool.Discard");
            }
            else
            {
                dialog.PrimaryButtonText = Loc.Get("Tool.Discard");
            }

            var result = await dialog.ShowAsync();
            if (canSave)
            {
                if (result == ContentDialogResult.Primary && await Page.SaveAsync())
                {
                    _forceClose = true;
                    Close();
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    _forceClose = true;
                    Close();
                }
            }
            else if (result == ContentDialogResult.Primary)
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

    // Built from the codepoint, not a literal character in source — see the memory note on PUA
    // glyph corruption from a stray copy/paste. The same "X" glyph the ring's own Close item and
    // Certificate's invalid badge already use elsewhere in this app.
    private static readonly string CloseGlyph = char.ConvertFromUtf32(0xE711);

    /// <summary>
    /// A title row with a close "X" at its trailing edge, the way a normal window's title bar
    /// has one — <see cref="ContentDialog"/> has no such button built in, and Cancel used to be a
    /// third text button doing the same job. Clicking it hides the dialog with no result
    /// (<see cref="ContentDialogResult.None"/>), which falls through every branch in
    /// <see cref="OnClosing"/> just like the old Cancel did: nothing happens and the window stays
    /// open.
    /// </summary>
    private static FrameworkElement BuildDiscardTitle(ContentDialog dialog)
    {
        var text = new TextBlock
        {
            Text = Loc.Get("Tool.DiscardTitle"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            // Leaves room under the close button pulled up into the corner (see close.Margin
            // below) so the wrapped title never runs under it.
            Margin = new Thickness(0, 0, 40, 0),
        };

        var close = new Button
        {
            Content = new FontIcon { Glyph = CloseGlyph, FontSize = 12 },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            // ContentDialog insets its Title row by ContentDialogPadding (24px) on every side —
            // theme-defined, not something this dialog's own layout controls. Pulling the button
            // back out by most of that on the top and right edges is what actually lands it in
            // the corner "like a normal window," rather than 24px shy of it; a few px of that
            // padding stays as clearance so the button doesn't run under the dialog's own rounded
            // corner.
            Margin = new Thickness(0, -16, -16, 0),
        };
        ToolTipService.SetToolTip(close, Loc.Get("Common.Cancel"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(close, Loc.Get("Common.Cancel"));
        close.Click += (_, _) => dialog.Hide();

        var grid = new Grid();
        Grid.SetColumn(text, 0);
        Grid.SetColumn(close, 0);
        grid.Children.Add(text);
        grid.Children.Add(close);
        return grid;
    }
}
