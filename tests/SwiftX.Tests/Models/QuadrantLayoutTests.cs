using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

/// <summary>
/// Covers the quarter circle a hot corner set to <see cref="HotCornerLayout.Quadrant"/> summons:
/// the two things its geometry has to guarantee — items that clear the screen edges and each
/// other — and which items land on it.
/// </summary>
public class QuadrantLayoutTests
{
    private const double Bubble = 50 * 1.14;   // medium item, hover-grown
    private const double RingRadius = 100;     // the ring's own default

    private static (double X, double Y) PointAt(HotCorner corner, int index, int count, double radius)
    {
        double local = QuadrantLayout.LocalAngleRad(index, count, Bubble, radius);
        double angle = QuadrantLayout.ScreenAngleRad(corner, local);
        var (cx, cy) = QuadrantLayout.Center(corner, 1920, 1080);
        return (cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));
    }

    [Theory]
    [InlineData(HotCorner.TopLeft)]
    [InlineData(HotCorner.TopRight)]
    [InlineData(HotCorner.BottomLeft)]
    [InlineData(HotCorner.BottomRight)]
    public void Every_item_is_wholly_on_screen(HotCorner corner)
    {
        int count = QuadrantLayout.ItemCount;
        double radius = QuadrantLayout.Radius(count, Bubble, RingRadius);

        for (int i = 0; i < count; i++)
        {
            var (x, y) = PointAt(corner, i, count, radius);
            // The center sits on the corner, so an item too near either edge is cut in half by it.
            Assert.InRange(x, Bubble / 2, 1920 - Bubble / 2);
            Assert.InRange(y, Bubble / 2, 1080 - Bubble / 2);
        }
    }

    [Fact]
    public void Neighbouring_items_do_not_touch()
    {
        int count = QuadrantLayout.ItemCount;
        double radius = QuadrantLayout.Radius(count, Bubble, RingRadius);

        for (int i = 1; i < count; i++)
        {
            var a = PointAt(HotCorner.TopLeft, i - 1, count, radius);
            var b = PointAt(HotCorner.TopLeft, i, count, radius);
            double gap = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
            Assert.True(gap >= Bubble, $"items {i - 1} and {i} are {gap:F1}px apart, closer than {Bubble:F1}px");
        }
    }

    [Fact]
    public void The_arc_grows_only_as_far_as_it_has_to()
    {
        // Four medium items cannot fit on a quarter arc at the ring's own radius, so the layout
        // pushes them out — but a radius that already fits is left exactly alone.
        Assert.True(QuadrantLayout.Radius(4, Bubble, RingRadius) > RingRadius);
        Assert.Equal(1000d, QuadrantLayout.Radius(4, Bubble, 1000));
        Assert.Equal(RingRadius, QuadrantLayout.Radius(1, Bubble, RingRadius));
    }

    [Theory]
    [InlineData(HotCorner.TopLeft, 0d, 0d)]
    [InlineData(HotCorner.TopRight, 1920d, 0d)]
    [InlineData(HotCorner.BottomLeft, 0d, 1080d)]
    [InlineData(HotCorner.BottomRight, 1920d, 1080d)]
    public void The_center_is_the_corner_itself(HotCorner corner, double x, double y)
    {
        Assert.Equal((x, y), QuadrantLayout.Center(corner, 1920, 1080));
    }

    [Theory]
    [InlineData(HotCorner.TopLeft)]
    [InlineData(HotCorner.TopRight)]
    [InlineData(HotCorner.BottomLeft)]
    [InlineData(HotCorner.BottomRight)]
    public void Canvas_angles_convert_back_to_the_angle_they_came_from(HotCorner corner)
    {
        // The flyout fan is clamped in the corner's own frame, so it needs the round trip to hold.
        for (double local = 0.05; local < Math.PI / 2; local += 0.2)
        {
            double screen = QuadrantLayout.ScreenAngleRad(corner, local);
            Assert.Equal(local, QuadrantLayout.LocalAngleFromScreenRad(corner, screen), precision: 9);
        }
    }

    // ---- Which items -------------------------------------------------------------------------

    private static RingItem App(string name) => new()
    {
        Kind = RingItemKind.Application,
        DisplayName = name,
        Target = name,
    };

    [Fact]
    public void Four_items_by_default_user_items_first_then_Tools_then_Settings()
    {
        var tools = ToolCatalog.CreateToolsGroup();
        var items = new List<RingItem> { App("one"), App("two"), tools, App("three") };

        var quadrant = QuadrantLayout.Compose(items, SystemRingItems.CreateSettings());

        Assert.Equal(
            new[] { "one", "two", "Tools", "Settings" },
            quadrant.Select(i => i.DisplayName));
    }

    [Fact]
    public void Hidden_items_are_passed_over()
    {
        var hidden = App("hidden");
        hidden.Hidden = true;
        var items = new List<RingItem> { hidden, App("one"), App("two"), App("three") };

        var quadrant = QuadrantLayout.Compose(items, SystemRingItems.CreateSettings());

        // No Tools item on this ring, so the third place goes to another of the user's.
        Assert.Equal(
            new[] { "one", "two", "three", "Settings" },
            quadrant.Select(i => i.DisplayName));
    }

    [Fact]
    public void An_almost_empty_ring_still_gets_its_built_ins()
    {
        var quadrant = QuadrantLayout.Compose(new List<RingItem>(), SystemRingItems.CreateSettings());
        Assert.Equal(new[] { "Settings" }, quadrant.Select(i => i.DisplayName));
    }
}
