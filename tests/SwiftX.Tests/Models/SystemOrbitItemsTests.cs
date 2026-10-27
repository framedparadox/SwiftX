using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

/// <summary>
/// Covers the two built-in orbit entries. They are synthesized onto the orbit at render time rather
/// than stored in a profile, so the persistence-facing behaviour that remains is
/// <see cref="SystemOrbitItems.Strip"/> — OrbitManager.Start runs it on every load and re-saves when
/// it reports a change, migrating configs written while Settings/Close still lived in
/// OrbitProfile.Items.
/// </summary>
public class SystemOrbitItemsTests
{
    private static OrbitItem User(string name, string target) => new()
    {
        Kind = OrbitItemKind.Application,
        DisplayName = name,
        Target = target,
    };

    private static OrbitItem Group(string name, params OrbitItem[] children)
    {
        var group = new OrbitItem { Kind = OrbitItemKind.Group, DisplayName = name };
        group.Children.AddRange(children);
        return group;
    }

    [Fact]
    public void Strip_removes_stored_settings_and_close_entries_and_reports_the_change()
    {
        var items = new List<OrbitItem>
        {
            User("Notepad", @"C:\Windows\System32\notepad.exe"),
            SystemOrbitItems.CreateSettings(),
            User("Docs", @"C:\Docs"),
            SystemOrbitItems.CreateClose(),
        };

        Assert.True(SystemOrbitItems.Strip(items));

        // Only the user's own entries survive, in their original order — the orbit adds Settings
        // and Close back itself, so leaving a stored copy would draw each of them twice.
        Assert.Equal(new[] { "Notepad", "Docs" }, items.Select(i => i.DisplayName));
    }

    [Fact]
    public void Strip_reaches_system_entries_nested_inside_a_group()
    {
        var group = Group("Tools", User("JSON", "Json"), SystemOrbitItems.CreateClose());
        var items = new List<OrbitItem> { User("Notepad", "notepad.exe"), group };

        Assert.True(SystemOrbitItems.Strip(items));

        Assert.Equal(2, items.Count);
        Assert.Equal("JSON", Assert.Single(group.Children).DisplayName);
    }

    [Fact]
    public void Strip_recurses_through_more_than_one_level_of_grouping()
    {
        var inner = Group("Inner", SystemOrbitItems.CreateSettings(), User("JSON", "Json"));
        var outer = Group("Outer", inner);
        var items = new List<OrbitItem> { outer };

        Assert.True(SystemOrbitItems.Strip(items));

        Assert.Equal("JSON", Assert.Single(inner.Children).DisplayName);
    }

    [Fact]
    public void Strip_leaves_a_config_without_system_entries_untouched()
    {
        var group = Group("Tools", User("JSON", "Json"), User("XML", "Xml"));
        var items = new List<OrbitItem>
        {
            User("Notepad", @"C:\Windows\System32\notepad.exe"),
            User("Site", "https://example.com"),
            group,
        };

        // False is load-bearing: OrbitManager.Start persists the config whenever Strip reports a
        // change, so a spurious true would rewrite orbit.json on every single launch.
        Assert.False(SystemOrbitItems.Strip(items));

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
        var items = new List<OrbitItem> { User("Notepad", "notepad.exe"), User("Legacy", target) };

        Assert.True(SystemOrbitItems.Strip(items));

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
        Assert.True(SystemOrbitItems.IsSystemTarget(target));
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
        // would make Strip silently delete an entry the orbit no longer knows how to re-add.
        Assert.False(SystemOrbitItems.IsSystemTarget(target));
    }

    [Fact]
    public void CreateSettings_builds_an_entry_the_orbit_can_recognise_and_label()
    {
        var item = SystemOrbitItems.CreateSettings();

        Assert.Equal(SystemOrbitItems.SettingsTarget, item.Target);
        Assert.True(SystemOrbitItems.IsSystemTarget(item.Target));
        Assert.True(item.IsSystem);
        Assert.False(item.IsGroup);
        Assert.Empty(item.Children);
        Assert.False(string.IsNullOrWhiteSpace(item.DisplayName));
    }

    [Fact]
    public void CreateClose_builds_an_entry_the_orbit_can_recognise_and_label()
    {
        var item = SystemOrbitItems.CreateClose();

        Assert.Equal(SystemOrbitItems.CloseTarget, item.Target);
        Assert.True(SystemOrbitItems.IsSystemTarget(item.Target));
        Assert.True(item.IsSystem);
        Assert.False(string.IsNullOrWhiteSpace(item.DisplayName));
    }

    [Fact]
    public void The_two_built_in_entries_are_told_apart_by_their_targets()
    {
        Assert.NotEqual(
            SystemOrbitItems.CreateSettings().Target,
            SystemOrbitItems.CreateClose().Target);
    }

    [Fact]
    public void Each_call_hands_back_a_fresh_instance_rather_than_a_shared_singleton()
    {
        // The orbit window keeps and mutates the items it renders (resolved icon, hover state), so
        // a shared instance would let one window's state bleed into the next one opened.
        Assert.False(ReferenceEquals(
            SystemOrbitItems.CreateSettings(), SystemOrbitItems.CreateSettings()));
        Assert.False(ReferenceEquals(
            SystemOrbitItems.CreateClose(), SystemOrbitItems.CreateClose()));
        Assert.NotEqual(
            SystemOrbitItems.CreateSettings().Id, SystemOrbitItems.CreateSettings().Id);
    }

