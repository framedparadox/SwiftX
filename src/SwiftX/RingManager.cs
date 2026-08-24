using SwiftX.Interop;
using SwiftX.Models;
using SwiftX.Services;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace SwiftX;

/// <summary>Owns store, tray, hotkey, settings, ring window, and tool hosting.</summary>
public sealed class RingManager
{
    public const int SummonHotkeyId = 1;

    private MessageWindow? _messageWindow;
    private TrayIconService? _tray;
    private HotkeyService? _hotkeys;
    private SettingsWindow? _settingsWindow;
    private AddItemWindow? _addItemWindow;
    private RingWindow? _ringWindow;
    private LauncherWindow? _launcher;
    private bool _hiddenByUser;
    private bool _shuttingDown;

    public RingManager(AppConfig config)
    {
        Config = config;
        ToolWindows = new ToolWindowManager();
        Tools = new ToolWindowLauncher(this);
    }

    public AppConfig Config { get; }
    public ToolWindowManager ToolWindows { get; }
    public ToolWindowLauncher Tools { get; }

    public event Action? ItemsChanged;

    public void Start()
    {
        bool firstRun = !Config.Seeded;
        Config.Seeded = true;
        RingMetrics.SetItemSize(Config.ItemSize);
        RingMetrics.SetRadius(Config.RingRadius);

        bool repaired = false;
        if (firstRun && Config.Primary.Items.Count == 0)
            SeedDefaults(Config.Primary);
        else
            // Settings and Close are synthesized onto the ring now, not stored — drop the copies
            // a config written by an older build still carries. See SystemRingItems.
            repaired = SystemRingItems.Strip(Config.Primary.Items);

        ApplyTheme();
        SetUpTrayAndHotkey();

        if (firstRun || repaired)
            Save();

        // Keep a hidden owner window alive so WinUI doesn't exit with no windows.
        _ringWindow = new RingWindow(this);
        _ringWindow.Closed += (_, _) =>
        {
            if (!_shuttingDown)
                Quit();
        };
        // The launcher steps aside for the ring and comes back when it has finished closing.
        // Dismissed (not Dismiss) so it waits out the exit animation rather than reappearing
        // in the middle of it — see RingWindow.Dismissed.
        _ringWindow.Dismissed += () => _launcher?.ResumeAfterRing();

        ApplyFloatingLauncher();
    }

    public void Save() => RingStore.Save(Config);

    public void NotifyItemsChanged()
    {
        Save();
        ItemsChanged?.Invoke();
        _ringWindow?.ReloadItems();
    }

    /// <summary>
    /// Fills the ring's <see cref="RingMetrics.UserSlotCount"/> user slots. Settings and Close are
    /// deliberately absent: the ring always adds them itself (see <see cref="SystemRingItems"/>).
    /// </summary>
    public void SeedDefaults(RingProfile profile)
    {
        profile.Items.Clear();
        profile.Items.Add(new RingItem
        {
            Kind = RingItemKind.Application,
            DisplayName = "Notepad",
            Target = Path.Combine(Environment.SystemDirectory, "notepad.exe"),
        });
        profile.Items.Add(new RingItem
        {
            Kind = RingItemKind.WebLink,
            DisplayName = "GitHub",
            Target = "https://github.com",
            CustomGlyph = "\uE943",
        });
        profile.Items.Add(new RingItem
        {
            Kind = RingItemKind.Folder,
            DisplayName = "Downloads",
            Target = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        });
        profile.Items.Add(new RingItem
        {
            Kind = RingItemKind.Group,
            DisplayName = "Dev tools",
            CustomGlyph = "\uE90F",
            Children =
            {
                new RingItem { Kind = RingItemKind.Tool, DisplayName = "JSON", Target = "Json", CustomGlyph = "\uE943" },
                new RingItem { Kind = RingItemKind.Tool, DisplayName = "XML", Target = "Xml", CustomGlyph = "</>" },
            },
        });
        profile.Items.Add(new RingItem
        {
            Kind = RingItemKind.Tool,
            DisplayName = "JSON",
            Target = "Json",
            CustomGlyph = "\uE943",
            Hidden = !Config.ShowJsonTool,
        });
        profile.Items.Add(new RingItem
        {
            Kind = RingItemKind.Tool,
            DisplayName = "XML",
            Target = "Xml",
            CustomGlyph = "</>",
            Hidden = !Config.ShowXmlTool,
        });
    }

    private void SetUpTrayAndHotkey()
    {
        try
        {
            _messageWindow = new MessageWindow();
            _tray = new TrayIconService(_messageWindow)
            {
                IsDockVisible = () => !_hiddenByUser,
            };
            _tray.Activated += ToggleRing;
            _tray.ShowHideRequested += ToggleVisibility;
            _tray.AddNewRequested += () => OpenAddItem();
            _tray.SettingsRequested += OpenSettings;
            _tray.QuitRequested += Quit;

            _hotkeys = new HotkeyService(_messageWindow);
            _hotkeys.Pressed += id =>
            {
                if (id == SummonHotkeyId)
                    ToggleRing();
            };
            ApplyHotkey();

            // A monitor coming or going invalidates the floating launcher's docked position,
            // which is only meaningful relative to one particular work area. The hidden message
            // window is already the app's sink for broadcasts like this one.
            _messageWindow.MessageReceived += (msg, _, _) =>
            {
                if (msg == NativeMethods.WM_DISPLAYCHANGE)
                    _launcher?.RevalidatePlacement();
            };
        }
        catch (Exception ex)
        {
            Diag.Log("Tray/hotkey setup failed: " + ex);
        }
    }

