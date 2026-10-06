using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// When the Settings Home marks may animate. They cost ~19% of one CPU core while they play (ring
/// 14%, tile-spin 8%, neither 0.3%; Release build, window open and untouched), so they rest once the
/// window has been left behind others; but they must play whenever someone is plainly in the window,
/// which the first version of the rule did not do (it stopped them 20 s after the last input even in
/// the active window, and never played them with Windows' animation effects off). Each input is
/// pinned here, both ways.
/// </summary>
public class HomeIdlePolicyTests
{
    private static readonly TimeSpan Recent = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LongIdle = TimeSpan.FromMinutes(10);

    [Fact]
    public void WhenSomeoneIsLooking_TheMarksPlay()
    {
        Assert.True(HomeIdlePolicy.ShouldAnimate(
            onHome: true, windowShown: true, windowActive: true, closed: false, sinceActivity: Recent));
    }

    [Fact]
    public void InTheActiveWindow_TheMarksKeepPlaying_HoweverLongSinceTheLastInput()
    {
        // Someone reading Home without touching the mouse is still looking at it.
        Assert.True(HomeIdlePolicy.ShouldAnimate(
            onHome: true, windowShown: true, windowActive: true, closed: false, sinceActivity: LongIdle));
    }

    [Fact]
    public void InAVisibleWindowThatIsNotActive_RecentActivityWakesTheMarks()
    {
        // The pointer passing over a window beside the one in front, or a window Activate() could
        // not bring forward: it is on screen and someone is at it.
        Assert.True(HomeIdlePolicy.ShouldAnimate(
            onHome: true, windowShown: true, windowActive: false, closed: false, sinceActivity: Recent));
    }

    [Theory]
    [InlineData(false, true, true, false)] // another screen is showing
    [InlineData(true, false, true, false)] // the window is minimised or hidden
    [InlineData(true, true, true, true)]   // the window is closed
    public void AnyOneConditionFailing_LeavesTheMarksAtRest(bool onHome, bool windowShown, bool windowActive, bool closed)
    {
        Assert.False(HomeIdlePolicy.ShouldAnimate(onHome, windowShown, windowActive, closed, Recent));
    }

    [Fact]
    public void LeftBehindOtherWindows_TheMarksRestAfterTwentySeconds_AndNotBefore()
    {
        var limit = HomeIdlePolicy.RestAfter;

        Assert.Equal(TimeSpan.FromSeconds(20), limit);
        Assert.True(HomeIdlePolicy.ShouldAnimate(true, true, false, false, limit - TimeSpan.FromMilliseconds(1)));
        Assert.False(HomeIdlePolicy.ShouldAnimate(true, true, false, false, limit));
        Assert.False(HomeIdlePolicy.ShouldAnimate(true, true, false, false, LongIdle));
    }

    [Fact]
    public void JustArrived_CountsAsActivity()
    {
        Assert.True(HomeIdlePolicy.ShouldAnimate(true, true, false, false, TimeSpan.Zero));
    }
}
