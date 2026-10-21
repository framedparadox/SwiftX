namespace SwiftX.Models;

/// <summary>
/// How the items are laid out around the hub — a separate axis from <see cref="HotCornerLayout"/>,
/// which says how much of the orbit a hot corner summons rather than how it is drawn. The full
/// orbit and a corner's quadrant both follow it.
/// <para>
/// Persisted by name (see <see cref="AppConfig.Arrangement"/>), so these member names are part of
/// the on-disk format and cannot be renamed without stranding every config that already carries one.
/// </para>
/// </summary>
public enum OrbitArrangement
{
    /// <summary>
    /// Equal items on an even circle around the hub, a group opening into a concentric outer ring
    /// — what every build before this one drew, and still what a fresh config gets.
    /// </summary>
    Ring,

    /// <summary>
    /// Hexagonal cells that share edges around the hub, each carrying its own name, with a group
    /// opening in place of the cells around it — the "Hex nest" study in
    /// docs/orbit-dock-alternatives.html. See <see cref="HexNestLayout"/>.
    /// </summary>
    HexNest,
}

/// <summary>
/// One cell of the hex nest, in axial coordinates on a pointy-top grid: <see cref="Q"/> runs to the
/// right, <see cref="R"/> down and to the right. The hub is (0, 0).
/// </summary>
public readonly record struct HexCell(int Q, int R)
{
    public static readonly HexCell Hub = new(0, 0);

    /// <summary>How many steps out from the hub the cell sits — 1 for the six that touch it.</summary>
    public int Ring => (Math.Abs(Q) + Math.Abs(R) + Math.Abs(Q + R)) / 2;
}

/// <summary>
/// Where the cells go when the orbit is drawn as a hex nest (<see cref="OrbitArrangement.HexNest"/>).
/// <para>
/// The full orbit puts the six user slots on the ring of cells touching the hub, in slot order,
/// clockwise from the upper right, and Settings and Close one ring further out, below — the same
/// places the study gives them. A hot corner's quadrant puts its hub in the corner and its items on
/// the nearest cells that are wholly on screen, in the same arc order the quarter circle uses.
/// </para>
/// <para>
/// A group does not open a second ring around the first. Its own cell becomes the way back, and
/// every cell its siblings held is handed to its children (see <see cref="FlyoutCells"/>), so the
/// nest stays one grid with no gaps to interpret. Only Settings and Close keep their cells.
/// </para>
/// <para>
/// Pure geometry, in DIPs and free of any windowing type, like <see cref="QuadrantLayout"/>: the
/// rules can be reasoned about and tested on their own, and OrbitWindow supplies the cell size and
/// draws the result. Corner layouts are worked out in the top-left corner's frame — x grows away
/// from the left edge, y away from the top — and <see cref="Mirror"/> turns that into any corner,
/// which works because the grid is symmetric across both axes.
/// </para>
/// </summary>
public static class HexNestLayout
{
    private const double Sqrt3 = 1.7320508075688772;

    /// <summary>Clear space between the drawn edges of two neighbouring cells.</summary>
    public const double CellGap = 5;

    /// <summary>How far out the spare cells a group can spill into are looked for.</summary>
    private const int MaxRing = 6;

    /// <summary>
    /// Distance from a cell's center to its corners, before the gap is taken off. Larger than an
    /// orbit bubble of the same item size, because a cell carries the item's name under its icon.
    /// </summary>
    public static double CellRadius(OrbitItemSize size) => size switch
    {
        OrbitItemSize.Small => 38,
        OrbitItemSize.Large => 56,
        _ => 46,
    };

    /// <summary>Size of the name written inside a cell.</summary>
    public static double LabelFontSize(OrbitItemSize size) => size switch
    {
        OrbitItemSize.Small => 9.5,
        OrbitItemSize.Large => 12,
        _ => 10.5,
    };

    /// <summary>The radius a cell is drawn at: its share of the grid, less half the gap on every edge.</summary>
    public static double DrawnRadius(double cellRadius) => Math.Max(1, cellRadius - CellGap / Sqrt3);

