using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

/// <summary>
/// A group on the hot corner has to be the orbit's group to follow what the "Add to orbit" sidebar
/// adds to it. Covers the repair for copies an earlier build left on the corner under ids of their
/// own (<see cref="HotCornerItems.RelinkOrbitGroups"/>), and the groups only the corner has
/// (<see cref="HotCornerItems.OwnGroups"/>), which the sidebar lists beside the orbit's.
/// </summary>
public class HotCornerGroupLinkTests
{
    private static OrbitItem Thing(OrbitItemKind kind, string name, string target) => new()
    {
        Kind = kind,
        DisplayName = name,
        Target = target,
    };

    private static OrbitItem Desktop() => Thing(OrbitItemKind.Folder, "Desktop", @"C:\Users\me\OneDrive\Desktop");
    private static OrbitItem Explorer() => Thing(OrbitItemKind.Application, "File Explorer", @"C:\windows\explorer.exe");
    private static OrbitItem GitHub() => Thing(OrbitItemKind.WebLink, "GitHub", "https://github.com");

    private static OrbitItem Group(string name, params OrbitItem[] children)
    {
        var group = new OrbitItem { Kind = OrbitItemKind.Group, DisplayName = name };
        group.Children.AddRange(children);
        return group;
    }

    private static AppConfig ConfigWith(params OrbitItem[] items)
    {
        var config = new AppConfig().EnsurePrimary();
        config.Primary.Items.AddRange(items);
        return config;
    }

    /// <summary>The corner as the user's own configuration held it: copies of "Apps" cut when it held
    /// two things, one in the other order, and the orbit's "Apps" since grown to three.</summary>
    private static (AppConfig Config, OrbitItem Apps, OrbitItem FirstCopy, OrbitItem SecondCopy) AppsWithTwoOlderCopies()
    {
        var apps = Group("Apps", Desktop(), Explorer(), GitHub());
        var config = ConfigWith(apps);

        var first = Group("Apps", Explorer(), Desktop());
        var second = Group("Apps", Desktop(), Explorer());
        config.HotCornerOrbit.AddRange([first, second, SystemOrbitItems.CreateSettings()]);
        return (config, apps, first, second);
    }

    [Fact]
    public void The_copies_an_older_build_left_on_the_corner_become_the_group_they_were_cut_from()
    {
        var (config, apps, first, second) = AppsWithTwoOlderCopies();
        var settings = SystemOrbitItems.CreateSettings();

        // Before: two groups of their own, each frozen at the two things it was dragged with.
        var before = HotCornerItems.Resolve(config, settings).Where(i => i.DisplayName == "Apps").ToList();
        Assert.All(before, shown => Assert.Equal(2, shown.Children.Count));
        Assert.DoesNotContain(before, shown => ReferenceEquals(shown, apps));

        Assert.True(HotCornerItems.RelinkOrbitGroups(config));

        Assert.Equal(apps.Id, first.Id);
        Assert.Equal(apps.Id, second.Id);
        var after = HotCornerItems.Resolve(config, settings).Where(i => i.DisplayName == "Apps").ToList();
        Assert.Equal(2, after.Count);
        Assert.All(after, shown => Assert.Same(apps, shown));

        // And what the sidebar adds to the orbit's group afterwards shows on the corner.
        apps.Children.Add(Thing(OrbitItemKind.Application, "Calculator", @"C:\windows\calc.exe"));
        Assert.All(
            HotCornerItems.Resolve(config, settings).Where(i => i.DisplayName == "Apps"),
            shown => Assert.Equal(4, shown.Children.Count));
    }

    [Fact]
    public void Linking_the_copies_is_done_once()
    {
        var (config, _, _, _) = AppsWithTwoOlderCopies();

        Assert.True(HotCornerItems.RelinkOrbitGroups(config));
        Assert.False(HotCornerItems.RelinkOrbitGroups(config));
    }

    [Fact]
    public void A_corner_the_user_has_not_arranged_has_nothing_to_link()
    {
        var config = ConfigWith(Group("Apps", Desktop()));

        Assert.False(HotCornerItems.RelinkOrbitGroups(config));
        Assert.True(HotCornerItems.IsDefault(config));
    }

    [Fact]
    public void A_corner_group_no_orbit_group_shares_a_name_with_is_left_alone()
    {
        var config = ConfigWith(Group("Work", Desktop()));
        var play = Group("Play", Desktop());
        string id = play.Id;
        config.HotCornerOrbit.Add(play);

        Assert.False(HotCornerItems.RelinkOrbitGroups(config));
        Assert.Equal(id, play.Id);
    }

    [Fact]
    public void A_corner_group_holding_something_the_orbit_group_lacks_is_left_alone()
    {
        // Same name, but the corner's has an item the orbit's does not: they have gone different ways
        // (or were never the same group), and guessing would swap one's contents for the other's.
        var config = ConfigWith(Group("Work", Desktop(), Explorer()));
        var corner = Group("Work", Desktop(), GitHub());
        string id = corner.Id;
        config.HotCornerOrbit.Add(corner);

        Assert.False(HotCornerItems.RelinkOrbitGroups(config));
        Assert.Equal(id, corner.Id);
    }

    [Fact]
    public void An_empty_corner_group_is_left_alone()
    {
        // An empty group has a name and nothing else to go on, and fits any group of that name.
        var config = ConfigWith(Group("Work", Desktop()));
        var corner = Group("Work");
        string id = corner.Id;
        config.HotCornerOrbit.Add(corner);

        Assert.False(HotCornerItems.RelinkOrbitGroups(config));
        Assert.Equal(id, corner.Id);
    }

