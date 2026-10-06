namespace SwiftX.Models;

/// <summary>
/// How the orbit's user slots are held in <see cref="OrbitProfile.Items"/>: slot <c>n</c> is the
/// <c>n</c>-th item that is not <see cref="OrbitItem.Hidden"/>, and a slot with nothing in it is an
/// <see cref="OrbitItemKind.Empty"/> placeholder rather than a gap closed up by the items after it.
/// <para>
/// Taking an item off the orbit therefore leaves its slot empty (<see cref="Vacate"/>), and the next
/// thing added goes into the first empty slot (<see cref="Place"/>) before it goes after the last
/// one. Placeholders at the end of the list carry no information and are dropped.
/// </para>
/// </summary>
public static class OrbitSlots
{
    /// <summary>The items that take a slot, placeholders included, in slot order.</summary>
    public static List<OrbitItem> Visible(
        IEnumerable<OrbitItem> items, int capacity = OrbitRingLayout.MaxUserCount) =>
        items.Where(i => !i.Hidden).Take(capacity).ToList();

    /// <summary>How many slots hold a real item.</summary>
    public static int Used(IEnumerable<OrbitItem> items) =>
        items.Count(i => !i.Hidden && !i.IsEmpty);

    /// <summary>Whether another item can go on the orbit: an empty slot, or one still free at the end.</summary>
    public static bool HasRoom(
        IEnumerable<OrbitItem> items, int capacity = OrbitRingLayout.MaxUserCount)
    {
        var visible = items.Where(i => !i.Hidden).ToList();
        return visible.Count < capacity ||
               visible.Take(capacity).Any(i => i.IsEmpty);
    }

    public static OrbitItem NewEmpty() => new() { Kind = OrbitItemKind.Empty };

    /// <summary>
    /// Puts <paramref name="item"/> in the first empty slot, or after the last one. False when the
    /// orbit has no room.
    /// </summary>
    public static bool Place(List<OrbitItem> items, OrbitItem item)
    {
        if (!HasRoom(items))
            return false;

        var hole = items.FirstOrDefault(i => !i.Hidden && i.IsEmpty);
        if (hole is not null && Visible(items).Contains(hole))
            items[items.IndexOf(hole)] = item;
        else
            items.Add(item);
        return true;
    }

    /// <summary>
    /// Puts <paramref name="item"/> in slot <paramref name="slot"/>, which must be empty or past the
    /// end. Slots between the end of the list and <paramref name="slot"/> become placeholders, so the
    /// item lands where it was dropped instead of sliding down to the first free place.
    /// </summary>
    public static bool PlaceAt(List<OrbitItem> items, int slot, OrbitItem item, int capacity = OrbitRingLayout.MaxUserCount)
    {
        if (slot < 0 || slot >= capacity)
            return false;

        var visible = items.Where(i => !i.Hidden).Take(capacity).ToList();
        if (slot < visible.Count)
        {
            var occupant = visible[slot];
            if (!occupant.IsEmpty)
                return false;
            items[items.IndexOf(occupant)] = item;
            return true;
        }

        while (items.Count(i => !i.Hidden) < slot)
            items.Add(NewEmpty());
        items.Add(item);
        return true;
    }

    /// <summary>
    /// Takes <paramref name="item"/> off the orbit, leaving an empty slot where it was. A hidden item
    /// takes no slot, so it is simply removed. False when the item is not in the list.
    /// </summary>
    public static bool Vacate(List<OrbitItem> items, OrbitItem item)
    {
        int at = items.IndexOf(item);
        if (at < 0)
            return false;

        if (item.Hidden)
            items.RemoveAt(at);
        else
            items[at] = NewEmpty();

        TrimTrailing(items);
        return true;
    }

    /// <summary>
    /// The slot order after dragging <paramref name="dragged"/> to <paramref name="targetSlot"/>,
    /// from <paramref name="visible"/> (see <see cref="Visible"/>). Dropped on an item or an empty
    /// slot inside the list it is moved there, the items between shifting by one. Dropped on an empty
    /// slot beyond the end of the list it goes exactly there, its old slot left empty, rather than
    /// sliding back to the first free place.
    /// </summary>
    public static List<OrbitItem> Moved(
        IReadOnlyList<OrbitItem> visible, OrbitItem dragged, int targetSlot, int capacity = OrbitRingLayout.MaxUserCount)
    {
        var order = visible.ToList();
        int from = order.IndexOf(dragged);
        if (from < 0 || targetSlot < 0 || targetSlot >= capacity)
            return order;

        if (targetSlot >= order.Count)
        {
            order[from] = NewEmpty();
            while (order.Count < targetSlot)
                order.Add(NewEmpty());
            order.Add(dragged);
        }
        else
        {
            order.RemoveAt(from);
            order.Insert(targetSlot, dragged);
        }
        return order;
    }

    /// <summary>Drops placeholders after the last real item; they say nothing a short list does not.</summary>
    public static void TrimTrailing(List<OrbitItem> items)
    {
        for (int i = items.Count - 1; i >= 0 && items[i].IsEmpty; i--)
            items.RemoveAt(i);
    }
}
