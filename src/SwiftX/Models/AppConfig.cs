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

    /// <summary>Whether shoving the pointer into <see cref="HotCorner"/> summons the ring.</summary>
    public bool HotCornerEnabled { get; set; } = true;

    /// <summary>The screen corner that summons the ring. Top-left, the way macOS ships.</summary>
    public HotCorner HotCorner { get; set; } = HotCorner.TopLeft;

    /// <summary>Whether a hot-corner summon brings up the whole ring or a corner quadrant.</summary>
    public HotCornerLayout HotCornerLayout { get; set; } = HotCornerLayout.Ring;

    /// <summary>
    /// What the quadrant shows, once the user has arranged it themselves. Empty means "whatever
    /// the ring suggests" — see <see cref="HotCornerItems"/>, which picks the default and is also
    /// what writes this list the first time the corner is edited.
    /// </summary>
    public List<RingItem> HotCornerRing { get; set; } = new();

    public bool Seeded { get; set; }

    /// <summary>How large the items around the ring are drawn. The hub is fixed and unaffected.</summary>
    public RingItemSize ItemSize { get; set; } = RingItemSize.Medium;

    public double? RingRadius { get; set; }
    public bool ShowDataTool { get; set; } = true;
    public bool ShowSecureValuesTool { get; set; } = true;
    public bool ShowBase64Tool { get; set; } = true;
    public bool ShowCertificateTool { get; set; } = true;
    public bool ReuseToolWindows { get; set; } = true;

    /// <summary>
    /// Whether <paramref name="kind"/> is one of the tools the ring is currently set to show —
    /// the read side of the Tools page's switches, which <see cref="ToolCatalog.ApplyVisibility"/>
    /// turns into the <see cref="RingItem.Hidden"/> flag on the ring's own tool items.
    /// </summary>
    public bool IsToolShown(ToolKind kind) => kind switch
    {
        ToolKind.Data => ShowDataTool,
        ToolKind.SecureValues => ShowSecureValuesTool,
        ToolKind.Base64 => ShowBase64Tool,
        ToolKind.Certificate => ShowCertificateTool,
        _ => true,
    };

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
        HotCornerEnabled = other.HotCornerEnabled;
        HotCorner = other.HotCorner;
        HotCornerLayout = other.HotCornerLayout;
        HotCornerRing = other.HotCornerRing;
        ItemSize = other.ItemSize;
        RingRadius = other.RingRadius;
        ShowDataTool = other.ShowDataTool;
        ShowSecureValuesTool = other.ShowSecureValuesTool;
        ShowBase64Tool = other.ShowBase64Tool;
        ShowCertificateTool = other.ShowCertificateTool;
        ReuseToolWindows = other.ReuseToolWindows;
    }
}
