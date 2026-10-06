using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// The dock's dismissal and the window in which a second press is still read as part of the same
/// gesture. A press inside that window is ignored on purpose (the tray icon closes the dock on the
/// click's button-down and toggles on its button-up), so every millisecond of it that runs past the
/// dock vanishing is a millisecond a hotkey press does nothing.
/// <para>
/// It was a fixed 500 ms against a 446 ms dismissal. When the dismissal was shortened to ~300 ms the
/// 500 stayed, and a soak that re-summoned 100 ms after each dismissal lost 74 of its 150 cycles to
/// presses the dock swallowed, with the dock already gone.
/// </para>
/// </summary>
public class OrbitTimingTests
{
    /// <summary>
    /// The regression: the grace must follow the dismissal. A few tens of milliseconds of margin
    /// is what it needs (more than a click's own down-to-up is covered by the dismissal itself,
    /// which the tray's press and release both land inside); anything long is dead time.
    /// </summary>
    [Fact]
    public void A_press_is_ignored_for_only_a_short_while_after_the_dock_has_gone()
    {
        int deadTime = OrbitTiming.ClosingGraceMs - OrbitTiming.LongestDismissMs;

        Assert.InRange(deadTime, 0, 100);
    }

    /// <summary>
    /// The dock has to be gone before the grace ends, or a press in between is ignored while the
    /// dock is still on screen to be pressed.
    /// </summary>
    [Fact]
    public void The_grace_outlasts_the_longest_dismissal()
    {
        Assert.True(OrbitTiming.ClosingGraceMs > OrbitTiming.LongestDismissMs);
    }

    /// <summary>
    /// The window is torn down when the dismissal returns, and the hub mark has its own exit to
    /// finish first: a dock with one item has almost no stagger and the same four petals to put away.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void A_short_orbit_still_waits_for_the_hub_to_leave(int maxStagger)
    {
        Assert.True(OrbitTiming.DismissMs(maxStagger) >= AppLogo.Mark.ExitSpanMs + OrbitTiming.TeardownPadMs);
    }

    [Fact]
    public void A_longer_stagger_never_shortens_the_dismissal()
    {
        int last = 0;
        for (int stagger = 0; stagger < OrbitMetrics.SlotCount; stagger++)
        {
            int ms = OrbitTiming.DismissMs(stagger);
            Assert.True(ms >= last, $"Stagger {stagger} gave {ms} ms after {last} ms.");
            last = ms;
        }
    }

    /// <summary>
    /// The point of speeding the dismissal up: it is meaningfully quicker than the 446 ms it
    /// replaced, and the grace is correspondingly shorter than the 500 ms it replaced.
    /// </summary>
    [Fact]
    public void Dismissal_and_grace_are_shorter_than_they_were()
    {
        Assert.True(OrbitTiming.LongestDismissMs < 400);
        Assert.True(OrbitTiming.ClosingGraceMs < 500);
    }
}
