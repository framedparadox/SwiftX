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
/// The marks play whenever someone is plainly in the window: Home is the screen showing, the window
/// is on screen (not minimised), and either it is the active window or the person arrived on Home,
/// moved the pointer over it or pressed a key within <see cref="RestAfter"/>. So they run for as
/// long as Settings is in front, wake for a while when the pointer passes over it in the
/// background, and rest at the static logo once it has been left behind other windows.
/// </para>
/// <para>
/// The first version of this rule asked for all of those at once, which stopped the marks after
/// 20 s even with the window in front and the person looking at it, and never woke them in a
/// visible window that was not the active one. It also left them still whenever Windows'
/// "Animation effects" setting was off, which is the case in most Remote Desktop sessions and VMs
/// and on machines tuned for performance. To the person using the app, every one of those read as
/// "the Home animations do not play". The marks are a brand flourish, like the orbit's own
/// entrance (which never consulted that setting), so they do not consult it either.
/// </para>
/// </summary>
internal static class HomeIdlePolicy
{
    /// <summary>How long the marks keep playing in a window that is not the active one, after the
    /// last arrival on Home or the last pointer / keyboard activity in it.</summary>
    internal static readonly TimeSpan RestAfter = TimeSpan.FromSeconds(20);

    internal static bool ShouldAnimate(
        bool onHome, bool windowShown, bool windowActive, bool closed, TimeSpan sinceActivity) =>
        onHome && windowShown && !closed && (windowActive || sinceActivity < RestAfter);
}
