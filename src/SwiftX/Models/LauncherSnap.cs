using Windows.Graphics;

namespace SwiftX.Models;

/// <summary>Which work-area edge the floating launcher is docked against.</summary>
public enum ScreenEdge
{
    None,
    Left,
    Top,
    Right,
    Bottom,
}

/// <summary>
/// Pure geometry for the floating launcher's edge snapping. Every member here is a static
/// function over rectangles in <b>physical screen pixels</b> — no HWND, no AppWindow, no DPI
/// lookup — which is what lets the whole snapping rulebook be unit-tested without a display.
/// <para>
/// Physical pixels rather than DIPs on purpose: a multi-monitor desktop can mix scale factors, so
/// the only coordinate space in which "this window overlaps that monitor" is a meaningful question
/// is the virtual screen's own. Callers convert their DIP constants once, at the edge (see
/// <see cref="LauncherWindow"/>), and everything below stays integer arithmetic.
/// </para>
/// </summary>
public static class LauncherSnap
{
    /// <summary>How much of the launcher stays on-screen when it is hidden behind an edge.</summary>
    public const double NotchDip = 7;

    /// <summary>Center-to-edge distance (DIP) within which a drop snaps to that edge.</summary>
    public const double SnapThresholdDip = 28;

    /// <summary>
    /// Gap between the edge and the launcher when it peeks out under the pointer. Zero, and
    /// deliberately so: the pointer that triggered the peek is sitting on the notch, i.e. within
    /// the first <see cref="NotchDip"/> pixels of the edge, so any positive margin slides the
    /// launcher out from under its own cursor. That fires a pointer-exit, which schedules the
    /// hide, which slides it back under the cursor — a launcher that flickers in and out for as
    /// long as you hover it. Flush against the edge, the notch stays part of the peeked button and
    /// the pointer never leaves.
    /// </summary>
    public const double PeekMarginDip = 0;

    // ---- Edge selection ----------------------------------------------------

    /// <summary>
    /// The work-area edge <paramref name="window"/> is close enough to snap to, or
    /// <see cref="ScreenEdge.None"/> when it was dropped out in open space.
    /// <para>
    /// Distance is measured from the window's near edge to the matching work-area edge, so a
    /// window already hanging off the edge (a negative distance) reads as nearer still. Ties go to
    /// the vertical edges: dropping into a corner leaves a launcher docked left or right, which is
    /// both the more useful strip and the one a subsequent drag along the edge stays snapped to.
    /// </para>
    /// </summary>
    public static ScreenEdge NearestEdge(RectInt32 window, RectInt32 workArea, int threshold)
    {
        int left = window.X - workArea.X;
        int right = Right(workArea) - Right(window);
        int top = window.Y - workArea.Y;
        int bottom = Bottom(workArea) - Bottom(window);

        var edge = ScreenEdge.Left;
        int best = left;
        // Strict "<" throughout, so the earlier-tested edge wins a tie: Left, Right, then the
        // horizontal pair. See the doc comment on why a corner resolves to a vertical edge.
        if (right < best) { best = right; edge = ScreenEdge.Right; }
        if (top < best) { best = top; edge = ScreenEdge.Top; }
        if (bottom < best) { best = bottom; edge = ScreenEdge.Bottom; }

        return best <= threshold ? edge : ScreenEdge.None;
    }

    // ---- Docked positions --------------------------------------------------

    /// <summary>
    /// Where the launcher sits while hidden: pushed out through <paramref name="edge"/> until only
    /// <paramref name="notch"/> pixels of it remain inside the work area. That sliver is the notch
    /// the pointer hunts for.
    /// <para>
    /// The along-edge coordinate is preserved from <paramref name="window"/> — a launcher docked
    /// two thirds of the way down the left edge stays there — but clamped so the notch cannot end
    /// up past the ends of the edge it is docked to.
    /// </para>
    /// </summary>
    public static PointInt32 HiddenOrigin(RectInt32 window, RectInt32 workArea, ScreenEdge edge, int notch)
    {
        // A notch wider than the launcher itself would place it *inside* the work area and defeat
        // the hiding; a non-positive one would swallow it whole with nothing left to hover.
        notch = Math.Clamp(notch, 1, Math.Max(1, Math.Min(window.Width, window.Height)));

        return edge switch
        {
            ScreenEdge.Left => new PointInt32(
                workArea.X - (window.Width - notch), ClampY(window, workArea)),
            ScreenEdge.Right => new PointInt32(
                Right(workArea) - notch, ClampY(window, workArea)),
            ScreenEdge.Top => new PointInt32(
                ClampX(window, workArea), workArea.Y - (window.Height - notch)),
            ScreenEdge.Bottom => new PointInt32(
                ClampX(window, workArea), Bottom(workArea) - notch),
            _ => new PointInt32(window.X, window.Y),
        };
    }

    /// <summary>
    /// Where the launcher slides to when the pointer finds its notch: fully inside the work area,
    /// held <paramref name="margin"/> pixels off the edge so it reads as offered up rather than
    /// jammed against the border. The along-edge coordinate is preserved, exactly as when hidden,
    /// so peeking is a straight slide out and back.
    /// </summary>
    public static PointInt32 PeekOrigin(RectInt32 window, RectInt32 workArea, ScreenEdge edge, int margin) =>
        edge switch
        {
            ScreenEdge.Left => new PointInt32(workArea.X + margin, ClampY(window, workArea)),
            ScreenEdge.Right => new PointInt32(
                Right(workArea) - window.Width - margin, ClampY(window, workArea)),
            ScreenEdge.Top => new PointInt32(ClampX(window, workArea), workArea.Y + margin),
            ScreenEdge.Bottom => new PointInt32(
                ClampX(window, workArea), Bottom(workArea) - window.Height - margin),
            _ => new PointInt32(window.X, window.Y),
        };

