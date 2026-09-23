using Microsoft.UI;
using SwiftX.Models;
using SwiftX.Services;
using Windows.UI;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// The pure rules behind the orbit's hover styles: which colour Tint paints and what is drawn on it,
/// which style is really in force, and how big an item settles under the pointer.
/// <para>
/// None of these need a window. What they guard is the kind of mistake that only shows up by hovering
/// the right item, in the right theme, with the right colour picked: a glyph that vanishes on a pale
/// tint, a Zoom In that outgrows the room the layout reserved, a Tint that leaves the pointer with no
/// visible effect at all under High Contrast.
/// </para>
/// </summary>
public class OrbitHoverTests
{
    // ---- Tint colour: parsing and fallback -------------------------------------------------------

    [Fact]
    public void Tint_defaults_to_black_when_nothing_is_stored()
    {
        Assert.Equal(Colors.Black, OrbitHover.TintColor(new AppConfig()));
    }

    [Theory]
    [InlineData("#FF8800", 0xFF, 0x88, 0x00)]
    [InlineData("#ff8800", 0xFF, 0x88, 0x00)]
    [InlineData("#0000FF", 0x00, 0x00, 0xFF)]
    [InlineData("#FFFFFF", 0xFF, 0xFF, 0xFF)]
    public void A_stored_colour_is_what_tint_paints(string hex, byte r, byte g, byte b)
    {
        var tint = OrbitHover.TintColor(new AppConfig { HoverTintColorHex = hex });

        Assert.Equal(Color.FromArgb(0xFF, r, g, b), tint);
    }

    /// <summary>
    /// A value that does not parse must fall back to black instead of throwing out of a hover, the
    /// same way an unparsable accent does. Every one of these is something a hand-edited config could
    /// plausibly hold.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("red")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    [InlineData("#12345678")]
    public void A_stored_colour_that_does_not_parse_falls_back_to_black(string? hex)
    {
        var tint = OrbitHover.TintColor(new AppConfig { HoverTintColorHex = hex });

        Assert.Equal(Colors.Black, tint);
    }

    /// <summary>
    /// The picker writes with <see cref="AccentPalette.ToHex"/>, and Tint reads with the accent's
    /// own parser, so whatever the picker stores has to come back as the colour that was picked.
    /// </summary>
    [Fact]
    public void A_colour_written_by_the_picker_reads_back_unchanged()
    {
        var picked = Color.FromArgb(0xFF, 0x12, 0xAB, 0xEF);
        var config = new AppConfig { HoverTintColorHex = AccentPalette.ToHex(picked) };

        Assert.Equal("#12ABEF", config.HoverTintColorHex);
        Assert.Equal(picked, OrbitHover.TintColor(config));
    }

    // ---- Glyph colour on the tint ----------------------------------------------------------------

    [Theory]
    [InlineData(0x00, 0x00, 0x00, true)]   // black: the default, so white glyphs
    [InlineData(0x00, 0x00, 0x80, true)]   // navy
    [InlineData(0x8A, 0x67, 0xFF, false)]  // the dark-theme accent violet: black reads better on it
    [InlineData(0xFF, 0xFF, 0x00, false)]  // yellow
    [InlineData(0xFF, 0xFF, 0xFF, false)]  // white
    [InlineData(0x00, 0xFF, 0x00, false)]  // pure green: needs black, though its channel average is 85
    [InlineData(0x00, 0x00, 0xFF, true)]   // pure blue: needs white, with the same average
    public void The_glyph_is_white_on_dark_tints_and_black_on_light_ones(
        byte r, byte g, byte b, bool expectWhite)
    {
        var glyph = OrbitHover.OnTint(Color.FromArgb(0xFF, r, g, b));

        Assert.Equal(expectWhite ? Colors.White : Colors.Black, glyph);
    }

