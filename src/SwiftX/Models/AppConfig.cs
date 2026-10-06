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
    /// member itself stays <c>Black</c> — it's persisted by name (see <c>OrbitStore</c>), so
    /// renaming it would strand every config that already has "Black" written into orbit.json.
    /// </para>
    /// </summary>
    Black,

    /// <summary>
    /// Dark's native ElementTheme again, but with every SwiftX surface dropped to a low-alpha wash
    /// over a real acrylic backdrop, so the desktop reads through the window rather than around it.
    /// Shown to the user as "Frosted glass".
    /// <para>
    /// Told apart the same way <see cref="Black"/> is — see that member's note, and
    /// <see cref="Services.FrostedGlassPalette"/> for the surfaces, <see cref="Services.FrostedGlassBackdrop"/>
    /// for the blur behind them. Like Midnight, it is never reachable via <see cref="System"/>:
    /// Windows has no such theme to follow.
    /// </para>
    /// </summary>
    FrostedGlass,

    System,
}

/// <summary>
/// How much frost <see cref="AppTheme.FrostedGlass"/> puts between the window and the desktop —
/// three fixed steps rather than a slider, because the theme picker has no room for one and three
/// is enough to cover "I want to see my wallpaper" through "I want to read my text".
/// <para>
/// A setting of its own rather than three <see cref="AppTheme"/> members: the theme is persisted by
/// name and is already drawn as a card in the picker, so splitting it in three would strand every
/// config carrying <c>"FrostedGlass"</c> and put three near-identical cards in a row. This sits
/// beside it the way the accent colour does — a property of the theme, shown only while that theme
/// is the one selected.
/// </para>
/// </summary>
public enum FrostedGlassLevel
{
    /// <summary>The thinnest frost, and what the theme shipped as.</summary>
    Light,
    Medium,
    Dark,
}

/// <summary>
/// How an orbit item reads as "under the pointer".
/// <para>
/// Persisted by name (see <see cref="AppConfig.HoverStyle"/>), so these member names are part of
/// the on-disk format. That is why <see cref="Zoom"/> kept its name when it stopped meaning "shrink":
/// the label the user reads is theirs to change, the member a config file carries is not.
/// </para>
/// </summary>
public enum OrbitHoverStyle
{
    /// <summary>Fill and glyph swap to the opposite theme color while the item grows slightly. The original behavior.</summary>
    Invert,

    /// <summary>
    /// Fill and glyph keep their resting color; the item alone changes size under the pointer, in
    /// the direction <see cref="AppConfig.HoverZoomDirection"/> names. Shown to the user as "Zoom".
    /// </summary>
    Zoom,

    /// <summary>
    /// The item keeps its size and its glyph is redrawn for legibility, but its fill becomes the
    /// colour <see cref="AppConfig.HoverTintColorHex"/> holds while the pointer is on it. Shown to
    /// the user as "Tint".
    /// </summary>
    Tint,

    /// <summary>
    /// The item keeps its fill and settles at its resting size, but plays the motion
    /// <see cref="AppConfig.HoverAnimation"/> names for as long as the pointer is on it. Shown to
    /// the user as "Animate". Gives way to Zoom when Windows has animations switched off.
    /// </summary>
    Animate,
}

/// <summary>
/// The motion <see cref="OrbitHoverStyle.Animate"/> plays. <see cref="Pulse"/> is first on purpose:
/// it is the default, and it is also what <see cref="Services.TolerantEnumConverter{T}"/> falls back
/// to (<c>default(T)</c>) for a name this build does not know. Persisted by name, like the other
/// enums here, so members may be added but never renamed.
/// </summary>
public enum HoverAnimation
{
    /// <summary>Swells and settles, repeatedly.</summary>
    Pulse,

    /// <summary>Hops up and lands with a small rebound.</summary>
    Bounce,

    /// <summary>Tilts from side to side and comes to rest.</summary>
    Wobble,

    /// <summary>A quick side-to-side rattle, then a pause.</summary>
    Shake,

    /// <summary>One full turn.</summary>
    Spin,

    /// <summary>Drifts gently up and down.</summary>
    Float,
}

/// <summary>
/// Which way <see cref="OrbitHoverStyle.Zoom"/> moves an item under the pointer.
/// <para>
/// <see cref="In"/> is the first member on purpose: it is the default, and it is also what
/// <see cref="Services.TolerantEnumConverter{T}"/> falls back to (<c>default(T)</c>) when a config
/// carries a name this build does not know. Persisted by name, like the other enums here.
/// </para>
/// </summary>
public enum HoverZoomDirection
{
    /// <summary>The item grows toward the pointer.</summary>
    In,

    /// <summary>The item recedes from the pointer: what Zoom did before it had a direction.</summary>
    Out,
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

