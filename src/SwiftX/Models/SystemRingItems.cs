namespace SwiftX.Models;

/// <summary>
/// The two built-in ring entries — Settings and Close — that are always on the ring and can never
/// be reordered, hidden, or removed.
/// <para>
/// They are <em>not</em> stored in <see cref="RingProfile.Items"/>: the ring synthesizes them at
/// render time into the last two slots. Keeping them out of the profile is what stops them
/// showing up in Settings › Apps &amp; links as ordinary rows the user can toggle or delete, and
/// removes the whole class of "config saved before Close existed" repairs — there is nothing to
/// repair when the items do not live in the config at all. <see cref="Strip"/> migrates configs
/// written by the versions that did store them.
/// </para>
/// </summary>
public static class SystemRingItems
{
    public const string SettingsTarget = "swiftx:settings";
    public const string CloseTarget = "swiftx:close";

    /// <summary>Quick action nested under Settings — see <see cref="CreateAddNew"/>.</summary>
    public const string AddNewTarget = "swiftx:quickadd";

    /// <summary>
    /// Settings is a group now, not a plain launch item — <see cref="CreateAddNew"/> hangs off
    /// it as a flyout child the same way any user group's children do (see
    /// RingWindow.ExpandFlyout, which reads a bubble's Children generically and has no special
    /// case for system items). It is synthesized fresh here rather than stored anywhere, for the
    /// same reason Settings/Close never were — see the class doc comment.
    /// </summary>
    public static RingItem CreateSettings() => new()
    {
        Kind = RingItemKind.Group,
        DisplayName = "Settings",
        Target = SettingsTarget,
        CustomGlyph = "\uE713",
        IsSystem = true,
        Children = { CreateAddNew() },
    };

    public static RingItem CreateClose() => new()
    {
        Kind = RingItemKind.Application,
        DisplayName = "Close",
        Target = CloseTarget,
        CustomGlyph = "\uE711",
        IsSystem = true,
    };

    /// <summary>Opens the same "Add to ring" window as the tray icon's Add entry, targeting the
    /// same list (<c>Config.Primary.Items</c> — Settings › Apps &amp; links). That window
    /// itself now accepts a drag-and-drop, so this is also the entry point for "bring something in
    /// from outside" — see AddItemWindow's own drop handling.</summary>
    public static RingItem CreateAddNew() => new()
    {
        Kind = RingItemKind.Application,
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
    public static bool Strip(List<RingItem> items)
    {
        bool changed = items.RemoveAll(i => IsSystemTarget(i.Target)) > 0;
        foreach (var group in items.Where(i => i.IsGroup))
            changed |= Strip(group.Children);
        return changed;
    }
}
