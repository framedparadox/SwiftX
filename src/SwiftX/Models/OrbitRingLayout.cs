namespace SwiftX.Models;

/// <summary>
/// How orbit items are laid out across rings: an inner circle of 3–8 slots, and when there are more
/// than eight items in total, an outer concentric circle for the overflow.
/// <para>
/// Settings and Close always occupy the last two inner slots (see <see cref="SystemOrbitItems"/>).
/// User items fill the slots before them; anything past the inner ring's user capacity lands on the
/// outer circle. Angular spacing is <c>360° / n</c> on each ring, so three items sit 120° apart and
/// eight sit at the familiar 45°.
/// </para>
/// </summary>
public static class OrbitRingLayout
{
    /// <summary>Fewest items the inner circle will show (including Settings and Close).</summary>
    public const int MinInnerCount = 3;

    /// <summary>Most items the inner circle will show — the historical fixed slot count.</summary>
    public const int MaxInnerCount = OrbitMetrics.SlotCount;

    /// <summary>Settings and Close, always synthesized onto the inner ring.</summary>
    public const int SystemCount = 2;

    /// <summary>User items that fit on the inner ring before overflow.</summary>
    public const int MaxInnerUserCount = MaxInnerCount - SystemCount;

    /// <summary>User items that fit on the outer ring.</summary>
    public const int MaxOuterCount = OrbitMetrics.FlyoutSlotCount;

    /// <summary>Total user items the orbit will hold across both rings.</summary>
    public const int MaxUserCount = MaxInnerUserCount + MaxOuterCount;

    /// <summary>One painted place on either ring.</summary>
    /// <param name="Item">What to draw (Add-new placeholders already substituted for empties).</param>
    /// <param name="Index">Index within its own ring, 0-based.</param>
    /// <param name="UserSlot">Index in the visible user-slot list, or -1 for Settings/Close.</param>
    /// <param name="AngleRad">Canvas angle (0 = east, clockwise-positive via cos/sin as elsewhere).</param>
    /// <param name="IsOuter">True when the entry sits on the overflow ring.</param>
    /// <param name="IsPinned">True for Settings and Close.</param>
    public readonly record struct Entry(
        OrbitItem Item,
        int Index,
        int UserSlot,
        double AngleRad,
        bool IsOuter,
        bool IsPinned);

    /// <summary>
    /// Builds the entries to paint for <paramref name="profileItems"/>. Empty user slots become
    /// Add-new placeholders. The inner ring is padded only as far as
    /// <see cref="MinInnerCount"/> requires (plus one trailing Add-new when there is still room and
    /// no empty slot already offers one); it never grows past <see cref="MaxInnerCount"/>.
    /// </summary>
    public static IReadOnlyList<Entry> Build(
        IEnumerable<OrbitItem> profileItems, OrbitItem settings, OrbitItem close)
    {
        var visible = profileItems.Where(i => !i.Hidden).Take(MaxUserCount).ToList();

        var innerUsers = new List<(OrbitItem Item, int UserSlot)>();
        var outerUsers = new List<(OrbitItem Item, int UserSlot)>();
        for (int i = 0; i < visible.Count; i++)
        {
            var entry = (visible[i], i);
            if (i < MaxInnerUserCount)
                innerUsers.Add(entry);
            else
                outerUsers.Add(entry);
        }

        // Pad so Settings + Close + user places reach the minimum inner count.
        int minUserPlaces = Math.Max(0, MinInnerCount - SystemCount);
        while (innerUsers.Count < minUserPlaces && outerUsers.Count == 0)
            innerUsers.Add((SystemOrbitItems.CreateAddNew(), innerUsers.Count));

        // One trailing Add-new affordance while the inner ring still has room and nothing empty
        // already offers a place to drop into — but never invent extras once overflow exists (the
        // inner user band is full by definition then).
        if (outerUsers.Count == 0 &&
            innerUsers.Count < MaxInnerUserCount &&
            !innerUsers.Any(e => e.Item.IsEmpty ||
                                 string.Equals(e.Item.Target, SystemOrbitItems.AddNewTarget,
                                     StringComparison.OrdinalIgnoreCase)))
        {
            innerUsers.Add((SystemOrbitItems.CreateAddNew(), innerUsers.Count));
        }

        var innerItems = new List<(OrbitItem Item, int UserSlot, bool Pinned)>(
            innerUsers.Count + SystemCount);
        foreach (var (item, slot) in innerUsers)
        {
            var drawn = item.IsEmpty ? SystemOrbitItems.CreateAddNew() : item;
            innerItems.Add((drawn, slot, false));
        }
        innerItems.Add((settings, -1, true));
        innerItems.Add((close, -1, true));

        int innerCount = innerItems.Count;
        var result = new List<Entry>(innerCount + outerUsers.Count);
        for (int i = 0; i < innerCount; i++)
        {
            var (item, slot, pinned) = innerItems[i];
            result.Add(new Entry(item, i, slot, SlotAngleRad(i, innerCount), IsOuter: false, pinned));
        }

        int outerCount = outerUsers.Count;
        for (int i = 0; i < outerCount; i++)
        {
            var (item, slot) = outerUsers[i];
            var drawn = item.IsEmpty ? SystemOrbitItems.CreateAddNew() : item;
            result.Add(new Entry(drawn, i, slot, SlotAngleRad(i, outerCount), IsOuter: true, false));
        }

        return result;
    }

    /// <summary>Angle (radians) of slot <paramref name="index"/> on a ring of <paramref name="count"/>,
    /// matching OrbitWindow: slot 0 is straight up, slots advance clockwise.</summary>
    public static double SlotAngleRad(int index, int count)
    {
        if (count <= 0) return -Math.PI / 2;
        double step = 360.0 / count;
        return (-90 + index * step) * Math.PI / 180.0;
    }

    /// <summary>
    /// The slot (0..<paramref name="count"/>-1) angularly nearest a pointer at offset
    /// (<paramref name="dx"/>, <paramref name="dy"/>) from the orbit center.
    /// </summary>
    public static int NearestSlotIndex(double dx, double dy, int count)
    {
        if (count <= 0) return 0;
        double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        double normalized = (angle + 90 + 360) % 360;
        double step = 360.0 / count;
        return (int)Math.Round(normalized / step) % count;
    }

    /// <summary>How many user slots are free across both rings.</summary>
    public static bool HasRoom(IEnumerable<OrbitItem> items) =>
        OrbitSlots.HasRoom(items, MaxUserCount);
}
