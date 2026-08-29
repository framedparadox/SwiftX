namespace SwiftX.Models;

/// <summary>
/// Where the items go when a hot corner summons the quarter-circle ring: the center sits on the
/// screen corner itself and the items fan out along the quarter arc that is actually on screen.
/// <para>
/// Two things have to hold at once, and both depend on the radius: neighbouring items must not
/// touch each other, and the first and last must clear the two screen edges the corner is made
/// of. <see cref="Radius"/> is what reconciles them — it grows the arc until both fit, so the
/// same layout works for any item size, any item count, and any radius the user has chosen.
/// </para>
/// <para>
/// Pure geometry, in physical-ish DIP units and free of any windowing type, so the rule can be
/// reasoned about (and tested) on its own — RingWindow supplies the numbers and draws the result.
/// </para>
/// </summary>
public static class QuadrantLayout
{
    /// <summary>Items a quadrant shows: QuickAccess, Notepad, the ring's Tools item, and Settings.</summary>
    public const int ItemCount = 4;

    /// <summary>Stable marker target for the synthesized QuickAccess group — see
    /// <see cref="BuildQuickAccess"/>. Not a real, stored ring item (nothing is ever added to the
    /// ring itself), so this exists purely so <see cref="HotCornerItems"/> can recognize a stored
    /// entry as "the links and shortcuts group" and re-synthesize it fresh on every resolve,
    /// exactly the way it already does for the ring's own Tools group.</summary>
    public const string QuickAccessTarget = "swiftx:quickaccess";

    /// <summary>Glyph the QuickAccess group wears on a quadrant — the same one a lone Shortcut
    /// item falls back to (see <see cref="RingItem.Glyph"/>), since the group is exactly that
    /// kind of item, gathered up.</summary>
    public const string QuickAccessGlyph = "\uE71B";

    /// <summary>Clear space between two neighbouring items.</summary>
    public const double ItemGap = 10;

    /// <summary>Clear space between the outermost items and the screen edges beside them.</summary>
    public const double EdgeMargin = 10;

    /// <summary>How far the search below is allowed to grow the arc past <c>preferred</c>.</summary>
    private const double MaxGrowth = 4.0;

    /// <summary>
    /// The smallest radius at or above <paramref name="preferred"/> that fits
    /// <paramref name="count"/> items of <paramref name="bubble"/> diameter on the quarter arc
    /// without them touching each other or the two screen edges. Pass the hover-grown diameter:
    /// an item must still clear its neighbours and the edge at the moment the pointer swells it.
    /// </summary>
    public static double Radius(int count, double bubble, double preferred)
    {
        double radius = Math.Max(1, preferred);
        double ceiling = radius * MaxGrowth;
        while (!Fits(count, bubble, radius) && radius < ceiling)
            radius *= 1.04;
        return radius;
    }

    private static bool Fits(int count, double bubble, double radius)
    {
        if (count <= 1)
            return true;
        double span = Math.PI / 2 - 2 * EdgeInsetRad(bubble, radius);
        return span > 0 && (count - 1) * StepRad(bubble, radius) <= span;
    }

    /// <summary>
    /// Angle between the first item and the edge it sits nearest, chosen so the item's own circle
    /// (plus <see cref="EdgeMargin"/>) clears that edge. The center is on the corner, so an item
    /// at angle 0 would be cut clean in half by the screen. Public because an expanded group's
    /// outer arc has to respect the same two edges at its own radius.
    /// </summary>
    public static double EdgeInsetRad(double bubble, double radius) =>
        Math.Asin(Math.Clamp((bubble / 2 + EdgeMargin) / Math.Max(1, radius), 0, 1));

    /// <summary>Angle between two neighbouring items, from the chord their circles need.</summary>
    private static double StepRad(double bubble, double radius) =>
        2 * Math.Asin(Math.Clamp((bubble + ItemGap) / (2 * Math.Max(1, radius)), 0, 1));

    /// <summary>
    /// Angle of item <paramref name="index"/> measured from the corner's first edge, in radians:
    /// 0 is along one edge, π/2 along the other. Items are spread evenly across whatever the edge
    /// clearance leaves, which is exactly the span <see cref="Radius"/> sized the arc for.
    /// </summary>
    public static double LocalAngleRad(int index, int count, double bubble, double radius)
    {
        double inset = EdgeInsetRad(bubble, radius);
        double span = Math.Max(0, Math.PI / 2 - 2 * inset);
        if (count <= 1)
            return Math.PI / 4;
        return inset + span * index / (count - 1);
    }

    /// <summary>
    /// The same angle in the canvas's own frame, where 0 points right (+x) and angles advance
    /// clockwise because y grows downward — the convention every other ring angle uses. Each
    /// corner mirrors the quarter into the screen: from the top-left it sweeps right-to-down,
    /// from the bottom-right left-to-up.
    /// </summary>
    public static double ScreenAngleRad(HotCorner corner, double localAngleRad) => corner switch
    {
        HotCorner.TopLeft => localAngleRad,
        HotCorner.TopRight => Math.PI - localAngleRad,
        HotCorner.BottomLeft => -localAngleRad,
        _ => Math.PI + localAngleRad,
    };

    /// <summary>
    /// <see cref="ScreenAngleRad"/> read backwards — the local angle a canvas angle corresponds to
    /// for this corner, so a bubble already placed on the arc can say where it sits in the frame
    /// its neighbours are measured in.
    /// </summary>
    public static double LocalAngleFromScreenRad(HotCorner corner, double screenAngleRad)
    {
        double local = corner switch
        {
            HotCorner.TopLeft => screenAngleRad,
            HotCorner.TopRight => Math.PI - screenAngleRad,
            HotCorner.BottomLeft => -screenAngleRad,
            _ => screenAngleRad - Math.PI,
        };

        // The round trip can land a whole turn away from where it started (an angle of -π and one
        // of π name the same direction); bring it back beside the quarter it belongs to.
        double twoPi = Math.PI * 2;
        local %= twoPi;
        if (local < -Math.PI) local += twoPi;
        if (local > Math.PI) local -= twoPi;
        return local;
    }

