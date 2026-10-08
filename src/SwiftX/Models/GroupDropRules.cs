namespace SwiftX.Models;

/// <summary>Why a thing can or cannot go into a group. See <see cref="GroupDropRules.Check"/>.</summary>
public enum GroupDropVerdict
{
    Allowed,

    /// <summary>The target is not a group at all.</summary>
    NotAGroup,

    /// <summary>The target is the built-in Tools group, which the Tools screen owns.</summary>
    ToolsGroup,

    /// <summary>The thing is itself a group.</summary>
    NestedGroup,

    /// <summary>The group already holds something with the same target.</summary>
    Duplicate,

    /// <summary>The group already holds as many items as the orbit can show.</summary>
    Full,
}

/// <summary>
/// What may be dropped onto a group in the "Add to orbit" sidebar, kept apart from the drag
/// plumbing so every rule has a test.
/// </summary>
public static class GroupDropRules
{
    /// <summary>
    /// The most children a group can show. Not a limit the model enforces (nothing stops a config
    /// holding more, and the Elements screen's own add paths do not check), but the orbit's flyout
    /// draws at most <see cref="OrbitMetrics.FlyoutSlotCount"/> of a group's children and quietly
    /// drops the rest, so a drop that added a ninth would put something on the orbit nobody can
    /// ever reach.
    /// </summary>
    public static int Capacity => OrbitMetrics.FlyoutSlotCount;

    /// <summary>
    /// How many of <paramref name="group"/>'s children take a flyout slot. Hidden ones do not: the
    /// flyout filters them out before it counts (see <c>OrbitWindow.ExpandFlyout</c>).
    /// </summary>
    public static int Count(OrbitItem group) => group.Children.Count(c => !c.Hidden);

    public static bool IsFull(OrbitItem group) => Count(group) >= Capacity;

    /// <summary>
    /// Whether <paramref name="candidate"/> may become a child of <paramref name="group"/>.
    /// <para>
    /// A group inside a group is refused because the orbit expands one flyout ring and has no third
    /// to open a nested group into (the add dialog hides its Group type for the same reason).
    /// </para>
    /// <para>
    /// A tool is allowed, like anything else: what goes in is a copy, and the Tools group keeps its
    /// own (see <see cref="ToolCatalog.EnsureToolsGroup"/>, which leaves a tool inside another group
    /// where it was put).
    /// </para>
    /// </summary>
    public static GroupDropVerdict Check(OrbitItem group, OrbitItem candidate)
    {
        if (!group.IsGroup)
            return GroupDropVerdict.NotAGroup;
        if (ElementLibrary.IsBuiltInToolsGroup(group))
            return GroupDropVerdict.ToolsGroup;
        if (candidate.IsGroup)
            return GroupDropVerdict.NestedGroup;
        if (ElementLibrary.ContainsTarget(group.Children, candidate.Target))
            return GroupDropVerdict.Duplicate;
        if (IsFull(group))
            return GroupDropVerdict.Full;
        return GroupDropVerdict.Allowed;
    }

    /// <summary>Adds <paramref name="candidate"/> to <paramref name="group"/> if <see cref="Check"/> allows it.</summary>
    public static GroupDropVerdict TryAdd(OrbitItem group, OrbitItem candidate)
    {
        var verdict = Check(group, candidate);
        if (verdict == GroupDropVerdict.Allowed)
            group.Children.Add(candidate);
        return verdict;
    }

    /// <summary>
    /// A short line saying why a drop was refused, or null when it was allowed. Short because it
    /// is shown in a drag caption and in the trailing slot of a sidebar row.
    /// </summary>
    public static string? Reason(GroupDropVerdict verdict) => verdict switch
    {
        GroupDropVerdict.Allowed => null,
        GroupDropVerdict.NotAGroup => "Not a group",
        GroupDropVerdict.ToolsGroup => "Managed on the Tools screen",
        GroupDropVerdict.NestedGroup => "Groups cannot hold groups",
        GroupDropVerdict.Duplicate => "Already in this group",
        GroupDropVerdict.Full => $"Group is full ({Capacity} items)",
        _ => null,
    };
}
