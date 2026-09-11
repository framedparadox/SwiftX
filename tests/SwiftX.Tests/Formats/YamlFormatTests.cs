using SwiftX.Models;
using SwiftX.Services.Formats;
using Xunit;

namespace SwiftX.Tests.Formats;

public class YamlFormatTests
{
    [Fact]
    public void Format_ReindentsWithRequestedIndent()
    {
        var result = YamlFormat.Instance.Format("a:\n  b: 1\n", new FormatOptions { IndentWidth = 4 });
        Assert.True(result.Success);
        Assert.Contains("\n    b: 1\n", result.Text);
    }

    [Fact]
    public void Format_DefaultIndentIsTwoSpaces()
    {
        var result = YamlFormat.Instance.Format("a:\n  b: 1\n", FormatOptions.Default);
        Assert.True(result.Success);
        Assert.Equal("a:\n  b: 1\n", result.Text);
    }

    [Fact]
    public void Format_ParsesBlockSequenceOfMappings()
    {
        const string text = """
            items:
              - name: a
                value: 1
              - name: b
                value: 2
            """;
        var result = YamlFormat.Instance.Format(text, FormatOptions.Default);
        Assert.True(result.Success);
        Assert.Contains("- name: a", result.Text);
        Assert.Contains("  value: 2", result.Text);
    }

    [Fact]
    public void Format_ParsesFlowCollections()
    {
        var result = YamlFormat.Instance.Format("a: {b: 1, c: [1, 2, 3]}", FormatOptions.Default);
        Assert.True(result.Success);
        Assert.Contains("b: 1", result.Text);
        Assert.Contains("- 1", result.Text);
    }

    [Fact]
    public void Format_StripsInlineCommentsButNotUrlFragments()
    {
        var result = YamlFormat.Instance.Format("a: value # trailing comment\nb: http://x#frag\n", FormatOptions.Default);
        Assert.True(result.Success);
        Assert.Contains("a: value\n", result.Text);
        Assert.Contains("http://x#frag", result.Text);
    }

    [Fact]
    public void Format_QuotesStringsThatWouldOtherwiseBeAmbiguous()
    {
        var node = new ObjectNode([("a", new ScalarNode("true", ScalarKind.String)), ("b", new ScalarNode("42", ScalarKind.String))]);
        var text = YamlFormat.Instance.FromCanonical(node, FormatOptions.Default);
        Assert.Contains("a: \"true\"", text);
        Assert.Contains("b: \"42\"", text);
    }

    [Fact]
    public void Format_InvalidYaml_NeverClearsUserText_ReturnsDiagnosticInstead()
    {
        var result = YamlFormat.Instance.Format("a: [1, 2\n", FormatOptions.Default);
        Assert.False(result.Success);
        Assert.Equal("", result.Text);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void Format_RejectsMultipleDocuments()
    {
        var result = YamlFormat.Instance.Format("a: 1\n---\nb: 2\n", FormatOptions.Default);
        Assert.False(result.Success);
    }

    [Fact]
    public void Format_RejectsTabIndentation()
    {
        var result = YamlFormat.Instance.Format("a:\n\tb: 1\n", FormatOptions.Default);
        Assert.False(result.Success);
    }

    [Fact]
    public void Minify_ProducesCompactFlowStyle()
    {
        var result = YamlFormat.Instance.Minify("a: 1\nb:\n  c: 2\n");
        Assert.True(result.Success);
        Assert.Equal("{\"a\":1,\"b\":{\"c\":2}}", result.Text);
    }

    [Fact]
    public void Validate_ReportsProblems()
    {
        var diagnostics = YamlFormat.Instance.Validate("a: [1, 2\n");
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void ToCanonical_RoundTripsThroughJson()
    {
        var node = YamlFormat.Instance.ToCanonical("name: Ada\nage: 36\nactive: true\ntags:\n  - x\n  - y\n");
        var json = JsonFormat.Instance.FromCanonical(node, FormatOptions.Default);
        Assert.Contains("\"name\": \"Ada\"", json);
        Assert.Contains("\"age\": 36", json);
        Assert.Contains("\"active\": true", json);
    }

    [Fact]
    public void DetectConfidence_RecognizesKeyValueLines()
    {
        Assert.True(YamlFormat.Instance.DetectConfidence("a: 1\nb: 2\n") > 0);
    }

    [Fact]
    public void DetectConfidence_DefersToJsonForBraceLedInput()
    {
        Assert.Equal(0, YamlFormat.Instance.DetectConfidence("{\"a\": 1}"));
    }
}
