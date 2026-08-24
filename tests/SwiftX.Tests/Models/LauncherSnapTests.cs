using SwiftX.Models;
using Windows.Graphics;
using Xunit;

namespace SwiftX.Tests.Models;

/// <summary>
/// Covers the floating launcher's placement rules. Everything the launcher decides about where to
/// sit lives in <see cref="LauncherSnap"/> as pure functions over physical screen rectangles, so
/// the interesting cases — corner drops, a launcher hidden on an edge shared with a second
/// monitor, a position left over from a display that has been unplugged — are all testable here
/// with no window, no display and no DPI involved.
/// </summary>
public class LauncherSnapTests
{
    // A single 1920x1080 monitor at the origin, minus a 40px taskbar along the bottom.
    private static readonly RectInt32 Primary = new(0, 0, 1920, 1040);

    // Second monitor immediately to the right of the first — they share the x = 1920 edge, which
    // is the one that makes hiding a cross-monitor problem.
    private static readonly RectInt32 Secondary = new(1920, 0, 1920, 1040);

    private const int Size = 46;
    private const int Notch = 7;
    private const int Threshold = 28;

    private static RectInt32 Launcher(int x, int y) => new(x, y, Size, Size);

    // ---- NearestEdge -------------------------------------------------------

    [Theory]
    [InlineData(4, 500, ScreenEdge.Left)]
    [InlineData(1920 - Size - 4, 500, ScreenEdge.Right)]
    [InlineData(900, 4, ScreenEdge.Top)]
    [InlineData(900, 1040 - Size - 4, ScreenEdge.Bottom)]
    public void NearestEdge_picks_the_edge_the_launcher_was_dropped_against(
        int x, int y, ScreenEdge expected)
    {
        Assert.Equal(expected, LauncherSnap.NearestEdge(Launcher(x, y), Primary, Threshold));
    }

    [Fact]
    public void NearestEdge_leaves_a_launcher_dropped_in_open_space_floating()
    {
        Assert.Equal(ScreenEdge.None, LauncherSnap.NearestEdge(Launcher(900, 500), Primary, Threshold));
    }

    [Fact]
    public void NearestEdge_resolves_a_corner_drop_to_the_vertical_edge()
    {
        // Equidistant from left and top. A vertical strip is the more useful dock, and it is the
        // one a later drag along that edge stays snapped to — see the doc comment.
        Assert.Equal(ScreenEdge.Left, LauncherSnap.NearestEdge(Launcher(6, 6), Primary, Threshold));
    }

    [Fact]
    public void NearestEdge_still_snaps_a_launcher_already_hanging_off_the_edge()
    {
        // Negative distance: the drop went past the edge rather than merely near it.
        Assert.Equal(ScreenEdge.Left, LauncherSnap.NearestEdge(Launcher(-20, 500), Primary, Threshold));
    }

    // ---- Hidden / peek positions -------------------------------------------

    [Fact]
    public void HiddenOrigin_leaves_exactly_the_notch_inside_the_work_area()
    {
        var hidden = LauncherSnap.HiddenOrigin(Launcher(4, 500), Primary, ScreenEdge.Left, Notch);

        Assert.Equal(Primary.X - (Size - Notch), hidden.X);
        // Only the docked axis moves; the position along the edge is the user's choice.
        Assert.Equal(500, hidden.Y);
        Assert.Equal(Notch, hidden.X + Size - Primary.X);
    }

    [Theory]
    [InlineData(ScreenEdge.Right)]
    [InlineData(ScreenEdge.Top)]
    [InlineData(ScreenEdge.Bottom)]
    public void HiddenOrigin_leaves_exactly_the_notch_inside_on_every_edge(ScreenEdge edge)
    {
        var hidden = LauncherSnap.HiddenOrigin(Launcher(900, 500), Primary, edge, Notch);
        var rect = new RectInt32(hidden.X, hidden.Y, Size, Size);
        var visible = LauncherSnap.ClipToWorkArea(rect, Primary);

        int across = edge is ScreenEdge.Left or ScreenEdge.Right ? visible.Width : visible.Height;
        Assert.Equal(Notch, across);
    }

    [Fact]
    public void HiddenOrigin_keeps_the_notch_from_sliding_past_the_ends_of_its_edge()
    {
        // Dropped below the bottom of the work area while docking to the left edge.
        var hidden = LauncherSnap.HiddenOrigin(Launcher(4, 5000), Primary, ScreenEdge.Left, Notch);

        Assert.Equal(Primary.Y + Primary.Height - Size, hidden.Y);
    }

    [Fact]
    public void PeekOrigin_brings_the_whole_launcher_back_inside()
    {
        var peek = LauncherSnap.PeekOrigin(Launcher(-39, 500), Primary, ScreenEdge.Left, margin: 0);
        var visible = LauncherSnap.ClipToWorkArea(new RectInt32(peek.X, peek.Y, Size, Size), Primary);

        Assert.Equal(Size, visible.Width);
        Assert.Equal(Size, visible.Height);
    }

    [Fact]
    public void Peek_and_hide_are_the_same_move_in_reverse()
    {
        var start = Launcher(4, 640);
        var hidden = LauncherSnap.HiddenOrigin(start, Primary, ScreenEdge.Left, Notch);
        var peek = LauncherSnap.PeekOrigin(
            new RectInt32(hidden.X, hidden.Y, Size, Size), Primary, ScreenEdge.Left, margin: 0);
        var reHidden = LauncherSnap.HiddenOrigin(
            new RectInt32(peek.X, peek.Y, Size, Size), Primary, ScreenEdge.Left, Notch);

        // Round-tripping must not drift along the edge, or a launcher would walk up or down the
        // screen a little further with every hover.
        Assert.Equal(hidden.X, reHidden.X);
        Assert.Equal(hidden.Y, reHidden.Y);
        Assert.Equal(640, peek.Y);
    }

