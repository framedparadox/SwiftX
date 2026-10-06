using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

/// <summary>
/// Inner ring of 3–8 items (Settings/Close pinned at the end), with overflow on an outer ring.
/// </summary>
public class OrbitRingLayoutTests
{
    private static OrbitItem App(string name) => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = name,
        Target = @"C:\Apps\" + name + ".exe",
    };

    private static IReadOnlyList<OrbitRingLayout.Entry> Layout(params OrbitItem[] items) =>
        OrbitRingLayout.Build(items, SystemOrbitItems.CreateSettings(), SystemOrbitItems.CreateClose());

    [Fact]
    public void An_empty_orbit_still_shows_the_minimum_inner_ring()
    {
        var layout = Layout();

        Assert.Equal(OrbitRingLayout.MinInnerCount, layout.Count);
        Assert.All(layout, e => Assert.False(e.IsOuter));
        Assert.Equal(SystemOrbitItems.AddNewTarget, layout[0].Item.Target);
        Assert.True(layout[^2].IsPinned);
        Assert.True(layout[^1].IsPinned);
        Assert.Equal(SystemOrbitItems.SettingsTarget, layout[^2].Item.Target);
        Assert.Equal(SystemOrbitItems.CloseTarget, layout[^1].Item.Target);
    }

    [Fact]
    public void Three_user_items_get_a_trailing_add_new_and_space_evenly_on_the_inner_ring()
    {
        var layout = Layout(App("a"), App("b"), App("c"));

        // 3 users + trailing Add-new affordance + Settings + Close = 6.
        Assert.Equal(6, layout.Count);
        Assert.All(layout, e => Assert.False(e.IsOuter));
        Assert.Equal(60.0, 360.0 / layout.Count, 3);
        Assert.Equal(-Math.PI / 2, layout[0].AngleRad, 5);
    }

    [Fact]
    public void Eight_total_inner_items_use_forty_five_degree_steps()
    {
        var users = Enumerable.Range(0, OrbitRingLayout.MaxInnerUserCount).Select(i => App("i" + i)).ToArray();
        var layout = Layout(users);

        Assert.Equal(OrbitRingLayout.MaxInnerCount, layout.Count(e => !e.IsOuter));
        Assert.Empty(layout.Where(e => e.IsOuter));
        Assert.Equal(OrbitMetrics.SlotAngleRad(1), layout[1].AngleRad, 5);
    }

    [Fact]
    public void A_ninth_user_item_lands_on_the_outer_ring()
    {
        var users = Enumerable.Range(0, OrbitRingLayout.MaxInnerUserCount + 1)
            .Select(i => App("i" + i)).ToArray();
        var layout = Layout(users);

        Assert.Equal(OrbitRingLayout.MaxInnerCount, layout.Count(e => !e.IsOuter));
        Assert.Single(layout.Where(e => e.IsOuter));
        Assert.Equal("i6", layout.Single(e => e.IsOuter).Item.DisplayName);
    }

    [Fact]
    public void Outer_capacity_caps_total_user_items()
    {
        var users = Enumerable.Range(0, OrbitRingLayout.MaxUserCount + 3)
            .Select(i => App("i" + i)).ToArray();
        var layout = Layout(users);

        Assert.Equal(OrbitRingLayout.MaxInnerCount, layout.Count(e => !e.IsOuter));
        Assert.Equal(OrbitRingLayout.MaxOuterCount, layout.Count(e => e.IsOuter));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(8)]
    public void Nearest_slot_wraps_around_a_variable_count_ring(int count)
    {
        // Straight up is slot 0 for every count.
        Assert.Equal(0, OrbitRingLayout.NearestSlotIndex(0, -1, count));
        // Straight down is halfway around.
        int expected = count / 2;
        Assert.Equal(expected, OrbitRingLayout.NearestSlotIndex(0, 1, count));
    }
}
