using SwiftX.Services.Formats;
using Xunit;

namespace SwiftX.Tests.Formats;

public class JsonFormatTests
{
    [Fact]
    public void Format_PrettyPrintsWithRequestedIndent()
    {
        var result = JsonFormat.Instance.Format("""{"a":1}""", new FormatOptions { IndentWidth = 4 });
        Assert.True(result.Success);
        Assert.Contains("\n    \"a\"", result.Text);
    }

    [Fact]
    public void Format_InvalidJson_NeverClearsUserText_ReturnsDiagnosticInstead()
    {
        var result = JsonFormat.Instance.Format("{not valid", FormatOptions.Default);
        Assert.False(result.Success);
        Assert.Equal("", result.Text);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void Minify_RemovesWhitespace()
    {
        var result = JsonFormat.Instance.Minify("{\n  \"a\": 1\n}");
        Assert.True(result.Success);
        Assert.Equal("{\"a\":1}", result.Text);
    }

    [Fact]
    public void Validate_ReportsProblems()
    {
        var diagnostics = JsonFormat.Instance.Validate("{\n  \"a\": ,\n}");
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void Format_InvalidJson_ColumnIsCharacterBased_NotUtf8ByteBased()
    {
        // "é" is one UTF-16 character but two UTF-8 bytes. System.Text.Json's JsonException
        // reports BytePositionInLine as a raw UTF-8 byte offset (it parses by transcoding to
        // UTF-8 internally); the diagnostic has to convert that back to a character column so it
        // lines up with the editor's own caret math, not run one column ahead on any line with
        // non-ASCII text before the error.
        const string text = "{\"é\": bad}";
        var result = JsonFormat.Instance.Format(text, FormatOptions.Default);
        Assert.False(result.Success);
        int expectedColumn = text.IndexOf("bad", StringComparison.Ordinal) + 1;
        Assert.Equal(expectedColumn, result.Diagnostics[0].Column);
    }

    [Fact]
    public void Format_PreservesNonAsciiAndPunctuation_DoesNotOverEscape()
    {
        // The default System.Text.Json encoder escapes non-ASCII and a handful of HTML-sensitive
        // characters (<, >, &, ', +) as \uXXXX even outside a web context. Format/Minify are
        // supposed to rewrite structure and whitespace, not the user's string content.
        var result = JsonFormat.Instance.Format("""{"a":"café <b>&amp;</b> 'x'"}""", FormatOptions.Default);
        Assert.True(result.Success);
        Assert.Contains("café <b>&amp;</b> 'x'", result.Text);
    }
}
