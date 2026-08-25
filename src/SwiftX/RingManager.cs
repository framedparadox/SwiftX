using SwiftX.Interop;
using SwiftX.Models;
using SwiftX.Services;
using Microsoft.UI.Xaml;

namespace SwiftX;

/// <summary>Owns store, tray, hotkey, settings, ring window, and tool hosting.</summary>
public sealed class RingManager
{
    public const int SummonHotkeyId = 1;

    private MessageWindow? _messageWindow;
    private TrayIconService? _tray;
    private HotkeyService? _hotkeys;
    private HotCornerWatcher? _hotCorner;
    private SettingsWindow? _settingsWindow;
    private AddItemWindow? _addItemWindow;
    private RingWindow? _ringWindow;
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

    /// <summary>Whether Settings is currently open — <see cref="RingWindow"/> checks this so it can
    /// stay on screen instead of dismissing itself the instant Settings takes foreground
    /// activation away from it.</summary>
    public bool IsSettingsOpen => _settingsWindow is not null;

    /// <summary>Same idea as <see cref="IsSettingsOpen"/>, for the "Add new" quick action's own
    /// window (a Settings ring item flyout child — see SystemRingItems.CreateAddNew).</summary>
    public bool IsAddItemOpen => _addItemWindow is not null;

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
        {
            // Settings and Close are synthesized onto the ring now, not stored. Drop the copies
            // a config written by an older build still carries. See SystemRingItems.
            repaired = SystemRingItems.Strip(Config.Primary.Items);
            // JSON and XML were merged into the single Data Formatter tool. Rewrite any ring item
            // still pointing at either retired target so it keeps working in the same slot.
            repaired |= ToolCatalog.MigrateLegacyTargets(Config.Primary.Items);
        }

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
                new RingItem { Kind = RingItemKind.Tool, DisplayName = "Data", Target = "Data", CustomGlyph = "\uE943" },
                new RingItem { Kind = RingItemKind.Tool, DisplayName = "Secure values", Target = "SecureValues", CustomGlyph = "\uE72E" },
                new RingItem { Kind = RingItemKind.Tool, DisplayName = "Base64", Target = "Base64", CustomGlyph = "B64" },
                new RingItem { Kind = RingItemKind.Tool, DisplayName = "Certificate", Target = "Certificate", CustomGlyph = "\uEB95" },
            },
        });
        // Data and Secure values are already one tap away inside "Dev tools" above \u2014 a top-level
        // duplicate of each just doubled up on the same tool, so the two remaining user slots go
        // to a couple of stock Windows apps instead (the ring's other defaults \u2014 Notepad, GitHub,
        // Downloads \u2014 are the same idea: something useful out of the box, not an empty ring).
        profile.Items.Add(new RingItem
        {
            Kind = RingItemKind.Application,
            DisplayName = "Calculator",
            Target = Path.Combine(Environment.SystemDirectory, "calc.exe"),
        });
        profile.Items.Add(new RingItem
        {
            Kind = RingItemKind.Application,
            DisplayName = "File Explorer",
            Target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
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

            _hotCorner = new HotCornerWatcher(_messageWindow);
            _hotCorner.Triggered += SummonFromHotCorner;
            ApplyHotCorner();
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

    /// <summary>Puts <see cref="Config"/>'s hot-corner choice into effect. Call after changing
    /// either the corner or whether it is on.</summary>
    public void ApplyHotCorner() =>
        _hotCorner?.Configure(Config.HotCornerEnabled, Config.HotCorner);

    /// <summary>
    /// The mouse-only counterpart to the summon hotkey (see <see cref="HotCornerWatcher"/>).
    /// Summons rather than toggles: the pointer reaching a corner is a request for the ring, and
    /// re-summoning an open one would only yank it across the screen to the corner, so an already
    /// open ring is left exactly where it is. Hiding the ring from the tray stays honored — that
    /// is an explicit "not now", and unlike the hotkey the pointer can wander into a corner
    /// without being asked to.
    /// </summary>
    private void SummonFromHotCorner()
    {
        if (_hiddenByUser)
            return;
        _ringWindow ??= new RingWindow(this);
        if (!_ringWindow.IsOpen)
            _ringWindow.Summon();
    }

    public void ToggleRing()
    {
        if (_hiddenByUser)
        {
            _hiddenByUser = false;
        }
        _ringWindow ??= new RingWindow(this);
        if (_ringWindow.IsOpen)
            _ringWindow.Dismiss();
        else
            _ringWindow.Summon();
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

        if (string.Equals(item.Target, SystemRingItems.AddNewTarget, StringComparison.OrdinalIgnoreCase))
        {
            // Same call the tray icon's own Add entry makes: no explicit callback/target list, so
            // AddItemWindow falls back to adding straight into Config.Primary.Items itself (Settings
            // › Apps & links) and persists on its own — see AddItemWindow.Add_Click.
            OpenAddItem();
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

    public void ReleaseShellIntegration()
    {
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _hotCorner?.Dispose();
        _messageWindow?.Dispose();
        _tray = null;
        _hotkeys = null;
        _hotCorner = null;
        _messageWindow = null;
    }

    public void Quit()
    {
        _shuttingDown = true;
        ReleaseShellIntegration();
        Application.Current.Exit();
    }
}
