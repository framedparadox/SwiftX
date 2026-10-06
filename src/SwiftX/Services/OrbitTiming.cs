using SwiftX.Models;

namespace SwiftX.Services;

/// <summary>
/// How long the dock takes to put itself away, and how long after that a second press is still read
/// as part of the same gesture. Kept apart from the window so the relationship between the two can
/// be tested: they have to move together, and once did not.
/// </summary>
public static class OrbitTiming
{
    /// <summary>One bubble's closing fade and shrink. Shorter than the entrance and fast from the
    /// first frame: arriving can afford to be watched, leaving is something the user has already
    /// decided on, and a dock that lingers is lag. Measured at the previous 220 ms on a slow-start
    /// curve, nothing visibly moved for ~100 ms after a dismiss.</summary>
    public const int ExitMs = 170;

    /// <summary>Gap between neighbouring bubbles' closing motions (the last to arrive leaves first).</summary>
    public const int ExitStaggerMs = 14;

    /// <summary>What the dismissal waits after the motion before it tears the window down.</summary>
    public const int TeardownPadMs = 30;

    /// <summary>What <see cref="ClosingGraceMs"/> allows beyond the dismissal itself.</summary>
    public const int ClosingGraceMarginMs = 50;

    /// <summary>
    /// How long the dismissal runs, from its start to the window being torn down, for an orbit
    /// whose longest closing delay is <paramref name="maxStagger"/> beats. Never less than the hub
    /// mark's own exit: the window goes when this is over, and an orbit with only a few items has a
    /// short stagger but the same four-petal mark to put away.
    /// </summary>
    public static int DismissMs(int maxStagger) =>
        Math.Max(ExitMs + maxStagger * ExitStaggerMs, AppLogo.Mark.ExitSpanMs) + TeardownPadMs;

    /// <summary>The longest <see cref="DismissMs"/> can be: a full orbit.</summary>
    public static int LongestDismissMs => DismissMs(OrbitMetrics.SlotCount - 1);

    /// <summary>
    /// How long after a dismiss begins a summon is still read as part of that same gesture (the
    /// tray icon closes the dock on the click's button-down and toggles on its button-up).
    /// Comfortably longer than a click's own down-to-up, and shorter than any deliberate
    /// close-then-reopen.
    /// <para>
    /// Derived from the dismissal rather than a number of its own, because a press is ignored for
    /// as long as this lasts and any of it that runs past the dock vanishing is time a hotkey press
    /// does nothing. It was a fixed 500 ms against a 446 ms dismissal (about 50 ms of dead time);
    /// when the dismissal was shortened to ~300 ms the same 500 left 200 ms in which a second
    /// press, with the dock already gone, was ignored — and a soak that re-summoned 100 ms after
    /// each dismissal lost half its cycles to it.
    /// </para>
    /// </summary>
    public static int ClosingGraceMs => LongestDismissMs + ClosingGraceMarginMs;
}
