using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// Every enum a configuration file carries, read back with a name this build does not know.
/// <para>
/// <b>What is being pinned is not the fallback value — it is the orbit.</b> Each test below stores
/// an unrecognised word in one setting and then asserts that the user's items are still there.
/// That is the whole failure this exists to prevent: <c>OrbitStore.Load</c> answers any exception
/// from deserializing by handing back a blank <see cref="AppConfig"/>, and the app's next save
/// writes that blank over the real file. So a throwing enum converter does not degrade one
/// setting, it destroys the document — silently, with no backup and no undo.
/// </para>
/// <para>
/// The guard used to be an opt-in <c>[JsonConverter]</c> on three hover properties, which left
/// <c>Theme</c>, <c>Language</c>, <c>FrostedGlass</c>, <c>HotCorner</c>, <c>HotCornerLayout</c>,
/// <c>ItemSize</c>, <c>ItemShape</c> and <see cref="OrbitItem.Kind"/> reading through the throwing
/// built-in converter — eight open doors behind three locked ones. These are the eight, and they
/// are written one per setting rather than as a single theory so that a regression names the
/// property it broke.
/// </para>
/// <para>
/// This is not a hypothetical. The names are reachable by hand-editing, by a config written by a
/// newer SwiftX and opened by an older one after a downgrade or a roaming-profile sync, and by any
/// future release that adds a member to one of these enums — which is exactly what a 2.0 is for.
/// </para>
/// </summary>
[Collection(SharedDataDirectoryCollection.Name)]
public class TolerantEnumConfigTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "swiftx-tolerant-enum-tests", Guid.NewGuid().ToString("N"));

    public TolerantEnumConfigTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch { /* ignore */ }
    }

    /// <summary>
    /// A configuration carrying one real orbit item plus whatever <paramref name="settings"/>
    /// names, written to disk and read back through the real import path — converters, legacy
    /// upgrade pass and all.
    /// <para>
    /// Returns null the way the product does when the file is not usable, so a test can assert on
    /// "the orbit came back" rather than on an exception type.
    /// </para>
    /// </summary>
    private AppConfig? Read(string settings)
    {
        var json = $$"""
        {
          {{settings}}
          "Orbits": [
            {
              "Id": "home",
              "Name": "Home",
              "Items": [
                { "Id": "a1", "Kind": "Application", "DisplayName": "Notepad", "Target": "C:\\Windows\\notepad.exe" }
              ]
            }
          ]
        }
        """;

        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return OrbitStore.ImportFrom(path);
    }

    /// <summary>Reads <paramref name="settings"/> and asserts the orbit survived, which is the
    /// property every test here is really about.</summary>
    private AppConfig ReadAndKeepTheOrbit(string settings)
    {
        var config = Read(settings);
        Assert.NotNull(config);
        Assert.Equal("Notepad", Assert.Single(config!.Primary.Items).DisplayName);
        return config;
    }

    // ---- The eight that had no guard ---------------------------------------------------------

    [Fact]
    public void An_unknown_theme_falls_back_to_dark_and_keeps_the_orbit()
    {
        // Dark, not Light: Light is merely AppTheme's first member, while Dark is what the
        // property defaults to and therefore what the user's window was. See
        // TolerantAppThemeConverter.
        var config = ReadAndKeepTheOrbit("\"Theme\": \"Solarized\",");
        Assert.Equal(AppTheme.Dark, config.Theme);
    }

    [Fact]
    public void An_unknown_item_size_falls_back_to_medium_and_keeps_the_orbit()
    {
        // Medium for the same reason Theme falls back to Dark — OrbitItemSize leads with Small.
        var config = ReadAndKeepTheOrbit("\"ItemSize\": \"Enormous\",");
        Assert.Equal(OrbitItemSize.Medium, config.ItemSize);
    }

    [Fact]
    public void An_unknown_language_falls_back_to_system_and_keeps_the_orbit()
    {
        var config = ReadAndKeepTheOrbit("\"Language\": \"Esperanto\",");
        Assert.Equal(AppLanguage.System, config.Language);
    }

    [Fact]
    public void An_unknown_frosted_glass_level_falls_back_and_keeps_the_orbit()
    {
        var config = ReadAndKeepTheOrbit("\"FrostedGlass\": \"Opaque\",");
        Assert.Equal(FrostedGlassLevel.Light, config.FrostedGlass);
    }

    [Fact]
    public void An_unknown_hot_corner_falls_back_to_top_left_and_keeps_the_orbit()
    {
        var config = ReadAndKeepTheOrbit("\"HotCorner\": \"Middle\",");
        Assert.Equal(HotCorner.TopLeft, config.HotCorner);
    }

    [Fact]
    public void An_unknown_hot_corner_layout_falls_back_to_orbit_and_keeps_the_orbit()
    {
        var config = ReadAndKeepTheOrbit("\"HotCornerLayout\": \"Hexagon\",");
        Assert.Equal(HotCornerLayout.Orbit, config.HotCornerLayout);
    }

    [Fact]
    public void An_unknown_item_shape_falls_back_to_circle_and_keeps_the_orbit()
    {
        var config = ReadAndKeepTheOrbit("\"ItemShape\": \"Trapezoid\",");
        Assert.Equal(OrbitItemShape.Circle, config.ItemShape);
    }

    [Fact]
    public void An_unknown_arrangement_falls_back_to_ring_and_keeps_the_orbit()
    {
        var config = ReadAndKeepTheOrbit("\"Arrangement\": \"Spiral\",");
        Assert.Equal(OrbitArrangement.Ring, config.Arrangement);
    }

    [Fact]
    public void A_hex_nest_arrangement_is_read_back_by_name()
    {
        var config = ReadAndKeepTheOrbit("\"Arrangement\": \"HexNest\",");
        Assert.Equal(OrbitArrangement.HexNest, config.Arrangement);
    }

    /// <summary>
    /// The one nested inside the item list rather than beside it, so it also pins that the
    /// tolerance reaches through the object graph and not merely across the top-level properties.
    /// </summary>
    [Fact]
    public void An_unknown_item_kind_falls_back_to_application_and_keeps_the_item()
    {
        var json = """
        {
          "Orbits": [
            {
              "Id": "home",
              "Name": "Home",
              "Items": [
                { "Id": "a1", "Kind": "Hologram", "DisplayName": "Notepad", "Target": "C:\\Windows\\notepad.exe" }
              ]
            }
          ]
        }
        """;
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);

        var config = OrbitStore.ImportFrom(path);

        Assert.NotNull(config);
        var item = Assert.Single(config!.Primary.Items);
        Assert.Equal("Notepad", item.DisplayName);
        Assert.Equal(OrbitItemKind.Application, item.Kind);
    }

    // ---- The properties around the bad value are untouched ------------------------------------

    /// <summary>
    /// The half that a "falls back to the default" assertion on its own would not catch: the rest
    /// of the file has to come through as written. A converter that swallowed the whole object, or
    /// a reader left mid-token inside it, would also report the default for the bad property while
    /// quietly zeroing everything after it.
    /// </summary>
    [Fact]
    public void Settings_stored_beside_an_unknown_name_are_read_normally()
    {
        var config = ReadAndKeepTheOrbit("""
            "Theme": "Solarized",
            "ItemShape": "Squircle",
            "HotCorner": "BottomRight",
            "AccentColorHex": "#FF8800",
            "HotkeyEnabled": false,
            "OrbitRadius": 260.0,
            """);

        Assert.Equal(AppTheme.Dark, config.Theme);
        Assert.Equal(OrbitItemShape.Squircle, config.ItemShape);
        Assert.Equal(HotCorner.BottomRight, config.HotCorner);
        Assert.Equal("#FF8800", config.AccentColorHex);
        Assert.False(config.HotkeyEnabled);
        Assert.Equal(260.0, config.OrbitRadius);
    }

    /// <summary>
    /// Several unknown names at once — the realistic shape of a downgrade, where a whole release's
    /// worth of new members is unreadable rather than a single hand-edited word.
    /// </summary>
    [Fact]
    public void A_config_from_a_newer_build_degrades_setting_by_setting_rather_than_all_at_once()
    {
        var config = ReadAndKeepTheOrbit("""
            "Theme": "Aurora",
            "FrostedGlass": "Frostier",
            "Language": "Klingon",
            "HotCorner": "Centre",
            "HotCornerLayout": "Spiral",
            "ItemSize": "Colossal",
            "ItemShape": "Octagon",
            "HoverStyle": "Ripple",
            "HoverAnimation": "Cartwheel",
            "HoverZoomDirection": "Sideways",
            """);

        Assert.Equal(AppTheme.Dark, config.Theme);
        Assert.Equal(FrostedGlassLevel.Light, config.FrostedGlass);
        Assert.Equal(AppLanguage.System, config.Language);
        Assert.Equal(HotCorner.TopLeft, config.HotCorner);
        Assert.Equal(HotCornerLayout.Orbit, config.HotCornerLayout);
        Assert.Equal(OrbitItemSize.Medium, config.ItemSize);
        Assert.Equal(OrbitItemShape.Circle, config.ItemShape);
        Assert.Equal(OrbitHoverStyle.Invert, config.HoverStyle);
        Assert.Equal(HoverAnimation.Pulse, config.HoverAnimation);
        Assert.Equal(HoverZoomDirection.In, config.HoverZoomDirection);
    }

    // ---- Round-trip -------------------------------------------------------------------------

    /// <summary>
    /// Tolerance on the way in must not change what goes out: the on-disk form is still the member
    /// name, so a config this build writes stays readable by anything that reads the documented
    /// format.
    /// </summary>
    [Fact]
    public void Known_names_still_round_trip_by_name()
    {
        var written = new AppConfig
        {
            Theme = AppTheme.FrostedGlass,
            FrostedGlass = FrostedGlassLevel.Dark,
            Language = AppLanguage.Japanese,
            HotCorner = HotCorner.BottomLeft,
            HotCornerLayout = HotCornerLayout.Quadrant,
            ItemSize = OrbitItemSize.Large,
            ItemShape = OrbitItemShape.Hexagon,
        }.EnsurePrimary();
        written.Primary.Items.Add(new OrbitItem
        {
            Kind = OrbitItemKind.WebLink,
            DisplayName = "Docs",
            Target = "https://example.com",
        });

        var path = Path.Combine(_directory, "roundtrip.json");
        Assert.True(OrbitStore.ExportTo(written, path));

        // The names, not the numbers: a converter that started writing ordinals would round-trip
        // through itself perfectly and strand every file any other build had written.
        var text = File.ReadAllText(path);
        Assert.Contains("\"FrostedGlass\"", text);
        Assert.Contains("\"Japanese\"", text);
        Assert.Contains("\"Quadrant\"", text);
        Assert.Contains("\"Hexagon\"", text);
        Assert.Contains("\"WebLink\"", text);

        var read = OrbitStore.ImportFrom(path);

        Assert.NotNull(read);
        Assert.Equal(AppTheme.FrostedGlass, read!.Theme);
        Assert.Equal(FrostedGlassLevel.Dark, read.FrostedGlass);
        Assert.Equal(AppLanguage.Japanese, read.Language);
        Assert.Equal(HotCorner.BottomLeft, read.HotCorner);
        Assert.Equal(HotCornerLayout.Quadrant, read.HotCornerLayout);
        Assert.Equal(OrbitItemSize.Large, read.ItemSize);
        Assert.Equal(OrbitItemShape.Hexagon, read.ItemShape);
        Assert.Equal(OrbitItemKind.WebLink, Assert.Single(read.Primary.Items).Kind);
    }

    /// <summary>
    /// Case-insensitivity is inherited from the built-in converter's behaviour and worth keeping:
    /// a hand-edited file is the main way an odd name arrives, and the difference between "dark"
    /// and "Dark" should not be one of them.
    /// </summary>
    [Fact]
    public void A_known_name_in_the_wrong_case_is_still_recognised()
    {
        var config = ReadAndKeepTheOrbit("\"Theme\": \"light\", \"ItemShape\": \"SQUARE\",");

        Assert.Equal(AppTheme.Light, config.Theme);
        Assert.Equal(OrbitItemShape.Square, config.ItemShape);
    }

    /// <summary>
    /// A value of the wrong JSON <em>type</em>, not merely the wrong name. The converter has to
    /// consume a container whole or the reader is left inside it and the caller's own bookkeeping
    /// throws — which would land right back in the blank-configuration path this all exists to
    /// avoid.
    /// </summary>
    [Theory]
    [InlineData("\"Theme\": {\"nested\": [1, 2, {\"deeper\": true}]},")]
    [InlineData("\"Theme\": [\"Dark\", \"Light\"],")]
    [InlineData("\"Theme\": 7,")]
    [InlineData("\"Theme\": null,")]
    [InlineData("\"Theme\": true,")]
    public void A_value_of_the_wrong_shape_falls_back_without_derailing_the_read(string settings)
    {
        var config = ReadAndKeepTheOrbit(settings);
        Assert.Equal(AppTheme.Dark, config.Theme);
    }
}
