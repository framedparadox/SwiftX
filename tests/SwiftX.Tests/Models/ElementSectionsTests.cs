using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

/// <summary>
/// Covers how the Elements screen sorts the orbit into category sections, what it leaves out, and
/// the small facts a card states about an item.
/// </summary>
public class ElementSectionsTests
{
    private static OrbitItem Item(OrbitItemKind kind, string name, string target = "") => new()
    {
        Kind = kind,
        DisplayName = name,
        Target = target.Length > 0 ? target : name,
    };

    private static OrbitItem Group(string name, params OrbitItem[] children)
    {
        var group = new OrbitItem { Kind = OrbitItemKind.Group, DisplayName = name, Target = "" };
        group.Children.AddRange(children);
        return group;
    }

    private static ElementSection Section(IReadOnlyList<ElementSection> sections, ElementCategory c) =>
        sections.Single(s => s.Category == c);

    [Fact]
    public void Every_category_but_tools_is_present_in_listing_order_even_when_empty()
    {
        var sections = ElementSections.Build(Array.Empty<OrbitItem>());

        Assert.Equal(
            ElementCategories.All.Where(c => c != ElementCategory.Tools),
            sections.Select(s => s.Category));
        Assert.All(sections, s => Assert.Empty(s.Entries));
    }

    [Fact]
    public void Items_land_in_the_section_their_kind_belongs_to()
    {
        var app = Item(OrbitItemKind.Application, "Notepad");
        var file = Item(OrbitItemKind.File, "Notes");
        var lnk = Item(OrbitItemKind.Shortcut, "Calc");
        var folder = Item(OrbitItemKind.Folder, "Docs");
        var link = Item(OrbitItemKind.WebLink, "Site", "https://example.com");
        var group = Group("Work");

        var sections = ElementSections.Build(new[] { app, file, lnk, folder, link, group });

        Assert.Equal(new[] { app }, Section(sections, ElementCategory.Apps).Entries.Select(e => e.Item));
        Assert.Equal(new[] { file, lnk }, Section(sections, ElementCategory.Shortcuts).Entries.Select(e => e.Item));
        Assert.Equal(new[] { folder }, Section(sections, ElementCategory.Folders).Entries.Select(e => e.Item));
        Assert.Equal(new[] { link }, Section(sections, ElementCategory.Links).Entries.Select(e => e.Item));
        Assert.Equal(new[] { group }, Section(sections, ElementCategory.Groups).Entries.Select(e => e.Item));
    }

    [Fact]
    public void Orbit_order_is_kept_within_a_section()
    {
        var b = Item(OrbitItemKind.Application, "B");
        var a = Item(OrbitItemKind.Application, "A");

        var apps = Section(ElementSections.Build(new[] { b, a }), ElementCategory.Apps);

        Assert.Equal(new[] { b, a }, apps.Entries.Select(e => e.Item));
    }

    [Fact]
    public void A_groups_children_stay_with_it_whatever_their_kind()
    {
        var folder = Item(OrbitItemKind.Folder, "Docs");
        var app = Item(OrbitItemKind.Application, "Notepad");
        var group = Group("Work", folder, app);

        var sections = ElementSections.Build(new[] { group });

        var entry = Assert.Single(Section(sections, ElementCategory.Groups).Entries);
        Assert.Equal(new[] { folder, app }, entry.Children);
        Assert.Empty(Section(sections, ElementCategory.Folders).Entries);
        Assert.Empty(Section(sections, ElementCategory.Apps).Entries);
    }

    [Fact]
    public void A_plain_item_has_no_children()
    {
        var app = Item(OrbitItemKind.Application, "Notepad");

        var entry = Assert.Single(Section(ElementSections.Build(new[] { app }), ElementCategory.Apps).Entries);

        Assert.Empty(entry.Children);
    }

    [Fact]
    public void Loose_tool_items_and_the_builtin_tools_group_are_left_to_the_tools_screen()
    {
        var tool = Item(OrbitItemKind.Tool, "Data", "Data");
        var toolsGroup = ToolCatalog.CreateToolsGroup();
        var inGroup = Group("Mine", Item(OrbitItemKind.Tool, "Base64", "Base64"),
            Item(OrbitItemKind.Application, "Notepad"));

        var sections = ElementSections.Build(new[] { tool, toolsGroup, inGroup });

        var groups = Section(sections, ElementCategory.Groups).Entries;
        var entry = Assert.Single(groups);
        Assert.Same(inGroup, entry.Item);
        Assert.DoesNotContain(sections, s => s.Category == ElementCategory.Tools);
    }

