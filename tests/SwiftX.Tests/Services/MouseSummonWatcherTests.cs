using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// Pure hold/re-arm state machine for the middle-button (default) long-press summon — same style
/// as the hot-corner dwell tests: no HWND, no GetAsyncKeyState.
/// </summary>
public class MouseSummonWatcherTests
{
    [Fact]
    public void A_short_press_does_not_fire()
    {
        long? pressedAt = null;
        Assert.False(MouseSummonWatcher.ShouldFire(
            buttonDown: true, otherDown: false, armed: true,
            ref pressedAt, holdMs: 400, now: 100));
        Assert.Equal(100, pressedAt);
        Assert.False(MouseSummonWatcher.ShouldFire(
            buttonDown: true, otherDown: false, armed: true,
            ref pressedAt, holdMs: 400, now: 300));
    }

    [Fact]
    public void Holding_past_the_threshold_fires_once_per_arm()
    {
        long? pressedAt = null;
        Assert.False(MouseSummonWatcher.ShouldFire(
            true, false, armed: true, ref pressedAt, 400, now: 0));
        Assert.True(MouseSummonWatcher.ShouldFire(
            true, false, armed: true, ref pressedAt, 400, now: 400));
        // Still held, but the watcher disarms after firing — armed:false must not re-fire.
        Assert.False(MouseSummonWatcher.ShouldFire(
            true, false, armed: false, ref pressedAt, 400, now: 800));
    }

    [Fact]
    public void Releasing_rearms_for_the_next_hold()
    {
        long? pressedAt = 50;
        Assert.False(MouseSummonWatcher.ShouldFire(
            buttonDown: false, otherDown: false, armed: false,
            ref pressedAt, 400, now: 200));
        Assert.Null(pressedAt);

        Assert.False(MouseSummonWatcher.ShouldFire(
            true, false, armed: true, ref pressedAt, 400, now: 200));
        Assert.True(MouseSummonWatcher.ShouldFire(
            true, false, armed: true, ref pressedAt, 400, now: 600));
    }

    [Fact]
    public void A_chord_with_another_button_cancels_the_hold()
    {
        long? pressedAt = null;
        Assert.False(MouseSummonWatcher.ShouldFire(
            true, otherDown: false, armed: true, ref pressedAt, 400, now: 0));
        Assert.False(MouseSummonWatcher.ShouldFire(
            true, otherDown: true, armed: true, ref pressedAt, 400, now: 100));
        Assert.Null(pressedAt);
        // After the other button is released, the clock starts fresh.
        Assert.False(MouseSummonWatcher.ShouldFire(
            true, otherDown: false, armed: true, ref pressedAt, 400, now: 100));
        Assert.Equal(100, pressedAt);
        Assert.False(MouseSummonWatcher.ShouldFire(
            true, otherDown: false, armed: true, ref pressedAt, 400, now: 400));
        Assert.True(MouseSummonWatcher.ShouldFire(
            true, otherDown: false, armed: true, ref pressedAt, 400, now: 500));
    }
}
