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
