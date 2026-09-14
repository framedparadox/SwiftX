using SwiftX.Interop;
using SwiftX.Models;
using SwiftX.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace SwiftX;

/// <summary>Owns store, tray, hotkey, settings, orbit window, and tool hosting.</summary>
public sealed class OrbitManager
{
    public const int SummonHotkeyId = 1;

    private MessageWindow? _messageWindow;
    private TrayIconService? _tray;
    private HotkeyService? _hotkeys;
    private HotCornerWatcher? _hotCorner;
    private ShellWindow? _shellWindow;
    private OnboardingWindow? _onboardingWindow;
    private AddItemWindow? _addItemWindow;
    private OrbitWindow? _orbitWindow;
    private bool _hiddenByUser;
    private bool _shuttingDown;

    public OrbitManager(AppConfig config)
    {
        Config = config;
        ToolWindows = new ToolWindowManager();
        Tools = new ToolWindowLauncher(this);
    }

    public AppConfig Config { get; }
    public ToolWindowManager ToolWindows { get; }
    public ToolWindowLauncher Tools { get; }

    /// <summary>Whether Settings is currently open — <see cref="OrbitWindow"/> checks this so it can
    /// stay on screen instead of dismissing itself the instant the app window takes foreground
    /// activation away from it.</summary>
    public bool IsSettingsOpen => _shellWindow is not null;

    /// <summary>Same idea as <see cref="IsSettingsOpen"/>, for the "Add new" quick action's own
    /// window (a Settings orbit item flyout child — see SystemOrbitItems.CreateAddNew).</summary>
    public bool IsAddItemOpen => _addItemWindow is not null;

    /// <summary>
    /// Whether the app window, the set-up guide or Add-new currently holds foreground focus —
    /// checked by hwnd against the
    /// live foreground window, not by <see cref="IsSettingsOpen"/>/<see cref="IsAddItemOpen"/>
    /// merely being true. <see cref="OrbitWindow"/>'s outside-click dismiss uses this rather than
    /// those, because a window being <em>open</em> (however briefly, or minimized, or sitting
    /// behind other windows for the rest of the session) is not the same thing as it having just
    /// taken the activation the orbit lost — the whole point of the exemption is to survive the one
    /// moment focus moves from the orbit to one of these, not to suppress click-anywhere-to-dismiss
    /// for as long as either window happens to still exist.
    /// </summary>
    public bool IsExemptWindowForeground()
    {
        nint fg = NativeMethods.GetForegroundWindow();
        if (_shellWindow is not null && WinRT.Interop.WindowNative.GetWindowHandle(_shellWindow) == fg)
            return true;
        if (_onboardingWindow is not null && WinRT.Interop.WindowNative.GetWindowHandle(_onboardingWindow) == fg)
            return true;
        if (_addItemWindow is not null && WinRT.Interop.WindowNative.GetWindowHandle(_addItemWindow) == fg)
            return true;
        return false;
    }

    public event Action? ItemsChanged;

