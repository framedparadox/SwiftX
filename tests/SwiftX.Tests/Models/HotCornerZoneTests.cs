using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

public class HotCornerZoneTests
{
    // A plain 1920x1080 primary display at the origin.
    private const int Left = 0;
    private const int Top = 0;
    private const int Width = 1920;
    private const int Height = 1080;

    private static bool At(HotCorner corner, int x, int y, int size = HotCornerZone.Size) =>
        HotCornerZone.Contains(corner, x, y, Left, Top, Width, Height, size);

    [Theory]
    [InlineData(HotCorner.TopLeft, 0, 0)]
    [InlineData(HotCorner.TopRight, Width - 1, 0)]
    [InlineData(HotCorner.BottomLeft, 0, Height - 1)]
    [InlineData(HotCorner.BottomRight, Width - 1, Height - 1)]
    public void The_very_corner_pixel_counts(HotCorner corner, int x, int y)
    {
        Assert.True(At(corner, x, y));
    }

    [Theory]
    [InlineData(HotCorner.TopLeft)]
    [InlineData(HotCorner.TopRight)]
    [InlineData(HotCorner.BottomLeft)]
    [InlineData(HotCorner.BottomRight)]
    public void Only_its_own_corner_counts(HotCorner corner)
    {
        // Every corner pixel of the display, checked against one configured corner: exactly one
        // of the four is a hit, so no two corners can ever both fire.
        (int X, int Y)[] corners =
        [
            (0, 0), (Width - 1, 0), (0, Height - 1), (Width - 1, Height - 1),
        ];
        Assert.Single(corners, c => At(corner, c.X, c.Y));
    }

    [Fact]
    public void The_patch_is_a_few_pixels_square_not_a_whole_edge()
    {
        Assert.True(At(HotCorner.TopLeft, HotCornerZone.Size - 1, HotCornerZone.Size - 1));
        Assert.False(At(HotCorner.TopLeft, HotCornerZone.Size, HotCornerZone.Size - 1));
        Assert.False(At(HotCorner.TopLeft, HotCornerZone.Size - 1, HotCornerZone.Size));
        // Mid-edge: hard against the top of the screen, nowhere near a corner.
        Assert.False(At(HotCorner.TopLeft, Width / 2, 0));
        Assert.False(At(HotCorner.BottomLeft, 0, Height / 2));
    }

    [Fact]
    public void Nothing_outside_the_display_counts()
    {
        Assert.False(At(HotCorner.TopLeft, -1, 0));
        Assert.False(At(HotCorner.TopLeft, 0, -1));
        Assert.False(At(HotCorner.BottomRight, Width, Height - 1));
        Assert.False(At(HotCorner.BottomRight, Width - 1, Height));
    }

    [Fact]
    public void A_secondary_display_uses_its_own_corners()
    {
        // A 1280x1024 monitor sitting above and to the left of the primary one, so its bounds are
        // negative in both axes — the case that catches any assumption of a zero origin.
        const int left = -1280;
        const int top = -1024;
        Assert.True(HotCornerZone.Contains(HotCorner.TopLeft, left, top, left, top, 1280, 1024));
        Assert.True(HotCornerZone.Contains(HotCorner.BottomRight, -1, -1, left, top, 1280, 1024));
        // The virtual-screen origin is inside the primary display, not this one.
        Assert.False(HotCornerZone.Contains(HotCorner.TopLeft, 0, 0, left, top, 1280, 1024));
    }

    [Fact]
    public void A_patch_can_never_cover_more_than_half_the_display()
    {
        // An absurd size can't turn one corner into the whole screen, or leave opposite corners
        // overlapping so that both would fire on the same pixel.
        int middleX = Width / 2;
        int middleY = Height / 2;
        Assert.False(At(HotCorner.TopLeft, middleX, middleY, size: 100_000));
        Assert.False(At(HotCorner.BottomRight, middleX - 1, middleY - 1, size: 100_000));
        Assert.True(At(HotCorner.TopLeft, middleX - 1, middleY - 1, size: 100_000));
    }

    [Fact]
    public void A_zero_sized_display_never_matches()
    {
        Assert.False(HotCornerZone.Contains(HotCorner.TopLeft, 0, 0, 0, 0, 0, 0));
    }
}
