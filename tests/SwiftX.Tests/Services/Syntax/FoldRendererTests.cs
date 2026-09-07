using SwiftX.Services.Syntax;
using Xunit;

namespace SwiftX.Tests.Services.Syntax;

public class FoldRendererTests
{
    [Fact]
    public void Render_NothingCollapsed_ReturnsSourceUnchangedWithOneExpandedMarkerPerRegion()
    {
        const string source = "{\n  \"a\": [\n    1\n  ]\n}";
        var regions = FoldFinder.FindJsonFolds(source);
        Assert.Equal(2, regions.Count); // sanity: object + array

        var (display, markers) = FoldRenderer.Render(source, regions, new HashSet<int>());

        Assert.Equal(source, display);
        Assert.Equal(regions.Count, markers.Count);
        Assert.All(markers, m => Assert.False(m.Collapsed));
    }

    [Fact]
    public void Render_CollapsingTheOuterRegion_HidesEverythingInsideItIncludingNestedMarkers()
    {
        const string source = "{\n  \"a\": [\n    1\n  ]\n}";
        var regions = FoldFinder.FindJsonFolds(source);
        var obj = Assert.Single(regions, r => source[r.Start - 1] == '{');

        var (display, markers) = FoldRenderer.Render(source, regions, new HashSet<int> { obj.Start });

        Assert.Equal("{" + FoldRenderer.FoldPlaceholder + "}", display);
        // Only the collapsed object itself shows a marker — the array nested inside it never
        // renders at all, so it can't have a marker either.
        var marker = Assert.Single(markers);
        Assert.True(marker.Collapsed);
        Assert.Equal(1, marker.Line);
    }

    [Fact]
    public void Render_CollapsingOnlyTheInnerRegion_LeavesTheOuterTextIntact()
    {
        const string source = "{\n  \"a\": [\n    1\n  ]\n}";
        var regions = FoldFinder.FindJsonFolds(source);
        var array = Assert.Single(regions, r => source[r.Start - 1] == '[');

        var (display, markers) = FoldRenderer.Render(source, regions, new HashSet<int> { array.Start });

        Assert.Equal("{\n  \"a\": [" + FoldRenderer.FoldPlaceholder + "]\n}", display);
        Assert.Equal(2, markers.Count);
        var arrayMarker = Assert.Single(markers, m => m.Region.Start == array.Start);
        Assert.True(arrayMarker.Collapsed);
        Assert.Equal(2, arrayMarker.Line); // "  \"a\": [" is the second line
    }

    [Fact]
    public void Render_MarkerLineNumbers_AccountForEarlierCollapsedSiblings()
    {
        // Two independent (sibling, non-nested) objects, each spanning multiple lines.
        const string source = "{\n  \"first\": {\n    \"x\": 1\n  },\n  \"second\": {\n    \"y\": 2\n  }\n}";
        var regions = FoldFinder.FindJsonFolds(source);
        Assert.Equal(3, regions.Count); // outer + "first" + "second"

        int firstOpenBrace = source.IndexOf("\"first\": {", StringComparison.Ordinal) + "\"first\": {".Length;
        int secondOpenBrace = source.IndexOf("\"second\": {", StringComparison.Ordinal) + "\"second\": {".Length;
        var first = regions.Single(r => r.Start == firstOpenBrace);
        var second = regions.Single(r => r.Start == secondOpenBrace);

        var (_, markersBefore) = FoldRenderer.Render(source, regions, new HashSet<int>());
        var secondMarkerBefore = markersBefore.Single(m => m.Region.Start == second.Start);

        var (_, markersAfter) = FoldRenderer.Render(source, regions, new HashSet<int> { first.Start });
        var secondMarkerAfter = markersAfter.Single(m => m.Region.Start == second.Start);

        // Collapsing "first" (which spanned several lines) pulls everything after it up, so
        // "second"'s marker line strictly decreases — this is what catches a cursor/line-count
        // bug that forgets to skip the hidden span when advancing.
        Assert.True(secondMarkerAfter.Line < secondMarkerBefore.Line);
    }

    [Fact]
    public void Render_CollapsedNestedInsideAnotherCollapsedRegion_IsTreatedAsJustTheOuterOne()
    {
        const string source = "{\n  \"a\": [\n    1\n  ]\n}";
        var regions = FoldFinder.FindJsonFolds(source);
        var obj = Assert.Single(regions, r => source[r.Start - 1] == '{');
        var array = Assert.Single(regions, r => source[r.Start - 1] == '[');

        // Both collapsed at once: the array's collapse is redundant since the object already
        // hides it, and must not produce a second placeholder or a stray marker.
        var (display, markers) = FoldRenderer.Render(source, regions, new HashSet<int> { obj.Start, array.Start });

        Assert.Equal("{" + FoldRenderer.FoldPlaceholder + "}", display);
        Assert.Single(markers);
    }

    [Fact]
    public void Render_EmptyRegionList_ReturnsSourceUnchangedWithNoMarkers()
    {
        const string source = "plain text, nothing foldable";

        var (display, markers) = FoldRenderer.Render(source, new List<FoldRegion>(), new HashSet<int>());

        Assert.Equal(source, display);
        Assert.Empty(markers);
    }
}
