namespace SwiftX.Models;

/// <summary>
/// The two built-in orbit entries — Settings and Close — that are always on the orbit and can never
/// be reordered, hidden, or removed.
/// <para>
/// They are <em>not</em> stored in <see cref="OrbitProfile.Items"/>: the orbit synthesizes them at
/// render time into the last two slots. Keeping them out of the profile is what stops them
/// showing up in Settings › Elements as ordinary rows the user can toggle or delete, and
/// removes the whole class of "config saved before Close existed" repairs — there is nothing to
/// repair when the items do not live in the config at all. <see cref="Strip"/> migrates configs
/// written by the versions that did store them.
/// </para>
/// </summary>
public static class SystemOrbitItems
{
    public const string SettingsTarget = "swiftx:settings";
    public const string CloseTarget = "swiftx:close";

    /// <summary>Placeholder painted onto an empty user slot — see <see cref="CreateAddNew"/>.</summary>
    public const string AddNewTarget = "swiftx:quickadd";

    /// <summary>
    /// A plain launch item, like Close. It used to be a group that owned an "Add new" flyout
    /// child; that nesting is gone — empty orbit slots carry their own Add-new placeholders, and
    /// Settings itself only opens the app window.
    /// </summary>
    public static OrbitItem CreateSettings() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Settings",
        Target = SettingsTarget,
        CustomGlyph = "\uE713",
        IsSystem = true,
    };

    public static OrbitItem CreateClose() => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = "Close",
        Target = CloseTarget,
        CustomGlyph = "\uE711",
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
        CustomGlyph = "\uE710",
        IsSystem = true,
    };

    public static bool IsSystemTarget(string? target) =>
        string.Equals(target, SettingsTarget, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, CloseTarget, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, AddNewTarget, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Removes any stored Settings/Close entries from <paramref name="items"/> and every nested
    /// group, so a config written before these became synthetic doesn't render them twice.
    /// Returns true if anything was removed, so the caller can persist the migration immediately.
    /// </summary>
    public static bool Strip(List<OrbitItem> items)
    {
        bool changed = items.RemoveAll(i => IsSystemTarget(i.Target)) > 0;
        foreach (var group in items.Where(i => i.IsGroup))
            changed |= Strip(group.Children);
        return changed;
    }
}
