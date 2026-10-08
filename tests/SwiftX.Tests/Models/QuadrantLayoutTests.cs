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
    private const double OrbitRadius = 100;     // the orbit's own default

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
        double radius = QuadrantLayout.Radius(count, Bubble, OrbitRadius);

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
        double radius = QuadrantLayout.Radius(count, Bubble, OrbitRadius);

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
        // Four medium items cannot fit on a quarter arc at the orbit's own radius, so the layout
        // pushes them out — but a radius that already fits is left exactly alone.
        Assert.True(QuadrantLayout.Radius(4, Bubble, OrbitRadius) > OrbitRadius);
        Assert.Equal(1000d, QuadrantLayout.Radius(4, Bubble, 1000));
        Assert.Equal(OrbitRadius, QuadrantLayout.Radius(1, Bubble, OrbitRadius));
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

    private static OrbitItem App(string name) => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = name,
        Target = name,
    };

    private static OrbitItem Link(string name) => new()
    {
        Kind = OrbitItemKind.WebLink,
        DisplayName = name,
        Target = "https://" + name,
    };

    private static OrbitItem Shortcut(string name) => new()
    {
        Kind = OrbitItemKind.Shortcut,
        DisplayName = name,
        Target = name + ".lnk",
    };

    private static OrbitItem Group(string name, params OrbitItem[] children)
    {
        var group = new OrbitItem { Kind = OrbitItemKind.Group, DisplayName = name };
        group.Children.AddRange(children);
        return group;
    }

    [Fact]
    public void Four_items_by_default_QuickAccess_then_Notepad_then_Tools_then_Settings()
    {
        var tools = ToolCatalog.CreateToolsGroup();
        var notepad = App("Notepad");
        var items = new List<OrbitItem> { notepad, Link("github"), tools };

        var quadrant = QuadrantLayout.Compose(items, SystemOrbitItems.CreateSettings());

        Assert.Equal(
            new[] { "QuickAccess", "Notepad", "Tools", "Settings" },
            quadrant.Select(i => i.DisplayName));
    }

    [Fact]
    public void QuickAccess_gathers_every_link_and_shortcut_wherever_it_sits()
    {
        var items = new List<OrbitItem>
        {
            Link("github"),
            Group("Work", Shortcut("project"), App("calc")),
        };

        var quadrant = QuadrantLayout.Compose(items, SystemOrbitItems.CreateSettings());

        var quickAccess = Assert.Single(quadrant, i => i.DisplayName == "QuickAccess");
        Assert.Equal(new[] { "github", "project" }, quickAccess.Children.Select(c => c.DisplayName));
    }

    [Fact]
    public void QuickAccess_drops_out_entirely_when_there_is_nothing_to_gather()
    {
        var items = new List<OrbitItem> { App("Notepad") };

        var quadrant = QuadrantLayout.Compose(items, SystemOrbitItems.CreateSettings());

        Assert.DoesNotContain(quadrant, i => i.DisplayName == "QuickAccess");
        // The place it would have taken goes unfilled rather than to some other item — the
        // default composition names exactly four things, not "whatever fits".
        Assert.Equal(new[] { "Notepad", "Settings" }, quadrant.Select(i => i.DisplayName));
    }

    [Fact]
    public void The_orbits_own_Notepad_is_used_when_it_has_one()
    {
        var notepad = App("My editor");
        notepad.Target = @"C:\Windows\System32\Notepad.exe";
        var items = new List<OrbitItem> { notepad };

        var quadrant = QuadrantLayout.Compose(items, SystemOrbitItems.CreateSettings());

        Assert.Same(notepad, quadrant.First());
    }

    [Fact]
    public void A_orbit_with_no_notepad_gets_the_same_default_SeedDefaults_plants()
    {
        var quadrant = QuadrantLayout.Compose(new List<OrbitItem>(), SystemOrbitItems.CreateSettings());

        var notepad = quadrant.First();
        Assert.Equal("Notepad", notepad.DisplayName);
        Assert.EndsWith("notepad.exe", notepad.Target, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hidden_items_are_passed_over()
    {
        var hiddenLink = Link("hidden");
        hiddenLink.Hidden = true;
        var items = new List<OrbitItem> { hiddenLink };

        var quadrant = QuadrantLayout.Compose(items, SystemOrbitItems.CreateSettings());

        Assert.DoesNotContain(quadrant, i => i.DisplayName == "QuickAccess");
    }

    [Fact]
    public void A_hidden_link_nested_inside_a_visible_group_is_still_passed_over()
    {
        var hiddenShortcut = Shortcut("hidden");
        hiddenShortcut.Hidden = true;
        var items = new List<OrbitItem> { Group("Work", hiddenShortcut, Link("visible")) };

        var quickAccess = QuadrantLayout.BuildQuickAccess(items);

        Assert.NotNull(quickAccess);
        Assert.Equal(new[] { "visible" }, quickAccess!.Children.Select(c => c.DisplayName));
    }

    [Fact]
    public void Only_the_gathered_QuickAccess_group_is_QuickAccess()
    {
        var gathered = QuadrantLayout.BuildQuickAccess(new[] { Link("github") });

        Assert.True(QuadrantLayout.IsQuickAccess(gathered!));
        // Renaming it on the way past does not change what it is; the marker target does.
        gathered!.DisplayName = "Renamed";
        Assert.True(QuadrantLayout.IsQuickAccess(gathered));

        Assert.False(QuadrantLayout.IsQuickAccess(Group("QuickAccess", Link("github"))));
        Assert.False(QuadrantLayout.IsQuickAccess(ToolCatalog.CreateToolsGroup()));
        Assert.False(QuadrantLayout.IsQuickAccess(Link("github")));
    }

    [Fact]
    public void An_almost_empty_orbit_still_gets_its_built_ins()
    {
        var quadrant = QuadrantLayout.Compose(new List<OrbitItem>(), SystemOrbitItems.CreateSettings());
        Assert.Equal(new[] { "Notepad", "Settings" }, quadrant.Select(i => i.DisplayName));
    }
}