    public bool ApplyHotkey()
    {
        if (_hotkeys is null) return false;
        if (!Config.HotkeyEnabled)
        {
            _hotkeys.Unregister(SummonHotkeyId);
            return true;
        }
        if (!HotkeyGesture.TryParse(Config.Hotkey, out var gesture) || !gesture.IsValid)
            return false;
        return _hotkeys.Register(SummonHotkeyId, gesture);
    }

    public void ToggleRing()
    {
        var window = EnsureRingWindow();
        if (window.IsOpen)
        {
            window.Dismiss();
            return;
        }

        _launcher?.SuspendForRing();
        window.Summon();
    }

    /// <summary>
    /// Opens the ring around <paramref name="screenPoint"/> (physical screen pixels) instead of at
    /// the cursor. This is the floating launcher's way in: the ring comes up around the puck that
    /// was clicked, which is the whole point of having summoned it from there.
    /// </summary>
    public void ToggleRingAt(PointInt32 screenPoint)
    {
        var window = EnsureRingWindow();
        if (window.IsOpen)
        {
            window.Dismiss();
            return;
        }

        _launcher?.SuspendForRing();
        window.SummonAt(screenPoint);
    }

    private RingWindow EnsureRingWindow()
    {
        _hiddenByUser = false;
        return _ringWindow ??= new RingWindow(this);
    }

    private void ToggleVisibility()
    {
        _hiddenByUser = !_hiddenByUser;
        if (_hiddenByUser)
            _ringWindow?.Dismiss();
    }

    public void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Activate();
        WindowChrome.CenterOnCursor(_settingsWindow.AppWindow, _settingsWindow.AppWindow.Id);
    }

    public void OpenAddItem(Action<RingItem>? onAdded = null, List<RingItem>? targetList = null)
    {
        if (_addItemWindow is not null)
        {
            _addItemWindow.Activate();
            return;
        }
        _addItemWindow = new AddItemWindow(this, onAdded, targetList ?? Config.Primary.Items);
        _addItemWindow.Closed += (_, _) => _addItemWindow = null;
        _addItemWindow.Activate();
    }

    public void OpenTool(ToolKind kind)
    {
        var def = ToolCatalog.Get(kind);
        if (def is not null)
            Tools.Open(def);
    }

    public void HandleItemActivation(RingItem item)
    {
        if (item.Kind == RingItemKind.Tool)
        {
            var def = ToolCatalog.GetByName(item.Target);
            if (def is not null)
                Tools.Open(def);
            return;
        }

        if (string.Equals(item.Target, SystemRingItems.SettingsTarget, StringComparison.OrdinalIgnoreCase))
        {
            OpenSettings();
            return;
        }

        Launcher.Launch(item);
    }

    public void ApplyTheme()
    {
        // No app-wide theme push here: WinUI's Application has no RequestedTheme setter that
        // propagates to open windows, so each window resolves and applies its own theme via
        // ThemeHelper (see the doc comment on RingWindow.ThemeBrush).
        ToolWindows.ApplyThemeToAll();
        _ringWindow?.ApplyTheme();
        _launcher?.ApplyTheme();
        _settingsWindow?.ApplyTheme();
        _addItemWindow?.ApplyTheme();
    }

    public void SetItemSize(RingItemSize size)
    {
        Config.ItemSize = size;
        RingMetrics.SetItemSize(size);
        Save();
        _ringWindow?.ReloadItems();
    }

    public void SetRadius(double? radius)
    {
        Config.RingRadius = radius;
        RingMetrics.SetRadius(radius);
        Save();
        _ringWindow?.ReloadItems();
    }

    // ---- Floating launcher -----------------------------------------------------------------

    /// <summary>
    /// Brings the floating launcher into line with <see cref="AppConfig.FloatingLauncherEnabled"/>,
    /// creating or retiring the window as needed. Safe to call when nothing has changed.
    /// </summary>
    public void ApplyFloatingLauncher()
    {
        if (!Config.FloatingLauncherEnabled)
        {
            _launcher?.Retire();
            _launcher = null;
            return;
        }

        if (_launcher is not null)
            return;

        try
        {
            var launcher = new LauncherWindow(this);
            // Compare identities before clearing: Close() raises Closed asynchronously, so a
            // rapid off-then-on would otherwise let the retired launcher's event null out the
            // replacement that has already taken its place.
            launcher.Closed += (_, _) =>
            {
                if (ReferenceEquals(_launcher, launcher))
                    _launcher = null;
            };
            _launcher = launcher;
        }
        catch (Exception ex)
        {
            // The ring is still reachable by hotkey and tray icon, so a launcher that cannot be
            // created is a missing convenience rather than a dead app — log it and carry on.
            Diag.Log("Floating launcher setup failed: " + ex);
            _launcher = null;
        }
    }

    /// <summary>
    /// Rebuilds the launcher from scratch so it picks up a position that changed underneath it —
    /// an imported configuration, say. <see cref="ApplyFloatingLauncher"/> alone would not do it:
    /// a live launcher reads its stored placement once, at construction.
    /// </summary>
    public void ReloadFloatingLauncher()
    {
        _launcher?.Retire();
        _launcher = null;
        ApplyFloatingLauncher();
    }

    public void SetFloatingLauncherEnabled(bool enabled)
    {
        Config.FloatingLauncherEnabled = enabled;
        Save();
        ApplyFloatingLauncher();
    }

    /// <summary>Puts the launcher back at its default home. No-op when it is switched off.</summary>
    public void ResetLauncherPosition() => _launcher?.ResetToDefault();

    public void ReleaseShellIntegration()
    {
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _messageWindow?.Dispose();
        _tray = null;
        _hotkeys = null;
        _messageWindow = null;
    }

    public void Quit()
    {
        _shuttingDown = true;
        _launcher?.Retire();
        _launcher = null;
        ReleaseShellIntegration();
        Application.Current.Exit();
    }
}
