using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

/// <summary>
/// How wide an orbit icon is decoded. The shell hands over a 256 px jumbo icon and the dock draws it
/// at 22 DIPs: shrunk at draw time that is a 10x reduction by a filter that skips most of the
/// source, and every edge inside the icon came out as a staircase. Decoded at the size it is drawn,
/// the decoder does the reduction instead. These are the bounds that make that the right size.
/// </summary>
public class OrbitMetricsIconDecodeTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    [InlineData(3.0)]
    public void The_decode_is_never_narrower_than_the_icon_is_drawn_after_a_hover(double scale)
    {
        // Narrower and the hover scale (1.14) enlarges a bitmap that was already as small as it
        // could be drawn: a blur on every hover.
        double hovered = OrbitMetrics.Icon * scale * OrbitMetrics.HoverScale;

        Assert.True(OrbitMetrics.IconDecodePixels(scale) >= hovered);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void The_decode_is_not_wider_than_it_needs_to_be(double scale)
    {
        // Wider and the compositor is back to shrinking it, which is the staircase. Less than a
        // pixel of rounding is the only slack there is.
        double hovered = OrbitMetrics.Icon * scale * OrbitMetrics.HoverScale;

        Assert.True(OrbitMetrics.IconDecodePixels(scale) < hovered + 1);
    }

    [Fact]
    public void A_sharper_monitor_decodes_wider()
    {
        Assert.True(OrbitMetrics.IconDecodePixels(2.0) > OrbitMetrics.IconDecodePixels(1.0));
    }
}
