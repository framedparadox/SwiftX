using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

/// <summary>
/// Covers <see cref="ToolCatalog.EnsureToolsGroup"/>, the migration OrbitManager.Start runs on
/// every load: every built-in tool belongs inside the one Tools group, wherever an older config
/// happened to leave them.
/// </summary>
public class ToolsGroupTests
{
    private static OrbitItem App(string name) => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = name,
        Target = @"C:\Windows\System32\" + name + ".exe",
    };

    private static OrbitItem Tool(string target) => new()
    {
        Kind = OrbitItemKind.Tool,
        DisplayName = target,
        Target = target,
    };

    private static OrbitItem Group(string name, params OrbitItem[] children)
    {
        var group = new OrbitItem { Kind = OrbitItemKind.Group, DisplayName = name };
        group.Children.AddRange(children);
        return group;
    }

    private static IEnumerable<string> Targets(IEnumerable<OrbitItem> items) => items.Select(i => i.Target);

    [Fact]
    public void The_seeded_group_already_holds_every_tool()
    {
        var items = new List<OrbitItem> { App("notepad"), ToolCatalog.CreateToolsGroup() };

        // Nothing to move and nothing missing, so nothing to persist either.
        Assert.False(ToolCatalog.EnsureToolsGroup(items));
        Assert.Equal(
            ToolCatalog.All.Select(t => t.Kind.ToString()),
            Targets(items[1].Children));
    }

    [Fact]
    public void Loose_tools_move_into_the_group_and_take_no_orbit_slot_of_their_own()
    {
        var group = Group("Tools");
        var items = new List<OrbitItem> { Tool("Data"), App("notepad"), group, Tool("Base64") };

        Assert.True(ToolCatalog.EnsureToolsGroup(items));

        Assert.Equal(new[] { "notepad" }, items.Where(i => !i.IsGroup).Select(i => i.DisplayName));
        Assert.Contains("Data", Targets(group.Children));
        Assert.Contains("Base64", Targets(group.Children));
    }

    [Fact]
    public void An_existing_group_is_adopted_by_name_rather_than_a_second_one_created()
    {
        // The shape earlier builds seeded: a group called "Dev tools" with no stable target.
        var devTools = Group("Dev tools", Tool("Data"), Tool("SecureValues"));
        var items = new List<OrbitItem> { App("notepad"), devTools, Tool("Certificate") };

        Assert.True(ToolCatalog.EnsureToolsGroup(items));

        Assert.Single(items, i => i.IsGroup);
        Assert.Equal(ToolCatalog.ToolsGroupTarget, devTools.Target);
        // Renaming it is the user's business; only the target is stamped on.
        Assert.Equal("Dev tools", devTools.DisplayName);
        Assert.Contains("Certificate", Targets(devTools.Children));
    }

    [Fact]
    public void Every_built_in_tool_ends_up_in_the_group_exactly_once()
    {
        // A tool at the top level, the same tool again inside another group, and two the orbit
        // never had at all.
        var nested = Group("Work", Tool("Data"), App("calc"));
        var items = new List<OrbitItem> { Tool("Data"), nested, Group("Tools", Tool("Base64")) };

        Assert.True(ToolCatalog.EnsureToolsGroup(items));

        var group = items.First(i => i.Target == ToolCatalog.ToolsGroupTarget);
        Assert.Equal(
            ToolCatalog.All.Select(t => t.Kind.ToString()).OrderBy(t => t),
            Targets(group.Children).OrderBy(t => t));
        // The Tools group's own count is what "exactly once" is about: the unrelated group's copy
        // is the user's own and stays (see the next test).
        Assert.Equal(new[] { "Data", "calc" }, nested.Children.Select(i => i.DisplayName));
    }

    [Fact]
    public void A_tool_the_user_put_in_another_group_stays_there()
    {
        var work = Group("Work", Tool("Base64"), App("calc"));
        var items = new List<OrbitItem> { ToolCatalog.CreateToolsGroup(), work };

        // Nothing moved and nothing missing, so nothing to persist.
        Assert.False(ToolCatalog.EnsureToolsGroup(items));

        Assert.Equal(new[] { "Base64", "calc" }, work.Children.Select(i => i.DisplayName));
        // And the Tools group still holds its own copy.
        Assert.Equal(
            ToolCatalog.All.Select(t => t.Kind.ToString()),
            Targets(items[0].Children));
    }

    [Fact]
    public void A_copy_in_another_group_does_not_bring_back_a_Tools_group_that_was_deleted()
    {
        // The user removed the Tools group and keeps one tool in a group of their own: that is not
        // a loose tool, so there is nothing to gather and no group to create.
        var work = Group("Work", Tool("Base64"));
        var items = new List<OrbitItem> { App("notepad"), work };

        Assert.False(ToolCatalog.EnsureToolsGroup(items));

        Assert.Equal(new[] { "notepad", "Work" }, items.Select(i => i.DisplayName));
        Assert.Equal(new[] { "Base64" }, work.Children.Select(i => i.DisplayName));
    }

    [Fact]
    public void A_group_created_here_takes_the_slot_of_the_first_tool_it_absorbed()
    {
        var items = new List<OrbitItem> { App("notepad"), Tool("Data"), App("calc") };

        Assert.True(ToolCatalog.EnsureToolsGroup(items));

        Assert.Equal(
            new[] { "notepad", "Tools", "calc" },
            items.Select(i => i.DisplayName));
    }

    [Fact]
    public void A_orbit_with_no_tools_and_no_group_is_left_exactly_as_it_is()
    {
        // Someone who deleted the group and its tools outright should not find them back on the
        // next launch.
        var items = new List<OrbitItem> { App("notepad"), Group("Work", App("calc")) };

        Assert.False(ToolCatalog.EnsureToolsGroup(items));
        Assert.Equal(new[] { "notepad", "Work" }, items.Select(i => i.DisplayName));
    }
}
