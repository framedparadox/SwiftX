using System.Text.Json;
using System.Text.Json.Serialization;
using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// The ring-to-orbit rename crossed the persistence boundary, so an upgrading user's whole dock
/// rides on this mapping. These are data-loss regression tests, not shape tests: each one asserts
/// that something a real pre-rename config carried still arrives intact on the other side.
/// </summary>
public class LegacyConfigFormatTests
{
    /// <summary>The same options <see cref="OrbitStore"/> reads configurations with — the enum
    /// converter in particular, since a stale <c>"Ring"</c> layout value is one of the two things
    /// the upgrade exists to fix and it only misbehaves through that converter.</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static AppConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<AppConfig>(LegacyConfigFormat.Upgrade(json), Options)!;

    /// <summary>A configuration as a pre-rename build actually wrote one, trimmed to the four
    /// names that changed.</summary>
    private const string LegacyJson = """
        {
          "Theme": "Light",
          "HotCornerLayout": "Ring",
          "RingRadius": 168.5,
          "HotCornerRing": [
            { "Kind": "Folder", "DisplayName": "Downloads", "Target": "C:\\Users\\x\\Downloads" }
          ],
          "Rings": [
            {
              "Name": "Home",
              "Items": [
                { "Kind": "Application", "DisplayName": "Notepad", "Target": "C:\\Windows\\System32\\notepad.exe" },
                { "Kind": "WebLink", "DisplayName": "GitHub", "Target": "https://github.com" }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void A_pre_rename_configuration_keeps_every_orbit_item_it_had()
    {
        var config = Deserialize(LegacyJson);

        var items = Assert.Single(config.Orbits).Items;
        Assert.Equal(2, items.Count);
        Assert.Equal("Notepad", items[0].DisplayName);
        Assert.Equal(OrbitItemKind.WebLink, items[1].Kind);
        // Asserted explicitly because every target in a real configuration is a Windows path, and
        // a backslash is the one character in them that JSON also spells with a backslash. An
        // escaping mistake does not surface as a wrong path here — Upgrade cannot parse the file
        // at all, hands it straight back, and the failure lands somewhere else entirely as an
        // unrelated-looking deserialize error. That is exactly how this fixture was wrong once.
        Assert.Equal(@"C:\Windows\System32\notepad.exe", items[0].Target);
    }

    [Fact]
    public void The_radius_and_corner_arrangement_survive_their_rename()
    {
        var config = Deserialize(LegacyJson);

        Assert.Equal(168.5, config.OrbitRadius);
        Assert.Equal("Downloads", Assert.Single(config.HotCornerOrbit).DisplayName);
    }

    /// <summary>
    /// The one that would not merely lose data but throw: <c>JsonStringEnumConverter</c> has no
    /// member called <c>Ring</c> any more, and <see cref="OrbitStore.Load"/> answers a throw by
    /// falling back to a blank configuration — i.e. the user's entire dock, not just this one
    /// field.
    /// </summary>
    [Fact]
    public void The_retired_Ring_layout_value_becomes_Orbit_rather_than_throwing()
    {
        var config = Deserialize(LegacyJson);

        Assert.Equal(HotCornerLayout.Orbit, config.HotCornerLayout);
    }

    [Fact]
    public void Quadrant_the_layout_member_that_did_not_change_is_left_alone()
    {
        var config = Deserialize("""{ "HotCornerLayout": "Quadrant" }""");

        Assert.Equal(HotCornerLayout.Quadrant, config.HotCornerLayout);
    }

    /// <summary>Today's configurations are the overwhelmingly common input; upgrading one must be
    /// a no-op, byte for byte, rather than a reserialization that quietly reformats it.</summary>
    [Fact]
    public void A_current_configuration_is_handed_back_untouched()
    {
        const string current = """{ "Theme": "Dark", "Orbits": [], "OrbitRadius": 200 }""";

        Assert.Same(current, LegacyConfigFormat.Upgrade(current));
    }

    /// <summary>
    /// A hand-edited file carrying both spellings must not let the older one win — otherwise
    /// editing a config by hand after an upgrade could silently roll it back.
    /// </summary>
    [Fact]
    public void The_current_name_wins_when_a_file_somehow_carries_both()
    {
        var config = Deserialize("""
            {
              "Rings":  [ { "Name": "Stale", "Items": [] } ],
              "Orbits": [ { "Name": "Live",  "Items": [] } ]
            }
            """);

        Assert.Equal("Live", Assert.Single(config.Orbits).Name);
    }

    /// <summary>
    /// Diagnosing a bad configuration is <see cref="OrbitStore"/>'s job, so this pass has to hand
    /// malformed input straight back rather than throwing out of it — a throw here would be
    /// reported as a failed upgrade instead of as the malformed file it actually is.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{ \"Rings\": [ ")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"a bare string\"")]
    [InlineData("null")]
    public void Anything_that_is_not_a_configuration_object_is_returned_unchanged(string json)
    {
        Assert.Same(json, LegacyConfigFormat.Upgrade(json));
    }
}
