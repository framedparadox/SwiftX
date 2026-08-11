using SwiftX.Services.Formats;
using Xunit;

namespace SwiftX.Tests.Formats;

public class InputSanitizerTests
{
    [Fact]
    public void Clean_StripsEnclosingQuotesAndUnescapes()
    {
        var input = "\"{\\\"a\\\":1}\"";
        var cleaned = InputSanitizer.Clean(input);
        Assert.Equal("{\"a\":1}", cleaned);
    }

    [Fact]
    public void Clean_UnescapesNewlinesAndTabs()
    {
        var cleaned = InputSanitizer.Clean("{\\n  \\\"a\\\": 1\\n}");
        Assert.Equal("{\n  \"a\": 1\n}", cleaned);
    }

    [Fact]
    public void StripEnclosingQuotes_LeavesInteriorQuotesAlone()
    {
        var json = "{\"a\":\"b\"}";
        Assert.Equal(json, InputSanitizer.StripEnclosingQuotes(json));
    }

    [Fact]
    public void Clean_TrimsWhitespace()
    {
        Assert.Equal("{\"a\":1}", InputSanitizer.Clean("  {\"a\":1}  "));
    }
}
