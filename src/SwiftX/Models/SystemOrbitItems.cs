namespace SwiftX.Models;

/// <summary>
/// The orbit's built-in, non-scannable entries: Home, Settings and Close, which the "Add to
/// orbit" sidebar's Controls section offers like any other suggestion, and the one truly pinned
/// slot that is never anything else — the orbit logo, always last, always present, never
/// reorderable, removable, or offered for adding (see <see cref="CreateOrbitLogo"/>).
/// <para>
/// Home, Settings and Close used to be synthesized at render time rather than stored, the same way
/// the logo still is — see <see cref="Strip"/>'s own history below. That stopped being true the
/// moment they became optional: an item the user can choose to add has to be stored somewhere to
/// remember the choice, and <see cref="OrbitProfile.Items"/> is that somewhere, exactly like any
/// other orbit item. Only the logo is still synthesized, because it is never anything the user
/// picked — there is nothing to remember.
/// </para>
/// </summary>
public static class SystemOrbitItems
{
    public const string SettingsTarget = "swiftx:settings";
    public const string CloseTarget = "swiftx:close";
    public const string HomeTarget = "swiftx:home";

    /// <summary>The one slot that is always pinned — see <see cref="CreateOrbitLogo"/>.</summary>
    public const string OrbitLogoTarget = "swiftx:orbit-logo";

    /// <summary>Placeholder painted onto an empty user slot — see <see cref="CreateAddNew"/>.</summary>
    public const string AddNewTarget = "swiftx:quickadd";

    // Built from the codepoint, not a literal character in source, so a glyph can't be silently
    // swapped for a different (or invisible) private-use character by a stray copy/paste. See the
    // memory note on this exact failure mode.
    private static readonly string SettingsGlyph = char.ConvertFromUtf32(0xE713);
    private static readonly string CloseGlyph = char.ConvertFromUtf32(0xE711);
    private static readonly string HomeGlyph = char.ConvertFromUtf32(0xE80F);
    private static readonly string AddNewGlyph = char.ConvertFromUtf32(0xE710);

    /// <summary>
    /// Opens the app window on its Settings screen — see <see cref="OrbitManager.HandleItemActivation"/>.
    /// Offered from the sidebar's Controls section rather than pinned: the orbit's one fixed slot is
    /// <see cref="CreateOrbitLogo"/> now, which opens Orbit settings specifically.
    /// </summary>
    public static OrbitItem CreateSettings() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Settings",
        Target = SettingsTarget,
        CustomGlyph = SettingsGlyph,
        IsSystem = true,
    };

    /// <summary>Dismisses the live orbit (see <see cref="OrbitWindow.OnSelectAsync"/>'s own
    /// IsCloseItem check) — purely a target match, so this works the same whichever slot the item
    /// ends up placed in, pinned or not.</summary>
    public static OrbitItem CreateClose() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Close",
        Target = CloseTarget,
        CustomGlyph = CloseGlyph,
        IsSystem = true,
    };

    /// <summary>Opens the app window on its Home screen.</summary>
    public static OrbitItem CreateHome() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Home",
        Target = HomeTarget,
        CustomGlyph = HomeGlyph,
        IsSystem = true,
    };

    /// <summary>
    /// The orbit's one truly fixed slot, always last, standing in for Settings' old pinned position
    /// — except it opens Orbit settings specifically rather than the app window's default screen,
    /// and is drawn as the SwiftX mark itself rather than a glyph (see
    /// <see cref="ShellWindow.RenderPreview"/> and <see cref="OrbitWindow.CreateBubble"/>, both of
    /// which special-case <see cref="OrbitLogoTarget"/> to swap in <see cref="Services.AppLogo"/>).
    /// The glyph set here is only a fallback for any generic code that reads <see cref="OrbitItem.Glyph"/>
    /// without knowing about that substitution.
    /// </summary>
    public static OrbitItem CreateOrbitLogo() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Configure Orbit",
        Target = OrbitLogoTarget,
        CustomGlyph = SettingsGlyph,
        IsSystem = true,
    };

    /// <summary>Opens the same "Add to orbit" window as the tray icon's Add entry, targeting the
    /// same list (<c>Config.Primary.Items</c> — Settings › Elements). That window
    /// itself now accepts a drag-and-drop, so this is also the entry point for "bring something in
    /// from outside" — see AddItemWindow's own drop handling. Empty user slots on the live orbit
    /// use this as a placeholder.</summary>
    public static OrbitItem CreateAddNew() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Add new",
        Target = AddNewTarget,
        CustomGlyph = AddNewGlyph,
        IsSystem = true,
    };

    /// <summary>
    /// Every non-scannable built-in target, Home/Settings/Close included — used to keep all of them
    /// out of listings built for the user's own content (<see cref="ElementLibrary.OnOrbit"/>,
    /// <see cref="HotCornerItems"/>'s own group checks), which is a different question from whether
    /// a given one can be stored on the orbit at all (see <see cref="Strip"/>).
    /// </summary>
    public static bool IsSystemTarget(string? target) =>
        string.Equals(target, SettingsTarget, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, CloseTarget, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, HomeTarget, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, OrbitLogoTarget, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, AddNewTarget, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Removes any stored orbit-logo entries from <paramref name="items"/> and every nested group.
    /// The logo is the only target here that can never legitimately be stored — it is synthesized
    /// onto the last slot at render time and offered nowhere a user could have added it — so a
    /// stored one can only be leftover state a future build should not render twice. Home, Settings
    /// and Close are deliberately left alone: those are optional now, added from the sidebar's
    /// Controls section like any other item, and a stored one is the user's own choice, not stale
    /// state to repair away. Returns true if anything was removed, so the caller can persist the
    /// migration immediately.
    /// </summary>
    public static bool Strip(List<OrbitItem> items)
    {
        bool changed = items.RemoveAll(i =>
            string.Equals(i.Target, OrbitLogoTarget, StringComparison.OrdinalIgnoreCase)) > 0;
        foreach (var group in items.Where(i => i.IsGroup))
            changed |= Strip(group.Children);
        return changed;
    }
}
