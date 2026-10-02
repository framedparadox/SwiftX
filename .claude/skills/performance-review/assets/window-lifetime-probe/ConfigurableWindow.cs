using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LeakProbe;

/// <summary>A window whose only variable is HOW it subscribes to its own events.</summary>
public sealed class ConfigurableWindow : Window
{
    private int _touched;

    public ConfigurableWindow(string variant)
    {
        var panel = new StackPanel();
        for (int i = 0; i < 60; i++)
            panel.Children.Add(new Button { Content = "Button " + i, Margin = new Thickness(2) });
        panel.Children.Add(new ToggleSwitch { Header = "Toggle" });
        Content = panel;

        // ctl-<a>+<b>: add control kinds to the window (no handlers on the window itself).
        if (variant.StartsWith("ctl-", StringComparison.Ordinal))
        {
            panel.Children.Clear();
            foreach (var part in variant[4..].Split('+'))
                AddControl(panel, part);
            return;
        }

        switch (variant)
        {
            case "none":
                break;

            // Subscribed on the Window itself, delegate targets the Window.
            case "closed-lambda-this": Closed += (_, _) => Touch(); break;
            case "closed-method-group": Closed += OnClosed; break;
            case "activated-lambda-this": Activated += (_, _) => Touch(); break;
            case "appwindow-closing-this": AppWindow.Closing += OnClosing; break;

            // Same subscriptions, but the delegate targets a helper that holds only a weak reference.
            case "closed-weak": { var h = new WeakHandler(this); Closed += h.OnClosed; break; }
            case "activated-weak": { var h = new WeakHandler(this); Activated += h.OnActivated; break; }
            case "appwindow-closing-weak": { var h = new WeakHandler(this); AppWindow.Closing += h.OnClosing; break; }

            // The handler removes itself when it runs.
            case "closed-unsub-self": Closed += OnClosedUnsubscribe; break;

            // Everything ShellWindow does to its own events.
            case "shell-like-this":
                Activated += (_, _) => Touch();
                Closed += (_, _) => Touch();
                break;
            case "shell-like-weak":
            {
                var h = new WeakHandler(this);
                Activated += h.OnActivated;
                Closed += h.OnClosed;
                break;
            }

            // Subscribes on the CONTENT (a DependencyObject), delegate targets the window.
            case "content-loaded-this": panel.Loaded += (_, _) => Touch(); break;

            // Named handlers, all removed inside the Closed handler (the pattern proposed for ShellWindow).
            case "shell-like-unsub":
                Activated += OnActivated;
                Closed += OnClosedUnsubscribeAll;
                break;

            // Same, but the Activated handler is never removed (does removing Closed alone suffice?).
            case "activated-kept-closed-unsub":
                Activated += OnActivated;
                Closed += OnClosedUnsubscribe;
                break;

            default: throw new ArgumentException("unknown variant " + variant);
        }
    }

