using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

/// <summary>
/// Covers the two built-in ring entries. They are synthesized onto the ring at render time rather
/// than stored in a profile, so the persistence-facing behaviour that remains is
/// <see cref="SystemRingItems.Strip"/> — RingManager.Start runs it on every load and re-saves when
/// it reports a change, migrating configs written while Settings/Close still lived in
/// RingProfile.Items.
/// </summary>
public class SystemRingItemsTests
{
    private static RingItem User(string name, string target) => new()
    {
        Kind = RingItemKind.Application,
        DisplayName = name,
        Target = target,
    };

    private static RingItem Group(string name, params RingItem[] children)
    {
        var group = new RingItem { Kind = RingItemKind.Group, DisplayName = name };
        group.Children.AddRange(children);
        return group;
    }

    [Fact]
    public void Strip_removes_stored_settings_and_close_entries_and_reports_the_change()
    {
        var items = new List<RingItem>
        {
            User("Notepad", @"C:\Windows\System32\notepad.exe"),
            SystemRingItems.CreateSettings(),
            User("Docs", @"C:\Docs"),
            SystemRingItems.CreateClose(),
        };

        Assert.True(SystemRingItems.Strip(items));

        // Only the user's own entries survive, in their original order — the ring adds Settings
        // and Close back itself, so leaving a stored copy would draw each of them twice.
        Assert.Equal(new[] { "Notepad", "Docs" }, items.Select(i => i.DisplayName));
    }

    [Fact]
    public void Strip_reaches_system_entries_nested_inside_a_group()
    {
        var group = Group("Tools", User("JSON", "Json"), SystemRingItems.CreateClose());
        var items = new List<RingItem> { User("Notepad", "notepad.exe"), group };

        Assert.True(SystemRingItems.Strip(items));

        Assert.Equal(2, items.Count);
        Assert.Equal("JSON", Assert.Single(group.Children).DisplayName);
    }

    [Fact]
    public void Strip_recurses_through_more_than_one_level_of_grouping()
    {
        var inner = Group("Inner", SystemRingItems.CreateSettings(), User("JSON", "Json"));
        var outer = Group("Outer", inner);
        var items = new List<RingItem> { outer };

        Assert.True(SystemRingItems.Strip(items));

        Assert.Equal("JSON", Assert.Single(inner.Children).DisplayName);
    }

    [Fact]
    public void Strip_leaves_a_config_without_system_entries_untouched()
    {
        var group = Group("Tools", User("JSON", "Json"), User("XML", "Xml"));
        var items = new List<RingItem>
        {
            User("Notepad", @"C:\Windows\System32\notepad.exe"),
            User("Site", "https://example.com"),
            group,
        };

        // False is load-bearing: RingManager.Start persists the config whenever Strip reports a
        // change, so a spurious true would rewrite ring.json on every single launch.
        Assert.False(SystemRingItems.Strip(items));

        Assert.Equal(new[] { "Notepad", "Site", "Tools" }, items.Select(i => i.DisplayName));
        Assert.Equal(new[] { "JSON", "XML" }, group.Children.Select(i => i.DisplayName));
    }

    [Theory]
    [InlineData("SwiftX:Settings")]
    [InlineData("SWIFTX:CLOSE")]
    [InlineData("swiftx:Close")]
    public void Strip_matches_system_targets_regardless_of_casing(string target)
    {
        // A hand-edited config can carry either casing; a case-sensitive match would leave the
        // stored copy sitting next to the synthesized one.
        var items = new List<RingItem> { User("Notepad", "notepad.exe"), User("Legacy", target) };

        Assert.True(SystemRingItems.Strip(items));

        Assert.Equal("Notepad", Assert.Single(items).DisplayName);
    }

    [Theory]
    [InlineData("swiftx:settings")]
    [InlineData("SwiftX:Settings")]
    [InlineData("SWIFTX:SETTINGS")]
    [InlineData("swiftx:close")]
    [InlineData("SwiftX:Close")]
    [InlineData("SWIFTX:CLOSE")]
    public void IsSystemTarget_recognises_both_built_in_targets_in_any_casing(string target)
    {
        Assert.True(SystemRingItems.IsSystemTarget(target));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\Windows\System32\notepad.exe")]
    [InlineData("https://example.com")]
    [InlineData("Json")]
    [InlineData("swiftx:back")]
    [InlineData("swiftx:settings.bak")]
    [InlineData("myswiftx:settings")]
    public void IsSystemTarget_rejects_anything_that_is_not_one_of_the_two_built_ins(string? target)
    {
        // "swiftx:back" in particular: it is a retired target, and treating it as a built-in
        // would make Strip silently delete an entry the ring no longer knows how to re-add.
        Assert.False(SystemRingItems.IsSystemTarget(target));
    }

    [Fact]
    public void CreateSettings_builds_an_entry_the_ring_can_recognise_and_label()
    {
        var item = SystemRingItems.CreateSettings();

        Assert.Equal(SystemRingItems.SettingsTarget, item.Target);
        Assert.True(SystemRingItems.IsSystemTarget(item.Target));
        Assert.True(item.IsSystem);
        Assert.False(string.IsNullOrWhiteSpace(item.DisplayName));
    }

    [Fact]
    public void CreateClose_builds_an_entry_the_ring_can_recognise_and_label()
    {
        var item = SystemRingItems.CreateClose();

        Assert.Equal(SystemRingItems.CloseTarget, item.Target);
        Assert.True(SystemRingItems.IsSystemTarget(item.Target));
        Assert.True(item.IsSystem);
        Assert.False(string.IsNullOrWhiteSpace(item.DisplayName));
    }

    [Fact]
    public void The_two_built_in_entries_are_told_apart_by_their_targets()
    {
        Assert.NotEqual(
            SystemRingItems.CreateSettings().Target,
            SystemRingItems.CreateClose().Target);
    }

    [Fact]
    public void Each_call_hands_back_a_fresh_instance_rather_than_a_shared_singleton()
    {
        // The ring window keeps and mutates the items it renders (resolved icon, hover state), so
        // a shared instance would let one window's state bleed into the next one opened.
        Assert.False(ReferenceEquals(
            SystemRingItems.CreateSettings(), SystemRingItems.CreateSettings()));
        Assert.False(ReferenceEquals(
            SystemRingItems.CreateClose(), SystemRingItems.CreateClose()));
        Assert.NotEqual(
            SystemRingItems.CreateSettings().Id, SystemRingItems.CreateSettings().Id);
    }

    [Fact]
    public void A_freshly_created_built_in_entry_is_one_that_Strip_would_remove_from_a_config()
    {
        // Ties the two halves together: whatever CreateSettings/CreateClose produce must be
        // exactly what the migration recognises, so a target rename can never split them.
        var items = new List<RingItem>
        {
            SystemRingItems.CreateSettings(),
            SystemRingItems.CreateClose(),
        };

        Assert.True(SystemRingItems.Strip(items));
        Assert.Empty(items);
    }
}
