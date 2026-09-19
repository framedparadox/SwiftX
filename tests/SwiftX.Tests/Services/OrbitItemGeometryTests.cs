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
}
