using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

/// <summary>
/// Covers <see cref="ElementLibrary"/>: which items each category of the "Add to orbit" sidebar
/// lists: the catalog's suggestions plus whatever the orbit already carries.
/// </summary>
public class ElementLibraryTests
{
    private static OrbitItem Item(OrbitItemKind kind, string name, string target) => new()
    {
        Kind = kind,
        DisplayName = name,
        Target = target,
    };

    private static OrbitItem App(string name) => Item(OrbitItemKind.Application, name, @"C:\Apps\" + name + ".exe");
    private static OrbitItem Link(string url) => Item(OrbitItemKind.WebLink, url, url);
    private static OrbitItem Folder(string name) => Item(OrbitItemKind.Folder, name, @"C:\Users\me\" + name);
    private static OrbitItem Shortcut(string name) => Item(OrbitItemKind.Shortcut, name, @"C:\Links\" + name + ".lnk");
    private static OrbitItem FileItem(string name) => Item(OrbitItemKind.File, name, @"C:\Docs\" + name + ".txt");

    private static OrbitItem Tool(string target) => Item(OrbitItemKind.Tool, target, target);

    private static OrbitItem Group(string name, params OrbitItem[] children)
    {
        var group = new OrbitItem { Kind = OrbitItemKind.Group, DisplayName = name };
        group.Children.AddRange(children);
        return group;
    }

    private static IEnumerable<string> Names(IEnumerable<OrbitItem> items) => items.Select(i => i.DisplayName);

    [Theory]
    [InlineData(OrbitItemKind.Application, ElementCategory.Apps)]
    [InlineData(OrbitItemKind.File, ElementCategory.Shortcuts)]
    [InlineData(OrbitItemKind.Shortcut, ElementCategory.Shortcuts)]
    [InlineData(OrbitItemKind.Folder, ElementCategory.Folders)]
    [InlineData(OrbitItemKind.WebLink, ElementCategory.Links)]
    [InlineData(OrbitItemKind.Group, ElementCategory.Groups)]
    [InlineData(OrbitItemKind.Tool, ElementCategory.Tools)]
    public void Every_kind_lives_in_exactly_one_category(OrbitItemKind kind, ElementCategory expected)
    {
        Assert.Equal(expected, ElementCategories.Of(kind));
    }

    [Fact]
    public void Every_category_has_a_label_and_a_glyph_of_its_own()
    {
        Assert.Equal(6, ElementCategories.All.Count);
        Assert.Equal(ElementCategories.All.Count, ElementCategories.All.Select(ElementCategories.Label).Distinct().Count());
        Assert.Equal(ElementCategories.All.Count, ElementCategories.All.Select(ElementCategories.Glyph).Distinct().Count());
        Assert.All(ElementCategories.All, c => Assert.Equal(ElementCategories.Label(c), ElementCategories.Label(c).ToUpperInvariant()));
    }

    [Fact]
    public void A_link_added_by_hand_appears_under_links_beside_the_suggestions()
    {
        var catalog = new List<OrbitItem> { Link("https://github.com") };
        var orbit = new List<OrbitItem> { Link("https://example.com/docs") };

        var rows = ElementLibrary.RowsFor(ElementCategory.Links, catalog, orbit);

        Assert.Equal(new[] { "https://github.com", "https://example.com/docs" }, rows.Select(r => r.Target));
    }

    [Fact]
    public void Items_inside_a_group_are_listed_too()
    {
        var orbit = new List<OrbitItem> { Group("Work", Folder("Projects"), Link("https://jira.example.com")) };

        Assert.Equal(new[] { "Projects" }, Names(ElementLibrary.RowsFor(ElementCategory.Folders, [], orbit)));
        Assert.Equal(new[] { "https://jira.example.com" }, Names(ElementLibrary.RowsFor(ElementCategory.Links, [], orbit)));
    }

    [Fact]
    public void A_target_on_both_the_catalog_and_the_orbit_is_listed_once_and_the_catalog_wins()
    {
        var suggested = App("notepad");
        var onOrbit = App("notepad");
        onOrbit.Target = onOrbit.Target.ToUpperInvariant();

        var rows = ElementLibrary.RowsFor(ElementCategory.Apps, [suggested], [onOrbit]);

        var only = Assert.Single(rows);
        Assert.Same(suggested, only);
    }

    [Fact]
    public void The_same_target_at_the_top_level_and_inside_a_group_is_listed_once()
    {
        var orbit = new List<OrbitItem>
        {
            Link("https://a.example.com"),
            Group("Work", Link("https://a.example.com"), Link("https://b.example.com")),
        };

        var rows = ElementLibrary.RowsFor(ElementCategory.Links, [], orbit);

        Assert.Equal(new[] { "https://a.example.com", "https://b.example.com" }, rows.Select(r => r.Target));
    }

    [Fact]
    public void Folders_no_longer_live_under_shortcuts()
    {
        var orbit = new List<OrbitItem> { Folder("Projects"), Shortcut("Build"), FileItem("notes"), App("calc") };

        Assert.Equal(new[] { "Projects" }, Names(ElementLibrary.RowsFor(ElementCategory.Folders, [], orbit)));
        Assert.Equal(new[] { "Build", "notes" }, Names(ElementLibrary.RowsFor(ElementCategory.Shortcuts, [], orbit)));
        Assert.Equal(new[] { "calc" }, Names(ElementLibrary.RowsFor(ElementCategory.Apps, [], orbit)));
    }

    [Fact]
    public void The_built_in_tools_group_and_the_tools_in_it_are_never_listed()
    {
        var orbit = new List<OrbitItem>
        {
            ToolCatalog.CreateToolsGroup(),
            Group("Work", App("code")),
        };

        Assert.Equal(new[] { "Work" }, Names(ElementLibrary.RowsFor(ElementCategory.Groups, [], orbit)));
        Assert.Equal(new[] { "code" }, Names(ElementLibrary.OnOrbit(orbit).Where(i => !i.IsGroup)));
        Assert.DoesNotContain(ElementLibrary.OnOrbit(orbit), i => i.IsTool);
    }

    [Fact]
    public void A_group_the_catalog_offers_is_not_a_group_to_add_to()
    {
        // OnboardingCatalog offers the Tools group as a tile; it must not surface as a drop target.
        var rows = ElementLibrary.RowsFor(ElementCategory.Groups, [ToolCatalog.CreateToolsGroup()], []);

        Assert.Empty(rows);
    }

    [Fact]
    public void Groups_are_told_apart_by_id_not_by_their_empty_targets()
    {
        var orbit = new List<OrbitItem> { Group("Work"), Group("Play"), Group("Work") };

        Assert.Equal(new[] { "Work", "Play", "Work" }, Names(ElementLibrary.RowsFor(ElementCategory.Groups, [], orbit)));
    }

    [Fact]
    public void A_group_nested_inside_a_group_is_not_a_row_but_what_it_holds_still_is()
    {
        var inner = Group("Inner", Link("https://deep.example.com"));
        var orbit = new List<OrbitItem> { Group("Outer", inner) };

        Assert.Equal(new[] { "Outer" }, Names(ElementLibrary.RowsFor(ElementCategory.Groups, [], orbit)));
        Assert.Equal(new[] { "https://deep.example.com" }, Names(ElementLibrary.RowsFor(ElementCategory.Links, [], orbit)));
    }

    [Fact]
    public void Tools_come_from_the_catalog_only()
    {
        var catalog = ToolCatalog.CreateToolsGroup().Children;
        var orbit = new List<OrbitItem> { Tool("Base64"), Tool("Retired") };

        var rows = ElementLibrary.RowsFor(ElementCategory.Tools, catalog, orbit);

        Assert.Equal(catalog.Select(t => t.Target), rows.Select(r => r.Target));
    }

    [Fact]
    public void System_entries_and_items_with_no_target_are_skipped()
    {
        var settings = SystemOrbitItems.CreateSettings();
        var blank = Item(OrbitItemKind.Application, "blank", "");
        var orbit = new List<OrbitItem> { settings, blank, App("calc") };

        Assert.Equal(new[] { "calc" }, Names(ElementLibrary.RowsFor(ElementCategory.Apps, [], orbit)));
    }

    [Fact]
    public void An_item_taken_off_the_orbit_that_is_not_in_the_catalog_leaves_its_category()
    {
        var link = Link("https://gone.example.com");
        var orbit = new List<OrbitItem> { link };

        Assert.Single(ElementLibrary.RowsFor(ElementCategory.Links, [], orbit));
        orbit.Remove(link);
        Assert.Empty(ElementLibrary.RowsFor(ElementCategory.Links, [], orbit));
    }

    [Fact]
    public void ContainsTarget_finds_top_level_and_nested_targets_ignoring_case()
    {
        var orbit = new List<OrbitItem> { App("calc"), Group("Work", Link("https://x.example.com")) };

        Assert.True(ElementLibrary.ContainsTarget(orbit, @"c:\apps\CALC.EXE"));
        Assert.True(ElementLibrary.ContainsTarget(orbit, "HTTPS://X.EXAMPLE.COM"));
        Assert.False(ElementLibrary.ContainsTarget(orbit, "https://y.example.com"));
        Assert.False(ElementLibrary.ContainsTarget(orbit, ""));
    }

    [Fact]
    public void A_group_on_the_orbit_is_the_orbits_own_but_a_copy_of_it_is_not()
    {
        var work = Group("Work", App("code"));
        var orbit = new List<OrbitItem> { App("calc"), work };

        Assert.True(ElementLibrary.IsOrbitUserGroup(orbit, work));

        // Same name, same children, even the same id: still another object, and the sidebar's
        // Groups list holds the orbit's objects, so it is not one a row can add to.
        var copy = Group("Work", App("code"));
        copy.Id = work.Id;
        Assert.False(ElementLibrary.IsOrbitUserGroup(orbit, copy));
    }

    [Fact]
    public void Only_the_orbits_user_groups_count_as_the_orbits_own()
    {
        var tools = ToolCatalog.CreateToolsGroup();
        var inner = Group("Inner");
        var outer = Group("Outer", inner);
        var app = App("calc");
        var orbit = new List<OrbitItem> { tools, outer, app };

        Assert.True(ElementLibrary.IsOrbitUserGroup(orbit, outer));
        Assert.False(ElementLibrary.IsOrbitUserGroup(orbit, tools));   // the Tools screen's, not a place to add to
        Assert.False(ElementLibrary.IsOrbitUserGroup(orbit, inner));   // the orbit cannot open a group in a group
        Assert.False(ElementLibrary.IsOrbitUserGroup(orbit, app));     // not a group at all
        Assert.False(ElementLibrary.IsOrbitUserGroup([], outer));      // a group that is no longer on the orbit
    }

    [Fact]
    public void NewGroup_uses_the_name_given_and_falls_back_to_Group()
    {
        var named = ElementLibrary.NewGroup("  Work  ");
        Assert.Equal(OrbitItemKind.Group, named.Kind);
        Assert.Equal("Work", named.DisplayName);
        Assert.Equal(ElementCategories.Glyph(ElementCategory.Groups), named.CustomGlyph);
        Assert.Empty(named.Children);

        Assert.Equal("Group", ElementLibrary.NewGroup("   ").DisplayName);
        Assert.Equal("Group", ElementLibrary.NewGroup(null).DisplayName);
        Assert.NotEqual(named.Id, ElementLibrary.NewGroup("Work").Id);
    }
}
