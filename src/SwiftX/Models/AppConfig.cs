using System.Text.Json.Serialization;

namespace SwiftX.Models;

public enum AppTheme
{
    Light,
    Dark,
    /// <summary>Dark's own native ElementTheme (chrome stays dark-themed) but with SwiftX's own
    /// surfaces swapped to a flat, higher-opacity black instead of Dark's graduated greys — see
    /// ThemeHelper.ColorFor's isBlack overload and SettingsWindow's resource-override pair
    /// (ApplyBlackOverrides). Never reachable via System — there is no OS-level "Black".
    /// <para>
    /// Shown to the user as "Midnight" (see the Theme ComboBox in SettingsWindow.xaml). The enum
    /// member itself stays <c>Black</c> — it's persisted by name via JsonStringEnumConverter, so
    /// renaming it would strand every config that already has "Black" written into ring.json.
    /// </para>
    /// </summary>
    Black,
    System,
}

/// <summary>
/// How a ring item reads as "under the pointer".
/// </summary>
public enum RingHoverStyle
{
    /// <summary>Fill and glyph swap to the opposite theme color while the item grows slightly. The original behavior.</summary>
    Invert,

    /// <summary>Fill and glyph keep their resting color; the item just shrinks slightly instead.</summary>
    Zoom,
}

/// <summary>Persisted app settings and ring profiles.</summary>
public sealed class AppConfig
{
    public List<RingProfile> Rings { get; set; } = new();

    public bool LaunchAtStartup { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>The UI language — see <see cref="AppLanguage"/> and <see cref="Services.Loc"/>.</summary>
    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>How ring items respond to the pointer. See <see cref="RingHoverStyle"/>.</summary>
    public RingHoverStyle HoverStyle { get; set; } = RingHoverStyle.Invert;

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
    public bool ShowQuickFormatTool { get; set; } = true;
    public bool ShowSecureValuesTool { get; set; } = true;
    public bool ShowBase64Tool { get; set; } = true;
    public bool ShowCertificateTool { get; set; } = true;
    public bool ReuseToolWindows { get; set; } = true;

    /// <summary>
    /// The star rating from Settings' Feedback &amp; Support page, 1-5, or 0 for "not rated yet".
    /// <para>
    /// Local-only and inert: nothing reads this but the control that wrote it, and there is no code
    /// path anywhere in the app that transmits it. It is persisted purely so the page can show the
    /// user what they already said instead of asking again every time Settings opens — the same
    /// reason every other field here is persisted. Settings' Privacy page states this explicitly;
    /// if that ever stops being true, that page has to change with it.
    /// </para>
    /// </summary>
    public int FeedbackRating { get; set; }

    /// <summary>
    /// A custom Settings accent colour as "#RRGGBB", or null to use the built-in default — the
    /// same teal already used for the Feedback &amp; Support links (Themes/Colors.xaml's
    /// SettingsAccentBrush). Set from General's Accent colour picker; applied by
    /// SettingsWindow.ApplyAccentColorOverride, which shadows the handful of WinUI brush keys that
    /// drive Settings' toggles, radio buttons, the Radius slider, the rating stars, the nav
    /// selection indicator, and the Feedback links — all of it Settings-only, the same way the
    /// rest of this file's appearance fields never reach the ring, AddItemWindow, or the tool
    /// windows.
    /// </summary>
    public string? AccentColorHex { get; set; }

    /// <summary>
    /// Whether <paramref name="kind"/> is one of the tools the ring is currently set to show —
    /// the read side of the Tools page's switches, which <see cref="ToolCatalog.ApplyVisibility"/>
    /// turns into the <see cref="RingItem.Hidden"/> flag on the ring's own tool items.
    /// </summary>
    public bool IsToolShown(ToolKind kind) => kind switch
    {
        ToolKind.Data => ShowDataTool,
        ToolKind.QuickFormat => ShowQuickFormatTool,
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
        Language = other.Language;
        HoverStyle = other.HoverStyle;
        Hotkey = other.Hotkey;
        HotkeyEnabled = other.HotkeyEnabled;
        HotCornerEnabled = other.HotCornerEnabled;
        HotCorner = other.HotCorner;
        HotCornerLayout = other.HotCornerLayout;
        HotCornerRing = other.HotCornerRing;
        ItemSize = other.ItemSize;
        RingRadius = other.RingRadius;
        ShowDataTool = other.ShowDataTool;
        ShowQuickFormatTool = other.ShowQuickFormatTool;
        ShowSecureValuesTool = other.ShowSecureValuesTool;
        ShowBase64Tool = other.ShowBase64Tool;
        ShowCertificateTool = other.ShowCertificateTool;
        ReuseToolWindows = other.ReuseToolWindows;
        FeedbackRating = other.FeedbackRating;
        AccentColorHex = other.AccentColorHex;
    }
}