    /// <summary>
    /// The property that matters, over the whole gamut rather than a handful of picks: whichever of
    /// black and white is chosen, the glyph is readable. The worst colour for a black-or-white
    /// choice is a mid grey, where the better of the two still comes out near 4.58:1, so 4.5:1 (WCAG
    /// AA for body text) holds for every possible tint. A rule that fell short of it anywhere would
    /// be a colour the picker offers that hides the glyph.
    /// </summary>
    [Fact]
    public void The_glyph_is_legible_on_any_tint()
    {
        for (int r = 0; r <= 255; r += 15)
        for (int g = 0; g <= 255; g += 15)
        for (int b = 0; b <= 255; b += 15)
        {
            var tint = Color.FromArgb(0xFF, (byte)r, (byte)g, (byte)b);
            double contrast = AccentPalette.ContrastRatio(OrbitHover.OnTint(tint), tint);

            Assert.True(contrast >= 4.5, $"#{r:X2}{g:X2}{b:X2} gets only {contrast:F2}:1.");
        }
    }

    // ---- Which style is in force ------------------------------------------------------------------

    /// <summary>
    /// Under High Contrast the chosen colour is not applied, but Tint changes nothing else, so
    /// leaving it as Tint would leave the pointer with no effect at all. It has to become a style
    /// that still says "this one".
    /// </summary>
    [Fact]
    public void Tint_becomes_invert_under_high_contrast()
    {
        Assert.Equal(OrbitHoverStyle.Invert, OrbitHover.Effective(OrbitHoverStyle.Tint, highContrast: true));
    }

    [Theory]
    [InlineData(OrbitHoverStyle.Invert, false)]
    [InlineData(OrbitHoverStyle.Zoom, false)]
    [InlineData(OrbitHoverStyle.Tint, false)]
    [InlineData(OrbitHoverStyle.Invert, true)]
    [InlineData(OrbitHoverStyle.Zoom, true)]
    [InlineData(OrbitHoverStyle.Animate, false)]
    [InlineData(OrbitHoverStyle.Animate, true)]
    public void Every_other_combination_is_left_alone(OrbitHoverStyle style, bool highContrast)
    {
        Assert.Equal(style, OrbitHover.Effective(style, highContrast));
    }

    /// <summary>
    /// Animate repeats for as long as the pointer rests on an item, which is what "no animations" in
    /// Windows asks not to happen, and it changes nothing else, so leaving it as Animate would leave
    /// the pointer with no cue at all. Zoom is one short grow that settles.
    /// </summary>
    [Fact]
    public void Animate_becomes_zoom_when_windows_animations_are_off()
    {
        Assert.Equal(
            OrbitHoverStyle.Zoom,
            OrbitHover.Effective(OrbitHoverStyle.Animate, highContrast: false, reducedMotion: true));
    }

    /// <summary>Reduced motion only has anything to say about Animate; the other styles are already still.</summary>
    [Theory]
    [InlineData(OrbitHoverStyle.Invert)]
    [InlineData(OrbitHoverStyle.Zoom)]
    [InlineData(OrbitHoverStyle.Tint)]
    public void Reduced_motion_leaves_the_other_styles_alone(OrbitHoverStyle style)
    {
        Assert.Equal(style, OrbitHover.Effective(style, highContrast: false, reducedMotion: true));
    }

    /// <summary>
    /// High Contrast is about colour and reduced motion is about movement, so neither may answer for
    /// the other: Animate has no colour to override, and Tint has no movement to drop.
    /// </summary>
    [Fact]
    public void High_contrast_and_reduced_motion_each_answer_only_for_their_own_style()
    {
        Assert.Equal(
            OrbitHoverStyle.Animate,
            OrbitHover.Effective(OrbitHoverStyle.Animate, highContrast: true, reducedMotion: false));
        Assert.Equal(
            OrbitHoverStyle.Invert,
            OrbitHover.Effective(OrbitHoverStyle.Tint, highContrast: true, reducedMotion: true));
    }

    // ---- Size under the pointer -------------------------------------------------------------------

