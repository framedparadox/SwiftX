namespace SwiftX.Models;

/// <summary>
/// Which items the hot corner's quadrant shows.
/// <para>
/// Until the corner is arranged by hand there is nothing stored: the four places are filled from
/// the orbit itself (<see cref="QuadrantLayout.Compose"/>), so a corner that has never been touched
/// keeps up with the orbit as items are added and reordered. The first edit
/// (<see cref="Edit"/>) writes that same selection into <see cref="AppConfig.HotCornerOrbit"/>,
/// and from then on the corner is the user's own arrangement.
/// </para>
/// <para>
/// Stored entries are copies, not references — the config is one JSON file with no notion of a
/// pointer — so <see cref="Resolve"/> maps each one back to the live thing it names before it is
/// drawn: Settings to the synthesized entry, the tools group to the one on the orbit (whose
/// children follow the Tools page's switches), QuickAccess to a freshly gathered group of
/// whatever web links and shortcuts are on the orbit right now, and everything else to the orbit
/// item of the same id. An entry naming something no longer on the orbit — including a QuickAccess
/// with nothing left to gather, or a Notepad the orbit never really had — keeps working from its
/// stored copy, which is what lets the corner hold an item the orbit itself does not.
/// </para>
/// </summary>
public static class HotCornerItems
{
    /// <summary>The items the corner shows right now, in arc order.</summary>
    public static List<OrbitItem> Resolve(AppConfig config, OrbitItem settings)
    {
        if (config.HotCornerOrbit.Count == 0)
            return QuadrantLayout.Compose(config.Primary.Items, settings);

        return config.HotCornerOrbit
            .Take(QuadrantLayout.ItemCount)
            .Select(entry => Live(entry, config, settings))
            .ToList();
    }

    /// <summary>
    /// The stored list, ready to be rearranged, added to, or removed from — seeded from the
    /// default selection the first time, so an edit starts from what the user can already see
    /// rather than from nothing. Mutate the returned list, then persist the config.
    /// </summary>
    public static List<OrbitItem> Edit(AppConfig config, OrbitItem settings)
    {
        if (config.HotCornerOrbit.Count == 0)
            config.HotCornerOrbit.AddRange(Resolve(config, settings));
        return config.HotCornerOrbit;
    }

    /// <summary>Whether the corner is still following the orbit rather than a stored arrangement.</summary>
    public static bool IsDefault(AppConfig config) => config.HotCornerOrbit.Count == 0;

    /// <summary>Drops the user's arrangement, putting the corner back on the orbit's own picks.</summary>
    public static void Reset(AppConfig config) => config.HotCornerOrbit.Clear();

    /// <summary>
    /// The entry to store when <paramref name="dragged"/>, a row of the "Add to orbit" sidebar, is
    /// put on the corner: <paramref name="copy"/>, which is what the corner stores for everything,
    /// except that a copy of a group that is on the orbit is given that group's id.
    /// <para>
    /// The id is the whole link. <see cref="Resolve"/> maps a stored entry to the orbit's item of
    /// the same id, so the corner then draws the orbit's group itself, and what is added to it from
    /// the sidebar's Groups list shows on the corner and on the dock as it is added. Left under the
    /// id it was copied with, the entry is a group of its own, frozen at the children it was dragged
    /// with and out of the sidebar's reach: the list only holds the orbit's groups.
    /// </para>
    /// <para>
    /// Only groups. A copy of anything else has nothing to keep in step, and sharing an id with the
    /// orbit's item would make an edit to the corner's one an edit to the orbit's.
    /// </para>
    /// </summary>
    /// <param name="copy">A fresh copy of <paramref name="dragged"/>, as the corner stores it.</param>
    /// <param name="dragged">The live item the sidebar row stands for.</param>
    /// <param name="orbitItems"><c>Config.Primary.Items</c>.</param>
    public static OrbitItem Entry(OrbitItem copy, OrbitItem dragged, IEnumerable<OrbitItem> orbitItems)
    {
        if (ElementLibrary.IsOrbitUserGroup(orbitItems, dragged))
            copy.Id = dragged.Id;
        return copy;
    }

