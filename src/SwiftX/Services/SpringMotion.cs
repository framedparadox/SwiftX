namespace SwiftX.Services;

/// <summary>
/// Turns the length of a spring's swing into the <c>Period</c> composition has to be given.
/// <para>
/// A composition spring's <c>Period</c> is not the wall-clock length of one oscillation. Measured on
/// the live dock, a bubble spring given 220 ms took about 0.9 s to settle, one given 60 ms about
/// 0.45 s, and one given 25 ms about 0.36 s (all including the 196 ms stagger); the curve fits the
/// spring's angular frequency being <c>1 / Period</c>, so the real period is <c>2π × Period</c>. A
/// spring written as "220 ms" therefore ran about six times slower than anyone reading the number
/// would expect — the entrance read as a slow linear swell rather than a spring.
/// </para>
/// <para>
/// Spring motion is described in this codebase by the swing it should have, and that is what
/// <see cref="Period"/> takes. Keyframe animations are unaffected: their durations are honoured as
/// written (a 260 ms ease measured 260 ms).
/// </para>
/// </summary>
public static class SpringMotion
{
    /// <summary>
    /// The <c>Period</c> that gives a spring an oscillation lasting <paramref name="oscillationMs"/>.
    /// </summary>
    public static TimeSpan Period(double oscillationMs) =>
        TimeSpan.FromMilliseconds(oscillationMs / (2 * Math.PI));
}