    [Fact]
    public void Zoom_in_grows_the_item_and_zoom_out_shrinks_it()
    {
        double zoomIn = OrbitMetrics.HoverScaleFor(OrbitHoverStyle.Zoom, HoverZoomDirection.In);
        double zoomOut = OrbitMetrics.HoverScaleFor(OrbitHoverStyle.Zoom, HoverZoomDirection.Out);

        Assert.True(zoomIn > 1, $"Zoom In settles at {zoomIn}.");
        Assert.True(zoomOut < 1, $"Zoom Out settles at {zoomOut}.");
        Assert.Equal(OrbitMetrics.ZoomOutHoverScale, zoomOut);
    }

    /// <summary>
    /// The layout budgets <see cref="OrbitMetrics.HoverScale"/> of growth into the orbit's radius and
    /// the flyout's clearance. A style that grew an item further would overlap its neighbours, so
    /// nothing may exceed it.
    /// </summary>
    [Theory]
    [InlineData(OrbitHoverStyle.Invert, HoverZoomDirection.In)]
    [InlineData(OrbitHoverStyle.Invert, HoverZoomDirection.Out)]
    [InlineData(OrbitHoverStyle.Zoom, HoverZoomDirection.In)]
    [InlineData(OrbitHoverStyle.Zoom, HoverZoomDirection.Out)]
    [InlineData(OrbitHoverStyle.Tint, HoverZoomDirection.In)]
    [InlineData(OrbitHoverStyle.Tint, HoverZoomDirection.Out)]
    [InlineData(OrbitHoverStyle.Animate, HoverZoomDirection.In)]
    [InlineData(OrbitHoverStyle.Animate, HoverZoomDirection.Out)]
    public void No_style_grows_an_item_past_the_layouts_budget(
        OrbitHoverStyle style, HoverZoomDirection direction)
    {
        Assert.True(OrbitMetrics.HoverScaleFor(style, direction) <= OrbitMetrics.HoverScale);
    }

    /// <summary>Zoom In grows by exactly what the layout reserved, the same as Invert always has.</summary>
    [Fact]
    public void Zoom_in_uses_the_whole_of_the_layouts_budget()
    {
        Assert.Equal(
            OrbitMetrics.HoverScale,
            OrbitMetrics.HoverScaleFor(OrbitHoverStyle.Zoom, HoverZoomDirection.In));
    }

    /// <summary>
    /// Invert ignores the direction: it is Zoom's setting, and a stale Out left over from an earlier
    /// Zoom must not turn Invert's grow into a shrink.
    /// </summary>
    [Theory]
    [InlineData(HoverZoomDirection.In)]
    [InlineData(HoverZoomDirection.Out)]
    public void Invert_grows_whatever_the_zoom_direction_says(HoverZoomDirection direction)
    {
        Assert.Equal(OrbitMetrics.HoverScale, OrbitMetrics.HoverScaleFor(OrbitHoverStyle.Invert, direction));
    }

    /// <summary>
    /// Tint's contract is that the size does not change. That includes a group whose flyout is open,
    /// which rests slightly enlarged: hovering it must leave it there instead of snapping it to 1.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.08)]
    public void Tint_leaves_the_size_where_it_rests(double restScale)
    {
        Assert.Equal(
            restScale,
            OrbitMetrics.HoverScaleFor(OrbitHoverStyle.Tint, HoverZoomDirection.In, restScale));
        Assert.Equal(
            restScale,
            OrbitMetrics.HoverScaleFor(OrbitHoverStyle.Tint, HoverZoomDirection.Out, restScale));
    }

    /// <summary>
    /// Animate settles at the size the item rests at and lets its own motion do the moving: a pulse
    /// swells about that size, so a hover-scale on top of it would fight the animation for the same
    /// property.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.08)]
    public void Animate_leaves_the_size_where_it_rests(double restScale)
    {
        Assert.Equal(
            restScale,
            OrbitMetrics.HoverScaleFor(OrbitHoverStyle.Animate, HoverZoomDirection.In, restScale));
        Assert.Equal(
            restScale,
            OrbitMetrics.HoverScaleFor(OrbitHoverStyle.Animate, HoverZoomDirection.Out, restScale));
    }
}
