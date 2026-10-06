using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// The back-off that stops a host with no favicon (or a machine with no network) being asked again
/// on every summon. The clock is injected, so none of this waits for real time.
/// </summary>
public class FaviconMissCacheTests
{
    private static (FaviconMissCache Cache, Action<TimeSpan> Advance) Create()
    {
        long now = 1_000_000;
        return (new FaviconMissCache(() => now), by => now += (long)by.TotalMilliseconds);
    }

    [Fact]
    public void AHostThatHasNotFailed_IsNeverSkipped()
    {
        var (cache, _) = Create();

        Assert.False(cache.ShouldSkip("example.com"));
    }

    [Fact]
    public void AFirstMiss_IsSkippedForThirtySeconds_ThenRetried()
    {
        var (cache, advance) = Create();

        cache.RecordMiss("example.com");

        Assert.True(cache.ShouldSkip("example.com"));
        advance(TimeSpan.FromSeconds(29));
        Assert.True(cache.ShouldSkip("example.com"));
        advance(TimeSpan.FromSeconds(2));
        Assert.False(cache.ShouldSkip("example.com"));
    }

    [Fact]
    public void ConsecutiveMisses_BackOffLonger_AndHoldAtTheCeiling()
    {
        var (cache, advance) = Create();
        var expected = new[] { 30, 60, 120, 300, 600, 600, 600 };

        foreach (var seconds in expected)
        {
            cache.RecordMiss("example.com");
            advance(TimeSpan.FromSeconds(seconds - 1));
            Assert.True(cache.ShouldSkip("example.com"), $"still inside the {seconds}s window");
            advance(TimeSpan.FromSeconds(2));
            Assert.False(cache.ShouldSkip("example.com"), $"{seconds}s window is over");
        }
    }

    [Fact]
    public void ASuccess_ForgetsTheHost_SoTheNextFailureStartsTheScheduleOver()
    {
        var (cache, advance) = Create();
        cache.RecordMiss("example.com");
        advance(TimeSpan.FromSeconds(31));
        cache.RecordMiss("example.com"); // second miss: a minute

        cache.RecordSuccess("example.com");
        Assert.False(cache.ShouldSkip("example.com"));

        cache.RecordMiss("example.com");
        advance(TimeSpan.FromSeconds(31));
        Assert.False(cache.ShouldSkip("example.com")); // back to 30 s, not the minute it had reached
    }

    [Fact]
    public void Hosts_AreIndependent_AndTheComparisonIgnoresCase()
    {
        var (cache, _) = Create();

        cache.RecordMiss("Example.COM");

        Assert.True(cache.ShouldSkip("example.com"));
        Assert.False(cache.ShouldSkip("localhost:3000"));
        Assert.False(cache.ShouldSkip("localhost:8080"));
    }

    [Fact]
    public void TheCache_DoesNotGrowWithEveryHostEverySeen()
    {
        var (cache, advance) = Create();

        for (int i = 0; i < FaviconMissCache.MaxHosts + 50; i++)
        {
            cache.RecordMiss($"host-{i}.example");
            advance(TimeSpan.FromMilliseconds(1));
        }

        Assert.Equal(FaviconMissCache.MaxHosts, cache.Count);
        // The oldest were the ones dropped; the newest is still being left alone.
        Assert.True(cache.ShouldSkip($"host-{FaviconMissCache.MaxHosts + 49}.example"));
        Assert.False(cache.ShouldSkip("host-0.example"));
    }
}
