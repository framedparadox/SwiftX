using SwiftX.Models;
using SwiftX.Services;
using Windows.UI;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// The rest fill of an orbit bubble, per shape. A hexagon cannot have live acrylic behind it (its
/// edge can only be rounded, not hexagonal), so it goes without and firms up its tint instead; every
/// other shape must keep the translucent theme tint that sits over the blur.
/// <para>
/// The shape lives in a static (<see cref="OrbitMetrics.SetItemShape"/>) because the orbit window has
/// no config reference of its own to read it from, so each test puts it back in a <c>finally</c> and
/// they share a collection to keep them from overlapping each other.
/// </para>
/// </summary>
[Collection("OrbitMetrics item shape")]
public class OrbitItemGeometryTests
{
    /// <summary>The dark theme's glass tint: black at 40%.</summary>
    private static readonly Color GlassTint = Color.FromArgb(0x66, 0x00, 0x00, 0x00);

    private static Color RestTintUnder(OrbitItemShape shape, Color tint)
    {
        var before = OrbitMetrics.ItemShape;
        try
        {
            OrbitMetrics.SetItemShape(shape);
            return OrbitItemGeometry.RestTint(tint);
        }
        finally
        {
            OrbitMetrics.SetItemShape(before);
        }
    }

    [Theory]
    [InlineData(OrbitItemShape.Circle)]
    [InlineData(OrbitItemShape.Squircle)]
    [InlineData(OrbitItemShape.Square)]
    public void Shapes_that_sit_on_acrylic_keep_the_themes_translucent_tint(OrbitItemShape shape)
    {
        Assert.Equal(GlassTint, RestTintUnder(shape, GlassTint));
    }

    /// <summary>
    /// The hexagon has no blur under it, so a 40% tint would leave the desktop showing through sharply
    /// behind the glyph. It has to be mostly opaque.
    /// </summary>
    [Fact]
    public void A_hexagon_is_filled_mostly_opaque()
    {
        var tint = RestTintUnder(OrbitItemShape.Hexagon, GlassTint);

        Assert.True(tint.A >= 0xC0, $"The hexagon rests at alpha {tint.A}.");
    }

    /// <summary>Only the opacity changes: the theme's colour is still what the bubble is tinted with.</summary>
    [Theory]
    [InlineData(0x00, 0x00, 0x00)]
    [InlineData(0xFF, 0xFF, 0xFF)]
    [InlineData(0x30, 0x60, 0x90)]
    public void A_hexagon_keeps_the_themes_colour(byte r, byte g, byte b)
    {
        var tint = RestTintUnder(OrbitItemShape.Hexagon, Color.FromArgb(0x66, r, g, b));

        Assert.Equal((r, g, b), (tint.R, tint.G, tint.B));
    }

    /// <summary>A tint that is already firmer than the floor (a high-frost theme) is not made weaker.</summary>
    [Fact]
    public void A_hexagon_never_makes_a_tint_more_transparent()
    {
        var opaque = Color.FromArgb(0xFF, 0x10, 0x10, 0x10);

        Assert.Equal(opaque, RestTintUnder(OrbitItemShape.Hexagon, opaque));
    }

    /// <summary>
    /// The ring over the acrylic's hard edge hides it only as well as it is opaque: what shows
    /// through is the contrast between glass and desktop times what is left of the ring's
    /// transparency. The dark theme's 40% tint would hide 40% of it.
    /// </summary>
    [Fact]
    public void The_rim_is_nearly_opaque_where_the_glass_tint_is_not()
    {
        var rim = OrbitItemGeometry.RimTint(GlassTint);

        Assert.True(rim.A >= 0xE0, $"The rim is alpha {rim.A}.");
    }

    /// <summary>The rim is the glass's own colour, so it reads as the bezel of the glass.</summary>
    [Theory]
    [InlineData(0x00, 0x00, 0x00)]
    [InlineData(0xFF, 0xFF, 0xFF)]
    [InlineData(0x30, 0x60, 0x90)]
    public void The_rim_keeps_the_glass_tints_colour(byte r, byte g, byte b)
    {
        var rim = OrbitItemGeometry.RimTint(Color.FromArgb(0x66, r, g, b));

        Assert.Equal((r, g, b), (rim.R, rim.G, rim.B));
    }

    [Fact]
    public void The_rim_never_makes_a_tint_more_transparent()
    {
        var opaque = Color.FromArgb(0xFF, 0x10, 0x10, 0x10);

        Assert.Equal(opaque, OrbitItemGeometry.RimTint(opaque));
    }

    /// <summary>
    /// The geometry that makes the rim hide anything. Measured on the live dock, the acrylic clip's
    /// edge stands up to <see cref="OrbitItemGeometry.AcrylicEdgeOvershootPx"/> outside its nominal
    /// size; the silhouette's own edge antialiases over half a pixel. The disc has to be held far
    /// enough in that its worst case stays inside the silhouette.
    /// </summary>
    [Fact]
    public void The_acrylic_is_inset_past_its_own_overshoot_and_the_silhouettes_antialiasing()
    {
        Assert.True(
            OrbitItemGeometry.BackdropInsetPx >= OrbitItemGeometry.AcrylicEdgeOvershootPx + 0.5);
    }

    /// <summary>
    /// And the ring has to reach back past where the disc's edge can be at its innermost: the
    /// inset, half a pixel the other way for the stair-step, and half a pixel for the antialiasing
    /// of the ring's own inner edge.
    /// </summary>
    [Fact]
    public void The_rim_reaches_back_past_the_acrylics_innermost_edge()
    {
        Assert.True(
            OrbitItemGeometry.RimWidthPx >= OrbitItemGeometry.BackdropInsetPx + 0.5 + 0.5);
    }
}