    /// <summary>
    /// Widest a name inside a cell may run before it wraps. The name sits just under the middle,
    /// where the hexagon is still nearly its full width; a second line runs lower, where the sides
    /// have started to close in, and this is as wide as that line can be and still clear them.
    /// </summary>
    public static double LabelWidth(double drawnRadius) => Sqrt3 * drawnRadius * 0.86;

    /// <summary>
    /// The box the hub's mark sits in. Square, so it is held inside the circle the hub's own cell
    /// can hold: a box any larger pokes its corners into the neighbouring cells, and a press there
    /// would dismiss the orbit rather than pick the cell the grid says it is on.
    /// </summary>
    public static double HubBox(double cellRadius) =>
        Math.Min(OrbitMetrics.Hub, Math.Floor(Math.Sqrt(2) * Sqrt3 / 2 * DrawnRadius(cellRadius)));

    // ---- The grid ------------------------------------------------------------------------------

    /// <summary>Center of <paramref name="cell"/> relative to the hub, for cells of <paramref name="cellRadius"/>.</summary>
    public static (double X, double Y) Offset(HexCell cell, double cellRadius) =>
        (cellRadius * Sqrt3 * (cell.Q + cell.R / 2.0), cellRadius * 1.5 * cell.R);

    /// <summary>
    /// The cell a point (relative to the hub) falls in. Cells tile the plane, so every point is in
    /// exactly one, including the gaps drawn between them: there is no space between two cells
    /// that belongs to neither.
    /// </summary>
    public static HexCell CellAt(double x, double y, double cellRadius)
    {
        double size = Math.Max(1e-9, cellRadius);
        double q = (Sqrt3 / 3 * x - y / 3) / size;
        double r = 2.0 / 3 * y / size;
        double s = -q - r;

        double rq = Math.Round(q), rr = Math.Round(r), rs = Math.Round(s);
        double dq = Math.Abs(rq - q), dr = Math.Abs(rr - r), ds = Math.Abs(rs - s);

        // Rounding each coordinate on its own can land off the grid (q + r + s must stay 0); the one
        // that moved furthest is the one to recompute from the other two.
        if (dq > dr && dq > ds)
            rq = -rr - rs;
        else if (dr > ds)
            rr = -rq - rs;

        return new HexCell((int)rq, (int)rr);
    }

    /// <summary>Distance from the hub to the center of <paramref name="cell"/>, in cell radii.</summary>
    public static double Distance(HexCell cell)
    {
        var (x, y) = Offset(cell, 1);
        return Math.Sqrt(x * x + y * y);
    }

    /// <summary>Direction from the hub to <paramref name="cell"/>: 0 is right, angles grow clockwise.</summary>
    public static double AngleRad(HexCell cell)
    {
        var (x, y) = Offset(cell, 1);
        return Math.Atan2(y, x);
    }

    /// <summary>
    /// Every cell exactly <paramref name="ring"/> steps from the hub, clockwise from the right.
    /// </summary>
    public static IReadOnlyList<HexCell> Ring(int ring)
    {
        if (ring <= 0)
            return [HexCell.Hub];

        var cells = new List<HexCell>(6 * ring);
        for (int q = -ring; q <= ring; q++)
        {
            for (int r = -ring; r <= ring; r++)
            {
                var cell = new HexCell(q, r);
                if (cell.Ring == ring)
                    cells.Add(cell);
            }
        }

        return cells.OrderBy(c => Normalize(AngleRad(c))).ToList();
    }

    /// <summary>
    /// Which way the edge of a cell that faces away from the hub points, for a cell lying at
    /// <paramref name="angleRad"/> from it: the nearest of the six directions a pointy-top
    /// hexagon's edges face. A group draws its mark along that edge.
    /// </summary>
    public static double EdgeNormalRad(double angleRad)
    {
        double step = Math.PI / 3;
        return Math.Round(angleRad / step) * step;
    }