    /// <summary>
    /// Carries its own converter only to name a fallback: every enum in this file is read
    /// tolerantly through <see cref="Services.TolerantEnumConverterFactory"/>, which falls back to
    /// the first declared member, and <see cref="AppTheme"/>'s first member is
    /// <see cref="AppTheme.Light"/> while this setting's default is <see cref="AppTheme.Dark"/>.
    /// An unknown theme name has to come back as the dark window the user had, not as a light one
    /// they never chose.
    /// </summary>
    [JsonConverter(typeof(Services.TolerantAppThemeConverter))]
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>
    /// Which of <see cref="FrostedGlassLevel"/>'s three steps the Frosted glass theme uses. Read
    /// only while <see cref="Theme"/> is <see cref="AppTheme.FrostedGlass"/>, but persisted
    /// regardless, so switching away and back returns you to the frost you had rather than the
    /// default.
    /// </summary>
    public FrostedGlassLevel FrostedGlass { get; set; } = FrostedGlassLevel.Light;

    /// <summary>The UI language — see <see cref="AppLanguage"/> and <see cref="Services.Loc"/>.</summary>
    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>
    /// How orbit items respond to the pointer. See <see cref="OrbitHoverStyle"/>.
    /// <para>
    /// Like every enum here, read tolerantly (see
    /// <see cref="Services.TolerantEnumConverterFactory"/>): a name this build does not know reads
    /// as <see cref="OrbitHoverStyle.Invert"/> rather than throwing, because
    /// <c>OrbitStore.Load</c> answers a throw by starting from a blank configuration and a single
    /// hand-edited word here would otherwise cost the user their whole orbit.
    /// </para>
    /// </summary>
    public OrbitHoverStyle HoverStyle { get; set; } = OrbitHoverStyle.Invert;

    /// <summary>
    /// Which way <see cref="OrbitHoverStyle.Zoom"/> moves an item: <see cref="HoverZoomDirection.In"/>
    /// (grow) by default, or <see cref="HoverZoomDirection.Out"/> (shrink). Read only while
    /// <see cref="HoverStyle"/> is Zoom, but persisted regardless so that switching styles and
    /// back returns you to the direction you had. Tolerant of an unknown name for the same reason
    /// <see cref="HoverStyle"/> is.
    /// </summary>
    public HoverZoomDirection HoverZoomDirection { get; set; } = HoverZoomDirection.In;

    /// <summary>
    /// The fill an item takes under the pointer when <see cref="HoverStyle"/> is
    /// <see cref="OrbitHoverStyle.Tint"/>, as "#RRGGBB", or null for the built-in black. Stored and
    /// parsed the way <see cref="AccentColorHex"/> is, and by the same code
    /// (<see cref="Services.AccentPalette.TryParseHex"/>): a value that does not parse falls back
    /// to black rather than failing the load. <see cref="Services.OrbitHover.TintColor"/> is the
    /// one place that resolves it.
    /// </summary>
    public string? HoverTintColorHex { get; set; }

    /// <summary>
    /// The motion an item plays under the pointer when <see cref="HoverStyle"/> is
    /// <see cref="OrbitHoverStyle.Animate"/>. Read only then, but persisted regardless so that
    /// switching styles and back returns you to the motion you had. Tolerant of an unknown name for
    /// the same reason <see cref="HoverStyle"/> is; an unknown one reads as
    /// <see cref="Models.HoverAnimation.Pulse"/>.
    /// </summary>
    public HoverAnimation HoverAnimation { get; set; } = HoverAnimation.Pulse;

    public string Hotkey { get; set; } = HotkeyGesture.Default.ToString();
    public bool HotkeyEnabled { get; set; } = true;

    /// <summary>Whether shoving the pointer into <see cref="HotCorner"/> summons the orbit.</summary>
    public bool HotCornerEnabled { get; set; } = true;

    /// <summary>The screen corner that summons the orbit. Top-left, the way macOS ships.</summary>
    public HotCorner HotCorner { get; set; } = HotCorner.TopLeft;

    /// <summary>Whether a hot-corner summon brings up the whole orbit or a corner quadrant.</summary>
    public HotCornerLayout HotCornerLayout { get; set; } = HotCornerLayout.Orbit;

    /// <summary>
    /// Whether holding <see cref="MouseSummonButton"/> for <see cref="MouseSummonHoldMs"/> summons
    /// the orbit. On by default — a mouse-only way in beside the hot corner and the tray icon.
    /// </summary>
    public bool MouseSummonEnabled { get; set; } = true;

    /// <summary>
    /// Which button a long-press summons with. Middle by default; see <see cref="MouseSummonButton"/>.
    /// </summary>
    public MouseSummonButton MouseSummonButton { get; set; } = MouseSummonButton.Middle;

    /// <summary>
    /// How long, in milliseconds, <see cref="MouseSummonButton"/> has to stay down before the
    /// orbit is summoned. Defaults to <see cref="Services.MouseSummonWatcher.DefaultHoldMilliseconds"/>.
    /// </summary>
    public int MouseSummonHoldMs { get; set; } = MouseSummonDefaults.HoldMilliseconds;

