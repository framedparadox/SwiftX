using System.Text.RegularExpressions;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// A composition spring's <c>Period</c> is not the length of its swing: measured on the live dock,
/// one given 220 ms took about 0.9 s to settle, so every spring written as "220 ms" ran about six
/// times slower than its number says and the orbit's entrance read as a slow linear swell. The
/// dock's springs go through <see cref="SpringMotion.Period"/>, which is the conversion.
/// </summary>
public class SpringMotionTests
{
    /// <summary>
    /// The fit the measurements gave: the spring's angular frequency is 1 / Period, so one swing
    /// lasts 2π × Period. The dock's own 220 ms swing is therefore a Period of about 35 ms.
    /// </summary>
    [Theory]
    [InlineData(220, 35.014)]
    [InlineData(320, 50.930)]
    [InlineData(60, 9.549)]
    public void A_swing_takes_two_pi_periods(double oscillationMs, double expectedPeriodMs)
    {
        Assert.Equal(expectedPeriodMs, SpringMotion.Period(oscillationMs).TotalMilliseconds, 2);
    }

    [Fact]
    public void A_longer_swing_never_gets_a_shorter_period()
    {
        var last = TimeSpan.Zero;
        foreach (int ms in new[] { 40, 100, 220, 320, 640 })
        {
            var period = SpringMotion.Period(ms);
            Assert.True(period > last, $"{ms} ms gave {period}, not longer than {last}.");
            last = period;
        }
    }

    /// <summary>
    /// The conversion only helps if it is used: a spring that takes a raw
    /// <c>TimeSpan.FromMilliseconds</c> as its <c>Period</c> is the bug again, and nothing at run
    /// time distinguishes it — it simply plays slowly. The dock and its mark are the two files that
    /// build springs, so this reads them.
    /// </summary>
    [Theory]
    [InlineData("OrbitWindow.xaml.cs")]
    [InlineData("AppLogo.cs")]
    public void Springs_take_their_period_through_SpringMotion(string fileName)
    {
        var path = SourceTree.Files().Single(f => Path.GetFileName(f) == fileName);
        var source = File.ReadAllText(path);

        var assignments = Regex.Matches(source, @"\.Period\s*=\s*(?<value>[^;]+);");
        Assert.NotEmpty(assignments);

        foreach (Match assignment in assignments)
        {
            Assert.StartsWith("SpringMotion.Period(", assignment.Groups["value"].Value.Trim());
        }
    }
}
