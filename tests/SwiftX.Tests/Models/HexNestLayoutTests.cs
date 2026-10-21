using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

/// <summary>
/// Covers the hex nest (<see cref="OrbitArrangement.HexNest"/>): cells that share edges without
/// overlapping, a hit test with no gaps in it, the places the full orbit and a hot corner's quadrant
/// give their items, and where a group's children go when it opens in place.
/// </summary>
public class HexNestLayoutTests
{
    private const double Size = 46;   // a medium cell
    private static readonly double Sqrt3 = Math.Sqrt(3);

    private static double Gap((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    [Fact]
    public void Neighbouring_cells_share_an_edge_and_drawn_cells_keep_the_gap()
    {
        var hub = HexNestLayout.Offset(HexCell.Hub, Size);
        foreach (var cell in HexNestLayout.FirstRing)
        {
            // Centers one cell-width apart is what "sharing an edge" means on this grid.
            Assert.Equal(Sqrt3 * Size, Gap(hub, HexNestLayout.Offset(cell, Size)), 6);
        }

        // Two drawn cells' flat edges face each other across exactly the gap.
        double inradius = Sqrt3 / 2 * HexNestLayout.DrawnRadius(Size);
        Assert.Equal(HexNestLayout.CellGap, Sqrt3 * Size - 2 * inradius, 6);
    }

    [Fact]
    public void Every_cell_is_found_at_its_own_center_and_anywhere_inside_it()
    {
        foreach (var cell in HexNestLayout.Ring(1).Concat(HexNestLayout.Ring(2)).Append(HexCell.Hub))
        {
            var (x, y) = HexNestLayout.Offset(cell, Size);
            Assert.Equal(cell, HexNestLayout.CellAt(x, y, Size));

            // Just inside each of the drawn cell's six corners.
            double reach = HexNestLayout.DrawnRadius(Size) * 0.98;
            for (int i = 0; i < 6; i++)
            {
                double a = (-90 + i * 60) * Math.PI / 180;
                Assert.Equal(cell, HexNestLayout.CellAt(x + reach * Math.Cos(a), y + reach * Math.Sin(a), Size));
            }
        }
    }

    [Fact]
    public void The_gap_between_two_cells_belongs_to_one_of_them()
    {
        // Half way between the hub and its right-hand neighbour is the shared edge's midpoint; a
        // hair either side of it is one cell or the other, never neither.
        double mid = Sqrt3 * Size / 2;
        Assert.Equal(HexCell.Hub, HexNestLayout.CellAt(mid - 0.5, 0, Size));
        Assert.Equal(new HexCell(1, 0), HexNestLayout.CellAt(mid + 0.5, 0, Size));
    }

    [Fact]
    public void Rings_hold_six_cells_per_step_out()
    {
        Assert.Single(HexNestLayout.Ring(0));
        for (int ring = 1; ring <= 4; ring++)
        {
            var cells = HexNestLayout.Ring(ring);
            Assert.Equal(6 * ring, cells.Count);
            Assert.All(cells, c => Assert.Equal(ring, c.Ring));
        }
    }

    [Fact]
    public void The_orbit_puts_six_slots_on_the_first_ring_and_settings_and_close_one_further_out()
    {
        Assert.Equal(OrbitMetrics.UserSlotCount, HexNestLayout.FirstRing.Count);
        Assert.Equal(HexNestLayout.FirstRing.Count, HexNestLayout.FirstRing.Distinct().Count());
        Assert.All(HexNestLayout.FirstRing, c => Assert.Equal(1, c.Ring));

        Assert.Equal(2, HexNestLayout.SettingsCell.Ring);
        Assert.Equal(2, HexNestLayout.CloseCell.Ring);
        Assert.NotEqual(HexNestLayout.SettingsCell, HexNestLayout.CloseCell);

        // Slot order runs clockwise, the way the ring's own slots do.
        var angles = HexNestLayout.FirstRing.Select(HexNestLayout.AngleRad).ToList();
        for (int i = 1; i < angles.Count; i++)
        {
            double step = angles[i] - angles[i - 1];
            if (step < 0) step += 2 * Math.PI;
            Assert.Equal(Math.PI / 3, step, 6);
        }
    }

    [Fact]
    public void Spare_cells_never_include_settings_close_or_the_first_ring()
    {
        Assert.DoesNotContain(HexNestLayout.SettingsCell, HexNestLayout.OrbitSpareCells);
        Assert.DoesNotContain(HexNestLayout.CloseCell, HexNestLayout.OrbitSpareCells);
        Assert.DoesNotContain(HexCell.Hub, HexNestLayout.OrbitSpareCells);
        Assert.All(HexNestLayout.FirstRing, c => Assert.DoesNotContain(c, HexNestLayout.OrbitSpareCells));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public void A_group_hands_its_siblings_places_to_its_children_first(int groupSlot)
    {
        var group = HexNestLayout.FirstRing[groupSlot];
        var cells = HexNestLayout.FlyoutCells(group, HexNestLayout.FirstRing, HexNestLayout.OrbitSpareCells, 5);

        Assert.Equal(5, cells.Count);
        Assert.DoesNotContain(group, cells);
        Assert.True(cells.ToHashSet().SetEquals(HexNestLayout.FirstRing.Where(c => c != group)));

        // Starting from the slot after the group, clockwise.
        Assert.Equal(HexNestLayout.FirstRing[(groupSlot + 1) % 6], cells[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void A_full_flyout_spills_onto_free_cells_and_leaves_settings_and_close_alone(int groupSlot)
    {
        var group = HexNestLayout.FirstRing[groupSlot];
        var cells = HexNestLayout.FlyoutCells(
            group, HexNestLayout.FirstRing, HexNestLayout.OrbitSpareCells, OrbitMetrics.FlyoutSlotCount);

        Assert.Equal(OrbitMetrics.FlyoutSlotCount, cells.Count);
        Assert.Equal(cells.Count, cells.Distinct().Count());
        Assert.DoesNotContain(group, cells);
        Assert.DoesNotContain(HexCell.Hub, cells);
        Assert.DoesNotContain(HexNestLayout.SettingsCell, cells);
        Assert.DoesNotContain(HexNestLayout.CloseCell, cells);
    }

    [Fact]
    public void Reach_covers_every_cell_a_group_can_open_onto()
    {
        var group = HexNestLayout.FirstRing[3];
        var main = HexNestLayout.FirstRing.Append(HexNestLayout.SettingsCell).Append(HexNestLayout.CloseCell).ToList();

        double withoutGroups = HexNestLayout.Reach(main, HexNestLayout.FirstRing, HexNestLayout.OrbitSpareCells, []);
        double withGroup = HexNestLayout.Reach(
            main, HexNestLayout.FirstRing, HexNestLayout.OrbitSpareCells, [(group, OrbitMetrics.FlyoutSlotCount)]);

        Assert.Equal(main.Max(HexNestLayout.Distance), withoutGroups, 6);
        Assert.All(
            HexNestLayout.FlyoutCells(group, HexNestLayout.FirstRing, HexNestLayout.OrbitSpareCells, OrbitMetrics.FlyoutSlotCount),
            c => Assert.True(HexNestLayout.Distance(c) <= withGroup + 1e-9));

        // A group that fits in its siblings' places adds nothing to how far the nest reaches.
        Assert.Equal(withoutGroups,
            HexNestLayout.Reach(main, HexNestLayout.FirstRing, HexNestLayout.OrbitSpareCells, [(group, 5)]), 6);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public void A_quadrant_takes_distinct_cells_in_arc_order(int count)
    {
        var cells = HexNestLayout.QuadrantCells(count);
        Assert.Equal(count, cells.Count);
        Assert.Equal(count, cells.Distinct().Count());
        Assert.DoesNotContain(HexCell.Hub, cells);

        var angles = cells.Select(HexNestLayout.AngleRad).ToList();
        Assert.Equal(angles.OrderBy(a => a), angles);
        Assert.All(angles, a => Assert.InRange(a, -1e-9, Math.PI / 2 + 1e-9));
    }

    [Fact]
    public void Four_quadrant_items_run_from_one_edge_to_the_other()
    {
        var angles = HexNestLayout.QuadrantCells(QuadrantLayout.ItemCount).Select(HexNestLayout.AngleRad).ToList();

        // The first lies along the corner's first edge and the last along its second, the order
        // the quarter circle reads in.
        Assert.Equal(0, angles[0], 6);
        Assert.Equal(Math.PI / 2, angles[^1], 6);
    }

    [Theory]
    [InlineData(HotCorner.TopLeft)]
    [InlineData(HotCorner.TopRight)]
    [InlineData(HotCorner.BottomLeft)]
    [InlineData(HotCorner.BottomRight)]
    public void Every_quadrant_cell_is_wholly_on_screen_even_grown(HotCorner corner)
    {
        const double Width = 1920, Height = 1080;
        var (cornerX, cornerY) = QuadrantLayout.Center(corner, Width, Height);
        var (sx, sy) = HexNestLayout.Mirror(corner);
        var (ix, iy) = HexNestLayout.QuadrantHubInset(Size);
        double hubX = cornerX + sx * ix, hubY = cornerY + sy * iy;

        double grown = HexNestLayout.DrawnRadius(Size) * OrbitMetrics.HoverScale;
        int count = QuadrantLayout.ItemCount;
        var cells = HexNestLayout.QuadrantCells(count)
            .Concat(HexNestLayout.QuadrantSpareCells(count).Take(OrbitMetrics.FlyoutSlotCount));

        foreach (var cell in cells)
        {
            var (x, y) = HexNestLayout.Offset(cell, Size);
            double cx = hubX + sx * x, cy = hubY + sy * y;

            // A pointy-top cell is √3/2 of its radius wide either side and its full radius tall.
            Assert.InRange(cx, Sqrt3 / 2 * grown, Width - Sqrt3 / 2 * grown);
            Assert.InRange(cy, grown, Height - grown);
        }
    }

    [Fact]
    public void A_quadrant_group_spills_onto_cells_past_its_own_four()
    {
        int count = QuadrantLayout.ItemCount;
        var places = HexNestLayout.QuadrantCells(count);

        // Settings keeps the last place, so the group (first place) has two siblings to hand over.
        var userPlaces = places.Take(count - 1).ToList();
        var cells = HexNestLayout.FlyoutCells(
            userPlaces[0], userPlaces, HexNestLayout.QuadrantSpareCells(count), OrbitMetrics.FlyoutSlotCount);

        Assert.Equal(OrbitMetrics.FlyoutSlotCount, cells.Count);
        Assert.Equal(cells.Count, cells.Distinct().Count());
        Assert.DoesNotContain(places[^1], cells);
        Assert.DoesNotContain(userPlaces[0], cells);
        Assert.Equal(userPlaces[1], cells[0]);
        Assert.Equal(userPlaces[2], cells[1]);
    }

    [Theory]
    [InlineData(OrbitItemSize.Small)]
    [InlineData(OrbitItemSize.Medium)]
    [InlineData(OrbitItemSize.Large)]
    public void The_hub_box_stays_inside_the_hubs_own_cell(OrbitItemSize size)
    {
        double radius = HexNestLayout.CellRadius(size);
        double box = HexNestLayout.HubBox(radius);

        // Every corner of the box has to land in the hub's cell, or a press there would read as
        // the hub on a neighbour's ground.
        foreach (var (x, y) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
            Assert.Equal(HexCell.Hub, HexNestLayout.CellAt(x * box / 2, y * box / 2, radius));

        Assert.True(box >= OrbitMetrics.HubLogo, "the mark has to fit in its box");
    }

    [Fact]
    public void The_content_radius_covers_the_furthest_cell_grown()
    {
        double reach = HexNestLayout.Distance(HexNestLayout.SettingsCell);
        double radius = HexNestLayout.ContentRadius(reach, Size);
        Assert.True(radius >= reach * Size + HexNestLayout.DrawnRadius(Size) * OrbitMetrics.HoverScale);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(55, 60)]
    [InlineData(-170, 180)]
    [InlineData(-65, -60)]
    [InlineData(100, 120)]
    public void A_groups_mark_faces_the_nearest_edge(double degrees, double expected)
    {
        // Compared as directions rather than numbers: -180° and 180° are the same edge.
        double normal = HexNestLayout.EdgeNormalRad(degrees * Math.PI / 180);
        double want = expected * Math.PI / 180;
        Assert.Equal(Math.Cos(want), Math.Cos(normal), 6);
        Assert.Equal(Math.Sin(want), Math.Sin(normal), 6);
    }

    [Fact]
    public void Every_first_ring_cell_marks_the_edge_facing_away_from_the_hub()
    {
        // The outward edge of a cell touching the hub is the one opposite the edge it shares
        // with the hub, which faces straight along the line from the hub to the cell.
        foreach (var cell in HexNestLayout.FirstRing)
        {
            double angle = HexNestLayout.AngleRad(cell);
            Assert.Equal(Math.Cos(angle), Math.Cos(HexNestLayout.EdgeNormalRad(angle)), 6);
            Assert.Equal(Math.Sin(angle), Math.Sin(HexNestLayout.EdgeNormalRad(angle)), 6);
        }
    }
}