    // ---- Multi-monitor clipping --------------------------------------------

    [Fact]
    public void A_launcher_hidden_on_a_shared_edge_shows_only_its_notch_and_only_on_its_own_monitor()
    {
        // Docked to the *left* edge of the second monitor — the edge it shares with the first.
        var hidden = LauncherSnap.HiddenOrigin(Launcher(1930, 400), Secondary, ScreenEdge.Left, Notch);
        var rect = new RectInt32(hidden.X, hidden.Y, Size, Size);

        // The window genuinely straddles the two monitors: hiding pushed most of it over.
        Assert.True(rect.X < Secondary.X);

        // On its own monitor, the notch and nothing more.
        var onOwner = LauncherSnap.ClipToWorkArea(rect, Secondary);
        Assert.Equal(Notch, onOwner.Width);
        Assert.Equal(Size, onOwner.Height);

        // And on the neighbour, which is where the hidden part physically lands, the clip is what
        // stops any of it being painted: nothing outside the owner's work area survives.
        int paintedRight = hidden.X + onOwner.X + onOwner.Width;
        Assert.Equal(Secondary.X + Notch, paintedRight);
        Assert.True(hidden.X + onOwner.X >= Secondary.X);
    }

    [Fact]
    public void ClipToWorkArea_reports_window_local_coordinates()
    {
        var rect = new RectInt32(-20, 500, Size, Size);
        var visible = LauncherSnap.ClipToWorkArea(rect, Primary);

        // 20px of the window is off the left of the work area, so the visible part starts 20px
        // into the window and is that much narrower.
        Assert.Equal(20, visible.X);
        Assert.Equal(0, visible.Y);
        Assert.Equal(Size - 20, visible.Width);
        Assert.Equal(Size, visible.Height);
    }

    [Fact]
    public void ClipToWorkArea_returns_nothing_for_a_window_entirely_off_the_display()
    {
        var visible = LauncherSnap.ClipToWorkArea(Launcher(-500, 500), Primary);

        Assert.Equal(0, visible.Width);
        Assert.Equal(0, visible.Height);
    }

    // ---- Owner resolution --------------------------------------------------

    [Fact]
    public void AnchorPoint_of_a_hidden_launcher_lands_on_the_monitor_it_is_docked_to()
    {
        var hidden = LauncherSnap.HiddenOrigin(Launcher(1930, 400), Secondary, ScreenEdge.Left, Notch);
        var rect = new RectInt32(hidden.X, hidden.Y, Size, Size);

        var anchor = LauncherSnap.AnchorPoint(rect, ScreenEdge.Left);
        Assert.True(Contains(Secondary, anchor));

        // The centre is the trap this exists to avoid: it sits on the *other* monitor, which is
        // why resolving the owner from it would clip the launcher to the neighbour and paint the
        // supposedly hidden part there in full.
        var centre = LauncherSnap.AnchorPoint(rect, ScreenEdge.None);
        Assert.True(Contains(Primary, centre));
        Assert.False(Contains(Secondary, centre));
    }

    [Fact]
    public void AnchorPoint_of_a_free_floating_launcher_is_its_centre()
    {
        var anchor = LauncherSnap.AnchorPoint(Launcher(900, 500), ScreenEdge.None);

        Assert.Equal(900 + Size / 2, anchor.X);
        Assert.Equal(500 + Size / 2, anchor.Y);
    }

    [Theory]
    [InlineData(ScreenEdge.Right)]
    [InlineData(ScreenEdge.Top)]
    [InlineData(ScreenEdge.Bottom)]
    public void AnchorPoint_stays_inside_the_work_area_for_every_docked_edge(ScreenEdge edge)
    {
        var hidden = LauncherSnap.HiddenOrigin(Launcher(900, 500), Primary, edge, Notch);
        var anchor = LauncherSnap.AnchorPoint(new RectInt32(hidden.X, hidden.Y, Size, Size), edge);

        Assert.True(Contains(Primary, anchor));
    }

    // ---- Restore validation ------------------------------------------------

    [Fact]
    public void IsReachable_accepts_a_launcher_hidden_behind_an_edge()
    {
        var hidden = LauncherSnap.HiddenOrigin(Launcher(4, 500), Primary, ScreenEdge.Left, Notch);

        Assert.True(LauncherSnap.IsReachable(
            new RectInt32(hidden.X, hidden.Y, Size, Size), Primary, minVisible: 2));
    }

    [Fact]
    public void IsReachable_rejects_a_position_left_over_from_a_display_that_is_gone()
    {
        // Saved while a second monitor was attached to the right; it since went away.
        Assert.False(LauncherSnap.IsReachable(Launcher(2400, 500), Primary, minVisible: 2));
    }

    [Fact]
    public void DefaultOrigin_puts_the_launcher_flush_with_the_right_edge_and_on_screen()
    {
        var origin = LauncherSnap.DefaultOrigin(Primary, Size, Size);
        var rect = new RectInt32(origin.X, origin.Y, Size, Size);

        Assert.Equal(Primary.X + Primary.Width - Size, origin.X);
        var visible = LauncherSnap.ClipToWorkArea(rect, Primary);
        Assert.Equal(Size, visible.Width);
        Assert.Equal(Size, visible.Height);
    }

    private static bool Contains(RectInt32 area, PointInt32 point) =>
        point.X >= area.X && point.X < area.X + area.Width
        && point.Y >= area.Y && point.Y < area.Y + area.Height;
}
