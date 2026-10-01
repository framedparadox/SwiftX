using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// When the Settings Home marks may animate. They cost ~19% of one CPU core while they play (ring
/// 14%, tile-spin 8%, neither 0.3%; Release build, window open and untouched), so the rule that they
/// rest unless someone is looking is a performance guarantee, and each input of it is pinned here.
/// </summary>
public class HomeIdlePolicyTests
{
    private static readonly TimeSpan Recent = TimeSpan.FromSeconds(1);

    [Fact]
    public void WhenSomeoneIsLooking_TheMarksPlay()
    {
        Assert.True(HomeIdlePolicy.ShouldAnimate(
            onHome: true, windowActive: true, reducedMotion: false, closed: false, sinceActivity: Recent));
    }

    [Theory]
    [InlineData(false, true, false, false)] // another screen is showing
    [InlineData(true, false, false, false)] // the window is not the active one
    [InlineData(true, true, true, false)]   // Windows has animations switched off
    [InlineData(true, true, false, true)]   // the window is closed
    public void AnyOneConditionFailing_LeavesTheMarksAtRest(bool onHome, bool windowActive, bool reducedMotion, bool closed)
    {
        Assert.False(HomeIdlePolicy.ShouldAnimate(onHome, windowActive, reducedMotion, closed, Recent));
    }

    [Fact]
    public void AfterTwentySecondsWithoutActivity_TheMarksRest_AndNotBefore()
    {
        var limit = HomeIdlePolicy.RestAfter;

        Assert.Equal(TimeSpan.FromSeconds(20), limit);
        Assert.True(HomeIdlePolicy.ShouldAnimate(true, true, false, false, limit - TimeSpan.FromMilliseconds(1)));
        Assert.False(HomeIdlePolicy.ShouldAnimate(true, true, false, false, limit));
        Assert.False(HomeIdlePolicy.ShouldAnimate(true, true, false, false, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void JustArrived_CountsAsActivity()
    {
        Assert.True(HomeIdlePolicy.ShouldAnimate(true, true, false, false, TimeSpan.Zero));
    }
}