    // ---- Multi-monitor clipping -------------------------------------------

    /// <summary>
    /// The part of <paramref name="window"/> that lies inside <paramref name="workArea"/>,
    /// expressed in <b>window-local</b> pixels ready for <c>SetWindowRgn</c>. An empty rect means
    /// nothing of the window may paint at all.
    /// <para>
    /// This is what makes edge snapping behave on a multi-monitor desktop. Hiding works by pushing
    /// the launcher out through an edge, and on an <em>interior</em> edge — one shared with the
    /// next monitor along — the pushed-out part is not off the desktop at all: it lands squarely
    /// on the neighbour, where a launcher "hidden" on this screen would show up as a mystery blob
    /// floating on that one. Clipping the window to its own monitor's work area means the hidden
    /// part is not drawn anywhere, so a launcher docked on the shared edge hides on this monitor
    /// and stays absent from the other, which is the whole point.
    /// </para>
    /// </summary>
    public static RectInt32 ClipToWorkArea(RectInt32 window, RectInt32 workArea)
    {
        int left = Math.Max(window.X, workArea.X);
        int top = Math.Max(window.Y, workArea.Y);
        int right = Math.Min(Right(window), Right(workArea));
        int bottom = Math.Min(Bottom(window), Bottom(workArea));

        if (right <= left || bottom <= top)
            return new RectInt32(0, 0, 0, 0);

        return new RectInt32(left - window.X, top - window.Y, right - left, bottom - top);
    }

    /// <summary>
    /// A point on the launcher that is guaranteed to lie on the launcher's <em>own</em> monitor,
    /// whatever its dock state — which is how the owning monitor is identified.
    /// <para>
    /// The obvious candidate, the window's center, is wrong for a docked launcher and wrong in the
    /// one case that matters most: hidden behind an edge, most of the window — its center
    /// included — sits past that edge, so on a shared edge the center lands on the
    /// <em>neighbouring</em> monitor. Resolving the owner from it would pick the neighbour, clip
    /// the launcher to the neighbour's work area, and paint the "hidden" launcher in full on the
    /// screen it is supposed to be absent from.
    /// </para>
    /// <para>
    /// The far edge is the fix: hiding pushes the launcher out through the docked edge, so the
    /// side <em>opposite</em> that edge is the part still inside the owner's work area. For an
    /// undocked launcher the center is inside by definition and is used as-is.
    /// </para>
    /// </summary>
    public static PointInt32 AnchorPoint(RectInt32 window, ScreenEdge edge)
    {
        int midX = window.X + window.Width / 2;
        int midY = window.Y + window.Height / 2;

        return edge switch
        {
            // Right() / Bottom() are one past the last pixel of the window, hence the -1.
            ScreenEdge.Left => new PointInt32(Right(window) - 1, midY),
            ScreenEdge.Right => new PointInt32(window.X, midY),
            ScreenEdge.Top => new PointInt32(midX, Bottom(window) - 1),
            ScreenEdge.Bottom => new PointInt32(midX, window.Y),
            _ => new PointInt32(midX, midY),
        };
    }

    // ---- Restore validation ------------------------------------------------

    /// <summary>
    /// True when at least <paramref name="minVisible"/> pixels of <paramref name="window"/>, in
    /// each axis, fall inside <paramref name="workArea"/> — i.e. that display is showing enough of
    /// the launcher for it to be the one the launcher lives on.
    /// <para>
    /// A persisted position outlives the display layout that produced it — a laptop undocked from
    /// its second monitor, a resolution change — and a launcher restored onto a monitor that is no
    /// longer there is one the user can neither see nor reach. Callers fall back to a default
    /// position when this test fails.
    /// </para>
    /// </summary>
    public static bool IsReachable(RectInt32 window, RectInt32 workArea, int minVisible)
    {
        var visible = ClipToWorkArea(window, workArea);
        return visible.Width >= minVisible && visible.Height >= minVisible;
    }

    /// <summary>
    /// The launcher's out-of-the-box home: docked to the right edge, a little below the vertical
    /// middle, clear of both the notification area and anything centred on screen.
    /// </summary>
    public static PointInt32 DefaultOrigin(RectInt32 workArea, int width, int height) =>
        new(Right(workArea) - width, workArea.Y + (int)(workArea.Height * 0.55) - height / 2);

    // ---- Helpers -----------------------------------------------------------

    private static int Right(RectInt32 r) => r.X + r.Width;

    private static int Bottom(RectInt32 r) => r.Y + r.Height;

    /// <summary>
    /// Keeps the launcher's along-edge coordinate inside the edge it is docked to. The
    /// <c>Math.Max</c> guards the degenerate case of a work area shorter than the launcher, where
    /// the clamp's own bounds would otherwise cross over and throw.
    /// </summary>
    private static int ClampY(RectInt32 window, RectInt32 workArea) =>
        Math.Clamp(window.Y, workArea.Y, Math.Max(workArea.Y, Bottom(workArea) - window.Height));

    private static int ClampX(RectInt32 window, RectInt32 workArea) =>
        Math.Clamp(window.X, workArea.X, Math.Max(workArea.X, Right(workArea) - window.Width));
}
