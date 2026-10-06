using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

[Collection(SharedDataDirectoryCollection.Name)]
public class OrbitStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "swiftx-store-tests", Guid.NewGuid().ToString("N"));

    public OrbitStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch { /* ignore */ }
    }

    private string Path_(string name) => Path.Combine(_directory, name);

    private static AppConfig SampleConfig()
    {
        var config = new AppConfig
        {
            Theme = AppTheme.Light,
            Language = AppLanguage.French,
            ItemSize = OrbitItemSize.Small,
            Hotkey = "Ctrl+Alt+Space",
            MouseSummonEnabled = false,
            MouseSummonButton = MouseSummonButton.Right,
            MouseSummonHoldMs = 750,
            ShowDataTool = true,
            ShowCertificateTool = false,
        };
        config.Orbits.Add(new OrbitProfile
        {
            Name = "Home",
            Items =
            {
                new OrbitItem
                {
                    Kind = OrbitItemKind.Application,
                    DisplayName = "Notepad",
                    Target = @"C:\Windows\System32\notepad.exe",
                },
                new OrbitItem
                {
                    Kind = OrbitItemKind.Group,
                    DisplayName = "Tools",
                    Children =
                    {
                        new OrbitItem { Kind = OrbitItemKind.Tool, DisplayName = "Data", Target = "Data" },
                    },
                },
            },
        });
        return config;
    }

    [Fact]
    public void An_exported_config_imports_back_unchanged()
    {
        var path = Path_("backup.json");
        var original = SampleConfig();

        Assert.True(OrbitStore.ExportTo(original, path));
        var restored = OrbitStore.ImportFrom(path);

        Assert.NotNull(restored);
        Assert.Equal(AppTheme.Light, restored!.Theme);
        Assert.Equal(AppLanguage.French, restored.Language);
        Assert.Equal(OrbitItemSize.Small, restored.ItemSize);
        Assert.Equal("Ctrl+Alt+Space", restored.Hotkey);
        Assert.False(restored.MouseSummonEnabled);
        Assert.Equal(MouseSummonButton.Right, restored.MouseSummonButton);
        Assert.Equal(750, restored.MouseSummonHoldMs);
        Assert.True(restored.ShowDataTool);
        Assert.False(restored.ShowCertificateTool);

        var orbit = Assert.Single(restored.Orbits);
        Assert.Equal("Home", orbit.Name);
        Assert.Equal(2, orbit.Items.Count);
        var group = orbit.Items[1];
        Assert.Equal(OrbitItemKind.Group, group.Kind);
        Assert.Equal("Data", Assert.Single(group.Children).DisplayName);
    }

    [Fact]
    public void Import_rejects_non_config_json()
    {
        var path = Path_("not-a-config.json");
        File.WriteAllText(path, """{"hello":"world"}""");
        Assert.Null(OrbitStore.ImportFrom(path));
    }
}
