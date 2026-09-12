using SwiftX.Services.Formats;
using Xunit;

namespace SwiftX.Tests.Formats;

/// <summary>
/// FormatRegistry.DetectBest is what QuickFormatPage and FormatterPage both lean on for
/// auto-detect — these exercise it across all four built-in formats together, since each format's
/// own DetectConfidence is only meaningful relative to the others (see FormatRegistry.All's
/// ordering note on JSON/XML taking priority on genuinely ambiguous input).
/// </summary>
public class FormatRegistryDetectionTests
{
    [Fact]
    public void DetectBest_PicksJsonForObjectShapedInput() =>
        Assert.Same(FormatRegistry.Json, FormatRegistry.DetectBest("{\"a\": 1, \"b\": [1, 2]}"));

    [Fact]
    public void DetectBest_PicksXmlForAngleBracketInput() =>
        Assert.Same(FormatRegistry.Xml, FormatRegistry.DetectBest("<root><a>1</a></root>"));

    [Fact]
    public void DetectBest_PicksYamlForKeyValueLines() =>
        Assert.Same(FormatRegistry.Yaml, FormatRegistry.DetectBest("name: Ada\nage: 36\ntags:\n  - x\n  - y\n"));

    [Fact]
    public void DetectBest_PicksCsvForConsistentMultiLineCommaRows() =>
        Assert.Same(FormatRegistry.Csv, FormatRegistry.DetectBest("name,age\nAda,36\nGrace,85\n"));

    [Fact]
    public void DetectBest_PicksYamlOverCsvWhenBothCouldMatch()
    {
        // A single "key: value, value2" line has one comma but is unambiguously a YAML mapping,
        // not a two-column CSV row — Yaml's colon-based signal should win here.
        var best = FormatRegistry.DetectBest("items: a, b\n");
        Assert.Same(FormatRegistry.Yaml, best);
    }

    [Fact]
    public void DetectBest_FallsBackToJsonForPlainProse()
    {
        // Free-form text with no structure at all still needs a default; Json is that default
        // (matching FormatRegistry.DetectBest's own starting value), even though it won't parse.
        var best = FormatRegistry.DetectBest("just some plain notes, nothing structured here");
        Assert.Same(FormatRegistry.Json, best);
    }
}
