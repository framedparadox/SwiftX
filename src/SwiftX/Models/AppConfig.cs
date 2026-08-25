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

    public bool Seeded { get; set; }

    /// <summary>How large the items around the ring are drawn. The hub is fixed and unaffected.</summary>
    public RingItemSize ItemSize { get; set; } = RingItemSize.Medium;

    public double? RingRadius { get; set; }
    public bool ShowDataTool { get; set; } = true;
    public bool ShowSecureValuesTool { get; set; } = true;
    public bool ShowBase64Tool { get; set; } = true;
    public bool ShowCertificateTool { get; set; } = true;
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
        HotCornerEnabled = other.HotCornerEnabled;
        HotCorner = other.HotCorner;
        ItemSize = other.ItemSize;
        RingRadius = other.RingRadius;
        ShowDataTool = other.ShowDataTool;
        ShowSecureValuesTool = other.ShowSecureValuesTool;
        ShowBase64Tool = other.ShowBase64Tool;
        ShowCertificateTool = other.ShowCertificateTool;
        ReuseToolWindows = other.ReuseToolWindows;
    }
}
