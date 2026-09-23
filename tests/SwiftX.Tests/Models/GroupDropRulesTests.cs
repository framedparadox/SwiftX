using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

/// <summary>
/// Covers <see cref="GroupDropRules"/>: what may be dropped onto a group in the "Add to orbit"
/// sidebar.
/// </summary>
public class GroupDropRulesTests
{
    private static OrbitItem App(string name) => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = name,
        Target = @"C:\Apps\" + name + ".exe",
    };

    private static OrbitItem Group(string name, params OrbitItem[] children)
    {
        var group = new OrbitItem { Kind = OrbitItemKind.Group, DisplayName = name };
        group.Children.AddRange(children);
        return group;
    }

    private static OrbitItem Full(string name) =>
        Group(name, Enumerable.Range(0, GroupDropRules.Capacity).Select(i => App("filler" + i)).ToArray());

    [Fact]
    public void Capacity_is_what_the_orbit_flyout_can_actually_show()
    {
        Assert.Equal(OrbitMetrics.FlyoutSlotCount, GroupDropRules.Capacity);
    }

    [Fact]
    public void A_plain_item_is_allowed_into_a_group_with_room()
    {
        var group = Group("Work", App("code"));

        Assert.Equal(GroupDropVerdict.Allowed, GroupDropRules.Check(group, App("calc")));
    }

    [Fact]
    public void A_group_cannot_go_inside_a_group()
    {
        Assert.Equal(GroupDropVerdict.NestedGroup, GroupDropRules.Check(Group("Work"), Group("Play")));
    }

    [Fact]
    public void A_tool_cannot_go_inside_a_user_group()
    {
        var tool = new OrbitItem { Kind = OrbitItemKind.Tool, DisplayName = "Base64", Target = "Base64" };

        Assert.Equal(GroupDropVerdict.Tool, GroupDropRules.Check(Group("Work"), tool));
    }

    [Fact]
    public void Nothing_can_be_dropped_on_the_built_in_tools_group()
    {
        Assert.Equal(GroupDropVerdict.ToolsGroup, GroupDropRules.Check(ToolCatalog.CreateToolsGroup(), App("calc")));
    }

    [Fact]
    public void A_non_group_is_not_a_drop_target()
    {
        Assert.Equal(GroupDropVerdict.NotAGroup, GroupDropRules.Check(App("code"), App("calc")));
    }

    [Fact]
    public void The_same_target_twice_in_one_group_is_refused_ignoring_case()
    {
        var group = Group("Work", App("code"));
        var again = App("code");
        again.Target = again.Target.ToUpperInvariant();

        Assert.Equal(GroupDropVerdict.Duplicate, GroupDropRules.Check(group, again));
    }

    [Fact]
    public void The_same_target_in_two_different_groups_is_fine()
    {
        var first = Group("Work", App("code"));
        var second = Group("Play");

        Assert.Equal(GroupDropVerdict.Allowed, GroupDropRules.Check(second, App("code")));
        Assert.Single(first.Children);
    }

    [Fact]
    public void A_group_at_capacity_refuses_the_next_item_and_one_below_it_accepts()
    {
        var almost = Group("Work", Enumerable.Range(0, GroupDropRules.Capacity - 1).Select(i => App("filler" + i)).ToArray());
        Assert.False(GroupDropRules.IsFull(almost));
        Assert.Equal(GroupDropVerdict.Allowed, GroupDropRules.Check(almost, App("last")));

        Assert.Equal(GroupDropVerdict.Allowed, GroupDropRules.TryAdd(almost, App("last")));
        Assert.True(GroupDropRules.IsFull(almost));
        Assert.Equal(GroupDropVerdict.Full, GroupDropRules.Check(almost, App("over")));
    }

    [Fact]
    public void Hidden_children_take_no_flyout_slot()
    {
        var group = Full("Work");
        group.Children[0].Hidden = true;

        Assert.Equal(GroupDropRules.Capacity - 1, GroupDropRules.Count(group));
        Assert.Equal(GroupDropVerdict.Allowed, GroupDropRules.Check(group, App("extra")));
    }

    [Fact]
    public void TryAdd_adds_only_what_is_allowed()
    {
        var group = Group("Work", App("code"));

        Assert.Equal(GroupDropVerdict.Allowed, GroupDropRules.TryAdd(group, App("calc")));
        Assert.Equal(GroupDropVerdict.Duplicate, GroupDropRules.TryAdd(group, App("calc")));
        Assert.Equal(GroupDropVerdict.NestedGroup, GroupDropRules.TryAdd(group, Group("Play")));

        Assert.Equal(new[] { "code", "calc" }, group.Children.Select(c => c.DisplayName));
    }

    [Fact]
    public void A_batch_stops_taking_items_once_the_group_is_full()
    {
        var group = Group("Work");
        var batch = Enumerable.Range(0, GroupDropRules.Capacity + 3).Select(i => App("app" + i)).ToList();

        var verdicts = batch.Select(item => GroupDropRules.TryAdd(group, item)).ToList();

        Assert.Equal(GroupDropRules.Capacity, group.Children.Count);
        Assert.Equal(3, verdicts.Count(v => v == GroupDropVerdict.Full));
    }

    [Fact]
    public void Every_refusal_has_a_reason_and_an_allowed_drop_has_none()
    {
        Assert.Null(GroupDropRules.Reason(GroupDropVerdict.Allowed));
        foreach (var verdict in Enum.GetValues<GroupDropVerdict>().Where(v => v != GroupDropVerdict.Allowed))
            Assert.False(string.IsNullOrWhiteSpace(GroupDropRules.Reason(verdict)), verdict.ToString());
    }
}
