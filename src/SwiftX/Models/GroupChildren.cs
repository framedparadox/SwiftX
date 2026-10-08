namespace SwiftX.Models;

/// <summary>
/// Changing what a group already holds: putting its items in a different order, and taking one
/// out. The "Add to orbit" sidebar's group rows are where both are done, and what each does is
/// decided here, apart from the drag and menu plumbing, so every rule has a test.
/// <para>
/// What may go <em>in</em> is <see cref="GroupDropRules"/>'s question; this is only about what is
/// already there.
/// </para>
/// </summary>
public static class GroupChildren
{
    /// <summary>
    /// Whether the group's items may be rearranged and removed one by one. Not the built-in Tools
    /// group: <see cref="ToolCatalog.EnsureToolsGroup"/> puts every tool back in it on each launch,
    /// so a tool taken out would only come back, and a tool is turned off on the Tools screen. The
    /// sidebar does not list that group at all; this keeps the rule true for anyone who asks.
    /// </summary>
    public static bool IsEditable(OrbitItem group) =>
        group.IsGroup && !ElementLibrary.IsBuiltInToolsGroup(group);

    /// <summary>Whether <paramref name="item"/> is one of <paramref name="group"/>'s own children.</summary>
    public static bool Holds(OrbitItem group, OrbitItem item) =>
        group.Children.Any(c => ReferenceEquals(c, item));

    /// <summary>
    /// Moves <paramref name="child"/> to the place <paramref name="target"/> holds, the items
    /// between them shifting along by one — the same "take it out, then put it where the target
    /// was" the orbit's own reorder does, so dragging an item down lands it after the one it was
    /// dropped on and dragging it up lands it before. False, and nothing changed, when either is
    /// not in the group, they are the same item, or the group is not editable.
    /// </summary>
    public static bool Move(OrbitItem group, OrbitItem child, OrbitItem target)
    {
        if (!IsEditable(group) || ReferenceEquals(child, target))
            return false;

        int from = IndexOf(group, child);
        int to = IndexOf(group, target);
        if (from < 0 || to < 0)
            return false;

        group.Children.RemoveAt(from);
        group.Children.Insert(to, child);
        return true;
    }

    /// <summary>
    /// Moves <paramref name="child"/> one place up (<paramref name="delta"/> -1) or down (+1)
    /// among the items the sidebar lists, which are the group's children without its empty
    /// placeholders. False when it is already at that end, or is not in the group.
    /// </summary>
    public static bool MoveBy(OrbitItem group, OrbitItem child, int delta)
    {
        if (!IsEditable(group) || delta == 0 || IndexOf(group, child) < 0)
            return false;

        var listed = Listed(group);
        int at = listed.FindIndex(c => ReferenceEquals(c, child));
        int next = at + Math.Sign(delta);
        if (at < 0 || next < 0 || next >= listed.Count)
            return false;

        return Move(group, child, listed[next]);
    }

    /// <summary>
    /// Takes <paramref name="child"/> out of <paramref name="group"/>. The item is gone from the
    /// orbit altogether — it lived only inside the group — though it is offered again under its
    /// category if the sidebar's suggestions include it. False when it is not one of the group's
    /// children or the group is not editable.
    /// </summary>
    public static bool Remove(OrbitItem group, OrbitItem child)
    {
        if (!IsEditable(group))
            return false;

        int at = IndexOf(group, child);
        if (at < 0)
            return false;

        group.Children.RemoveAt(at);
        return true;
    }

    /// <summary>The children the sidebar lists under a group, in order: everything but placeholders.</summary>
    public static List<OrbitItem> Listed(OrbitItem group) =>
        group.Children.Where(c => !c.IsEmpty).ToList();

    private static int IndexOf(OrbitItem group, OrbitItem item) =>
        group.Children.FindIndex(c => ReferenceEquals(c, item));
}
