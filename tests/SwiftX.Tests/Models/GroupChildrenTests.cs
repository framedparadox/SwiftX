using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

/// <summary>
/// Covers <see cref="GroupChildren"/>: rearranging what a group holds and taking one item out of
/// it, as the "Add to orbit" sidebar's group rows do.
/// </summary>
public class GroupChildrenTests
{
    private static OrbitItem Item(string name) => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = name,
        Target = @"C:\apps\" + name + ".exe",
    };

    private static OrbitItem Group(params OrbitItem[] children)
    {
        var group = ElementLibrary.NewGroup("Work");
        group.Children.AddRange(children);
        return group;
    }

    private static string[] Names(OrbitItem group) =>
        group.Children.Select(c => c.DisplayName).ToArray();

    [Fact]
    public void Dragging_an_item_down_puts_it_where_the_target_was()
    {
        var a = Item("a"); var b = Item("b"); var c = Item("c"); var d = Item("d");
        var group = Group(a, b, c, d);

        Assert.True(GroupChildren.Move(group, a, c));

        Assert.Equal(new[] { "b", "c", "a", "d" }, Names(group));
    }

    [Fact]
    public void Dragging_an_item_up_puts_it_before_the_target()
    {
        var a = Item("a"); var b = Item("b"); var c = Item("c"); var d = Item("d");
        var group = Group(a, b, c, d);

        Assert.True(GroupChildren.Move(group, d, b));

        Assert.Equal(new[] { "a", "d", "b", "c" }, Names(group));
    }

    [Fact]
    public void A_move_keeps_every_item_exactly_once()
    {
        var items = Enumerable.Range(0, 6).Select(i => Item("i" + i)).ToArray();
        var group = Group(items);

        GroupChildren.Move(group, items[1], items[4]);
        GroupChildren.Move(group, items[5], items[0]);

        Assert.Equal(items.Length, group.Children.Count);
        Assert.All(items, i => Assert.Single(group.Children, c => ReferenceEquals(c, i)));
    }

    [Fact]
    public void Moving_an_item_onto_itself_or_something_not_in_the_group_changes_nothing()
    {
        var a = Item("a"); var b = Item("b");
        var group = Group(a, b);
        var stranger = Item("stranger");

        Assert.False(GroupChildren.Move(group, a, a));
        Assert.False(GroupChildren.Move(group, a, stranger));
        Assert.False(GroupChildren.Move(group, stranger, a));

        Assert.Equal(new[] { "a", "b" }, Names(group));
    }

    [Fact]
    public void An_item_is_found_by_identity_not_by_what_it_points_at()
    {
        var a = Item("a");
        var lookalike = Item("a");
        var group = Group(a);

        Assert.True(GroupChildren.Holds(group, a));
        Assert.False(GroupChildren.Holds(group, lookalike));
    }

    [Fact]
    public void Move_by_one_swaps_with_the_neighbour_the_sidebar_lists()
    {
        var a = Item("a"); var b = Item("b"); var c = Item("c");
        var group = Group(a, b, c);

        Assert.True(GroupChildren.MoveBy(group, b, +1));
        Assert.Equal(new[] { "a", "c", "b" }, Names(group));

        Assert.True(GroupChildren.MoveBy(group, b, -1));
        Assert.Equal(new[] { "a", "b", "c" }, Names(group));
    }

    [Fact]
    public void Move_by_one_stops_at_either_end()
    {
        var a = Item("a"); var b = Item("b");
        var group = Group(a, b);

        Assert.False(GroupChildren.MoveBy(group, a, -1));
        Assert.False(GroupChildren.MoveBy(group, b, +1));
        Assert.False(GroupChildren.MoveBy(group, a, 0));

        Assert.Equal(new[] { "a", "b" }, Names(group));
    }

    /// <summary>
    /// A placeholder between two items is not a place the sidebar shows, so "down" from the item
    /// above it goes to the item below it rather than onto the placeholder.
    /// </summary>
    [Fact]
    public void Move_by_one_steps_over_a_placeholder()
    {
        var a = Item("a"); var c = Item("c");
        var group = Group(a, SlotHole(), c);

        Assert.True(GroupChildren.MoveBy(group, a, +1));

        // What the sidebar lists is the order that matters; where the placeholder sits is not shown.
        Assert.Equal(new[] { c, a }, GroupChildren.Listed(group));
        Assert.Equal(3, group.Children.Count);
    }

    private static OrbitItem SlotHole() => OrbitSlots.NewEmpty();

    [Fact]
    public void An_item_can_be_taken_out_and_the_rest_keep_their_order()
    {
        var a = Item("a"); var b = Item("b"); var c = Item("c");
        var group = Group(a, b, c);

        Assert.True(GroupChildren.Remove(group, b));

        Assert.Equal(new[] { "a", "c" }, Names(group));
        Assert.False(GroupChildren.Holds(group, b));
    }

    [Fact]
    public void Removing_something_that_is_not_in_the_group_does_nothing()
    {
        var a = Item("a");
        var group = Group(a);

        Assert.False(GroupChildren.Remove(group, Item("a")));

        Assert.Equal(new[] { "a" }, Names(group));
    }

    [Fact]
    public void A_removed_item_can_be_added_again()
    {
        var a = Item("a");
        var group = Group(a);

        GroupChildren.Remove(group, a);

        Assert.Equal(GroupDropVerdict.Allowed, GroupDropRules.Check(group, Item("a")));
    }

    /// <summary>
    /// The Tools group puts every tool back on each launch, so taking one out or reordering them
    /// here would not last; both are refused.
    /// </summary>
    [Fact]
    public void The_Tools_group_cannot_be_rearranged_or_emptied_from_here()
    {
        var tools = ToolCatalog.CreateToolsGroup();
        var first = tools.Children[0];
        var second = tools.Children[1];
        var before = tools.Children.Select(c => c.Target).ToArray();

        Assert.False(GroupChildren.IsEditable(tools));
        Assert.False(GroupChildren.Move(tools, first, second));
        Assert.False(GroupChildren.MoveBy(tools, first, +1));
        Assert.False(GroupChildren.Remove(tools, first));

        Assert.Equal(before, tools.Children.Select(c => c.Target).ToArray());
    }

    [Fact]
    public void Only_a_group_is_editable()
    {
        Assert.False(GroupChildren.IsEditable(Item("a")));
        Assert.True(GroupChildren.IsEditable(Group()));
    }
}