    /// <summary>The corner point itself, within a rectangle of the given size at the origin.</summary>
    public static (double X, double Y) Center(HotCorner corner, double width, double height) => corner switch
    {
        HotCorner.TopLeft => (0, 0),
        HotCorner.TopRight => (width, 0),
        HotCorner.BottomLeft => (0, height),
        _ => (width, height),
    };

    /// <summary>
    /// Which items a quadrant shows, in arc order: QuickAccess (every web link and shortcut on the
    /// ring, gathered into one group), then Notepad, then the ring's Tools item, then Settings —
    /// content first, built-ins last, the same convention the full ring reads in with Settings and
    /// Close pinned to its own last two slots, so the two layouts never disagree about where
    /// anything is.
    /// <para>
    /// QuickAccess drops out entirely when there is nothing to gather (see
    /// <see cref="BuildQuickAccess"/>) rather than taking a place for an empty group, and Notepad
    /// falls back to the ring's own default entry when the ring has none of its own — see
    /// <see cref="FindOrDefaultNotepad"/>. Both are a starting point, not a fixture: once the
    /// corner is edited by hand (<see cref="HotCornerItems.Edit"/>), any of the four can be
    /// swapped for anything else.
    /// </para>
    /// <para>
    /// Close is deliberately absent. There are only <see cref="ItemCount"/> places, and a
    /// quadrant is dismissed the way the ring always was: Esc, or a click anywhere off it.
    /// </para>
    /// </summary>
    /// <param name="ringItems">The profile's own items, in ring order.</param>
    /// <param name="settings">The synthesized Settings entry — see <see cref="SystemRingItems"/>.</param>
    public static List<RingItem> Compose(IEnumerable<RingItem> ringItems, RingItem settings)
    {
        var visible = ringItems.Where(i => !i.Hidden).ToList();
        var tools = visible.FirstOrDefault(
            i => i.IsGroup &&
                 string.Equals(i.Target, ToolCatalog.ToolsGroupTarget, StringComparison.OrdinalIgnoreCase));

        // Built from the back: whatever places the built-ins don't take go to the content items.
        var tail = new List<RingItem>();
        if (tools is not null)
            tail.Add(tools);
        tail.Add(settings);

        var head = new List<RingItem>();
        var quickAccess = BuildQuickAccess(visible);
        if (quickAccess is not null)
            head.Add(quickAccess);
        head.Add(FindOrDefaultNotepad(visible));

        var items = head.Take(Math.Max(0, ItemCount - tail.Count)).ToList();
        items.AddRange(tail);
        return items;
    }

    /// <summary>
    /// Gathers every <see cref="RingItemKind.WebLink"/> and <see cref="RingItemKind.Shortcut"/>
    /// item anywhere on the ring — top level or nested inside a user's own group — into one
    /// synthesized group, in the order they appear. Standing in for "the user's own items" the
    /// way <see cref="ToolCatalog"/>'s group stands in for the built-in tools, and re-synthesized
    /// by <see cref="HotCornerItems"/> on every resolve so it stays live even once the corner has
    /// been customized. Null when there is nothing to gather, so an empty group never occupies
    /// one of the corner's four places for nothing.
    /// </summary>
    public static RingItem? BuildQuickAccess(IEnumerable<RingItem> ringItems)
    {
        var children = new List<RingItem>();
        void Collect(IEnumerable<RingItem> items)
        {
            foreach (var item in items)
            {
                // A hidden group's children never surface via its own flyout either (see
                // RingWindow.ExpandFlyout's matching filter), so a hidden item — group or leaf —
                // takes the rest of its subtree out of the gathering with it.
                if (item.Hidden)
                    continue;
                if (item.Kind is RingItemKind.WebLink or RingItemKind.Shortcut)
                    children.Add(item);
                if (item.Children.Count > 0)
                    Collect(item.Children);
            }
        }
        Collect(ringItems);

        if (children.Count == 0)
            return null;

        return new RingItem
        {
            Kind = RingItemKind.Group,
            DisplayName = "QuickAccess",
            Target = QuickAccessTarget,
            CustomGlyph = QuickAccessGlyph,
            Children = children,
        };
    }

    /// <summary>
    /// The ring's own Notepad, if it has one — matched by the target's file name so a renamed
    /// item still counts, and found wherever it sits, nested or not — or else the same entry
    /// <c>RingManager.SeedDefaults</c> plants on a fresh ring, so the fourth place always opens to
    /// something useful even on a ring that never had one.
    /// </summary>
    public static RingItem FindOrDefaultNotepad(IEnumerable<RingItem> ringItems)
    {
        RingItem? Find(IEnumerable<RingItem> items)
        {
            foreach (var item in items)
            {
                if (item.Hidden)
                    continue;
                if (item.Kind == RingItemKind.Application &&
                    string.Equals(Path.GetFileName(item.Target), "notepad.exe", StringComparison.OrdinalIgnoreCase))
                    return item;
                if (item.Children.Count > 0 && Find(item.Children) is { } nested)
                    return nested;
            }
            return null;
        }

        return Find(ringItems) ?? new RingItem
        {
            Kind = RingItemKind.Application,
            DisplayName = "Notepad",
            Target = Path.Combine(Environment.SystemDirectory, "notepad.exe"),
        };
    }
}
