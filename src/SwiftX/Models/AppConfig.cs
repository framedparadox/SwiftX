using System.Text.Json.Serialization;

namespace SwiftX.Models;

public enum AppTheme
{
    Light,
    Dark,
    /// <summary>Dark's own native ElementTheme (chrome stays dark-themed) but with SwiftX's own
    /// surfaces swapped to a flat, higher-opacity black instead of Dark's graduated greys — see
    /// ThemeHelper.ColorFor's isBlack overload and ShellWindow's resource-override pair
    /// (ApplyBlackOverrides). Never reachable via System — there is no OS-level "Black".
    /// <para>
    /// Shown to the user as "Midnight" (see the theme cards in ShellWindow.xaml). The enum
    /// member itself stays <c>Black</c> — it's persisted by name via JsonStringEnumConverter, so
    /// renaming it would strand every config that already has "Black" written into orbit.json.
    /// </para>
    /// </summary>
    Black,
    System,
}

/// <summary>
/// How an orbit item reads as "under the pointer".
/// </summary>
public enum OrbitHoverStyle
{
    /// <summary>Fill and glyph swap to the opposite theme color while the item grows slightly. The original behavior.</summary>
    Invert,

    /// <summary>Fill and glyph keep their resting color; the item just shrinks slightly instead.</summary>
    Zoom,
}

/// <summary>Persisted app settings and orbit profiles.</summary>
public sealed class AppConfig
{
    public List<OrbitProfile> Orbits { get; set; } = new();

    public bool LaunchAtStartup { get; set; }

    /// <summary>
    /// Whether the user has asked for a <c>SwiftX.lnk</c> on their desktop.
    /// <para>
    /// A setting rather than an install-time action because the Store build is an MSIX package,
    /// and packaged apps are not allowed to write to the desktop at deployment — see
    /// <see cref="Services.DesktopShortcutService"/>, which is also what reconciles this flag with
    /// what is actually on disk. Persisted, unlike the shortcut's mere presence, so that the
    /// toggle can show the user their own answer even when the file has since been deleted by
    /// hand; the Settings page reads both and shows what is really there.
    /// </para>
    /// </summary>
    public bool DesktopShortcut { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>The UI language — see <see cref="AppLanguage"/> and <see cref="Services.Loc"/>.</summary>
    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>How orbit items respond to the pointer. See <see cref="OrbitHoverStyle"/>.</summary>
    public OrbitHoverStyle HoverStyle { get; set; } = OrbitHoverStyle.Invert;

    public string Hotkey { get; set; } = HotkeyGesture.Default.ToString();
    public bool HotkeyEnabled { get; set; } = true;

    /// <summary>Whether shoving the pointer into <see cref="HotCorner"/> summons the orbit.</summary>
    public bool HotCornerEnabled { get; set; } = true;

    /// <summary>The screen corner that summons the orbit. Top-left, the way macOS ships.</summary>
    public HotCorner HotCorner { get; set; } = HotCorner.TopLeft;

    /// <summary>Whether a hot-corner summon brings up the whole orbit or a corner quadrant.</summary>
    public HotCornerLayout HotCornerLayout { get; set; } = HotCornerLayout.Orbit;

    /// <summary>
    /// What the quadrant shows, once the user has arranged it themselves. Empty means "whatever
    /// the orbit suggests" — see <see cref="HotCornerItems"/>, which picks the default and is also
    /// what writes this list the first time the corner is edited.
    /// </summary>
    public List<OrbitItem> HotCornerOrbit { get; set; } = new();

    public bool Seeded { get; set; }

    /// <summary>How large the items around the orbit are drawn. The hub is fixed and unaffected.</summary>
    public OrbitItemSize ItemSize { get; set; } = OrbitItemSize.Medium;

    public double? OrbitRadius { get; set; }
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
    /// A custom accent colour as "#RRGGBB", or null to use the built-in default — the same teal
    /// already used for the Feedback &amp; Support links (Themes/Colors.xaml's SettingsAccentBrush).
    /// Set from General's Accent colour picker. <see cref="Services.AccentPalette"/> resolves it
    /// (including the per-theme default and the derived hover/pressed shades); each window that
    /// honours it shadows the WinUI brush keys its own controls read — Settings' toggles, radio
    /// buttons, Radius slider, rating stars, nav selection indicator and Feedback links, and the
    /// Add-item dialog's commit button, type tiles and drag-over affordance.
    /// <para>
    /// Not reached by the orbit itself or the tool windows: both are deliberately monochrome (the
    /// orbit hard-codes its palette a second time in <see cref="Services.ThemeHelper.ColorFor"/>),
    /// and the tool windows have no accent concept at all. A window that starts honouring the
    /// accent has to re-apply it when the theme changes too, since the built-in default differs
    /// per theme.
    /// </para>
    /// </summary>
    public string? AccentColorHex { get; set; }

    /// <summary>
    /// Whether <paramref name="kind"/> is one of the tools the orbit is currently set to show —
    /// the read side of the Tools page's switches, which <see cref="ToolCatalog.ApplyVisibility"/>
    /// turns into the <see cref="OrbitItem.Hidden"/> flag on the orbit's own tool items.
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

    /// <summary>
    /// The write side of <see cref="IsToolShown"/>.
    /// <para>
    /// Exists so that callers wanting to set the whole set — Settings' Reset, chiefly — can walk
    /// <see cref="ToolCatalog.All"/> instead of naming each flag by hand. Five hand-written
    /// assignments is exactly the shape of list that silently loses an entry when a sixth tool
    /// arrives, and Reset had in fact already lost <see cref="ShowQuickFormatTool"/> that way:
    /// hiding Quick Format and pressing Reset restored the other four and left that one hidden,
    /// with nothing on screen to explain why. A kind this method does not recognise is ignored, on
    /// the same reasoning as <see cref="IsToolShown"/>'s default arm — a tool with no switch of its
    /// own is simply always shown.
    /// </para>
    /// </summary>
    public void SetToolShown(ToolKind kind, bool shown)
    {
        switch (kind)
        {
            case ToolKind.Data: ShowDataTool = shown; break;
            case ToolKind.QuickFormat: ShowQuickFormatTool = shown; break;
            case ToolKind.SecureValues: ShowSecureValuesTool = shown; break;
            case ToolKind.Base64: ShowBase64Tool = shown; break;
            case ToolKind.Certificate: ShowCertificateTool = shown; break;
        }
    }

    [JsonIgnore]
    public OrbitProfile Primary => Orbits.Count > 0
        ? Orbits[0]
        : throw new InvalidOperationException("AppConfig has no orbits; EnsurePrimary() must run first.");

    public AppConfig EnsurePrimary()
    {
        if (Orbits.Count == 0)
            Orbits.Add(new OrbitProfile { Name = "Home" });
        return this;
    }

    public void CopyAppSettingsFrom(AppConfig other)
    {
        LaunchAtStartup = other.LaunchAtStartup;
        DesktopShortcut = other.DesktopShortcut;
        Theme = other.Theme;
        Language = other.Language;
        HoverStyle = other.HoverStyle;
        Hotkey = other.Hotkey;
        HotkeyEnabled = other.HotkeyEnabled;
        HotCornerEnabled = other.HotCornerEnabled;
        HotCorner = other.HotCorner;
        HotCornerLayout = other.HotCornerLayout;
        HotCornerOrbit = other.HotCornerOrbit;
        ItemSize = other.ItemSize;
        OrbitRadius = other.OrbitRadius;
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
