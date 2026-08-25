using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

/// <summary>
/// Covers what the hot corner's quadrant shows: the ring's own picks until the corner is arranged
/// by hand, the stored arrangement after that, and the mapping from a stored entry back to the
/// live thing it names.
/// </summary>
public class HotCornerItemsTests
{
    private static RingItem App(string name) => new()
    {
        Kind = RingItemKind.Application,
        DisplayName = name,
        Target = name,
    };

    private static RingItem Link(string name) => new()
    {
        Kind = RingItemKind.WebLink,
        DisplayName = name,
        Target = "https://" + name,
    };

    /// <summary>An Application item that <see cref="QuadrantLayout.FindOrDefaultNotepad"/> will
    /// actually recognize — matched by the target's file name, not its display name.</summary>
    private static RingItem Notepad(string name = "Notepad") => new()
    {
        Kind = RingItemKind.Application,
        DisplayName = name,
        Target = @"C:\Windows\System32\notepad.exe",
    };

    private static AppConfig ConfigWith(params RingItem[] items)
    {
        var config = new AppConfig().EnsurePrimary();
        config.Primary.Items.AddRange(items);
        return config;
    }

    [Fact]
    public void An_untouched_corner_follows_the_ring()
    {
        var config = ConfigWith(Link("github"), Notepad());

        Assert.True(HotCornerItems.IsDefault(config));
        Assert.Equal(
            new[] { "QuickAccess", "Notepad", "Settings" },
            HotCornerItems.Resolve(config, SystemRingItems.CreateSettings()).Select(i => i.DisplayName));

        // Following the ring means keeping up with it, not snapshotting it — a new link joins
        // QuickAccess rather than needing its own place.
        config.Primary.Items.Add(Link("docs"));
        var quickAccess = HotCornerItems.Resolve(config, SystemRingItems.CreateSettings())
            .First(i => i.DisplayName == "QuickAccess");
        Assert.Equal(new[] { "github", "docs" }, quickAccess.Children.Select(c => c.DisplayName));
    }

    [Fact]
    public void Editing_starts_from_what_the_corner_already_shows()
    {
        var config = ConfigWith(Link("github"), Notepad());

        var list = HotCornerItems.Edit(config, SystemRingItems.CreateSettings());

        Assert.False(HotCornerItems.IsDefault(config));
        Assert.Equal(
            new[] { "QuickAccess", "Notepad", "Settings" },
            list.Select(i => i.DisplayName));
    }

    [Fact]
    public void An_arrangement_is_kept_as_the_ring_moves_on()
    {
        var config = ConfigWith(Link("github"), Notepad());
        var list = HotCornerItems.Edit(config, SystemRingItems.CreateSettings());
        // Seeded as [QuickAccess, Notepad, Settings].

        // Rearrange, and take one off.
        var quickAccess = list[0];
        list.RemoveAt(0);
        list.Add(quickAccess);
        list.RemoveAt(0);
        // Now [Settings, QuickAccess].

        // A ring change does not push its way onto a corner the user has arranged.
        config.Primary.Items.Add(Link("docs"));

        Assert.Equal(
            new[] { "Settings", "QuickAccess" },
            HotCornerItems.Resolve(config, SystemRingItems.CreateSettings()).Select(i => i.DisplayName));
    }

    [Fact]
    public void QuickAccess_keeps_gathering_new_links_once_locked_into_an_arrangement()
    {
        var config = ConfigWith(Link("github"));
        HotCornerItems.Edit(config, SystemRingItems.CreateSettings());

        config.Primary.Items.Add(Link("docs"));

        var quickAccess = HotCornerItems.Resolve(config, SystemRingItems.CreateSettings())
            .First(i => i.DisplayName == "QuickAccess");
        Assert.Equal(new[] { "github", "docs" }, quickAccess.Children.Select(c => c.DisplayName));
    }

    [Fact]
    public void Tools_stays_the_live_ring_group_once_locked_into_an_arrangement()
    {
        var tools = ToolCatalog.CreateToolsGroup();
        var config = ConfigWith(tools);
        HotCornerItems.Edit(config, SystemRingItems.CreateSettings());

        var resolved = HotCornerItems.Resolve(config, SystemRingItems.CreateSettings())
            .First(i => i.Target == ToolCatalog.ToolsGroupTarget);
        // The live ring object, not a frozen copy — so the Tools page's own switches (which hide
        // children directly on this object) keep applying wherever the corner shows it.
        Assert.Same(tools, resolved);
    }

    [Fact]
    public void A_stored_entry_resolves_to_the_live_item_it_names()
    {
        var notepad = Notepad("My editor");
        var config = ConfigWith(notepad);
        HotCornerItems.Edit(config, SystemRingItems.CreateSettings());

        // Stored entries are copies in the file, so renaming the ring item has to show through.
        notepad.DisplayName = "Renamed";

        var resolved = HotCornerItems.Resolve(config, SystemRingItems.CreateSettings());
        var slot = Assert.Single(resolved, i => i.Id == notepad.Id);
        Assert.Same(notepad, slot);
        Assert.Equal("Renamed", slot.DisplayName);
    }

    [Fact]
    public void The_settings_entry_resolves_to_the_rings_own_settings_item()
    {
        var config = ConfigWith(App("one"));
        HotCornerItems.Edit(config, SystemRingItems.CreateSettings());

        var settings = SystemRingItems.CreateSettings();
        var resolved = HotCornerItems.Resolve(config, settings);

        Assert.Same(settings, resolved.Last());
    }

    [Fact]
    public void An_entry_whose_item_left_the_ring_keeps_working_from_its_own_copy()
    {
        var notepad = Notepad();
        var config = ConfigWith(notepad);
        HotCornerItems.Edit(config, SystemRingItems.CreateSettings());

        config.Primary.Items.Remove(notepad);

        var resolved = HotCornerItems.Resolve(config, SystemRingItems.CreateSettings());
        Assert.Contains(resolved, i => i.DisplayName == "Notepad");
    }

    [Fact]
    public void Reset_puts_the_corner_back_on_the_rings_picks()
    {
        var config = ConfigWith(Link("github"), Notepad());
        var list = HotCornerItems.Edit(config, SystemRingItems.CreateSettings());
        list.RemoveAt(0);
        Assert.False(HotCornerItems.IsDefault(config));

        HotCornerItems.Reset(config);

        Assert.True(HotCornerItems.IsDefault(config));
        Assert.Equal(
            new[] { "QuickAccess", "Notepad", "Settings" },
            HotCornerItems.Resolve(config, SystemRingItems.CreateSettings()).Select(i => i.DisplayName));
    }

    [Fact]
    public void A_corner_never_shows_more_than_its_four_places()
    {
        var config = ConfigWith(App("one"), App("two"));
        var list = HotCornerItems.Edit(config, SystemRingItems.CreateSettings());
        list.Add(App("extra one"));
        list.Add(App("extra two"));
        list.Add(App("extra three"));

        Assert.Equal(
            QuadrantLayout.ItemCount,
            HotCornerItems.Resolve(config, SystemRingItems.CreateSettings()).Count);
    }
}