    [Fact]
    public void A_corner_group_two_orbit_groups_could_have_been_cut_from_is_left_alone()
    {
        var config = ConfigWith(Group("Work", Desktop(), Explorer()), Group("work", Desktop(), GitHub()));
        var corner = Group("Work", Desktop());
        string id = corner.Id;
        config.HotCornerOrbit.Add(corner);

        Assert.False(HotCornerItems.RelinkOrbitGroups(config));
        Assert.Equal(id, corner.Id);
    }

    [Fact]
    public void Names_and_targets_are_compared_ignoring_case_and_padding()
    {
        var work = Group("Work", Thing(OrbitItemKind.Application, "Editor", @"C:\Apps\Editor.exe"));
        var config = ConfigWith(work);
        var corner = Group("  work ", Thing(OrbitItemKind.Application, "editor", @"c:\apps\editor.EXE"));
        config.HotCornerOrbit.Add(corner);

        Assert.True(HotCornerItems.RelinkOrbitGroups(config));
        Assert.Equal(work.Id, corner.Id);
    }

    [Fact]
    public void The_same_target_as_a_different_kind_of_thing_is_not_the_same_thing()
    {
        var config = ConfigWith(Group("Work", Thing(OrbitItemKind.Folder, "Projects", @"C:\Projects")));
        var corner = Group("Work", Thing(OrbitItemKind.File, "Projects", @"C:\Projects"));
        string id = corner.Id;
        config.HotCornerOrbit.Add(corner);

        Assert.False(HotCornerItems.RelinkOrbitGroups(config));
        Assert.Equal(id, corner.Id);
    }

    [Fact]
    public void A_linked_entry_and_the_built_in_entries_are_never_touched()
    {
        var work = Group("Work", Desktop());
        var tools = ToolCatalog.CreateToolsGroup();
        var config = ConfigWith(work, tools);

        // Already linked (a stored entry with the orbit group's id), the Tools group, and Settings.
        var linked = Group("Work", Desktop());
        linked.Id = work.Id;
        var toolsCopy = ToolCatalog.CreateToolsGroup();
        string toolsCopyId = toolsCopy.Id;
        var settings = SystemOrbitItems.CreateSettings();
        string settingsId = settings.Id;
        config.HotCornerOrbit.AddRange([linked, toolsCopy, settings]);

        Assert.False(HotCornerItems.RelinkOrbitGroups(config));
        Assert.Equal(work.Id, linked.Id);
        Assert.Equal(toolsCopyId, toolsCopy.Id);
        Assert.Equal(settingsId, settings.Id);
    }

    [Fact]
    public void A_group_only_the_corner_has_is_one_of_its_own_groups()
    {
        var onOrbit = Group("Work", Desktop());
        var config = ConfigWith(onOrbit, ToolCatalog.CreateToolsGroup());
        var settings = SystemOrbitItems.CreateSettings();

        var linked = Group("Work", Desktop());
        linked.Id = onOrbit.Id;
        var cornerOnly = Group("Corner stuff", Explorer());
        config.HotCornerOrbit.AddRange([linked, cornerOnly, ToolCatalog.CreateToolsGroup(), settings]);

        var own = HotCornerItems.OwnGroups(config, settings);

        // The linked entry is the orbit's group, which the sidebar lists already; the Tools group is
        // the Tools screen's; Settings is not a group of the user's.
        Assert.Same(cornerOnly, Assert.Single(own));
    }

    [Fact]
    public void A_copy_that_could_not_be_placed_is_listed_as_the_corners_own_so_it_can_still_be_added_to()
    {
        var config = ConfigWith(Group("Work", Desktop(), Explorer()));
        var unplaced = Group("Work", Desktop(), GitHub());
        config.HotCornerOrbit.AddRange([unplaced, SystemOrbitItems.CreateSettings()]);

        Assert.False(HotCornerItems.RelinkOrbitGroups(config));
        Assert.Same(unplaced, Assert.Single(HotCornerItems.OwnGroups(config, SystemOrbitItems.CreateSettings())));
    }

    [Fact]
    public void A_corner_following_the_orbit_has_no_groups_of_its_own()
    {
        // Its groups are QuickAccess, gathered from the orbit, and Tools.
        var config = ConfigWith(Group("Work", GitHub()), ToolCatalog.CreateToolsGroup());

        Assert.Empty(HotCornerItems.OwnGroups(config, SystemOrbitItems.CreateSettings()));
    }

    [Fact]
    public void Something_added_to_a_group_only_the_corner_has_shows_on_the_corner()
    {
        // The sidebar adds to the object it lists, which for a corner-only group is the corner's own
        // entry; the corner resolves that entry to itself.
        var config = ConfigWith(Group("Work", Desktop()));
        var settings = SystemOrbitItems.CreateSettings();
        var cornerOnly = Group("Corner stuff", Explorer());
        config.HotCornerOrbit.AddRange([cornerOnly, settings]);

        var listed = Assert.Single(HotCornerItems.OwnGroups(config, settings));
        Assert.Equal(GroupDropVerdict.Allowed, GroupDropRules.TryAdd(listed, GitHub()));

        var shown = Assert.Single(HotCornerItems.Resolve(config, settings), i => i.DisplayName == "Corner stuff");
        Assert.Equal(new[] { "File Explorer", "GitHub" }, shown.Children.Select(c => c.DisplayName));
    }
}
