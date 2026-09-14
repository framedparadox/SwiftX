namespace SwiftX.Services;

/// <summary>
/// The time-of-day line at the top of the app's Home screen.
/// <para>
/// Ported from windows-app-mx's <c>LogiApp/Helpers/GreetingHelper.cs</c> (branch
/// <c>logiApp/dotnet</c>), whose header this screen is built on — the boundaries (05:00 / 12:00 /
/// 17:00) and the three strings are Logi's, unchanged, so the two read the same at the same hour.
/// </para>
/// <para>
/// Goes through <see cref="Loc"/> rather than returning literals the way the original does: SwiftX
/// ships nineteen string tables and a greeting is the first thing a user reads, so it is the last
/// place to leave hard-coded English. A table that carries no entry for one of these keys falls
/// back to English (US) on its own — see <see cref="Loc.Get"/>.
/// </para>
/// </summary>
public static class Greeting
{
    /// <summary>Hour the morning greeting starts, local time.</summary>
    private const int MorningHour = 5;

    /// <summary>Hour the afternoon greeting starts, local time.</summary>
    private const int AfternoonHour = 12;

    /// <summary>Hour the evening greeting starts, local time, and runs until <see cref="MorningHour"/>.</summary>
    private const int EveningHour = 17;

    /// <summary>The greeting for the current local time.</summary>
    public static string Current() => For(DateTime.Now);

    /// <summary>
    /// The greeting for a given local time. Split out from <see cref="Current"/> so the boundaries
    /// can be exercised without moving the clock.
    /// </summary>
    public static string For(DateTime local) => ForHour(local.Hour);

    /// <summary>
    /// The greeting for a given hour of the day, 0-23.
    /// </summary>
    public static string ForHour(int hour) => hour switch
    {
        >= MorningHour and < AfternoonHour => Loc.Get("Greeting.Morning"),
        >= AfternoonHour and < EveningHour => Loc.Get("Greeting.Afternoon"),
        _ => Loc.Get("Greeting.Evening"),
    };

    /// <summary>
    /// The next local time at which <see cref="For"/> would start returning a different string —
    /// today's 05:00, 12:00 or 17:00, or tomorrow's 05:00 once the evening has started.
    /// <para>
    /// This is what lets the Home screen follow the clock by sleeping until the boundary instead of
    /// waking every minute to ask whether it is noon yet. Always strictly greater than
    /// <paramref name="local"/>, so a caller that re-arms from it can never spin: a timer armed at
    /// exactly 12:00:00 gets 17:00, not 12:00 again.
    /// </para>
    /// </summary>
    public static DateTime NextChangeAfter(DateTime local)
    {
        var midnight = local.Date;
        foreach (int hour in new[] { MorningHour, AfternoonHour, EveningHour })
        {
            var boundary = midnight.AddHours(hour);
            if (boundary > local)
                return boundary;
        }
        return midnight.AddDays(1).AddHours(MorningHour);
    }

    /// <summary>
    /// How long to wait before the greeting changes, floored at one second so a caller can arm a
    /// timer with it unconditionally — <see cref="System.TimeSpan"/>s of zero or less are not a
    /// legal <c>DispatcherTimer.Interval</c>, and a clock that jumps backwards mid-wait would
    /// otherwise produce one.
    /// </summary>
    public static TimeSpan TimeUntilNextChange(DateTime local)
    {
        var delay = NextChangeAfter(local) - local;
        return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
    }
}
