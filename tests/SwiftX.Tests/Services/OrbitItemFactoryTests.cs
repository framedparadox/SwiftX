using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

public class OrbitItemFactoryTests
{
    [Theory]
    [InlineData(@"C:\Windows\System32\notepad.exe", OrbitItemKind.Application)]
    [InlineData(@"C:\Windows", OrbitItemKind.Folder)]
    [InlineData("https://example.com", OrbitItemKind.WebLink)]
    [InlineData(@"C:\Temp\notes.txt", OrbitItemKind.File)]
    public void Classify_infers_kind(string target, OrbitItemKind expected)
    {
        Assert.Equal(expected, OrbitItemFactory.Classify(target));
    }

    [Fact]
    public void SuggestName_uses_host_for_urls()
    {
        Assert.Equal("example.com", OrbitItemFactory.SuggestName("https://example.com/path"));
    }
}

/// <summary>
/// Covers the pure angle math behind the orbit's radial hover (OrbitWindow.OrbitCanvas_PointerMoved
/// delegates to OrbitMetrics.NearestSlotIndex, which cannot be exercised directly since OrbitWindow
/// is a live WinUI Window). Slot 0 is straight up; slots advance clockwise 45° apart, matching
/// OrbitWindow.CreateBubble's placement formula.
/// </summary>
public class OrbitMetricsNearestSlotIndexTests
{
    [Theory]
    [InlineData(0, -1, 0)]     // straight up
    [InlineData(1, -1, 1)]     // up-right
    [InlineData(1, 0, 2)]      // straight right
    [InlineData(1, 1, 3)]      // down-right
    [InlineData(0, 1, 4)]      // straight down
    [InlineData(-1, 1, 5)]     // down-left
    [InlineData(-1, 0, 6)]     // straight left
    [InlineData(-1, -1, 7)]    // up-left
    public void NearestSlotIndex_matches_bubble_placement_at_the_eight_cardinal_directions(
        double dx, double dy, int expected)
    {
        Assert.Equal(expected, OrbitMetrics.NearestSlotIndex(dx, dy));
    }

    [Theory]
    [InlineData(-30, 30, 5)]  // between slot 4 (down) and slot 5 (down-left), closer to 5
    [InlineData(30, -30, 1)]  // between slot 0 (up) and slot 1 (up-right), closer to 1
    public void NearestSlotIndex_snaps_to_the_nearer_slot_across_a_gap(double dx, double dy, int expected)
    {
        // A point in the gap between two bubbles must resolve to exactly one of them (whichever
        // is angularly nearer) rather than landing between slots or out of range — this is what
        // makes hover feel continuous across the annulus instead of only over each bubble's own
        // small circle.
        Assert.Equal(expected, OrbitMetrics.NearestSlotIndex(dx, dy));
    }

    [Fact]
    public void NearestSlotIndex_is_stable_under_a_full_rotation()
    {
        // Every 45° step around the circle should land on a distinct, valid slot exactly once.
        var seen = new HashSet<int>();
        for (int deg = 0; deg < 360; deg += 45)
        {
            double rad = deg * Math.PI / 180.0;
            // Sweep starting from straight up (matches CreateBubble's -90° start) so this walks
            // slots 0..7 in order.
            double dx = Math.Sin(rad);
            double dy = -Math.Cos(rad);
            int index = OrbitMetrics.NearestSlotIndex(dx, dy);
            Assert.True(seen.Add(index), $"slot {index} was hit more than once");
        }
        Assert.Equal(OrbitMetrics.SlotCount, seen.Count);
    }
}
