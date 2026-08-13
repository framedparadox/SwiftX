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

    /// <summary>Quick action nested under Settings \u2014 see <see cref="CreateDragDrop"/>. Never
    /// reaches <c>RingManager.HandleItemActivation</c>: <c>RingWindow.OnSelectAsync</c> intercepts
    /// it to arm the drop-target mode instead of "activating" anything.</summary>
    public const string DragDropTarget = "swiftx:quickdrop";

    /// <summary>Quick action nested under Settings \u2014 see <see cref="CreateAddNew"/>.</summary>
    public const string AddNewTarget = "swiftx:quickadd";

    /// <summary>
    /// Settings is a group now, not a plain launch item \u2014 <see cref="CreateDragDrop"/> and
    /// <see cref="CreateAddNew"/> hang off it as flyout children the same way any user group's
    /// children do (see RingWindow.ExpandFlyout, which reads a bubble's Children generically and
    /// has no special case for system items). Both children are themselves synthesized fresh here
    /// rather than stored anywhere, for the same reason Settings/Close never were \u2014 see the class
    /// doc comment.
    /// </summary>
    public static RingItem CreateSettings() => new()
    {
        Kind = RingItemKind.Group,
        DisplayName = "Settings",
        Target = SettingsTarget,
        CustomGlyph = "\uE713",
        IsSystem = true,
        Children = { CreateDragDrop(), CreateAddNew() },
    };

    public static RingItem CreateClose() => new()
    {
        Kind = RingItemKind.Application,
        DisplayName = "Close",
        Target = CloseTarget,
        CustomGlyph = "\uE711",
        IsSystem = true,
    };

    /// <summary>
    /// Arms RingWindow's drop-target mode on click rather than launching anything \u2014 see
    /// <c>RingWindow.OnSelectAsync</c>'s <c>IsDragDropItem</c> branch. Its bubble wears an import
    /// glyph so it reads as "bring something in" at a glance, and pulses for as long as drop mode
    /// stays armed (see <c>RingWindow.StartDragDropPulse</c>).
    /// </summary>
    public static RingItem CreateDragDrop() => new()
    {
        Kind = RingItemKind.Application,
        DisplayName = "Drag & drop",
        Target = DragDropTarget,
        CustomGlyph = "\uE8B5",
        IsSystem = true,
    };

    /// <summary>Opens the same "Add to ring" window as the tray icon's Add entry, targeting the
    /// same list (<c>Config.Primary.Items</c> \u2014 Settings \u203A Apps &amp; links).</summary>
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
        string.Equals(target, DragDropTarget, StringComparison.OrdinalIgnoreCase) ||
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