    /// <summary>
    /// Gives a group the corner holds a copy of its link back to the orbit group it was copied from.
    /// <para>
    /// A build from before <see cref="Entry"/> stored a group dragged onto the corner as a copy under
    /// an id of its own. The orbit's group went on changing - items added from the sidebar, put in a
    /// different order, taken out - and the copy went on showing what the group held on the day it
    /// was dragged. Nothing records which orbit group a copy came from, so it is worked out from what
    /// a copy is: cut from that group, so it has the group's name and holds only things the group
    /// holds too (a group is added to and put in order far more often than it is emptied). When
    /// exactly one orbit group fits, the copy takes its id, and from then on it is that group, as a
    /// fresh drag makes it.
    /// </para>
    /// <para>
    /// Slow to conclude on purpose, because a wrong answer swaps one group's contents for another's.
    /// A copy is left exactly as it was when it holds nothing (an empty group has a name and no
    /// evidence, and fits any group of that name), when no orbit group of its name holds everything
    /// it holds, and when more than one does. The corner can hold a group of its own that the orbit
    /// lacks (see <see cref="OwnGroups"/>), and a name alone is not enough to say it is not one.
    /// </para>
    /// <para>
    /// Returns whether anything changed, so the caller knows to persist, and changes nothing a second
    /// time: a linked entry names an orbit item and is skipped.
    /// </para>
    /// </summary>
    public static bool RelinkOrbitGroups(AppConfig config)
    {
        if (config.HotCornerOrbit.Count == 0)
            return false;

        var orbit = config.Primary.Items;
        var groups = ElementLibrary.UserGroups(orbit);
        bool changed = false;

        foreach (var entry in config.HotCornerOrbit)
        {
            if (!entry.IsGroup || entry.IsSystem || SystemOrbitItems.IsSystemTarget(entry.Target) ||
                ElementLibrary.IsBuiltInToolsGroup(entry) || QuadrantLayout.IsQuickAccess(entry))
                continue;

            // Already names an orbit item, which is what Resolve maps it to.
            if (FindById(orbit, entry.Id) is not null)
                continue;

            if (SourceGroup(entry, groups) is not { } source)
                continue;

            entry.Id = source.Id;
            changed = true;
        }

        return changed;
    }

    /// <summary>The one orbit group <paramref name="copy"/> can have been cut from, or null.</summary>
    private static OrbitItem? SourceGroup(OrbitItem copy, IReadOnlyList<OrbitItem> groups)
    {
        var held = copy.Children.Where(c => !c.IsEmpty).ToList();
        if (held.Count == 0)
            return null;

        OrbitItem? found = null;
        foreach (var group in groups)
        {
            if (!SameName(group, copy))
                continue;
            if (!held.All(child => group.Children.Any(other => SameThing(other, child))))
                continue;

            // Two orbit groups would do, so neither can be said to be the one.
            if (found is not null)
                return null;
            found = group;
        }

        return found;
    }