    // ---- The full orbit ------------------------------------------------------------------------

    /// <summary>
    /// The six cells touching the hub, clockwise from the upper right — the places the six user
    /// slots take, in slot order.
    /// </summary>
    public static IReadOnlyList<HexCell> FirstRing { get; } =
    [
        new(1, -1),
        new(1, 0),
        new(0, 1),
        new(-1, 1),
        new(-1, 0),
        new(0, -1),
    ];

    /// <summary>Settings, one ring out and straight below the hub.</summary>
    public static HexCell SettingsCell { get; } = new(-1, 2);

    /// <summary>Close, one ring out to the lower right, beside Settings.</summary>
    public static HexCell CloseCell { get; } = new(1, 1);

    /// <summary>
    /// Cells a group on the full orbit may spill its children into once its siblings' places are
    /// used up: the next two rings out, less the two that Settings and Close hold.
    /// </summary>
    public static IReadOnlyList<HexCell> OrbitSpareCells { get; } =
        Ring(2).Concat(Ring(3)).Where(c => c != SettingsCell && c != CloseCell).ToList();

    // ---- A hot corner's quadrant ---------------------------------------------------------------

    /// <summary>
    /// Every cell that lies wholly inside the corner (in the top-left frame), nearest the hub
    /// first. Distance ties go to the cell nearer the corner's diagonal, so a short list gathers
    /// into a tight cluster rather than running out along one edge.
    /// </summary>
    private static IReadOnlyList<HexCell> QuadrantOrder { get; } = BuildQuadrantOrder();

    private static IReadOnlyList<HexCell> BuildQuadrantOrder()
    {
        var cells = new List<HexCell>();
        for (int ring = 1; ring <= MaxRing; ring++)
        {
            foreach (var cell in Ring(ring))
            {
                var (x, y) = Offset(cell, 1);
                if (x > -1e-9 && y > -1e-9)
                    cells.Add(cell);
            }
        }

        return cells
            .OrderBy(c => Math.Round(Distance(c), 6))
            .ThenBy(c => Math.Abs(AngleRad(c) - Math.PI / 4))
            .ThenBy(AngleRad)
            .ToList();
    }

    /// <summary>
    /// The <paramref name="count"/> cells a quadrant's items take, in arc order: index 0 lies along
    /// the corner's first edge and the last along its second, the order
    /// <see cref="QuadrantLayout.LocalAngleRad"/> gives the quarter circle.
    /// </summary>
    public static IReadOnlyList<HexCell> QuadrantCells(int count) =>
        QuadrantOrder.Take(Math.Max(0, count)).OrderBy(AngleRad).ToList();

    /// <summary>The cells past a quadrant's first <paramref name="count"/>, nearest first.</summary>
    public static IReadOnlyList<HexCell> QuadrantSpareCells(int count) =>
        QuadrantOrder.Skip(Math.Max(0, count)).ToList();

    /// <summary>
    /// The signs that turn the top-left frame into <paramref name="corner"/>'s: x flips for a right
    /// corner, y for a bottom one. The grid maps onto itself either way.
    /// </summary>
    public static (int X, int Y) Mirror(HotCorner corner) => corner switch
    {
        HotCorner.TopLeft => (1, 1),
        HotCorner.TopRight => (-1, 1),
        HotCorner.BottomLeft => (1, -1),
        _ => (-1, -1),
    };

    /// <summary>
    /// How far in from each of the corner's two edges the hub's own cell sits, so that every cell
    /// of the quadrant clears both by <see cref="QuadrantLayout.EdgeMargin"/> even while the
    /// pointer has it grown. X is from the side edge, Y from the top or bottom one.
    /// </summary>
    public static (double X, double Y) QuadrantHubInset(double cellRadius)
    {
        double grown = DrawnRadius(cellRadius) * OrbitMetrics.HoverScale;
        return (Sqrt3 / 2 * grown + QuadrantLayout.EdgeMargin, grown + QuadrantLayout.EdgeMargin);
    }

