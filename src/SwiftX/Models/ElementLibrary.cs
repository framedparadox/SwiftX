namespace SwiftX.Models;

/// <summary>
/// What each category of the "Add to orbit" sidebar lists, decided without any UI so it can be
/// tested: the catalog's suggestions, plus whatever the orbit is already carrying.
/// <para>
/// The catalog alone left the sidebar blind to anything the user had added by hand. A link typed
/// into the add dialog, a folder dropped on the ring, a shortcut picked from Explorer: none of
/// them are in <c>OnboardingCatalog</c>, so none of them showed up under Links, Folders or
/// Shortcuts, and the sidebar looked as if it had never heard of the thing sitting on the ring.
/// Each category is therefore the catalog's suggestions <em>union</em> the orbit's own items of
/// that category, at the top level and inside groups, de-duplicated by target.
/// </para>
/// <para>
/// There is deliberately no stored "library" behind this. An item that is taken off the orbit and
/// is not in the catalog has nowhere left to be listed from, so it leaves its category. That is the cost
/// of not adding another thing to persist and keep in step with the orbit.
/// </para>
/// </summary>
public static class ElementLibrary
{
    /// <summary>
    /// Whether <paramref name="item"/> is the built-in Tools group. It belongs to the Tools screen
    /// (see <see cref="ToolCatalog.EnsureToolsGroup"/>), so the sidebar neither offers it as a
    /// group to add things to nor lists the tools inside it as if they were the user's own items.
    /// </summary>
    public static bool IsBuiltInToolsGroup(OrbitItem item) =>
        item.IsGroup &&
        string.Equals(item.Target, ToolCatalog.ToolsGroupTarget, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether any of <paramref name="items"/>, or anything inside a group among them, already
    /// points at <paramref name="target"/>. The sidebar's "already on the orbit" test, and the one
    /// every dedupe here uses. Compared case-insensitively, because a Windows path is.
    /// </summary>
    public static bool ContainsTarget(IEnumerable<OrbitItem> items, string target)
    {
        if (string.IsNullOrEmpty(target))
            return false;

        foreach (var item in items)
        {
            if (string.Equals(item.Target, target, StringComparison.OrdinalIgnoreCase))
                return true;
            if (item.Children.Count > 0 && ContainsTarget(item.Children, target))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The orbit's own items, flattened: every top-level item and every item inside a group, with
    /// the built-in Tools group (and the tools in it) and the orbit's synthesized system entries
    /// left out. A top-level group is listed as itself and then its children; a group found inside
    /// another group is not listed, since the orbit has no way to open one, but what it holds still is.
    /// </summary>
    public static IReadOnlyList<OrbitItem> OnOrbit(IEnumerable<OrbitItem> orbitItems)
    {
        var found = new List<OrbitItem>();
        Collect(orbitItems, topLevel: true);
        return found;

        void Collect(IEnumerable<OrbitItem> items, bool topLevel)
        {
            foreach (var item in items)
            {
                if (item.IsSystem || item.IsEmpty || SystemOrbitItems.IsSystemTarget(item.Target))
                    continue;
                if (IsBuiltInToolsGroup(item))
                    continue;

                if (item.IsGroup)
                {
                    if (topLevel)
                        found.Add(item);
                    Collect(item.Children, topLevel: false);
                    continue;
                }

                found.Add(item);
            }
        }
    }

    /// <summary>
    /// The groups a thing can be dropped into: the orbit's top-level groups, in orbit order,
    /// without the built-in Tools group.
    /// </summary>
    public static IReadOnlyList<OrbitItem> UserGroups(IEnumerable<OrbitItem> orbitItems) =>
        orbitItems
            .Where(i => i.IsGroup && !i.IsSystem && !IsBuiltInToolsGroup(i))
            .DistinctBy(i => i.Id)
            .ToList();

    /// <summary>
    /// Whether <paramref name="item"/> is one of the orbit's own groups — the live object a Groups
    /// row in the sidebar stands for — rather than a copy of one, or a group that lives only on the
    /// hot corner. Compared by reference: a copy has the same name and the same children and is
    /// still not that group, which is the whole difference between a group that follows what is
    /// added to it from the sidebar and one that does not.
    /// </summary>
    public static bool IsOrbitUserGroup(IEnumerable<OrbitItem> orbitItems, OrbitItem item) =>
        item.IsGroup && UserGroups(orbitItems).Any(group => ReferenceEquals(group, item));

    /// <summary>
    /// The rows of one category. <paramref name="catalog"/> is everything that could be offered
    /// (the machine-scanned suggestions and the tool catalog, in the order they should appear);
    /// <paramref name="orbitItems"/> is <c>Config.Primary.Items</c>.
    /// <para>
    /// The catalog's entries come first and win a tie, then the orbit's own that the catalog did
    /// not already cover. Tools are the exception: the tool catalog is the whole truth about which
    /// tools exist, and a tool item on the orbit that is not in it is a dead entry rather than
    /// something to offer. Groups are the other: they are not suggestions at all, and are told
    /// apart by id because a group has no target to compare.
    /// </para>
    /// </summary>
    public static IReadOnlyList<OrbitItem> RowsFor(
        ElementCategory category, IEnumerable<OrbitItem> catalog, IEnumerable<OrbitItem> orbitItems)
    {
        if (category == ElementCategory.Groups)
            return UserGroups(orbitItems);

        var rows = new List<OrbitItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Offer(OrbitItem item)
        {
            if (ElementCategories.Of(item) != category)
                return;
            if (string.IsNullOrWhiteSpace(item.Target) || !seen.Add(item.Target))
                return;
            rows.Add(item);
        }

        foreach (var item in catalog)
            Offer(item);

        if (category != ElementCategory.Tools)
        {
            foreach (var item in OnOrbit(orbitItems))
                Offer(item);
        }

        return rows;
    }

    /// <summary>
    /// A new, empty group as the sidebar's "New group" builds it: the same shape the add
    /// dialog's Group type produces: the group mark, and "Group" when no name was given.
    /// </summary>
    public static OrbitItem NewGroup(string? name) => new()
    {
        Kind = OrbitItemKind.Group,
        DisplayName = string.IsNullOrWhiteSpace(name) ? "Group" : name.Trim(),
        CustomGlyph = ElementCategories.Glyph(ElementCategory.Groups),
    };
}