    [Fact]
    public void A_freshly_created_built_in_entry_is_one_that_Strip_would_remove_from_a_config()
    {
        // Ties the two halves together: whatever CreateSettings/CreateClose produce must be
        // exactly what the migration recognises, so a target rename can never split them.
        var items = new List<OrbitItem>
        {
            SystemOrbitItems.CreateSettings(),
            SystemOrbitItems.CreateClose(),
        };

        Assert.True(SystemOrbitItems.Strip(items));
        Assert.Empty(items);
    }

    // ---- The other default system operations -------------------------------------------------

    public static IEnumerable<object[]> AdditionalSystemActions()
    {
        yield return new object[] { SystemOrbitItems.CreateMute(), SystemOrbitItems.MuteTarget };
        yield return new object[] { SystemOrbitItems.CreateLock(), SystemOrbitItems.LockTarget };
        yield return new object[] { SystemOrbitItems.CreateSleep(), SystemOrbitItems.SleepTarget };
        yield return new object[] { SystemOrbitItems.CreateScreenshot(), SystemOrbitItems.ScreenshotTarget };
        yield return new object[]
            { SystemOrbitItems.CreateEmptyRecycleBin(), SystemOrbitItems.EmptyRecycleBinTarget };
        yield return new object[] { SystemOrbitItems.CreateTaskManager(), SystemOrbitItems.TaskManagerTarget };
    }

    [Theory]
    [MemberData(nameof(AdditionalSystemActions))]
    public void Each_additional_system_action_builds_an_entry_the_orbit_can_recognise_and_label(
        OrbitItem item, string expectedTarget)
    {
        Assert.Equal(expectedTarget, item.Target);
        Assert.True(SystemOrbitItems.IsSystemTarget(item.Target));
        Assert.True(item.IsSystem);
        Assert.Equal(OrbitItemKind.Application, item.Kind);
        Assert.False(string.IsNullOrWhiteSpace(item.DisplayName));
        Assert.False(string.IsNullOrEmpty(item.CustomGlyph));
    }

    [Fact]
    public void The_additional_system_actions_each_have_a_target_of_their_own()
    {
        var targets = new[]
        {
            SystemOrbitItems.CreateMute(), SystemOrbitItems.CreateLock(), SystemOrbitItems.CreateSleep(),
            SystemOrbitItems.CreateScreenshot(), SystemOrbitItems.CreateEmptyRecycleBin(),
            SystemOrbitItems.CreateTaskManager(),
        }.Select(i => i.Target);

        Assert.Equal(targets.Distinct(StringComparer.OrdinalIgnoreCase).Count(), targets.Count());
    }

    [Theory]
    [InlineData("swiftx:mute")]
    [InlineData("SwiftX:Mute")]
    [InlineData("swiftx:lock")]
    [InlineData("swiftx:sleep")]
    [InlineData("swiftx:screenshot")]
    [InlineData("swiftx:empty-recycle-bin")]
    [InlineData("swiftx:task-manager")]
    public void IsSystemTarget_recognises_the_additional_system_actions_in_any_casing(string target)
    {
        Assert.True(SystemOrbitItems.IsSystemTarget(target));
    }

    // ---- "Map keyboard action" ----------------------------------------------------------------

    [Fact]
    public void CreateKeyboardAction_carries_the_name_and_combination_given_to_it()
    {
        var item = SystemOrbitItems.CreateKeyboardAction("Mute Teams", "Ctrl+Shift+M");

        Assert.Equal(OrbitItemKind.KeyboardAction, item.Kind);
        Assert.True(item.IsKeyboardAction);
        Assert.Equal("Mute Teams", item.DisplayName);
        Assert.Equal("Ctrl+Shift+M", item.Arguments);
    }

    [Fact]
    public void CreateKeyboardAction_falls_back_to_a_default_name_when_none_is_given()
    {
        Assert.Equal("Map Keyboard Action", SystemOrbitItems.CreateKeyboardAction().DisplayName);
        Assert.Equal("Map Keyboard Action", SystemOrbitItems.CreateKeyboardAction("   ").DisplayName);
    }

    [Fact]
    public void CreateKeyboardAction_is_not_a_built_in_system_target()
    {
        // It has no target to recognise at all (see its own remarks) — unlike every other item on
        // this page, a configured instance is the user's own content and must survive a save and
        // reload the same way any other orbit item does, which IsSystem (never persisted) cannot
        // provide on its own.
        var item = SystemOrbitItems.CreateKeyboardAction("Anything", "Ctrl+Alt+T");

        Assert.False(item.IsSystem);
        Assert.False(SystemOrbitItems.IsSystemTarget(item.Target));
    }

    [Fact]
    public void Two_keyboard_actions_are_independent_instances_even_with_the_same_combination()
    {
        var a = SystemOrbitItems.CreateKeyboardAction("A", "Ctrl+Alt+T");
        var b = SystemOrbitItems.CreateKeyboardAction("B", "Ctrl+Alt+T");

        Assert.False(ReferenceEquals(a, b));
        Assert.NotEqual(a.Id, b.Id);
    }
}
