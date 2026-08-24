using System.Text.Json.Serialization;

namespace SwiftX.Models;

public enum AppTheme
{
    Light,
    Dark,
    System,
}

/// <summary>Persisted app settings and ring profiles.</summary>
public sealed class AppConfig
{
    public List<RingProfile> Rings { get; set; } = new();

    public bool LaunchAtStartup { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public string Hotkey { get; set; } = HotkeyGesture.Default.ToString();
    public bool HotkeyEnabled { get; set; } = true;
    public bool Seeded { get; set; }

    /// <summary>How large the items around the ring are drawn. The hub is fixed and unaffected.</summary>
    public RingItemSize ItemSize { get; set; } = RingItemSize.Medium;

    public double? RingRadius { get; set; }

    // ---- Floating launcher ---------------------------------------------------------------

    /// <summary>
    /// Whether the floating launcher — the draggable, edge-snappable button that summons the ring
    /// with the mouse — is on. The keyboard hotkey and the tray icon stay available either way, so
    /// turning this off never leaves the ring unreachable.
    /// </summary>
    public bool FloatingLauncherEnabled { get; set; } = true;

    /// <summary>
    /// Last position of the launcher's top-left corner, in <b>physical</b> virtual-screen pixels.
    /// Null until it has been moved, which is what asks for the default placement.
    /// <para>
    /// Physical rather than DIPs because a multi-monitor desktop can mix scale factors, so a DIP
    /// pair does not identify a point on the desktop without also naming the monitor it belongs
    /// to. See <see cref="LauncherSnap"/>. A position saved against a display layout that no
    /// longer exists is validated on load, not trusted.
    /// </para>
    /// </summary>
    public int? LauncherX { get; set; }

    public int? LauncherY { get; set; }

    /// <summary>Which edge the launcher is hidden behind, or <see cref="ScreenEdge.None"/> when it floats free.</summary>
    public ScreenEdge LauncherEdge { get; set; } = ScreenEdge.None;

    // ---- Tools ---------------------------------------------------------------------------

    public bool ShowJsonTool { get; set; } = true;
    public bool ShowXmlTool { get; set; } = true;
    public bool ReuseToolWindows { get; set; } = true;

    [JsonIgnore]
    public RingProfile Primary => Rings.Count > 0
        ? Rings[0]
        : throw new InvalidOperationException("AppConfig has no rings; EnsurePrimary() must run first.");

    public AppConfig EnsurePrimary()
    {
        if (Rings.Count == 0)
            Rings.Add(new RingProfile { Name = "Home" });
        return this;
    }

    public void CopyAppSettingsFrom(AppConfig other)
    {
        LaunchAtStartup = other.LaunchAtStartup;
        Theme = other.Theme;
        Hotkey = other.Hotkey;
        HotkeyEnabled = other.HotkeyEnabled;
        ItemSize = other.ItemSize;
        RingRadius = other.RingRadius;
        ShowJsonTool = other.ShowJsonTool;
        ShowXmlTool = other.ShowXmlTool;
        ReuseToolWindows = other.ReuseToolWindows;
        FloatingLauncherEnabled = other.FloatingLauncherEnabled;
        LauncherX = other.LauncherX;
        LauncherY = other.LauncherY;
        LauncherEdge = other.LauncherEdge;
    }
}
