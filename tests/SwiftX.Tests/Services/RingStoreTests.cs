using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

public class RingStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "swiftx-store-tests", Guid.NewGuid().ToString("N"));

    public RingStoreTests() => Directory.CreateDirectory(_directory);

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
            ItemSize = RingItemSize.Small,
            Hotkey = "Ctrl+Alt+Space",
            ShowJsonTool = true,
            ShowXmlTool = false,
        };
        config.Rings.Add(new RingProfile
        {
            Name = "Home",
            Items =
            {
                new RingItem
                {
                    Kind = RingItemKind.Application,
                    DisplayName = "Notepad",
                    Target = @"C:\Windows\System32\notepad.exe",
                },
                new RingItem
                {
                    Kind = RingItemKind.Group,
                    DisplayName = "Tools",
                    Children =
                    {
                        new RingItem { Kind = RingItemKind.Tool, DisplayName = "JSON", Target = "Json" },
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

        Assert.True(RingStore.ExportTo(original, path));
        var restored = RingStore.ImportFrom(path);

        Assert.NotNull(restored);
        Assert.Equal(AppTheme.Light, restored!.Theme);
        Assert.Equal(RingItemSize.Small, restored.ItemSize);
        Assert.Equal("Ctrl+Alt+Space", restored.Hotkey);
        Assert.True(restored.ShowJsonTool);
        Assert.False(restored.ShowXmlTool);

        var ring = Assert.Single(restored.Rings);
        Assert.Equal("Home", ring.Name);
        Assert.Equal(2, ring.Items.Count);
        var group = ring.Items[1];
        Assert.Equal(RingItemKind.Group, group.Kind);
        Assert.Equal("JSON", Assert.Single(group.Children).DisplayName);
    }

    [Fact]
    public void Import_rejects_non_config_json()
    {
        var path = Path_("not-a-config.json");
        File.WriteAllText(path, """{"hello":"world"}""");
        Assert.Null(RingStore.ImportFrom(path));
    }
}