    private void AddControl(StackPanel panel, string kind)
    {
        switch (kind)
        {
            case "none": break;
            case "buttons": for (int i = 0; i < 60; i++) panel.Children.Add(new Button { Content = "Button " + i }); break;
            case "colorpicker": panel.Children.Add(new ColorPicker()); panel.Children.Add(new ColorPicker()); break;
            case "rating": panel.Children.Add(new RatingControl()); break;
            case "listview":
            {
                var lv = new ListView();
                for (int i = 0; i < 20; i++) lv.Items.Add("Item " + i);
                panel.Children.Add(lv);
                break;
            }
            case "toggles": for (int i = 0; i < 14; i++) panel.Children.Add(new ToggleSwitch { Header = "T" + i }); break;
            case "textboxes": for (int i = 0; i < 10; i++) panel.Children.Add(new TextBox { Text = "text " + i }); break;
            case "comboboxes": for (int i = 0; i < 5; i++) { var c = new ComboBox(); c.Items.Add("a"); c.Items.Add("b"); panel.Children.Add(c); } break;
            case "fonticons": for (int i = 0; i < 60; i++) panel.Children.Add(new FontIcon { Glyph = "" }); break;
            case "sliders": for (int i = 0; i < 4; i++) panel.Children.Add(new Slider()); break;
            case "radios": for (int i = 0; i < 20; i++) panel.Children.Add(new RadioButton { Content = "r" + i }); break;
            case "scrollviewers": for (int i = 0; i < 10; i++) panel.Children.Add(new ScrollViewer { Content = new TextBlock { Text = "x" }, Height = 30 }); break;
            case "infobar": panel.Children.Add(new InfoBar { IsOpen = true, Title = "t", Message = "m" }); break;
            case "textblocks": for (int i = 0; i < 200; i++) panel.Children.Add(new TextBlock { Text = "line " + i }); break;
            case "ellipses": for (int i = 0; i < 40; i++) panel.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 10, Height = 10, Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red) }); break;
            case "titlebar":
            {
                var bar = new Grid { Height = 32 };
                panel.Children.Add(bar);
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(bar);
                break;
            }
            // ---- title-bar API variants (the native GDI/handle leak) ----
            case "tbextend": ExtendsContentIntoTitleBar = true; break;
            case "tbset": { var bar = new Grid { Height = 32 }; panel.Children.Add(bar); SetTitleBar(bar); break; }
            case "tbapp": AppWindow.TitleBar.ExtendsContentIntoTitleBar = true; break;
            case "tbapp-drag":
            {
                AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                AppWindow.TitleBar.SetDragRectangles([new Windows.Graphics.RectInt32(0, 0, 600, 32)]);
                break;
            }
            case "tbreset":
            {
                var bar = new Grid { Height = 32 };
                panel.Children.Add(bar);
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(bar);
                Closed += OnClosedResetTitleBar;
                break;
            }
            // Revert to the standard title bar while the window is still alive (in Closing), so the
            // caption subsystem can release what it allocated.
            case "tbreset-closing":
            {
                var bar = new Grid { Height = 32 };
                panel.Children.Add(bar);
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(bar);
                AppWindow.Closing += OnClosingResetTitleBar;
                break;
            }
            // Same, but hide first so the revert is never visible, then close for real.
            case "tbreset-hide-first":
            {
                var bar = new Grid { Height = 32 };
                panel.Children.Add(bar);
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(bar);
                AppWindow.Closing += OnClosingHideResetClose;
                break;
            }
            case "tbcollapsed":
            {
                var bar = new Grid { Height = 32 };
                panel.Children.Add(bar);
                ExtendsContentIntoTitleBar = true;
                AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Collapsed;
                SetTitleBar(bar);
                break;
            }
            case "tbnotitle":
            {
                if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter op) op.SetBorderAndTitleBar(true, false);
                break;
            }
            case "tbext-after-activate":
            {
                var bar = new Grid { Height = 32 };
                panel.Children.Add(bar);
                panel.Loaded += (_, _) => { ExtendsContentIntoTitleBar = true; SetTitleBar(bar); };
                break;
            }
            // ---- repeated chrome calls in a tight loop (what WindowChrome.ApplyShellFrame does) ----
            // Each window gets a custom title bar, then performs the call 20 times (or N, for repN-N)
            // as soon as it loads. GDI/window minus the plain 'titlebar' figure, over the number of
            // calls, is the cost of one call. Only the title-bar colour setters leak per call.
            case "rep-titlebar-colors":
            case "rep-setborder":
            case "rep-resizable":
            case "rep-framechanged":
            case "rep-dwm":
            case "rep-extendframe":
            case "rep-all":
            case "repN-1":
            case "repN-2":
            case "repN-5":
            {
                var bar = new Grid { Height = 32 };
                panel.Children.Add(bar);
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(bar);
                panel.Loaded += (_, _) => RepeatCalls(kind, 20);
                break;
            }
            case "tbresize":
            {
                var bar = new Grid { Height = 32 };
                panel.Children.Add(bar);
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(bar);
                panel.Loaded += (_, _) =>
                {
                    for (int i = 0; i < 5; i++) AppWindow.Resize(new Windows.Graphics.SizeInt32(600 + i * 20, 400 + i * 10));
                };
                break;
            }
            case "mica": SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop(); break;
            case "acrylic": SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop(); break;
            default: throw new ArgumentException("unknown control kind " + kind);
        }
    }

    internal void Touch() => _touched++;

    private void OnClosed(object sender, WindowEventArgs args) => Touch();

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args) => Touch();

    private void OnClosedUnsubscribe(object sender, WindowEventArgs args)
    {
        Closed -= OnClosedUnsubscribe;
        Touch();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private struct MARGINS { public int L, R, T, B; }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);

    private void RepeatCalls(string kind, int times)
    {
        if (kind.StartsWith("repN-", StringComparison.Ordinal))
        {
            int n = int.Parse(kind[5..]);
            for (int i = 0; i < n; i++) ApplyTitleBarColors();
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        bool all = kind == "rep-all";
        for (int i = 0; i < times; i++)
        {
            if (all || kind == "rep-titlebar-colors")
            {
                var tb = AppWindow.TitleBar;
                var clear = Microsoft.UI.Colors.Transparent;
                tb.PreferredTheme = Microsoft.UI.Windowing.TitleBarTheme.Dark;
                tb.BackgroundColor = clear; tb.InactiveBackgroundColor = clear;
                tb.ButtonBackgroundColor = clear; tb.ButtonInactiveBackgroundColor = clear;
                tb.ButtonHoverBackgroundColor = clear; tb.ButtonPressedBackgroundColor = clear;
            }
            if ((all || kind == "rep-setborder") && AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter op1)
                op1.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: true);
            if ((all || kind == "rep-resizable") && AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter op2)
                op2.IsResizable = true;
            if (all || kind == "rep-dwm")
            {
                int color = 0; int dark = 1; int round = 2;
                DwmSetWindowAttribute(hwnd, 34 /*BORDER_COLOR*/, ref color, 4);
                DwmSetWindowAttribute(hwnd, 35 /*CAPTION_COLOR*/, ref color, 4);
                DwmSetWindowAttribute(hwnd, 20 /*IMMERSIVE_DARK*/, ref dark, 4);
                DwmSetWindowAttribute(hwnd, 33 /*CORNER_PREF*/, ref round, 4);
            }
            if (all || kind == "rep-extendframe")
            {
                var m = new MARGINS();
                DwmExtendFrameIntoClientArea(hwnd, ref m);
            }
            if (all || kind == "rep-framechanged")
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0004 | 0x0010 | 0x0020); // NOMOVE|NOSIZE|NOZORDER|NOACTIVATE|FRAMECHANGED
        }
    }

    private void ApplyTitleBarColors()
    {
        var tb = AppWindow.TitleBar;
        var clear = Microsoft.UI.Colors.Transparent;
        tb.PreferredTheme = Microsoft.UI.Windowing.TitleBarTheme.Dark;
        tb.BackgroundColor = clear; tb.InactiveBackgroundColor = clear;
        tb.ButtonBackgroundColor = clear; tb.ButtonInactiveBackgroundColor = clear;
        tb.ButtonHoverBackgroundColor = clear; tb.ButtonPressedBackgroundColor = clear;
    }

    private void OnClosingResetTitleBar(AppWindow sender, AppWindowClosingEventArgs args)
    {
        AppWindow.Closing -= OnClosingResetTitleBar;
        try { SetTitleBar(null); ExtendsContentIntoTitleBar = false; } catch { }
    }

    private bool _closingForReal;

    private void OnClosingHideResetClose(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closingForReal) return;
        args.Cancel = true;
        _closingForReal = true;
        AppWindow.Hide();
        try { SetTitleBar(null); ExtendsContentIntoTitleBar = false; } catch { }
        AppWindow.Closing -= OnClosingHideResetClose;
        DispatcherQueue.TryEnqueue(Close);
    }

    private void OnClosedResetTitleBar(object sender, WindowEventArgs args)
    {
        Closed -= OnClosedResetTitleBar;
        try { SetTitleBar(null); ExtendsContentIntoTitleBar = false; } catch { }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args) => Touch();

    private void OnClosedUnsubscribeAll(object sender, WindowEventArgs args)
    {
        Activated -= OnActivated;
        Closed -= OnClosedUnsubscribeAll;
        Touch();
    }

    private sealed class WeakHandler(ConfigurableWindow window)
    {
        private readonly WeakReference<ConfigurableWindow> _window = new(window);

        public void OnClosed(object sender, WindowEventArgs args)
        {
            if (_window.TryGetTarget(out var w)) w.Touch();
        }

        public void OnActivated(object sender, WindowActivatedEventArgs args)
        {
            if (_window.TryGetTarget(out var w)) w.Touch();
        }

        public void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_window.TryGetTarget(out var w)) w.Touch();
        }
    }
}
