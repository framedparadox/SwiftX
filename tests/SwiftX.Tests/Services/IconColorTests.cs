using SwiftX.Models;
using SwiftX.Services;
using Windows.UI;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// Covers the glyph colour an item can be given (<see cref="OrbitItem.CustomIconColor"/>) and the
/// helper every glyph-drawing site asks (<see cref="IconColors"/>).
/// </summary>
[Collection(SharedDataDirectoryCollection.Name)]
public class IconColorTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "swiftx-icon-color-tests", Guid.NewGuid().ToString("N"));

    public IconColorTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch { /* ignore */ }
    }

    [Fact]
    public void An_item_with_no_colour_uses_the_theme_colour()
    {
        Assert.False(IconColors.TryGet(new OrbitItem(), out _));
        Assert.False(IconColors.TryGet(null, out _));
    }

    [Fact]
    public void A_stored_colour_reads_back_as_that_colour()
    {
        var item = new OrbitItem { CustomIconColor = "#E5484D" };

        Assert.True(IconColors.TryGet(item, out var color));
        Assert.Equal(Color.FromArgb(255, 0xE5, 0x48, 0x4D), color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void A_colour_that_is_not_RRGGBB_is_ignored_rather_than_failing_a_render(string stored)
    {
        Assert.False(IconColors.TryGet(new OrbitItem { CustomIconColor = stored }, out _));
    }

    [Fact]
    public void The_stored_form_round_trips()
    {
        var color = Color.FromArgb(255, 0x12, 0xA5, 0x94);

        var item = new OrbitItem { CustomIconColor = IconColors.ToStored(color) };

        Assert.Equal("#12A594", item.CustomIconColor);
        Assert.True(IconColors.TryGet(item, out var back));
        Assert.Equal(color, back);
    }

    [Fact]
    public void Every_preset_is_a_valid_distinct_colour()
    {
        foreach (var swatch in IconColors.Presets)
            Assert.True(AccentPalette.TryParseHex(swatch.Hex, out _), swatch.Name);

        Assert.Equal(IconColors.Presets.Count, IconColors.Presets.Select(s => s.Hex).Distinct().Count());
    }

    /// <summary>
    /// Colouring a glyph never changes which picture an item resolves to, so it must not invalidate
    /// one (see <see cref="OrbitItem.IconKey"/>): a favicon would be re-read for nothing.
    /// </summary>
    [Fact]
    public void Changing_the_colour_does_not_change_the_icon_key()
    {
        var item = new OrbitItem { Kind = OrbitItemKind.WebLink, Target = "https://example.com" };
        var before = item.IconKey;

        item.CustomIconColor = "#0090FF";

        Assert.Equal(before, item.IconKey);
    }

    [Fact]
    public void Changing_the_colour_notifies_once_and_the_same_value_is_silent()
    {
        var item = new OrbitItem();
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.CustomIconColor = "#3E63DD";
        Assert.Equal(new[] { nameof(OrbitItem.CustomIconColor) }, changed);

        changed.Clear();
        item.CustomIconColor = "#3E63DD";
        Assert.Empty(changed);
    }

    [Fact]
    public void The_colour_survives_an_export_and_import()
    {
        var config = new AppConfig();
        config.Orbits.Add(new OrbitProfile
        {
            Name = "Home",
            Items =
            {
                new OrbitItem
                {
                    Kind = OrbitItemKind.Group,
                    DisplayName = "Work",
                    CustomGlyph = "\uE838",
                    CustomIconColor = "#8E4EC6",
                },
                new OrbitItem { Kind = OrbitItemKind.Folder, DisplayName = "Plain", Target = @"C:\Temp" },
            },
        });
        var path = Path.Combine(_directory, "backup.json");

        Assert.True(OrbitStore.ExportTo(config, path));
        var restored = OrbitStore.ImportFrom(path);

        Assert.NotNull(restored);
        var items = Assert.Single(restored!.Orbits).Items;
        Assert.Equal("#8E4EC6", items[0].CustomIconColor);
        Assert.Null(items[1].CustomIconColor);
    }

    /// <summary>A config written before this field existed has no colour, and still loads.</summary>
    [Fact]
    public void A_config_without_the_field_loads_with_no_colour()
    {
        var path = Path.Combine(_directory, "old.json");
        File.WriteAllText(path, """
            { "Orbits": [ { "Name": "Home", "Items": [
                { "Kind": "Group", "DisplayName": "Work" } ] } ] }
            """);

        var restored = OrbitStore.ImportFrom(path);

        Assert.NotNull(restored);
        Assert.Null(Assert.Single(Assert.Single(restored!.Orbits).Items).CustomIconColor);
    }
}
