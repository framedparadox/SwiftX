using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Resilience;

/// <summary>
/// A configuration file the app did not write must not be able to stop it starting.
/// <para>
/// <b>The failure.</b> A <c>null</c> where a list or an entry belongs — <c>"Items": [null]</c>, the
/// shape a merge conflict or a hand edit can leave — deserializes without complaint, because the C#
/// type system is not enforced on JSON. The first line to walk the list threw:
/// <c>SystemOrbitItems.Strip</c>, called from <c>OrbitManager.Start</c> inside
/// <c>App.OnLaunched</c>, so the process died with an unhandled <c>NullReferenceException</c> on
/// every launch until <c>orbit.json</c> was deleted by hand. The performance review found it by
/// accident: a measurement script seeded one such entry, and the instance exited inside a second.
/// </para>
/// <para>
/// Every shape here goes through the import path, which shares its parsing with <c>Load</c> (the
/// store's file path is fixed per process, and other tests write it), and then through the same
/// migrations <c>OrbitManager.Start</c> runs, because "the file parsed" is not the property that
/// matters — "the app got past launch" is.
/// </para>
/// </summary>
public class ConfigShapeResilienceTests
{
    private static AppConfig? Import(string json)
    {
        var path = Path.Combine(OrbitStore.DataDirectory, "shape-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        try { return OrbitStore.ImportFrom(path); }
        finally { File.Delete(path); }
    }

    /// <summary>What <c>OrbitManager.Start</c> does to a configuration it has just loaded.</summary>
    private static void RunLaunchMigrations(AppConfig config)
    {
        SystemOrbitItems.Strip(config.Primary.Items);
        ToolCatalog.MigrateLegacyTargets(config.Primary.Items);
        ToolCatalog.EnsureToolsGroup(config.Primary.Items);
        ToolCatalog.ApplyVisibility(config.Primary.Items, config);
    }

    [Theory]
    [InlineData("{\"Orbits\":[{\"Items\":[null]}]}")]
    [InlineData("{\"Orbits\":[{\"Items\":[null,{\"Kind\":\"Folder\",\"Target\":\"C:\\\\data\"}]}]}")]
    [InlineData("{\"Orbits\":[{\"Items\":null}]}")]
    [InlineData("{\"Orbits\":[{}]}")]
    [InlineData("{\"Orbits\":[null,{\"Items\":[]}]}")]
    [InlineData("{\"Orbits\":[{\"Items\":[{\"Kind\":\"Group\",\"Children\":[null]}]}]}")]
    [InlineData("{\"Orbits\":[{\"Items\":[{\"Kind\":\"Group\",\"Children\":null}]}]}")]
    [InlineData("{\"Orbits\":[{\"Items\":[{\"Kind\":\"Folder\",\"Target\":null,\"DisplayName\":null,\"Id\":null}]}]}")]
    [InlineData("{\"Orbits\":[{\"Id\":null,\"Name\":null,\"Items\":[]}]}")]
    [InlineData("{\"Orbits\":[{\"Items\":[]}],\"HotCornerOrbit\":[null]}")]
    [InlineData("{\"Orbits\":[{\"Items\":[]}],\"HotCornerOrbit\":null}")]
    [InlineData("{\"Orbits\":[{\"Items\":[]}],\"Hotkey\":null}")]
    public void AConfigurationWithNullsWhereListsAndEntriesBelong_StillGetsPastLaunch(string json)
    {
        var config = Import(json);

        Assert.NotNull(config);
        var thrown = Record.Exception(() => RunLaunchMigrations(config!));
        Assert.Null(thrown);
        AssertNoHoles(config!);
    }

    [Fact]
    public void RepairKeepsEverythingThatCanBeRead_AndDropsOnlyTheNulls()
    {
        var config = Import(
            "{\"Orbits\":[{\"Items\":[null," +
            "{\"Kind\":\"Folder\",\"DisplayName\":\"Data\",\"Target\":\"C:\\\\data\"}," +
            "null," +
            "{\"Kind\":\"Group\",\"DisplayName\":\"Work\",\"Children\":[null," +
            "{\"Kind\":\"WebLink\",\"DisplayName\":\"Docs\",\"Target\":\"https://example.com\"}]}]}]}");

        Assert.NotNull(config);
        var items = config!.Primary.Items;
        Assert.Equal(2, items.Count);
        Assert.Equal("Data", items[0].DisplayName);
        Assert.Equal(@"C:\data", items[0].Target);
        Assert.Equal("Work", items[1].DisplayName);
        var child = Assert.Single(items[1].Children);
        Assert.Equal("https://example.com", child.Target);
    }

    [Theory]
    [InlineData("{\"Orbits\":[null]}")]
    [InlineData("{\"Orbits\":[null,null]}")]
    public void AConfigurationWithNoUsableOrbit_IsStillRefusedAsAnImport(string json)
    {
        // "Keep what you had" is the import's answer to a file that is not a configuration.
        Assert.Null(Import(json));
    }

    [Fact]
    public void RepairIsIdempotent_AndLeavesAHealthyConfigurationAlone()
    {
        var healthy = new AppConfig().EnsurePrimary();
        healthy.Primary.Items.Add(new OrbitItem { Kind = OrbitItemKind.Folder, DisplayName = "Data", Target = @"C:\data" });
        string id = healthy.Primary.Items[0].Id;

        healthy.RepairShape().RepairShape();

        var item = Assert.Single(healthy.Primary.Items);
        Assert.Equal(id, item.Id);
        Assert.Equal("Data", item.DisplayName);
        Assert.Equal(@"C:\data", item.Target);
    }

    private static void AssertNoHoles(AppConfig config)
    {
        Assert.NotNull(config.Orbits);
        Assert.NotNull(config.Hotkey);
        Assert.NotNull(config.HotCornerOrbit);
        Assert.DoesNotContain(null!, config.HotCornerOrbit);
        Assert.DoesNotContain(null!, config.Orbits);
        foreach (var orbit in config.Orbits)
        {
            Assert.NotNull(orbit.Id);
            Assert.NotNull(orbit.Name);
            AssertNoHoles(orbit.Items);
        }
        AssertNoHoles(config.HotCornerOrbit);
    }

    private static void AssertNoHoles(List<OrbitItem> items)
    {
        Assert.NotNull(items);
        foreach (var item in items)
        {
            Assert.NotNull(item);
            Assert.NotNull(item.Id);
            Assert.NotNull(item.Target);
            Assert.NotNull(item.DisplayName);
            AssertNoHoles(item.Children);
        }
    }
}
