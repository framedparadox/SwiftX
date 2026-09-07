using SwiftX.Services.Syntax;
using Xunit;

namespace SwiftX.Tests.Services.Syntax;

public class FoldFinderTests
{
    [Fact]
    public void FindJsonFolds_MultiLineObject_ReturnsOneRegionSpanningItsContent()
    {
        const string text = "{\n  \"a\": 1\n}";

        var regions = FoldFinder.FindJsonFolds(text);

        var region = Assert.Single(regions);
        Assert.Equal(text.IndexOf('{') + 1, region.Start);
        Assert.Equal(text.IndexOf('}'), region.End);
    }

    [Fact]
    public void FindJsonFolds_SingleLineObject_IsNotFoldable()
    {
        const string text = "{ \"a\": 1 }";

        var regions = FoldFinder.FindJsonFolds(text);

        Assert.Empty(regions);
    }

    [Fact]
    public void FindJsonFolds_NestedContainers_ReturnsOneRegionPerContainer()
    {
        const string text = "{\n  \"a\": [\n    1,\n    2\n  ]\n}";

        var regions = FoldFinder.FindJsonFolds(text);

        // The outer object and the inner array each span multiple lines, so both fold.
        Assert.Equal(2, regions.Count);
        var array = Assert.Single(regions, r => text[r.Start - 1] == '[');
        var obj = Assert.Single(regions, r => text[r.Start - 1] == '{');
        // The array's whole span sits inside the object's — proper nesting, not overlap.
        Assert.True(array.Start >= obj.Start && array.End <= obj.End);
    }

    [Fact]
    public void FindJsonFolds_BraceInsideStringValue_IsNotTreatedAsAContainer()
    {
        const string text = "{\n  \"a\": \"{ not a container }\"\n}";

        var regions = FoldFinder.FindJsonFolds(text);

        // Only the real outer object should fold — the braces inside the string value must not
        // be mistaken for a second, malformed container.
        var region = Assert.Single(regions);
        Assert.Equal(text.IndexOf('{') + 1, region.Start);
    }

    [Fact]
    public void FindJsonFolds_UnmatchedClosingBrace_IsIgnoredRatherThanThrowing()
    {
        const string text = "{\n  \"a\": 1\n}\n}";

        var regions = FoldFinder.FindJsonFolds(text);

        // The real object still folds; the stray trailing '}' has nothing to pop and is skipped.
        Assert.Single(regions);
    }

    [Fact]
    public void FindXmlFolds_MultiLineElement_ReturnsOneRegion()
    {
        const string text = "<root>\n  <child />\n</root>";

        var regions = FoldFinder.FindXmlFolds(text);

        var region = Assert.Single(regions);
        Assert.Equal(text.IndexOf('>') + 1, region.Start);
        Assert.Equal(text.IndexOf("</root>", StringComparison.Ordinal), region.End);
    }

    [Fact]
    public void FindXmlFolds_SingleLineElement_IsNotFoldable()
    {
        const string text = "<root><child/></root>";

        var regions = FoldFinder.FindXmlFolds(text);

        Assert.Empty(regions);
    }

    [Fact]
    public void FindXmlFolds_SelfClosingTag_NeverProducesARegionOfItsOwn()
    {
        const string text = "<root>\n  <child a=\"1\" b=\"2\" />\n  <child2/>\n</root>";

        var regions = FoldFinder.FindXmlFolds(text);

        // Only <root> spans multiple lines and has real (non-self-closing) content.
        var region = Assert.Single(regions);
        Assert.Equal(text.IndexOf('>') + 1, region.Start);
    }

    [Fact]
    public void FindXmlFolds_NestedElements_ReturnsOneRegionPerElement()
    {
        const string text = "<root>\n  <a>\n    <b>text</b>\n  </a>\n</root>";

        var regions = FoldFinder.FindXmlFolds(text);

        Assert.Equal(2, regions.Count);
        var root = Assert.Single(regions, r => text[r.Start - 1] == '>' && r.Start == text.IndexOf('>') + 1);
        var a = Assert.Single(regions, r => r.Start != root.Start);
        Assert.True(a.Start >= root.Start && a.End <= root.End);
    }

    [Fact]
    public void FindXmlFolds_MismatchedClosingTagName_IsIgnoredRatherThanThrowing()
    {
        const string text = "<root>\n  <a>text</wrong>\n</root>";

        var regions = FoldFinder.FindXmlFolds(text);

        // </wrong> doesn't match the top of the stack ("a"), so it's left alone rather than
        // guessed at — which leaves "a" stuck on the stack, so </root> (now facing "a" on top,
        // not "root") is left alone too. No crash, just no folds recovered past the mismatch —
        // the same cascading-error behavior a real XML parser would show for the same input.
        Assert.Empty(regions);
    }

    [Fact]
    public void Find_DispatchesOnFormatId()
    {
        Assert.Single(FoldFinder.Find("{\n\"a\":1\n}", "json"));
        Assert.Single(FoldFinder.Find("<a>\ntext\n</a>", "xml"));
        Assert.Empty(FoldFinder.Find("{\n\"a\":1\n}", "unknown-format"));
    }
}
