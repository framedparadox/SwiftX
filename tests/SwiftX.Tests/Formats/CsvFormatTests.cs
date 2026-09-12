using SwiftX.Models;
using SwiftX.Services.Formats;
using Xunit;

namespace SwiftX.Tests.Formats;

public class CsvFormatTests
{
    [Fact]
    public void Format_NormalizesLineEndingsAndQuoting()
    {
        var result = CsvFormat.Instance.Format("a,b\r\n1,\"x,y\"\r\n", FormatOptions.Default);
        Assert.True(result.Success);
        Assert.Equal("a,b\n1,\"x,y\"\n", result.Text);
    }

    [Fact]
    public void Format_UnquotesFieldsThatDontNeedQuoting()
    {
        var result = CsvFormat.Instance.Format("a,b\n\"1\",\"2\"\n", FormatOptions.Default);
        Assert.True(result.Success);
        Assert.Equal("a,b\n1,2\n", result.Text);
    }

    [Fact]
    public void Format_HandlesEscapedQuotesInsideAField()
    {
        // The field unescapes to the literal text say "hi" (with real quote characters), which
        // still contains a '"' — so it round-trips back through the same RFC 4180 quoting rather
        // than coming out bare.
        const string text = "a\n\"say \"\"hi\"\"\"\n";
        var result = CsvFormat.Instance.Format(text, FormatOptions.Default);
        Assert.True(result.Success);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void Format_AllowsRaggedRows()
    {
        var result = CsvFormat.Instance.Format("a,b,c\n1,2\n3,4,5,6\n", FormatOptions.Default);
        Assert.True(result.Success);
        Assert.Equal("a,b,c\n1,2\n3,4,5,6\n", result.Text);
    }

    [Fact]
    public void Format_InvalidCsv_NeverClearsUserText_ReturnsDiagnosticInstead()
    {
        var result = CsvFormat.Instance.Format("a,\"unterminated\n1,2\n", FormatOptions.Default);
        Assert.False(result.Success);
        Assert.Equal("", result.Text);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void Validate_ReportsUnterminatedQuote()
    {
        var diagnostics = CsvFormat.Instance.Validate("a,\"b\n");
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void ToCanonical_UsesFirstRowAsHeaders()
    {
        var node = CsvFormat.Instance.ToCanonical("name,age\nAda,36\nGrace,85\n");
        var arr = Assert.IsType<ArrayNode>(node);
        Assert.Equal(2, arr.Items.Count);
        var first = Assert.IsType<ObjectNode>(arr.Items[0]);
        Assert.Equal("name", first.Members[0].Key);
        Assert.Equal("Ada", ((ScalarNode)first.Members[0].Value).Raw);
        Assert.Equal("age", first.Members[1].Key);
        Assert.Equal("36", ((ScalarNode)first.Members[1].Value).Raw);
    }

    [Fact]
    public void ToCanonical_DisambiguatesDuplicateHeaders()
    {
        var node = CsvFormat.Instance.ToCanonical("name,name\na,b\n");
        var arr = Assert.IsType<ArrayNode>(node);
        var row = Assert.IsType<ObjectNode>(arr.Items[0]);
        Assert.Equal("name", row.Members[0].Key);
        Assert.Equal("name_2", row.Members[1].Key);
    }

    [Fact]
    public void ToCanonical_DisambiguatesHeaderThatWouldCollideWithASynthesizedName()
    {
        // Column 2's "foo" naively dedups to "foo_2", which collides with the literal "foo_2"
        // header already at column 1 — has to keep counting to "foo_3" rather than quietly
        // reusing "foo_2" for both.
        var node = CsvFormat.Instance.ToCanonical("foo,foo_2,foo\na,b,c\n");
        var arr = Assert.IsType<ArrayNode>(node);
        var row = Assert.IsType<ObjectNode>(arr.Items[0]);
        Assert.Equal("foo", row.Members[0].Key);
        Assert.Equal("foo_2", row.Members[1].Key);
        Assert.Equal("foo_3", row.Members[2].Key);
    }

    [Fact]
    public void FromCanonical_RoundTripsAllColumns_WhenAHeaderWouldCollideWithASynthesizedName()
    {
        var node = CsvFormat.Instance.ToCanonical("foo,foo_2,foo\na,b,c\n");
        var csv = CsvFormat.Instance.FromCanonical(node, FormatOptions.Default);
        Assert.Equal("foo,foo_2,foo_3\na,b,c\n", csv);
    }

    [Fact]
    public void FromCanonical_WritesHeaderRowFromArrayOfObjects()
    {
        var node = new ArrayNode([
            new ObjectNode([("name", new ScalarNode("Ada", ScalarKind.String)), ("age", new ScalarNode("36", ScalarKind.Number))]),
            new ObjectNode([("name", new ScalarNode("Grace", ScalarKind.String)), ("age", new ScalarNode("85", ScalarKind.Number))]),
        ]);
        var csv = CsvFormat.Instance.FromCanonical(node, FormatOptions.Default);
        Assert.Equal("name,age\nAda,36\nGrace,85\n", csv);
    }

    [Fact]
    public void DetectConfidence_RecognizesConsistentMultiLineCsv()
    {
        Assert.True(CsvFormat.Instance.DetectConfidence("a,b,c\n1,2,3\n4,5,6\n") > 0);
    }

    [Fact]
    public void DetectConfidence_DefersToJsonForBraceLedInput()
    {
        Assert.Equal(0, CsvFormat.Instance.DetectConfidence("{\"a\": 1}"));
    }
}