    /// <summary>
    /// What the quadrant shows, once the user has arranged it themselves. Empty means "whatever
    /// the orbit suggests" — see <see cref="HotCornerItems"/>, which picks the default and is also
    /// what writes this list the first time the corner is edited.
    /// </summary>
    public List<OrbitItem> HotCornerOrbit { get; set; } = new();

    public bool Seeded { get; set; }

    /// <summary>
    /// How large the items around the orbit are drawn. The hub is fixed and unaffected.
    /// <para>
    /// Names its own fallback for the same reason <see cref="Theme"/> does:
    /// <see cref="OrbitItemSize"/> leads with <see cref="OrbitItemSize.Small"/> and this setting
    /// defaults to <see cref="OrbitItemSize.Medium"/>.
    /// </para>
    /// </summary>
    [JsonConverter(typeof(Services.TolerantOrbitItemSizeConverter))]
    public OrbitItemSize ItemSize { get; set; } = OrbitItemSize.Medium;

    /// <summary>
    /// The outline those items are drawn with — see <see cref="OrbitItemShape"/>. Circle by
    /// default, which is what the orbit has always drawn, so a config written before this setting
    /// existed comes back looking exactly as it was left.
    /// </summary>
    public OrbitItemShape ItemShape { get; set; } = OrbitItemShape.Circle;

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
    /// Whether <paramref name="kind"/> is one of the tools the orbit is currently set to show.
    /// <see cref="ToolCatalog.ApplyVisibility"/> turns this into the
    /// <see cref="OrbitItem.Hidden"/> flag on the orbit's own tool items.
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

    /// <summary>
    /// Puts back the shape the types promise and a JSON file does not keep. <c>"Items": null</c>,
    /// <c>"Items": [null]</c> and <c>"Orbits": [null]</c> all deserialize without complaint into a
    /// null list or a null element, and the first line that walks one throws. On the launch path
    /// that was <see cref="SystemOrbitItems.Strip"/>, out of <c>App.OnLaunched</c>: a hand-edited or
    /// merge-damaged <c>orbit.json</c> stopped the app from starting at all, on every launch, until
    /// the file was deleted by hand. Everything that can be read is kept; only what cannot be
    /// anything (a null entry) is dropped, and a missing list or string becomes an empty one.
    /// </summary>
    public AppConfig RepairShape()
    {
        Orbits ??= new();
        Orbits.RemoveAll(o => o is null);
        foreach (var orbit in Orbits)
        {
            orbit.Id ??= Guid.NewGuid().ToString("N");
            orbit.Name ??= string.Empty;
            orbit.Items = RepairItems(orbit.Items);
        }
        HotCornerOrbit = RepairItems(HotCornerOrbit);
        Hotkey ??= HotkeyGesture.Default.ToString();
        return this;
    }

    private static List<OrbitItem> RepairItems(List<OrbitItem>? items)
    {
        items ??= new();
        items.RemoveAll(i => i is null);
        foreach (var item in items)
        {
            item.Id ??= Guid.NewGuid().ToString("N");
            item.Target ??= "";
            item.DisplayName ??= "";
            item.Children = RepairItems(item.Children);
        }
        return items;
    }

    public void CopyAppSettingsFrom(AppConfig other)
    {
        LaunchAtStartup = other.LaunchAtStartup;
        DesktopShortcut = other.DesktopShortcut;
        Theme = other.Theme;
        FrostedGlass = other.FrostedGlass;
        Language = other.Language;
        HoverStyle = other.HoverStyle;
        HoverZoomDirection = other.HoverZoomDirection;
        HoverTintColorHex = other.HoverTintColorHex;
        HoverAnimation = other.HoverAnimation;
        Hotkey = other.Hotkey;
        HotkeyEnabled = other.HotkeyEnabled;
        HotCornerEnabled = other.HotCornerEnabled;
        HotCorner = other.HotCorner;
        HotCornerLayout = other.HotCornerLayout;
        MouseSummonEnabled = other.MouseSummonEnabled;
        MouseSummonButton = other.MouseSummonButton;
        MouseSummonHoldMs = other.MouseSummonHoldMs;
        // Copied, not aliased. Every other field here is a value type or an immutable string, so
        // assigning the reference would quietly make this the one line where two AppConfig
        // instances share mutable state: HotCornerItems.Edit and Reset both mutate this list in
        // place, so an edit to the live configuration would reach back into whatever was copied
        // from. The only caller today (Settings' Import) discards its source immediately, which is
        // exactly why this would have gone unnoticed until the second one did not.
        HotCornerOrbit = new List<OrbitItem>(other.HotCornerOrbit);
        ItemSize = other.ItemSize;
        ItemShape = other.ItemShape;
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