    [Fact]
    public void A_tool_inside_a_users_group_is_listed_with_that_group()
    {
        var inGroup = Group("Mine", Item(OrbitItemKind.Tool, "Base64", "Base64"),
            Item(OrbitItemKind.Application, "Notepad"));

        var entry = Assert.Single(Section(ElementSections.Build(new[] { inGroup }), ElementCategory.Groups).Entries);

        Assert.Equal(new[] { "Base64", "Notepad" }, entry.Children.Select(c => c.DisplayName));
    }

    [Fact]
    public void Changing_an_items_kind_moves_it_to_the_new_section()
    {
        var item = Item(OrbitItemKind.Application, "Thing", @"C:\Thing");
        Assert.Single(Section(ElementSections.Build(new[] { item }), ElementCategory.Apps).Entries);

        var edited = Item(OrbitItemKind.Folder, "Thing", @"C:\Thing");
        edited.Id = item.Id;
        var sections = ElementSections.Build(new[] { edited });

        Assert.Empty(Section(sections, ElementCategory.Apps).Entries);
        Assert.Single(Section(sections, ElementCategory.Folders).Entries);
    }

    [Theory]
    [InlineData(OrbitItemKind.Application, @"C:\Windows\notepad.exe", @"C:\Windows\notepad.exe")]
    [InlineData(OrbitItemKind.File, @"D:/data/report.txt", "D:/data/report.txt")]
    [InlineData(OrbitItemKind.Folder, @"C:\Users", @"C:\Users")]
    [InlineData(OrbitItemKind.Shortcut, @"  C:\a\b.lnk  ", @"C:\a\b.lnk")]
    [InlineData(OrbitItemKind.Application, "notepad.exe", null)]
    [InlineData(OrbitItemKind.Application, @"%SystemRoot%\notepad.exe", null)]
    [InlineData(OrbitItemKind.Application, @"C:notepad.exe", null)]
    [InlineData(OrbitItemKind.Application, "ms-settings:display", null)]
    [InlineData(OrbitItemKind.Application, @"shell:AppsFolder\Some.App", null)]
    [InlineData(OrbitItemKind.File, @"\\server\share\file.txt", null)]
    [InlineData(OrbitItemKind.WebLink, "https://example.com", null)]
    [InlineData(OrbitItemKind.Group, @"C:\Users", null)]
    [InlineData(OrbitItemKind.Application, "", null)]
    public void Only_an_absolute_drive_letter_path_is_worth_checking(
        OrbitItemKind kind, string target, string? expected)
    {
        var item = new OrbitItem { Kind = kind, Target = target };

        Assert.Equal(expected, ElementSections.LocalPathToCheck(item));
    }

    [Fact]
    public void A_path_on_the_system_drive_is_missing_only_when_nothing_is_there()
    {
        string present = Environment.SystemDirectory;
        string absent = Path.Combine(present, "swiftx-no-such-file-" + Guid.NewGuid().ToString("N") + ".exe");

        Assert.False(ElementSections.IsMissing(present));
        Assert.True(ElementSections.IsMissing(absent));
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"\\server\share\file.txt")]
    public void An_unusable_path_is_never_reported_missing(string path)
    {
        Assert.False(ElementSections.IsMissing(path));
    }

    [Fact]
    public void A_default_icon_is_not_custom()
    {
        Assert.Equal(ElementIconKind.Default,
            ElementSections.IconKindOf(Item(OrbitItemKind.Application, "Notepad")));
    }

    [Fact]
    public void A_groups_own_mark_is_not_a_custom_glyph()
    {
        // The Add dialog stores a group's mark as its custom glyph, so it must not read as a choice.
        var group = Group("Work");
        group.CustomGlyph = new OrbitItem { Kind = OrbitItemKind.Group }.Glyph;

        Assert.Equal(ElementIconKind.Default, ElementSections.IconKindOf(group));
    }

    [Fact]
    public void A_glyph_that_differs_from_the_kinds_default_is_custom()
    {
        var item = Item(OrbitItemKind.Application, "Notepad");
        item.CustomGlyph = char.ConvertFromUtf32(0xE8B7);

        Assert.Equal(ElementIconKind.Glyph, ElementSections.IconKindOf(item));
    }

    [Fact]
    public void An_image_path_is_a_custom_image()
    {
        var item = Item(OrbitItemKind.Application, "Notepad");
        item.CustomIconPath = @"C:\icons\np.png";

        Assert.Equal(ElementIconKind.Image, ElementSections.IconKindOf(item));
    }
}
