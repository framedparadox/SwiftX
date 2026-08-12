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
    public bool ShowJsonTool { get; set; } = true;
    public bool ShowXmlTool { get; set; } = true;
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
        ItemSize = other.ItemSize;
        RingRadius = other.RingRadius;
        ShowJsonTool = other.ShowJsonTool;
        ShowXmlTool = other.ShowXmlTool;
        ShowDataTool = other.ShowDataTool;
        ShowSecureValuesTool = other.ShowSecureValuesTool;
        ShowBase64Tool = other.ShowBase64Tool;
        ShowCertificateTool = other.ShowCertificateTool;
        ReuseToolWindows = other.ReuseToolWindows;
    }
}
