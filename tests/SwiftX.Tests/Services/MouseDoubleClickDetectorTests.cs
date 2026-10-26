using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// The pure timing comparison behind <see cref="MouseTriggerWatcher"/>'s double-click gesture (see
/// <see cref="MouseDoubleClickDetector"/>), exercised with synthetic press timestamps since the
/// watcher's real inputs — <c>GetAsyncKeyState</c> and <c>GetDoubleClickTime</c> — cannot be driven
/// from a test.
/// </summary>
public class MouseDoubleClickDetectorTests
{
    private const int ThresholdMs = 500; // a representative GetDoubleClickTime() value

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(250)]
    [InlineData(500)]
    public void A_second_press_within_the_threshold_completes_a_double_click(long gapMs)
    {
        Assert.True(MouseDoubleClickDetector.IsDoubleClick(1_000, 1_000 + gapMs, ThresholdMs));
    }

    [Theory]
    [InlineData(501)]
    [InlineData(900)]
    [InlineData(5_000)]
    public void Two_clicks_spaced_further_apart_than_the_threshold_are_unrelated(long gapMs)
    {
        Assert.False(MouseDoubleClickDetector.IsDoubleClick(1_000, 1_000 + gapMs, ThresholdMs));
    }

    /// <summary>
    /// The detector compares press-to-press timestamps, so a button held well past
    /// <see cref="MouseTriggerWatcher.LongPressMilliseconds"/> before the next press arrives fails
    /// this check on exactly the same grounds as two genuinely unrelated clicks do — there is no
    /// separate "was that a long press?" branch to fool.
    /// </summary>
    [Fact]
    public void A_long_press_is_not_mistaken_for_a_double_click()
    {
        long firstPressAt = 0;
        long secondPressAt = MouseTriggerWatcher.LongPressMilliseconds + 500;

        Assert.False(MouseDoubleClickDetector.IsDoubleClick(firstPressAt, secondPressAt, ThresholdMs));
    }
}
