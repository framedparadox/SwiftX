using SwiftX.Services.Formats;
using Xunit;

namespace SwiftX.Tests.Formats;

public class XmlFormatSecurityTests
{
    [Fact]
    public void ExternalEntity_IsRejected_NotResolved()
    {
        const string xxe = """
            <?xml version="1.0"?>
            <!DOCTYPE root [ <!ENTITY xxe SYSTEM "file:///C:/Windows/win.ini"> ]>
            <root>&xxe;</root>
            """;

        var diagnostics = XmlFormat.Instance.Validate(xxe);
        Assert.NotEmpty(diagnostics);

        var result = XmlFormat.Instance.Format(xxe, FormatOptions.Default);
        Assert.False(result.Success);
    }

    [Fact]
    public void BillionLaughs_IsRefused_NotExpanded()
    {
        const string billionLaughs = """
            <?xml version="1.0"?>
            <!DOCTYPE lolz [
              <!ENTITY lol "lol">
              <!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
            ]>
            <lolz>&lol2;</lolz>
            """;

        var result = XmlFormat.Instance.Format(billionLaughs, FormatOptions.Default);
        Assert.False(result.Success);
    }

    [Fact]
    public void ValidXml_Formats()
    {
        var result = XmlFormat.Instance.Format("<root><a>1</a></root>", FormatOptions.Default);
        Assert.True(result.Success);
        Assert.Contains("<a>", result.Text);
    }

    // The two cases below are not security regressions — they are the correctness companions to
    // the XXE/billion-laughs guards above, and this is the one other XmlFormat.cs test file in
    // scope. XDocument.Load was called with LoadOptions.PreserveWhitespace, which kept every
    // whitespace-only text node between tags; XmlWriter's Indent option stops indenting an element
    // the moment it already has a text-node child, so both Format and Minify silently became
    // no-ops on any input that was not already on a single line.

    [Fact]
    public void Minify_StripsExistingWhitespaceBetweenElements()
    {
        var result = XmlFormat.Instance.Minify("<root>\n  <a>1</a>\n  <b>2</b>\n</root>");
        Assert.True(result.Success);
        Assert.Equal("<root><a>1</a><b>2</b></root>", result.Text);
    }

    [Fact]
    public void Format_ReindentsAlreadyIndentedInput_ToRequestedWidth()
    {
        var result = XmlFormat.Instance.Format("<root>\n<a>1</a>\n</root>", new FormatOptions { IndentWidth = 4 });
        Assert.True(result.Success);
        Assert.Contains("\n    <a>1</a>", result.Text);
    }
}