    /// <param name="openSettings">
    /// Whether to show Settings once the orbit is up. True for a launch the user performed and
    /// false for one Windows performed at sign-in, which is the only distinction that matters
    /// here — see <see cref="StartupService.StartupArgument"/>. SwiftX puts nothing on the taskbar,
    /// so without this the Start-menu, taskbar and desktop entries would appear to do nothing at
    /// all; Settings is the app's only real window and therefore the honest thing to show.
    /// </param>
    public void Start(bool openSettings = false)
    {
        bool firstRun = !Config.Seeded;
        Config.Seeded = true;
        OrbitMetrics.SetItemSize(Config.ItemSize);
        OrbitMetrics.SetRadius(Config.OrbitRadius);
        // Same shape as the two above: a piece of app-wide drawing state read from static colour
        // lookups that have no config reference of their own. See FrostedGlassBackdrop.Level.
        FrostedGlassBackdrop.SetLevel(Config.FrostedGlass);

        bool repaired = false;
        if (firstRun && Config.Primary.Items.Count == 0)
            SeedDefaults(Config.Primary);
        else
        {
            // Settings and Close are synthesized onto the orbit now, not stored. Drop the copies
            // a config written by an older build still carries. See SystemOrbitItems.
            repaired = SystemOrbitItems.Strip(Config.Primary.Items);
            // JSON and XML were merged into the single Data Formatter tool. Rewrite any orbit item
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
        _orbitWindow = new OrbitWindow(this);
        _orbitWindow.Closed += (_, _) =>
        {
            if (!_shuttingDown)
                Quit();
        };

        // Anyone who turned "Start with Windows" on before the autostart entry learned to say it
        // was an autostart entry has one that doesn't — and would get Settings in their face at
        // every sign-in. Rather than make them toggle the setting off and on again, the entry
        // repairs itself here, once, the first time a build that knows about it runs.
        StartupService.RepairRegistration();

        if (!openSettings)
            return;

        if (firstRun)
        {
            // A first run gets the set-up guide, not the app window: SwiftX puts nothing on the
            // taskbar and summons itself with a keyboard shortcut nobody has been told about, so
            // the two questions it asks — what goes on the orbit, and which keys bring it up — are
            // the difference between an app that works and one that appears to do nothing at all.
            //
            // Note the ordering with SeedDefaults above: the orbit is already seeded by the time
            // the guide opens, and the guide only ever replaces those items if the user presses
            // Finish. Dismissing it therefore lands exactly where SwiftX landed before the guide
            // existed — a working default orbit — rather than an empty one. It also means the
            // guide's own tick-boxes and the old default are the same six items (see
            // OnboardingCatalog), so "Finish" without touching anything is a no-op by design.
            OpenOnboarding(openShellAfter: true);
            return;
        }

        // Every later launch just opens the app window. The desktop-shortcut prompt is not offered
        // from here: it is a first-run question, and on a first run the guide above asks it outright
        // (or, if the guide was dismissed, hands it to the app window itself — see OpenOnboarding).
        OpenSettings();
    }

    public void Save() => OrbitStore.Save(Config);

    public void NotifyItemsChanged()
    {
        Save();
        ItemsChanged?.Invoke();
        _orbitWindow?.ReloadItems();
    }

    /// <summary>
    /// Fills the orbit's <see cref="OrbitMetrics.UserSlotCount"/> user slots. Settings and Close are
    /// deliberately absent: the orbit always adds them itself (see <see cref="SystemOrbitItems"/>).
    /// </summary>
    public void SeedDefaults(OrbitProfile profile)
    {
        profile.Items.Clear();
        profile.Items.Add(new OrbitItem
        {
            Kind = OrbitItemKind.Application,
            DisplayName = "Notepad",
            Target = Path.Combine(Environment.SystemDirectory, "notepad.exe"),
        });
        profile.Items.Add(new OrbitItem
        {
            Kind = OrbitItemKind.WebLink,
            DisplayName = "GitHub",
            Target = "https://github.com",
            CustomGlyph = "\uE943",
        });
        profile.Items.Add(new OrbitItem
        {
            Kind = OrbitItemKind.Folder,
            DisplayName = "Downloads",
            Target = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        });
        // One parent item owning every built-in tool, rather than tools scattered across the orbit
        // — see ToolCatalog.EnsureToolsGroup, which keeps an existing orbit in that shape too.
        profile.Items.Add(ToolCatalog.CreateToolsGroup());
        // Data and Secure values are already one tap away inside "Tools" above — a top-level
        // duplicate of each just doubled up on the same tool, so the two remaining user slots go
        // to a couple of stock Windows apps instead (the orbit's other defaults — Notepad, GitHub,
        // Downloads — are the same idea: something useful out of the box, not an empty orbit).
        profile.Items.Add(new OrbitItem
        {
            Kind = OrbitItemKind.Application,
            DisplayName = "Calculator",
            Target = Path.Combine(Environment.SystemDirectory, "calc.exe"),
        });
        profile.Items.Add(new OrbitItem
        {
            Kind = OrbitItemKind.Application,
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
            _tray.Activated += ToggleOrbit;
            _tray.ShowHideRequested += ToggleVisibility;
            _tray.AddNewRequested += () => OpenAddItem();
            _tray.SettingsRequested += OpenSettings;
            _tray.QuitRequested += Quit;

            // A second SwiftX process — someone clicking the Start-menu, taskbar or desktop icon
            // while this one is already running — broadcasts a request to come forward rather than
            // exiting into silence. The hidden message window is already pumping on the UI thread,
            // so the request arrives somewhere OpenSettings can simply be called.
            SingleInstanceSignal.Listen(_messageWindow, () => OpenSettings());

            _hotkeys = new HotkeyService(_messageWindow);
            _hotkeys.Pressed += id =>
            {
                if (id == SummonHotkeyId)
                    ToggleOrbit();
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
    /// this corner, the other corner, the hotkey, or the tray. Hiding the orbit from the tray still
    /// overrides it — that is an explicit "not now", and unlike the hotkey the pointer can wander
    /// into a corner without being asked to.
    /// </summary>
    private void SummonFromHotCorner()
    {
        if (_hiddenByUser)
            return;
        _orbitWindow ??= new OrbitWindow(this);
        if (_orbitWindow.IsOpen || _orbitWindow.IsClosing)
        {
            _orbitWindow.Dismiss();
            return;
        }

        // The quadrant form is anchored on the corner the pointer just reached; the full orbit
        // comes up under the pointer exactly as the hotkey summons it.
        _orbitWindow.Summon(
            Config.HotCornerLayout == HotCornerLayout.Quadrant ? Config.HotCorner : null);
    }

    public void ToggleOrbit()
    {
        if (_hiddenByUser)
        {
            _hiddenByUser = false;
        }
        _orbitWindow ??= new OrbitWindow(this);
        // IsClosing as well as IsOpen: a click on the tray icon toggles on the button coming up,
        // by which time that same click may already have dismissed the dock from outside it (see
        // OrbitWindow.IsClosing) — one gesture must not both close and reopen it.
        if (_orbitWindow.IsOpen || _orbitWindow.IsClosing)
            _orbitWindow.Dismiss();
        else
            _orbitWindow.Summon();
    }

    private void ToggleVisibility()
    {
        _hiddenByUser = !_hiddenByUser;
        if (_hiddenByUser)
            _orbitWindow?.Dismiss();
    }

    /// <summary>
    /// Opens the SwiftX app window (<see cref="ShellWindow"/>) on its Home screen.
    /// <para>
    /// Still called <c>OpenSettings</c> because that is what every caller is asking for — the tray
    /// menu's Settings entry, the orbit's own Settings bubble, and a second launch of the app
    /// handed here by <see cref="SingleInstanceSignal"/>. Settings is now one screen inside that
    /// window rather than the window itself, and the gear on Home is one click away.
    /// </para>
    /// </summary>
    public void OpenSettings() => OpenSettings(offerDesktopShortcut: false);

    /// <param name="offerDesktopShortcut">
    /// Opens on Settings › General with the prompt offering to put a SwiftX shortcut on the
    /// desktop. Only ever true for a first launch whose set-up guide did not run or was dismissed
    /// — the guide asks the same question itself, so it is not asked twice. See <see cref="Start"/>.
    /// </param>
    public void OpenSettings(bool offerDesktopShortcut)
    {
        if (_shellWindow is null)
        {
            _shellWindow = new ShellWindow(this, offerDesktopShortcut);
            _shellWindow.Closed += (_, _) => _shellWindow = null;
        }
        else if (_shellWindow.AppWindow.Presenter is OverlappedPresenter presenter &&
                 presenter.State == OverlappedPresenterState.Minimized)
        {
            // Activate() on a minimized window shows it but leaves it minimized, so an app window
            // the user had put away would answer a fresh launch of the app with a taskbar flash and
            // nothing else.
            presenter.Restore();
        }

        _shellWindow.Activate();
        WindowChrome.CenterOnCursor(_shellWindow.AppWindow, _shellWindow.AppWindow.Id);

        // Activate() alone is not enough to raise a window that is already open behind others: it
        // asks for activation, and Windows' foreground lock is free to turn that into a flashing
        // taskbar button when the requesting process cannot show it is acting on real user input.
        // That is exactly SwiftX's situation when the request arrived as a broadcast from a second
        // process (see SingleInstanceSignal, which hands this process the foreground right first).
        // ForceForeground is the same borrowed-input-state dance the orbit's own summon uses.
        WindowChrome.ForceForeground(WinRT.Interop.WindowNative.GetWindowHandle(_shellWindow));
    }

    /// <summary>
    /// Opens the first-run set-up guide — what goes on the orbit, and which keys summon it.
    /// <para>
    /// Opened automatically by <see cref="Start"/> on a first run, and on demand from Settings ›
    /// General ("Set-up guide") afterwards, which is why it is not gated on the first run here:
    /// re-running it against an orbit that already exists is a supported thing to do, and the guide
    /// writes nothing unless the user presses Finish.
    /// </para>
    /// </summary>
    /// <param name="openShellAfter">
    /// Whether to bring the app window up once the guide closes, finished or dismissed. True for
    /// the first run, where the guide <em>is</em> the launch and closing it would otherwise leave a
    /// brand-new user looking at an empty desktop; false when the guide was opened from inside the
    /// app window, which is already behind it.
    /// </param>
    public void OpenOnboarding(bool openShellAfter = false)
    {
        if (_onboardingWindow is not null)
        {
            _onboardingWindow.Activate();
            return;
        }

        var window = new OnboardingWindow(this);
        _onboardingWindow = window;
        window.Completed += finished =>
        {
            _onboardingWindow = null;
            if (!openShellAfter)
                return;
            // A guide the user finished has already asked about the desktop shortcut; one they
            // dismissed has not, so the app window still owes them that question.
            OpenSettings(offerDesktopShortcut: !finished && !DesktopShortcutService.Exists());
        };
        window.Closed += (_, _) => _onboardingWindow = null;
        window.Activate();
        WindowChrome.ForceForeground(WinRT.Interop.WindowNative.GetWindowHandle(window));
    }

    /// <param name="allowGroups">
    /// Pass false when <paramref name="targetList"/> is a group's own children — the orbit cannot
    /// expand a group nested inside a group, so the dialog must not offer to create one there.
    /// </param>
    /// <param name="heading">
    /// Overrides the dialog's own title and heading. Set by the callers that are not adding
    /// anywhere — Settings' "Replace…" reuses the same form to swap what is in a place it already
    /// has — so the window says what pressing its button will actually do.
    /// </param>
    /// <param name="confirmLabel">The commit button's label, for the same reason.</param>
    public void OpenAddItem(
        Action<OrbitItem>? onAdded = null, List<OrbitItem>? targetList = null,
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

    public void HandleItemActivation(OrbitItem item)
    {
        if (item.Kind == OrbitItemKind.Tool)
        {
            var def = ToolCatalog.GetByName(item.Target);
            if (def is not null)
                Tools.Open(def);
            return;
        }

        if (string.Equals(item.Target, SystemOrbitItems.SettingsTarget, StringComparison.OrdinalIgnoreCase))
        {
            OpenSettings();
            return;
        }

        if (string.Equals(item.Target, SystemOrbitItems.AddNewTarget, StringComparison.OrdinalIgnoreCase))
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
        // Before any window repaints: every frosted surface resolves through this, so a theme pass
        // that ran first would paint the level the app was wearing a moment ago.
        FrostedGlassBackdrop.SetLevel(Config.FrostedGlass);

        // No app-wide theme push here: WinUI's Application has no RequestedTheme setter that
        // propagates to open windows, so each window resolves and applies its own theme via
        // ThemeHelper (see the doc comment on OrbitWindow.ThemeBrush).
        ToolWindows.ApplyThemeToAll();
        _orbitWindow?.ApplyTheme();
        _shellWindow?.ApplyTheme();
        _onboardingWindow?.ApplyTheme();
        _addItemWindow?.ApplyTheme();
    }

    /// <summary>
    /// Pushes a just-changed accent colour to the other windows that honour it. Settings applies
    /// its own before calling this — it is the window the picker lives in — so this only has to
    /// reach the ones that would otherwise keep painting the old colour until they were reopened.
    /// <para>
    /// Separate from <see cref="ApplyTheme"/> rather than folded into it because the accent picker
    /// raises ColorChanged on every frame of a drag, and a full theme pass per frame would re-walk
    /// every themed resource in every open window for a change that touches a handful of brushes.
    /// Settings' "Add new" flyout child can be open at the same time as the picker, which is
    /// exactly when this matters.
    /// </para>
    /// </summary>
    public void ApplyAccentColour() => _addItemWindow?.ApplyAccent();

    /// <summary>
    /// Puts <see cref="Config"/>'s language choice into effect. Anything that reads
    /// <see cref="Services.Loc"/> when it next builds its UI already picks this up — the tool
    /// pages construct fresh each time their window opens, and the tray menu rebuilds on every
    /// click — so beyond re-pointing <see cref="Services.Loc"/> itself, the only thing that needs
    /// an explicit nudge is the tray icon's tooltip, which is set once and left alone otherwise
    /// (see <see cref="TrayIconService.RefreshTooltip"/>). Windows built once from literal XAML
    /// text (Settings itself, Add item, the orbit's own tooltips) still need a restart.
    /// </summary>
    public void ApplyLanguage()
    {
        Loc.Initialize(Config.Language);
        _tray?.RefreshTooltip();
    }

    public void SetItemSize(OrbitItemSize size)
    {
        Config.ItemSize = size;
        OrbitMetrics.SetItemSize(size);
        Save();
        _orbitWindow?.ReloadItems();
    }

    public void SetRadius(double? radius)
    {
        Config.OrbitRadius = radius;
        OrbitMetrics.SetRadius(radius);
        Save();
        _orbitWindow?.ReloadItems();
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
