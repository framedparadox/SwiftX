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

    public static RingItem CreateSettings() => new()
    {
        Kind = RingItemKind.Application,
        DisplayName = "Settings",
        Target = SettingsTarget,
        CustomGlyph = "\uE713",
        IsSystem = true,
    };

    public static RingItem CreateClose() => new()
    {
        Kind = RingItemKind.Application,
        DisplayName = "Close",
        Target = CloseTarget,
        CustomGlyph = "\uE711",
        IsSystem = true,
    };

    public static bool IsSystemTarget(string? target) =>
        string.Equals(target, SettingsTarget, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target, CloseTarget, StringComparison.OrdinalIgnoreCase);

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
