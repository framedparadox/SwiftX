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
    /// <summary>The greeting for the current local time.</summary>
    public static string Current() => ForHour(DateTime.Now.Hour);

    /// <summary>
    /// The greeting for a given hour of the day, 0-23. Split out from <see cref="Current"/> so the
    /// boundaries can be exercised without moving the clock.
    /// </summary>
    public static string ForHour(int hour) => hour switch
    {
        >= 5 and < 12 => Loc.Get("Greeting.Morning"),
        >= 12 and < 17 => Loc.Get("Greeting.Afternoon"),
        _ => Loc.Get("Greeting.Evening"),
    };
}
