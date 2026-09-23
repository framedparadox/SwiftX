using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// The Home header's time-of-day line. <see cref="Greeting"/> reads the clock only in
/// <see cref="Greeting.Current"/>; everything worth asserting is reachable through the overloads
/// that take the time, so none of these tests move the machine's clock.
/// <para>
/// <see cref="Greeting.ForHour"/> answers out of <see cref="Loc"/>, which holds its active language
/// as static state — so the two tests that compare against English text pin it first and restore it
/// in a <c>finally</c>, the same way <see cref="LocTests"/> does.
/// </para>
/// </summary>
[Collection(LocalizationCollection.Name)]
public class GreetingTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(11)]
    public void Morning_runs_from_five_until_noon(int hour)
    {
        try
        {
            Loc.Initialize(AppLanguage.EnglishUS);
            Assert.Equal("Good Morning", Greeting.ForHour(hour));
        }
        finally
        {
            Loc.Initialize(AppLanguage.EnglishUS);
        }
    }

    [Theory]
    [InlineData(12, "Good Afternoon")]
    [InlineData(16, "Good Afternoon")]
    [InlineData(17, "Good Evening")]
    [InlineData(23, "Good Evening")]
    [InlineData(0, "Good Evening")]
    [InlineData(4, "Good Evening")]
    public void Afternoon_and_evening_split_at_five(int hour, string expected)
    {
        try
        {
            Loc.Initialize(AppLanguage.EnglishUS);
            Assert.Equal(expected, Greeting.ForHour(hour));
        }
        finally
        {
            Loc.Initialize(AppLanguage.EnglishUS);
        }
    }

    [Fact]
    public void For_reads_the_hour_off_the_time_it_is_given()
    {
        try
        {
            Loc.Initialize(AppLanguage.EnglishUS);
            Assert.Equal("Good Morning", Greeting.For(new DateTime(2026, 3, 1, 9, 30, 0)));
            Assert.Equal("Good Afternoon", Greeting.For(new DateTime(2026, 3, 1, 13, 0, 0)));
            Assert.Equal("Good Evening", Greeting.For(new DateTime(2026, 3, 1, 21, 45, 0)));
        }
        finally
        {
            Loc.Initialize(AppLanguage.EnglishUS);
        }
    }

    [Theory]
    // Before the first boundary of the day, the next change is that morning's 05:00.
    [InlineData(2, 0, 5)]
    [InlineData(4, 59, 5)]
    // Inside the morning, it is noon.
    [InlineData(5, 0, 12)]
    [InlineData(11, 59, 12)]
    // Inside the afternoon, it is 17:00.
    [InlineData(12, 0, 17)]
    [InlineData(16, 59, 17)]
    public void NextChangeAfter_lands_on_the_next_boundary_the_same_day(int hour, int minute, int expectedHour)
    {
        var now = new DateTime(2026, 3, 1, hour, minute, 0);
        var next = Greeting.NextChangeAfter(now);

        Assert.Equal(now.Date, next.Date);
        Assert.Equal(expectedHour, next.Hour);
        Assert.Equal(0, next.Minute);
    }

    [Theory]
    [InlineData(17, 0)]
    [InlineData(23, 59)]
    public void NextChangeAfter_rolls_over_to_tomorrow_once_the_evening_has_started(int hour, int minute)
    {
        var now = new DateTime(2026, 3, 1, hour, minute, 0);
        var next = Greeting.NextChangeAfter(now);

        Assert.Equal(now.Date.AddDays(1), next.Date);
        Assert.Equal(5, next.Hour);
    }

    /// <summary>
    /// The boundaries themselves are the one input a re-arming timer hits dead on, and the one that
    /// would spin if <see cref="Greeting.NextChangeAfter"/> answered "now". Each has to move on to
    /// the boundary after it, not return itself.
    /// </summary>
    [Theory]
    [InlineData(5, 12)]
    [InlineData(12, 17)]
    public void NextChangeAfter_never_returns_the_boundary_it_was_given(int hour, int expectedHour)
    {
        var now = new DateTime(2026, 3, 1, hour, 0, 0);
        var next = Greeting.NextChangeAfter(now);

        Assert.True(next > now);
        Assert.Equal(expectedHour, next.Hour);
    }

    [Fact]
    public void NextChangeAfter_at_the_evening_boundary_rolls_over()
    {
        var now = new DateTime(2026, 3, 1, 17, 0, 0);
        var next = Greeting.NextChangeAfter(now);

        Assert.True(next > now);
        Assert.Equal(now.Date.AddDays(1).AddHours(5), next);
    }

    [Fact]
    public void TimeUntilNextChange_is_the_gap_to_that_boundary()
    {
        var now = new DateTime(2026, 3, 1, 11, 30, 0);
        Assert.Equal(TimeSpan.FromMinutes(30), Greeting.TimeUntilNextChange(now));
    }

    /// <summary>
    /// A DispatcherTimer rejects a non-positive interval, so the floor is what keeps a caller from
    /// having to special-case the instant a boundary falls on.
    /// </summary>
    [Fact]
    public void TimeUntilNextChange_never_returns_a_wait_a_timer_would_reject()
    {
        for (int hour = 0; hour < 24; hour++)
        {
            for (int minute = 0; minute < 60; minute += 5)
            {
                var delay = Greeting.TimeUntilNextChange(new DateTime(2026, 3, 1, hour, minute, 0));
                Assert.True(delay >= TimeSpan.FromSeconds(1), $"{hour:00}:{minute:00} produced {delay}");
            }
        }
    }

    /// <summary>
    /// Every language's greeting resolves to something printable — a table with no entry of its own
    /// falls through to English (US) rather than showing the raw key. That fallback is what lets the
    /// header ship in nineteen languages without nineteen translations of three strings.
    /// </summary>
    [Fact]
    public void Every_language_produces_a_greeting_rather_than_a_key()
    {
        try
        {
            foreach (var language in Enum.GetValues<AppLanguage>())
            {
                Loc.Initialize(language);
                foreach (int hour in new[] { 8, 14, 20 })
                {
                    var text = Greeting.ForHour(hour);
                    Assert.False(string.IsNullOrWhiteSpace(text));
                    Assert.DoesNotContain("Greeting.", text, StringComparison.Ordinal);
                }
            }
        }
        finally
        {
            Loc.Initialize(AppLanguage.EnglishUS);
        }
    }
}