    private static bool SameName(OrbitItem a, OrbitItem b) =>
        string.Equals(a.DisplayName?.Trim(), b.DisplayName?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The same thing to launch: the same kind, pointing at the same target. An item with no
    /// target (a group) names nothing, so it is never the same as another.</summary>
    private static bool SameThing(OrbitItem a, OrbitItem b) =>
        a.Kind == b.Kind &&
        !string.IsNullOrEmpty(a.Target) &&
        string.Equals(a.Target, b.Target, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The groups the corner shows that the orbit does not have: made on the corner's own canvas, or
    /// copies <see cref="RelinkOrbitGroups"/> could not place. Not QuickAccess, which is gathered
    /// from the orbit rather than kept, and not the Tools group, which the Tools screen owns.
    /// <para>
    /// The "Add to orbit" sidebar lists the orbit's groups as the places to put things, so without
    /// these a group that exists only on the corner could not be added to from it at all.
    /// </para>
    /// </summary>
    public static IReadOnlyList<OrbitItem> OwnGroups(AppConfig config, OrbitItem settings) =>
        ElementLibrary.UserGroups(Resolve(config, settings))
            .Where(group => !QuadrantLayout.IsQuickAccess(group) &&
                            !ElementLibrary.IsOrbitUserGroup(config.Primary.Items, group))
            .ToList();

    private static OrbitItem Live(OrbitItem entry, AppConfig config, OrbitItem settings)
    {
        if (string.Equals(entry.Target, SystemOrbitItems.SettingsTarget, StringComparison.OrdinalIgnoreCase))
            return settings;

        if (string.Equals(entry.Target, ToolCatalog.ToolsGroupTarget, StringComparison.OrdinalIgnoreCase))
            return FindByTarget(config.Primary.Items, ToolCatalog.ToolsGroupTarget) ?? entry;

        if (QuadrantLayout.IsQuickAccess(entry))
        {
            var gathered = QuadrantLayout.BuildQuickAccess(config.Primary.Items);
            return gathered is null ? entry : WithStoredIcon(gathered, entry);
        }

        return FindById(config.Primary.Items, entry.Id) ?? entry;
    }

    /// <summary>
    /// Keeps an icon the user gave the corner's QuickAccess group. That group is gathered anew every
    /// time the corner resolves it, so an icon set on the instance on screen would be gone on the
    /// next resolve; it is written onto the corner's stored entry for the group instead, which
    /// <see cref="Resolve"/> carries back onto each new one. Anything that is not QuickAccess is
    /// ignored: the other items are the orbit's own, and an edit to one is already an edit to the
    /// thing the corner shows.
    /// </summary>
    /// <param name="gathered">The QuickAccess group the icon was changed on.</param>
    public static void KeepGatheredIcon(AppConfig config, OrbitItem settings, OrbitItem gathered)
    {
        if (!QuadrantLayout.IsQuickAccess(gathered))
            return;

        var stored = Edit(config, settings).FirstOrDefault(QuadrantLayout.IsQuickAccess);
        if (stored is null || ReferenceEquals(stored, gathered))
            return;

        stored.CustomGlyph = gathered.CustomGlyph;
        stored.CustomIconPath = gathered.CustomIconPath;
        stored.CustomIconColor = gathered.CustomIconColor;
    }

    /// <summary>
    /// Puts the icon stored on <paramref name="entry"/> onto a freshly gathered QuickAccess group. A
    /// picture wins over a glyph, as it does everywhere else; with neither stored the group keeps the
    /// one <see cref="QuadrantLayout.BuildQuickAccess"/> gave it, which is what a reset goes back to.
    /// </summary>
    private static OrbitItem WithStoredIcon(OrbitItem gathered, OrbitItem entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.CustomIconPath))
        {
            gathered.CustomIconPath = entry.CustomIconPath;
            gathered.CustomGlyph = null;
        }
        else if (!string.IsNullOrEmpty(entry.CustomGlyph))
        {
            gathered.CustomGlyph = entry.CustomGlyph;
        }

        gathered.CustomIconColor = entry.CustomIconColor;
        return gathered;
    }

    private static OrbitItem? FindByTarget(List<OrbitItem> items, string target) =>
        items.FirstOrDefault(i => string.Equals(i.Target, target, StringComparison.OrdinalIgnoreCase));

    private static OrbitItem? FindById(List<OrbitItem> items, string id)
    {
        foreach (var item in items)
        {
            if (string.Equals(item.Id, id, StringComparison.Ordinal))
                return item;
            var nested = FindById(item.Children, id);
            if (nested is not null)
                return nested;
        }
        return null;
    }
}
