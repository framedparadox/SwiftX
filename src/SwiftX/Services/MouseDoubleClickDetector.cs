namespace SwiftX.Services;

/// <summary>
/// The timing check behind <see cref="MouseTriggerWatcher"/>'s double-click gesture: given when the
/// configured button went down the previous time and this time, and the OS's own double-click
/// interval, decide whether this press completes a double-click rather than starting an unrelated
/// click or following a long press.
/// <para>
/// Pulled out of the watcher's poll loop as its own pure, static comparison — mirroring the
/// windowing Windows itself uses (successive <c>WM_LBUTTONDOWN</c>-style press timestamps against
/// <c>GetDoubleClickTime()</c>) — so it can be unit-tested with synthetic timestamps. The watcher's
/// real inputs, <c>GetAsyncKeyState</c> and <c>GetDoubleClickTime</c>, can't be exercised in a test.
/// </para>
/// </summary>
internal static class MouseDoubleClickDetector
{
    /// <summary>
    /// True when <paramref name="secondPressAt"/> follows <paramref name="firstPressAt"/> closely
    /// enough, per <paramref name="thresholdMs"/>, to count as the second half of a double-click.
    /// A long press naturally fails this the same way two unrelated clicks do: holding the button
    /// past the threshold before the next press arrives pushes the gap beyond
    /// <paramref name="thresholdMs"/> regardless of why the gap is that long.
    /// </summary>
    public static bool IsDoubleClick(long firstPressAt, long secondPressAt, int thresholdMs)
    {
        long elapsed = secondPressAt - firstPressAt;
        return elapsed >= 0 && elapsed <= thresholdMs;
    }
}
