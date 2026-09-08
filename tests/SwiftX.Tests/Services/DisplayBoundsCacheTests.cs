using SwiftX.Services;
using SwiftX.Services.Syntax;
using Windows.Graphics;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// The containment test behind the hot corner's display cache, and the tokenizer nothing else
/// covers.
/// <para>
/// <c>HotCornerWatcher.BoundsFor</c> reuses the last resolved display rectangle while the pointer
/// is still inside it, which is what stops a 20 Hz poll making a WinRT activation every 50 ms for
/// the life of the process. The reuse is only correct if "still inside it" is exact: too generous
/// and a pointer that has crossed to the next monitor keeps the old monitor's corner, so the hot
/// corner fires at a coordinate that is nowhere near a corner; too strict and the cache never
/// hits and the optimisation does nothing. The edges are where both go wrong, so that is what is
/// asserted.
/// </para>
/// </summary>
public class DisplayBoundsCacheTests
{
    private static RectInt32 Rect(int x, int y, int w, int h) =>
        new() { X = x, Y = y, Width = w, Height = h };

    /// <summary>A point well inside is inside — the ordinary cache hit.</summary>
    [Fact]
    public void A_point_inside_the_rectangle_is_contained()
    {
        Assert.True(HotCornerWatcher.ContainsPoint(Rect(0, 0, 1920, 1080), 960, 540));
    }

    /// <summary>
    /// The rectangle is half-open: its origin belongs to it, its far edge does not. That is how
    /// Windows lays monitors out — a 1920-wide primary at x=0 owns columns 0..1919 and the
    /// secondary starts at 1920 — so treating the far edge as contained would let both monitors
    /// claim the same column and pin the pointer to whichever was cached first.
    /// </summary>
    [Theory]
    [InlineData(0, 0, true)]          // top-left origin: inside
    [InlineData(1919, 1079, true)]    // last real pixel: inside
    [InlineData(1920, 540, false)]    // first column of the next monitor
    [InlineData(960, 1080, false)]    // first row below
    [InlineData(-1, 540, false)]      // one left of the origin
    [InlineData(960, -1, false)]      // one above the origin
    public void The_rectangle_is_half_open_on_its_far_edges(int x, int y, bool expected)
    {
        Assert.Equal(expected, HotCornerWatcher.ContainsPoint(Rect(0, 0, 1920, 1080), x, y));
    }

    /// <summary>
    /// A monitor to the left of the primary has negative coordinates, and one above has negative
    /// Y — an ordinary multi-monitor arrangement, and the one a naive <c>x &lt; Width</c> check
    /// gets wrong by ignoring the origin entirely.
    /// </summary>
    [Fact]
    public void A_rectangle_at_negative_coordinates_still_contains_its_own_points()
    {
        var leftOfPrimary = Rect(-1920, -200, 1920, 1080);

        Assert.True(HotCornerWatcher.ContainsPoint(leftOfPrimary, -1920, -200));
        Assert.True(HotCornerWatcher.ContainsPoint(leftOfPrimary, -1, 879));
        Assert.False(HotCornerWatcher.ContainsPoint(leftOfPrimary, 0, 540));
    }

    /// <summary>
    /// A zero-extent rectangle contains nothing. This is load-bearing rather than pedantic: it is
    /// how <c>Configure</c> invalidates the cache — it assigns <c>default</c> and relies on the
    /// next poll missing. A containment test that admitted a point into an empty rectangle would
    /// make that invalidation silently do nothing, and the watcher would keep using stale bounds
    /// after the user changed corners.
    /// </summary>
    [Fact]
    public void An_empty_rectangle_contains_nothing()
    {
        Assert.False(HotCornerWatcher.ContainsPoint(default, 0, 0));
        Assert.False(HotCornerWatcher.ContainsPoint(Rect(0, 0, 0, 1080), 0, 0));
        Assert.False(HotCornerWatcher.ContainsPoint(Rect(0, 0, 1920, 0), 0, 0));
    }
}

/// <summary>
/// The fallback tokenizer, which had no coverage at all.
/// <para>
/// It is what every tool falls back to when a document is too large to colour, or when its format
/// failed to parse — so it runs on precisely the inputs the real tokenizers were not trusted
/// with, and a defect here shows up as a broken editor at exactly the wrong moment. The contract
/// is small: one <see cref="TokenKind.Plain"/> span covering everything, and nothing at all for
/// an empty document.
/// </para>
/// </summary>
public class PlainTokenizerTests
{
    /// <summary>
    /// One token, covering the whole document exactly. Both ends matter to <c>CodeView.Render</c>,
    /// which slices the text by token bounds: a span short of the end would silently drop the tail
    /// of the document from the view.
    /// </summary>
    [Theory]
    [InlineData("a")]
    [InlineData("{\"unparseable\": ")]
    [InlineData("line one\nline two\r\nline three")]
    public void A_non_empty_document_becomes_one_span_covering_all_of_it(string text)
    {
        var token = Assert.Single(PlainTokenizer.Instance.Tokenize(text));

        Assert.Equal(0, token.Start);
        Assert.Equal(text.Length, token.End);
        Assert.Equal(TokenKind.Plain, token.Kind);
    }

    /// <summary>
    /// An empty document produces no tokens rather than an empty span. A zero-length token would
    /// be a range the renderer has to special-case, and the renderer's own guard drops it anyway —
    /// so emitting one would be work that is always discarded.
    /// </summary>
    [Fact]
    public void An_empty_document_produces_no_tokens()
    {
        Assert.Empty(PlainTokenizer.Instance.Tokenize(""));
    }

    /// <summary>
    /// The tokenizer holds no state between calls. It is exposed as a process-wide singleton and
    /// reached from every tool window, so any state at all would be shared across threads.
    /// </summary>
    [Fact]
    public void The_singleton_is_stateless_across_calls()
    {
        Assert.Single(PlainTokenizer.Instance.Tokenize("first"));
        Assert.Empty(PlainTokenizer.Instance.Tokenize(""));
        var third = Assert.Single(PlainTokenizer.Instance.Tokenize("third document"));
        Assert.Equal("third document".Length, third.End);
    }

    /// <summary>
    /// The document is never resumable-scanned, so the state after any line is the initial one —
    /// which is what lets a caller switch to and from this tokenizer mid-document without carrying
    /// a scanner state that would mean nothing to the other one.
    /// </summary>
    [Fact]
    public void Scanner_state_is_always_the_initial_state()
    {
        const string text = "one\ntwo\nthree";
        var lines = new LineIndex(text);

        Assert.Equal(
            ScannerState.Initial,
            PlainTokenizer.Instance.StateAfter(text, lines, line: 1, ScannerState.Initial));
    }
}
