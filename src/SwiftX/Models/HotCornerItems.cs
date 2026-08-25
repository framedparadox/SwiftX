namespace SwiftX.Models;

/// <summary>
/// Which items the hot corner's quadrant shows.
/// <para>
/// Until the corner is arranged by hand there is nothing stored: the four places are filled from
/// the ring itself (<see cref="QuadrantLayout.Compose"/>), so a corner that has never been touched
/// keeps up with the ring as items are added and reordered. The first edit
/// (<see cref="Edit"/>) writes that same selection into <see cref="AppConfig.HotCornerRing"/>,
/// and from then on the corner is the user's own arrangement.
/// </para>
/// <para>
/// Stored entries are copies, not references — the config is one JSON file with no notion of a
/// pointer — so <see cref="Resolve"/> maps each one back to the live thing it names before it is
/// drawn: Settings to the synthesized entry, the tools group to the one on the ring (whose
/// children follow the Tools page's switches), and everything else to the ring item of the same
/// id. An entry naming something no longer on the ring keeps working from its stored copy, which
/// is what lets the corner hold an item the ring itself does not.
/// </para>
/// </summary>
public static class HotCornerItems
{
    /// <summary>The items the corner shows right now, in arc order.</summary>
    public static List<RingItem> Resolve(AppConfig config, RingItem settings)
    {
        if (config.HotCornerRing.Count == 0)
            return QuadrantLayout.Compose(config.Primary.Items, settings);

        return config.HotCornerRing
            .Take(QuadrantLayout.ItemCount)
            .Select(entry => Live(entry, config, settings))
            .ToList();
    }

    /// <summary>
    /// The stored list, ready to be rearranged, added to, or removed from — seeded from the
    /// default selection the first time, so an edit starts from what the user can already see
    /// rather than from nothing. Mutate the returned list, then persist the config.
    /// </summary>
    public static List<RingItem> Edit(AppConfig config, RingItem settings)
    {
        if (config.HotCornerRing.Count == 0)
            config.HotCornerRing.AddRange(Resolve(config, settings));
        return config.HotCornerRing;
    }

    /// <summary>Whether the corner is still following the ring rather than a stored arrangement.</summary>
    public static bool IsDefault(AppConfig config) => config.HotCornerRing.Count == 0;

    /// <summary>Drops the user's arrangement, putting the corner back on the ring's own picks.</summary>
    public static void Reset(AppConfig config) => config.HotCornerRing.Clear();

    private static RingItem Live(RingItem entry, AppConfig config, RingItem settings)
    {
        if (string.Equals(entry.Target, SystemRingItems.SettingsTarget, StringComparison.OrdinalIgnoreCase))
            return settings;

        if (string.Equals(entry.Target, ToolCatalog.ToolsGroupTarget, StringComparison.OrdinalIgnoreCase))
            return FindByTarget(config.Primary.Items, ToolCatalog.ToolsGroupTarget) ?? entry;

        return FindById(config.Primary.Items, entry.Id) ?? entry;
    }

    private static RingItem? FindByTarget(List<RingItem> items, string target) =>
        items.FirstOrDefault(i => string.Equals(i.Target, target, StringComparison.OrdinalIgnoreCase));

    private static RingItem? FindById(List<RingItem> items, string id)
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
