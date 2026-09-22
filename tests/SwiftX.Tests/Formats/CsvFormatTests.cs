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

    /// <summary>
    /// Prose with commas in it is not a table. This matters more than a mis-drawn label because
    /// Quick Format reads the clipboard the instant its window opens and formats whatever it
    /// detected with no confirmation step — so claiming an email or a paragraph as CSV means
    /// rewriting it. The quoted case is the one that does visible damage: a CSV round trip
    /// re-quotes any field carrying a quote character, turning
    /// <c>He said, "hello", then left.</c> into <c>He said," ""hello""", then left.</c>
    /// </summary>
    [Theory]
    [InlineData("He said, \"hello\", then left.\nShe replied, \"fine\".")]
    [InlineData("Dear Ada,\nThanks for the notes, they were useful.\nI will send the draft, the budget, and the plan.\nBest, Alan")]
    [InlineData("It arrived, finally.\nIt works, mostly.")]
    public void DetectConfidence_DoesNotClaimProseThatHappensToContainCommas(string prose)
    {
        Assert.Equal(0, CsvFormat.Instance.DetectConfidence(prose));

        // The guard is only worth anything if it survives the whole Quick Format path, which is
        // where the rewriting actually happened.
        var analyzed = QuickFormatter.Analyze(prose);
        Assert.Null(analyzed.Format);
        Assert.Equal(prose, analyzed.Text);
    }

    /// <summary>
    /// The other half of the same guard: it is narrow enough that real tables keep detecting.
    /// A sentence-shaped <em>cell</em> is ordinary data and must not disqualify the document —
    /// only a majority of sentence-shaped rows does, and a ragged table needs none at all.
    /// </summary>
    [Theory]
    [InlineData("a,b,c\n1,2,3\n4,5,6\n")]
    [InlineData("a,b,c\n1,2\n3,4,5\n")]
    [InlineData("name,note\n\"Ada, L\",\"said \"\"hi\"\"\"\n\"Alan T\",ok\n")]
    [InlineData("id,note\n1,Fix the bug.\n2,ok\n3,ok\n4,ok\n")]
    [InlineData("Shopping list\nApples, pears, plums\nBread, milk, cheese")]
    public void DetectConfidence_StillRecognizesRealTables(string csv)
    {
        Assert.True(
            CsvFormat.Instance.DetectConfidence(csv) > 0,
            $"the prose guard swallowed a real table: {csv.Replace("\n", "\\n")}");
    }
}
