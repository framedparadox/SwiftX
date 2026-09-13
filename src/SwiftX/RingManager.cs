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

    /// <summary>
    /// Whether Settings or Add-new currently holds foreground focus — checked by hwnd against the
    /// live foreground window, not by <see cref="IsSettingsOpen"/>/<see cref="IsAddItemOpen"/>
    /// merely being true. <see cref="RingWindow"/>'s outside-click dismiss uses this rather than
    /// those, because a window being <em>open</em> (however briefly, or minimized, or sitting
    /// behind other windows for the rest of the session) is not the same thing as it having just
    /// taken the activation the ring lost — the whole point of the exemption is to survive the one
    /// moment focus moves from the ring to one of these, not to suppress click-anywhere-to-dismiss
    /// for as long as either window happens to still exist.
    /// </summary>
    public bool IsExemptWindowForeground()
    {
        nint fg = NativeMethods.GetForegroundWindow();
        if (_settingsWindow is not null && WinRT.Interop.WindowNative.GetWindowHandle(_settingsWindow) == fg)
            return true;
        if (_addItemWindow is not null && WinRT.Interop.WindowNative.GetWindowHandle(_addItemWindow) == fg)
            return true;
        return false;
    }

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
            // Every tool belongs under the one Tools group, wherever an older config left them.
            repaired |= ToolCatalog.EnsureToolsGroup(Config.Primary.Items);
        }

        // After the migrations, not inside one: a tool item EnsureToolsGroup has just added starts
        // out visible, which is wrong for a tool the user had already switched off.
        ToolCatalog.ApplyVisibility(Config.Primary.Items, Config);

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
        // One parent item owning every built-in tool, rather than tools scattered across the ring
        // — see ToolCatalog.EnsureToolsGroup, which keeps an existing ring in that shape too.
        profile.Items.Add(ToolCatalog.CreateToolsGroup());
        // Data and Secure values are already one tap away inside "Tools" above — a top-level
        // duplicate of each just doubled up on the same tool, so the two remaining user slots go
        // to a couple of stock Windows apps instead (the ring's other defaults — Notepad, GitHub,
        // Downloads — are the same idea: something useful out of the box, not an empty ring).
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
    /// A toggle, the same as the hotkey and the tray icon: the pointer reaching the corner while
    /// the dock is already open is read as "put it away" rather than a second summon, so parking
    /// the mouse back in the corner is a reliable way to dismiss it however it was opened — by
    /// this corner, the other corner, the hotkey, or the tray. Hiding the ring from the tray still
    /// overrides it — that is an explicit "not now", and unlike the hotkey the pointer can wander
    /// into a corner without being asked to.
    /// </summary>
    private void SummonFromHotCorner()
    {
        if (_hiddenByUser)
            return;
        _ringWindow ??= new RingWindow(this);
        if (_ringWindow.IsOpen || _ringWindow.IsClosing)
        {
            _ringWindow.Dismiss();
            return;
        }

        // The quadrant form is anchored on the corner the pointer just reached; the full ring
        // comes up under the pointer exactly as the hotkey summons it.
        _ringWindow.Summon(
            Config.HotCornerLayout == HotCornerLayout.Quadrant ? Config.HotCorner : null);
    }

    public void ToggleRing()
    {
        if (_hiddenByUser)
        {
            _hiddenByUser = false;
        }
        _ringWindow ??= new RingWindow(this);
        // IsClosing as well as IsOpen: a click on the tray icon toggles on the button coming up,
        // by which time that same click may already have dismissed the dock from outside it (see
        // RingWindow.IsClosing) — one gesture must not both close and reopen it.
        if (_ringWindow.IsOpen || _ringWindow.IsClosing)
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

    /// <param name="allowGroups">
    /// Pass false when <paramref name="targetList"/> is a group's own children — the ring cannot
    /// expand a group nested inside a group, so the dialog must not offer to create one there.
    /// </param>
    /// <param name="heading">
    /// Overrides the dialog's own title and heading. Set by the callers that are not adding
    /// anywhere — Settings' "Replace…" reuses the same form to swap what is in a place it already
    /// has — so the window says what pressing its button will actually do.
    /// </param>
    /// <param name="confirmLabel">The commit button's label, for the same reason.</param>
    public void OpenAddItem(
        Action<RingItem>? onAdded = null, List<RingItem>? targetList = null,
        bool isGenericAddItem = false, bool allowGroups = true,
        string? heading = null, string? confirmLabel = null)
    {
        if (_addItemWindow is not null)
        {
            _addItemWindow.Activate();
            return;
        }
        _addItemWindow = new AddItemWindow(
            this, onAdded, targetList ?? Config.Primary.Items, isGenericAddItem, allowGroups,
            heading, confirmLabel);
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
            // › Elements) and persists on its own — see AddItemWindow.Add_Click.
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

    /// <summary>
    /// Puts <see cref="Config"/>'s language choice into effect. Anything that reads
    /// <see cref="Services.Loc"/> when it next builds its UI already picks this up — the tool
    /// pages construct fresh each time their window opens, and the tray menu rebuilds on every
    /// click — so beyond re-pointing <see cref="Services.Loc"/> itself, the only thing that needs
    /// an explicit nudge is the tray icon's tooltip, which is set once and left alone otherwise
    /// (see <see cref="TrayIconService.RefreshTooltip"/>). Windows built once from literal XAML
    /// text (Settings itself, Add item, the ring's own tooltips) still need a restart.
    /// </summary>
    public void ApplyLanguage()
    {
        Loc.Initialize(Config.Language);
        _tray?.RefreshTooltip();
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
