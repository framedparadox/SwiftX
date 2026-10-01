namespace SwiftX.Services;

/// <summary>
/// When the Settings Home screen's two logo marks may animate.
/// <para>
/// <b>Why the marks need a policy at all.</b> The Orbit mark turns its ring on a composition
/// animation that never ends and the Tools mark replays a tile-spin every five seconds. Together
/// they cost <b>~19% of one CPU core, continuously</b>, for as long as the window is open — measured
/// on a Release build with the window open and untouched (the ring alone 14%, the tile-spin alone
/// 8%, neither 0.3%). Settings is a window people leave open behind other windows, so that was a
/// quarter of a laptop core and its fan for a decoration nobody was looking at.
/// </para>
/// <para>
/// The marks now play only while someone can be looking: the window is the active one, Home is the
/// screen showing, Windows has not been asked to reduce motion, and the person arrived or moved the
/// pointer or pressed a key within <see cref="RestAfter"/>. Everything else leaves them at rest,
/// which is the static logo.
/// </para>
/// </summary>
internal static class HomeIdlePolicy
{
    /// <summary>How long the marks keep playing after the last arrival on Home or the last
    /// pointer / keyboard activity in the window.</summary>
    internal static readonly TimeSpan RestAfter = TimeSpan.FromSeconds(20);

    internal static bool ShouldAnimate(
        bool onHome, bool windowActive, bool reducedMotion, bool closed, TimeSpan sinceActivity) =>
        onHome && windowActive && !reducedMotion && !closed && sinceActivity < RestAfter;
}
