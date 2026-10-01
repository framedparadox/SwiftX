using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

/// <summary>
/// Covers <see cref="OrbitSlots"/>: removing an item leaves its slot empty instead of closing up
/// the ring, and adding fills empty slots before extending it.
/// </summary>
public class OrbitSlotsTests
{
    private static OrbitItem App(string name) => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = name,
        Target = @"C:\Apps\" + name + ".exe",
    };

    private static string[] Names(List<OrbitItem> items) =>
        items.Select(i => i.IsEmpty ? "_" : i.DisplayName).ToArray();

    [Fact]
    public void Vacating_the_middle_item_leaves_its_slot_empty_and_does_not_shift_the_rest()
    {
        var items = new List<OrbitItem> { App("a"), App("b"), App("c") };

        Assert.True(OrbitSlots.Vacate(items, items[1]));

        Assert.Equal(new[] { "a", "_", "c" }, Names(items));
    }

    [Fact]
    public void A_smaller_capacity_bounds_placement_and_moves_for_the_corner()
    {
        var items = new List<OrbitItem> { App("a") };
        Assert.True(OrbitSlots.PlaceAt(items, 3, App("d"), capacity: 4));
        Assert.Equal(new[] { "a", "_", "_", "d" }, Names(items));
        Assert.False(OrbitSlots.PlaceAt(items, 4, App("e"), capacity: 4));

        var moved = OrbitSlots.Moved(items, items[0], 4, capacity: 4);
        Assert.Equal(items, moved);
    }

    [Fact]
    public void Vacating_the_last_item_trims_the_empties_behind_it()
    {
        var items = new List<OrbitItem> { App("a"), App("b"), App("c") };
        OrbitSlots.Vacate(items, items[1]);

        OrbitSlots.Vacate(items, items[^1]);

        Assert.Equal(new[] { "a" }, Names(items));
    }

    [Fact]
    public void Vacating_a_hidden_item_just_removes_it_because_it_held_no_slot()
    {
        var hidden = App("h");
        hidden.Hidden = true;
        var items = new List<OrbitItem> { App("a"), hidden, App("c") };

        OrbitSlots.Vacate(items, hidden);

        Assert.Equal(new[] { "a", "c" }, Names(items));
    }

    [Fact]
    public void Vacating_something_not_in_the_list_does_nothing()
    {
        var items = new List<OrbitItem> { App("a") };

        Assert.False(OrbitSlots.Vacate(items, App("x")));
        Assert.Single(items);
    }

    [Fact]
    public void Place_fills_the_first_empty_slot_before_extending_the_list()
    {
        var items = new List<OrbitItem> { App("a"), App("b"), App("c") };
        OrbitSlots.Vacate(items, items[1]);

        Assert.True(OrbitSlots.Place(items, App("d")));
        Assert.Equal(new[] { "a", "d", "c" }, Names(items));

        Assert.True(OrbitSlots.Place(items, App("e")));
        Assert.Equal(new[] { "a", "d", "c", "e" }, Names(items));
    }

    [Fact]
    public void Place_refuses_once_every_slot_is_taken()
    {
        var items = Enumerable.Range(0, OrbitMetrics.UserSlotCount).Select(i => App("i" + i)).ToList();

        Assert.False(OrbitSlots.HasRoom(items));
        Assert.False(OrbitSlots.Place(items, App("extra")));
        Assert.Equal(OrbitMetrics.UserSlotCount, items.Count);
    }

    [Fact]
    public void A_full_ring_with_a_hole_has_room_and_counts_only_real_items()
    {
        var items = Enumerable.Range(0, OrbitMetrics.UserSlotCount).Select(i => App("i" + i)).ToList();
        OrbitSlots.Vacate(items, items[2]);

        Assert.True(OrbitSlots.HasRoom(items));
        Assert.Equal(OrbitMetrics.UserSlotCount - 1, OrbitSlots.Used(items));
    }

    [Fact]
    public void PlaceAt_puts_the_item_in_the_slot_and_pads_the_gap_with_empties()
    {
        var items = new List<OrbitItem> { App("a") };

        Assert.True(OrbitSlots.PlaceAt(items, 3, App("d")));

        Assert.Equal(new[] { "a", "_", "_", "d" }, Names(items));
    }

    [Fact]
    public void PlaceAt_uses_an_empty_slot_but_not_an_occupied_one()
    {
        var items = new List<OrbitItem> { App("a"), App("b"), App("c") };
        OrbitSlots.Vacate(items, items[1]);

        Assert.False(OrbitSlots.PlaceAt(items, 0, App("x")));
        Assert.True(OrbitSlots.PlaceAt(items, 1, App("x")));
        Assert.Equal(new[] { "a", "x", "c" }, Names(items));
    }

    [Fact]
    public void PlaceAt_refuses_a_slot_off_the_ring()
    {
        var items = new List<OrbitItem>();

        Assert.False(OrbitSlots.PlaceAt(items, OrbitMetrics.UserSlotCount, App("x")));
        Assert.False(OrbitSlots.PlaceAt(items, -1, App("x")));
    }

    [Fact]
    public void Dragging_to_an_empty_slot_past_the_end_leaves_the_old_slot_empty()
    {
        var a = App("a");
        var b = App("b");
        var visible = new List<OrbitItem> { a, b };

        var moved = OrbitSlots.Moved(visible, a, 4);

        Assert.Equal(new[] { "_", "b", "_", "_", "a" }, Names(moved));
    }

    [Fact]
    public void Dragging_within_the_list_moves_the_item_and_shifts_the_ones_between()
    {
        var a = App("a");
        var visible = new List<OrbitItem> { a, App("b"), App("c") };

        Assert.Equal(new[] { "b", "c", "a" }, Names(OrbitSlots.Moved(visible, a, 2)));
        Assert.Equal(new[] { "a", "b", "c" }, Names(OrbitSlots.Moved(visible, a, 0)));
    }

    [Fact]
    public void Dragging_onto_a_hole_takes_the_hole()
    {
        var a = App("a");
        var items = new List<OrbitItem> { a, App("b"), App("c") };
        OrbitSlots.Vacate(items, items[1]);

        var moved = OrbitSlots.Moved(OrbitSlots.Visible(items), a, 1);

        Assert.Equal(new[] { "_", "a", "c" }, Names(moved));
    }

    [Fact]
    public void Empty_slots_are_not_listed_or_offered_as_orbit_items()
    {
        var items = new List<OrbitItem> { App("a"), OrbitSlots.NewEmpty(), App("c") };

        Assert.False(ElementSections.IsListed(OrbitSlots.NewEmpty()));
        Assert.DoesNotContain(ElementLibrary.OnOrbit(items), i => i.IsEmpty);
    }
}