    // ---- Groups -----------------------------------------------------------------------------

    /// <summary>
    /// The cells a group's <paramref name="count"/> children take when it opens. The group's own
    /// cell is not among them: that one becomes the way back. Children take the places its
    /// siblings held first, in order starting from the one after the group, and spill into
    /// <paramref name="spare"/> when those run out — nearest the hub first, then nearest the
    /// group, so the overflow stays on the group's side of the nest.
    /// </summary>
    /// <param name="group">The cell of the group that is opening.</param>
    /// <param name="places">Every place the group's siblings hold, the group's own included, in layout order.</param>
    /// <param name="spare">Free cells beyond them, which must not include any cell something else keeps.</param>
    public static List<HexCell> FlyoutCells(
        HexCell group, IReadOnlyList<HexCell> places, IEnumerable<HexCell> spare, int count)
    {
        var cells = new List<HexCell>(Math.Max(0, count));
        int start = -1;
        for (int i = 0; i < places.Count; i++)
        {
            if (places[i] == group)
            {
                start = i;
                break;
            }
        }

        for (int k = 1; k <= places.Count && cells.Count < count; k++)
        {
            var cell = places[(start + k + places.Count) % places.Count];
            if (cell != group && !cells.Contains(cell))
                cells.Add(cell);
        }

        if (cells.Count < count)
        {
            double toward = AngleRad(group);
            cells.AddRange(spare
                .Where(c => c != group && c != HexCell.Hub && !places.Contains(c))
                .Distinct()
                .OrderBy(c => Math.Round(Distance(c), 6))
                .ThenBy(c => Math.Round(AngleBetween(AngleRad(c), toward), 6))
                .ThenBy(c => Normalize(AngleRad(c)))
                .Take(count - cells.Count));
        }

        return cells;
    }

    /// <summary>
    /// The furthest any cell of a nest can sit from its hub, in cell radii: its own
    /// <paramref name="cells"/>, and every cell each of its <paramref name="groups"/> would open
    /// onto. Worked out from what is actually there rather than from the largest group there could
    /// be, because it sizes the disc that takes the pointer's clicks while the orbit is up.
    /// </summary>
    /// <param name="places">As for <see cref="FlyoutCells"/>.</param>
    /// <param name="spare">As for <see cref="FlyoutCells"/>.</param>
    /// <param name="groups">Each group's cell and how many children it opens onto.</param>
    public static double Reach(
        IEnumerable<HexCell> cells,
        IReadOnlyList<HexCell> places,
        IReadOnlyList<HexCell> spare,
        IEnumerable<(HexCell Group, int Children)> groups)
    {
        double reach = cells.Select(Distance).DefaultIfEmpty(0).Max();
        foreach (var (group, children) in groups)
        {
            reach = Math.Max(reach, FlyoutCells(group, places, spare, children)
                .Select(Distance)
                .DefaultIfEmpty(0)
                .Max());
        }
        return reach;
    }

    /// <summary>
    /// How far out the hit region has to reach to cover a nest whose furthest cell sits
    /// <paramref name="reach"/> cell radii from the hub: that cell's center, plus its corner while
    /// the pointer has it grown, plus the same slack <see cref="OrbitMetrics.ContentRadiusFor"/> keeps.
    /// </summary>
    public static double ContentRadius(double reach, double cellRadius) =>
        reach * cellRadius + DrawnRadius(cellRadius) * OrbitMetrics.HoverScale + 14;

    /// <summary>The smaller of the two ways round between two directions, in [0, π].</summary>
    private static double AngleBetween(double a, double b)
    {
        double d = Normalize(a - b);
        return Math.Min(d, Math.PI * 2 - d);
    }

    /// <summary>An angle brought into [0, 2π), so cells sort clockwise from the right.</summary>
    private static double Normalize(double radians)
    {
        double twoPi = Math.PI * 2;
        radians %= twoPi;
        if (radians < 0) radians += twoPi;
        return radians;
    }
}
