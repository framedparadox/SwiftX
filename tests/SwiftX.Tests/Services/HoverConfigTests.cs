using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// The hover settings as they cross the persistence boundary: defaults, copy, the on-disk form, and
/// what a config that is old, newer than this build, or hand-edited does to them.
/// <para>
/// The last of those is the reason <see cref="TolerantEnumConverter{T}"/> exists. The built-in
/// <c>JsonStringEnumConverter</c> it replaced throws on a name it does not know, and
/// <c>OrbitStore.Load</c> answers a throw by starting from a blank configuration, so an
/// unrecognised hover word used to be able to cost a user their whole orbit. Every "falls back"
/// test below therefore also checks that a setting stored <em>alongside</em> the bad one survived,
/// which is the actual point. <see cref="TolerantEnumConfigTests"/> covers the same property for
/// the rest of the config's enums, which used to have no such guard at all.
/// </para>
/// </summary>
[Collection(SharedDataDirectoryCollection.Name)]
public class HoverConfigTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "swiftx-hover-tests", Guid.NewGuid().ToString("N"));

    public HoverConfigTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch { /* ignore */ }
    }

    /// <summary>Writes <paramref name="json"/> to disk and imports it: the real read path, converters and all.</summary>
    private AppConfig Import(string json)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        var config = OrbitStore.ImportFrom(path);
        Assert.NotNull(config);
        return config!;
    }

    private static AppConfig WithOrbit(AppConfig config)
    {
        config.Orbits.Add(new OrbitProfile { Name = "Home" });
        return config;
    }

    // ---- Defaults and copy -----------------------------------------------------------------------

    [Fact]
    public void A_new_config_hovers_as_invert_with_zoom_in_and_a_black_tint()
    {
        var config = new AppConfig();

        Assert.Equal(OrbitHoverStyle.Invert, config.HoverStyle);
        Assert.Equal(HoverZoomDirection.In, config.HoverZoomDirection);
        Assert.Null(config.HoverTintColorHex);
    }

    /// <summary>
    /// The converter falls back to <c>default(T)</c>, so each enum's first member has to be the
    /// setting's default. This is what breaks if someone reorders either enum.
    /// </summary>
    [Fact]
    public void Each_settings_default_is_its_enums_first_member()
    {
        var config = new AppConfig();

        Assert.Equal(default(OrbitHoverStyle), config.HoverStyle);
        Assert.Equal(default(HoverZoomDirection), config.HoverZoomDirection);
        Assert.Equal(default(HoverAnimation), config.HoverAnimation);
    }

    [Fact]
    public void A_new_config_animates_with_a_pulse()
    {
        Assert.Equal(HoverAnimation.Pulse, new AppConfig().HoverAnimation);
    }

    [Fact]
    public void CopyAppSettingsFrom_carries_the_hover_animation()
    {
        var target = new AppConfig();

        target.CopyAppSettingsFrom(new AppConfig
        {
            HoverStyle = OrbitHoverStyle.Animate,
            HoverAnimation = HoverAnimation.Wobble,
        });

        Assert.Equal(OrbitHoverStyle.Animate, target.HoverStyle);
        Assert.Equal(HoverAnimation.Wobble, target.HoverAnimation);
    }

    [Theory]
    [InlineData(HoverAnimation.Pulse)]
    [InlineData(HoverAnimation.Bounce)]
    [InlineData(HoverAnimation.Wobble)]
    [InlineData(HoverAnimation.Shake)]
    [InlineData(HoverAnimation.Spin)]
    [InlineData(HoverAnimation.Float)]
    public void The_animate_settings_survive_an_export_and_import(HoverAnimation animation)
    {
        var path = Path.Combine(_directory, "animate.json");
        var original = WithOrbit(new AppConfig
        {
            HoverStyle = OrbitHoverStyle.Animate,
            HoverAnimation = animation,
        });

        Assert.True(OrbitStore.ExportTo(original, path));
        var restored = OrbitStore.ImportFrom(path);

        Assert.NotNull(restored);
        Assert.Equal(OrbitHoverStyle.Animate, restored!.HoverStyle);
        Assert.Equal(animation, restored.HoverAnimation);
        Assert.Contains($"\"HoverAnimation\": \"{animation}\"", File.ReadAllText(path));
    }

    /// <summary>
    /// A config that names an animation this build does not know (one added by a newer build, say)
    /// reads as a pulse instead of costing the user the rest of the file.
    /// </summary>
    [Theory]
    [InlineData("\"Jiggle\"")]
    [InlineData("\"\"")]
    [InlineData("9")]
    [InlineData("null")]
    [InlineData("{ \"nested\": [1, 2] }")]
    public void An_unknown_hover_animation_reads_as_a_pulse_and_costs_nothing_else(string value)
    {
        var config = Import($$"""
            { "HoverAnimation": {{value}}, "HoverStyle": "Animate", "Theme": "Light", "Orbits": [ { "Name": "Home" } ] }
            """);

        Assert.Equal(HoverAnimation.Pulse, config.HoverAnimation);
        Assert.Equal(OrbitHoverStyle.Animate, config.HoverStyle);
        Assert.Equal(AppTheme.Light, config.Theme);
        Assert.Single(config.Orbits);
    }

    /// <summary>A config from before the animation existed carries no such field.</summary>
    [Fact]
    public void A_config_from_before_the_animation_existed_reads_it_as_a_pulse()
    {
        var config = Import("""
            { "HoverStyle": "Tint", "Orbits": [ { "Name": "Home" } ] }
            """);

        Assert.Equal(HoverAnimation.Pulse, config.HoverAnimation);
        Assert.Equal(OrbitHoverStyle.Tint, config.HoverStyle);
    }

    [Fact]
    public void CopyAppSettingsFrom_carries_every_hover_field()
    {
        var source = new AppConfig
        {
            HoverStyle = OrbitHoverStyle.Tint,
            HoverZoomDirection = HoverZoomDirection.Out,
            HoverTintColorHex = "#FF8800",
        };
        var target = new AppConfig();

        target.CopyAppSettingsFrom(source);

        Assert.Equal(OrbitHoverStyle.Tint, target.HoverStyle);
        Assert.Equal(HoverZoomDirection.Out, target.HoverZoomDirection);
        Assert.Equal("#FF8800", target.HoverTintColorHex);
    }

    /// <summary>Copying an unset tint clears the target's, as Reset relies on it doing.</summary>
    [Fact]
    public void CopyAppSettingsFrom_carries_an_unset_tint_too()
    {
        var target = new AppConfig { HoverTintColorHex = "#FF8800" };

        target.CopyAppSettingsFrom(new AppConfig());

        Assert.Null(target.HoverTintColorHex);
    }

    // ---- Round trip and on-disk form ---------------------------------------------------------------

    [Theory]
    [InlineData(OrbitHoverStyle.Invert, HoverZoomDirection.In, null)]
    [InlineData(OrbitHoverStyle.Zoom, HoverZoomDirection.Out, null)]
    [InlineData(OrbitHoverStyle.Tint, HoverZoomDirection.In, "#FF8800")]
    [InlineData(OrbitHoverStyle.Tint, HoverZoomDirection.Out, "#000000")]
    public void The_hover_settings_survive_an_export_and_import(
        OrbitHoverStyle style, HoverZoomDirection direction, string? tint)
    {
        var path = Path.Combine(_directory, "backup.json");
        var original = WithOrbit(new AppConfig
        {
            HoverStyle = style,
            HoverZoomDirection = direction,
            HoverTintColorHex = tint,
        });

        Assert.True(OrbitStore.ExportTo(original, path));
        var restored = OrbitStore.ImportFrom(path);

        Assert.NotNull(restored);
        Assert.Equal(style, restored!.HoverStyle);
        Assert.Equal(direction, restored.HoverZoomDirection);
        Assert.Equal(tint, restored.HoverTintColorHex);
    }

    /// <summary>
    /// By name, not by number: <see cref="OrbitHoverStyle.Zoom"/> in particular is already on users'
    /// disks under that word, and a number would silently change meaning the day a member is inserted.
    /// </summary>
    [Fact]
    public void The_hover_enums_are_written_by_name_and_an_unset_tint_is_omitted()
    {
        var path = Path.Combine(_directory, "shape.json");
        var config = WithOrbit(new AppConfig
        {
            HoverStyle = OrbitHoverStyle.Zoom,
            HoverZoomDirection = HoverZoomDirection.Out,
        });

        Assert.True(OrbitStore.ExportTo(config, path));
        var json = File.ReadAllText(path);

        Assert.Contains("\"HoverStyle\": \"Zoom\"", json);
        Assert.Contains("\"HoverZoomDirection\": \"Out\"", json);
        Assert.DoesNotContain("HoverTintColorHex", json);
    }

    // ---- Old, missing, unknown, hand-edited ---------------------------------------------------------

    /// <summary>
    /// A config written before the direction and the tint existed. Zoom is on disk under that name
    /// and has to keep meaning Zoom; the two new fields simply read as their defaults.
    /// </summary>
    [Fact]
    public void A_config_from_before_the_new_fields_reads_them_as_defaults()
    {
        var config = Import("""
            { "Theme": "Light", "HoverStyle": "Zoom", "Orbits": [ { "Name": "Home" } ] }
            """);

        Assert.Equal(OrbitHoverStyle.Zoom, config.HoverStyle);
        Assert.Equal(HoverZoomDirection.In, config.HoverZoomDirection);
        Assert.Null(config.HoverTintColorHex);
        Assert.Equal(AppTheme.Light, config.Theme);
    }

    [Theory]
    [InlineData("\"Wobble\"")]
    [InlineData("\"\"")]
    [InlineData("7")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{ \"nested\": [1, 2, { \"x\": null }] }")]
    [InlineData("[ \"Tint\" ]")]
    public void An_unknown_hover_style_reads_as_invert_and_costs_nothing_else(string value)
    {
        // Theme is written after the bad value so that a reader left in the wrong place by it would
        // lose that too, not just the hover setting.
        var config = Import($$"""
            { "HoverStyle": {{value}}, "Theme": "Light", "Orbits": [ { "Name": "Home" } ] }
            """);

        Assert.Equal(OrbitHoverStyle.Invert, config.HoverStyle);
        Assert.Equal(AppTheme.Light, config.Theme);
        Assert.Single(config.Orbits);
    }

    [Theory]
    [InlineData("\"Sideways\"")]
    [InlineData("\"\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("{ \"nested\": [1, 2] }")]
    public void An_unknown_zoom_direction_reads_as_in_and_costs_nothing_else(string value)
    {
        var config = Import($$"""
            { "HoverZoomDirection": {{value}}, "Theme": "Light", "Orbits": [ { "Name": "Home" } ] }
            """);

        Assert.Equal(HoverZoomDirection.In, config.HoverZoomDirection);
        Assert.Equal(AppTheme.Light, config.Theme);
        Assert.Single(config.Orbits);
    }

    [Theory]
    [InlineData("zoom", OrbitHoverStyle.Zoom)]
    [InlineData("TINT", OrbitHoverStyle.Tint)]
    public void Names_are_matched_without_regard_to_case(string name, OrbitHoverStyle expected)
    {
        var config = Import($$"""
            { "HoverStyle": "{{name}}", "Orbits": [ { "Name": "Home" } ] }
            """);

        Assert.Equal(expected, config.HoverStyle);
    }

    /// <summary>
    /// Only the parse can fail here, not the load: a tint that is not a colour reads as "unset" and
    /// resolves to black at the point of use, with the rest of the file intact.
    /// </summary>
    [Theory]
    [InlineData("#zzzzzz")]
    [InlineData("orange")]
    [InlineData("")]
    public void A_tint_that_is_not_a_colour_still_loads_and_resolves_to_black(string stored)
    {
        var config = Import($$"""
            { "HoverTintColorHex": "{{stored}}", "Theme": "Light", "Orbits": [ { "Name": "Home" } ] }
            """);

        Assert.Equal(AppTheme.Light, config.Theme);
        Assert.Equal(Microsoft.UI.Colors.Black, OrbitHover.TintColor(config));
    }
}
