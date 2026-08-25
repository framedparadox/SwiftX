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
    /// <summary>Items a quadrant shows: two of the user's, the ring's Tools item, and Settings.</summary>
    public const int ItemCount = 4;

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
    /// Which items a quadrant shows, in arc order: the user's own first items, then the ring's
    /// Tools item, then Settings — the same "user items first, built-ins last" order the full
    /// ring reads in, so the two layouts never disagree about where anything is.
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

        // Built from the back: whatever places the built-ins don't take go to the user's items.
        var tail = new List<RingItem>();
        if (tools is not null)
            tail.Add(tools);
        tail.Add(settings);

        var items = visible
            .Where(i => !ReferenceEquals(i, tools))
            .Take(Math.Max(0, ItemCount - tail.Count))
            .ToList();
        items.AddRange(tail);
        return items;
    }
}
