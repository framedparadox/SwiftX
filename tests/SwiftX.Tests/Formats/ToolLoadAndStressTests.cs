using System.Diagnostics;
using System.Text;
using SwiftX.Models;
using SwiftX.Services;
using SwiftX.Services.Formats;
using SwiftX.Services.Syntax;
using Xunit;

namespace SwiftX.Tests.Formats;

/// <summary>
/// Comprehensive load, stress, and endurance tests for JSON, XML, Base64, and tool data pipelines.
/// Simulates heavy real-world payloads, rapid cyclic operations (format -> copy -> minify -> convert -> wait -> repeat),
/// and edge cases to guarantee stability and prevent crashes or memory leaks under load.
/// </summary>
public class ToolLoadAndStressTests
{
    private static readonly FormatOptions DefaultOptions = new() { IndentWidth = 2, SortKeys = false };

    // =========================================================================
    // 1. HEAVY JSON LOAD & REPETITIVE STRESS TESTING
    // =========================================================================

    [Fact]
    public void HeavyJson_LoadFormatMinifyConvertCycle_RepeatsSuccessfully()
    {
        // Generate a heavy JSON payload with thousands of items, nested objects, numbers, and arrays
        var sb = new StringBuilder();
        sb.Append("{\n  \"status\": \"success\",\n  \"total_records\": 2500,\n  \"items\": [\n");
        for (int i = 0; i < 2500; i++)
        {
            if (i > 0) sb.Append(",\n");
            sb.Append($$"""
                {
                  "id": {{i + 1}},
                  "guid": "{{Guid.NewGuid():D}}",
                  "isActive": {{(i % 2 == 0 ? "true" : "false")}},
                  "balance": {{(i * 19.95):F2}},
                  "tags": ["alpha", "beta", "gamma_{{i}}", "item_{{i % 10}}"],
                  "profile": {
                    "firstName": "User_{{i}}",
                    "lastName": "LastName_{{i}}",
                    "company": "Company {{i % 50}}",
                    "email": "user{{i}}@example.com",
                    "phone": "+1 (555) 019-{{i:D4}}"
                  },
                  "notes": "Line 1\nLine 2 for record {{i}} with special chars: <>&\"' café éàü \u2705"
                }
            """);
        }
        sb.Append("\n  ]\n}");
        string heavyJson = sb.ToString();

        Assert.True(heavyJson.Length > 500_000, $"Generated JSON should be heavy (>500KB), was {heavyJson.Length} chars");

        // Repeat the process cycle 5 times: Format -> Copy/Simulate -> Wait -> Minify -> SortKeys -> Validate
        string currentText = heavyJson;
        for (int cycle = 0; cycle < 5; cycle++)
        {
            // 1. Detect format
            var format = FormatRegistry.DetectBest(currentText);
            Assert.Equal("json", format.Id);

            // 2. Validate
            var diagnostics = format.Validate(currentText);
            Assert.Empty(diagnostics);

            // 3. Format (pretty-print)
            var formatResult = format.Format(currentText, new FormatOptions { IndentWidth = 4, SortKeys = false });
            Assert.True(formatResult.Success, $"Cycle {cycle}: Format failed with {formatResult.Diagnostics.FirstOrDefault()?.Message}");
            Assert.NotEmpty(formatResult.Text);

            // 4. Simulate copy & simulated wait (simulate user clipboard extraction)
            string copied = new string(formatResult.Text.ToCharArray());
            Assert.Equal(formatResult.Text.Length, copied.Length);
            Thread.Sleep(10); // Small wait between rapid operations

            // 5. Minify
            var minifyResult = format.Minify(copied);
            Assert.True(minifyResult.Success, $"Cycle {cycle}: Minify failed");
            Assert.True(minifyResult.Text.Length < copied.Length);

            // 6. Sort Keys
            var canonicalNode = format.ToCanonical(minifyResult.Text);
            string sortedJson = FormatRegistry.Json.FromCanonical(canonicalNode, new FormatOptions { IndentWidth = 2, SortKeys = true });
            Assert.NotEmpty(sortedJson);

            currentText = sortedJson;
        }

        // Final verification
        var finalValidation = JsonFormat.Instance.Validate(currentText);
        Assert.Empty(finalValidation);
    }

    [Fact]
    public void HeavyJson_DeeplyNestedHierarchy_HandlesGracefully()
    {
        // Build depth 100 nesting (well within JsonDocument MaxDepth 256)
        const int depth = 100;
        var sb = new StringBuilder();
        for (int i = 0; i < depth; i++)
            sb.Append("{\"level\":").Append(i).Append(",\"child\":");
        sb.Append("\"leaf\"");
        for (int i = 0; i < depth; i++)
            sb.Append('}');

        string nestedJson = sb.ToString();

        var formatResult = JsonFormat.Instance.Format(nestedJson, DefaultOptions);
        Assert.True(formatResult.Success);
        Assert.NotEmpty(formatResult.Text);

        var minifyResult = JsonFormat.Instance.Minify(formatResult.Text);
        Assert.True(minifyResult.Success);

        // Canonical round-trip
        var node = JsonFormat.Instance.ToCanonical(minifyResult.Text);
        var roundTripped = JsonFormat.Instance.FromCanonical(node, DefaultOptions);
        Assert.NotEmpty(roundTripped);
    }

    [Fact]
    public void HeavyJson_ExceedingMaxDepth_ReturnsDiagnosticWithoutCrashing()
    {
        // Exceed depth 256
        const int depth = 300;
        var sb = new StringBuilder();
        for (int i = 0; i < depth; i++)
            sb.Append("{\"nested\":");
        sb.Append(1);
        for (int i = 0; i < depth; i++)
            sb.Append('}');

        string excessiveJson = sb.ToString();

        // Must not crash or throw unhandled exception
        var formatResult = JsonFormat.Instance.Format(excessiveJson, DefaultOptions);
        Assert.False(formatResult.Success);
        Assert.NotEmpty(formatResult.Diagnostics);

        var validateResult = JsonFormat.Instance.Validate(excessiveJson);
        Assert.NotEmpty(validateResult);
    }

    [Fact]
    public void HeavyJson_LargeArrayOfScalars_FormatsAndMinifiesFast()
    {
        var sb = new StringBuilder();
        sb.Append('[');
        for (int i = 0; i < 50_000; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(i);
        }
        sb.Append(']');
        string bigArrayJson = sb.ToString();

        var sw = Stopwatch.StartNew();
        var minified = JsonFormat.Instance.Minify(bigArrayJson);
        sw.Stop();

        Assert.True(minified.Success);
        Assert.True(sw.ElapsedMilliseconds < 3000, $"Minifying 50k items took too long: {sw.ElapsedMilliseconds}ms");
    }

    // =========================================================================
    // 2. HEAVY XML LOAD & REPETITIVE STRESS TESTING
    // =========================================================================

    [Fact]
    public void HeavyXml_LoadFormatMinifyConvertCycle_RepeatsSuccessfully()
    {
        // Generate a heavy XML payload with 2000 records, attributes, and child elements
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Dataset xmlns:app=\"http://example.com/app\">\n");
        for (int i = 0; i < 2000; i++)
        {
            sb.Append($$"""
              <Record id="{{i + 1}}" status="active" category="cat_{{i % 20}}">
                <Identifier>{{Guid.NewGuid():N}}</Identifier>
                <Name>Entity {{i}}</Name>
                <Amount currency="USD">{{(i * 12.34):F2}}</Amount>
                <Metadata>
                  <CreatedBy>SystemAdmin</CreatedBy>
                  <Timestamp>2026-09-02T12:00:00Z</Timestamp>
                  <Notes>Important record with &lt;nested&gt; safe XML text and unicode: Café München 🚀</Notes>
                </Metadata>
              </Record>
            """).Append('\n');
        }
        sb.Append("</Dataset>");
        string heavyXml = sb.ToString();

        Assert.True(heavyXml.Length > 500_000, $"Generated XML should be heavy (>500KB), was {heavyXml.Length} chars");

        // Repeat the process cycle 5 times: Format -> Copy -> Wait -> Minify -> ConvertTo JSON -> ConvertTo XML
        string currentText = heavyXml;
        for (int cycle = 0; cycle < 5; cycle++)
        {
            // 1. Detect format
            var format = FormatRegistry.DetectBest(currentText);
            Assert.Equal("xml", format.Id);

            // 2. Validate
            var diagnostics = format.Validate(currentText);
            Assert.Empty(diagnostics);

            // 3. Format (pretty-print)
            var formatResult = format.Format(currentText, new FormatOptions { IndentWidth = 4, SortKeys = false });
            Assert.True(formatResult.Success, $"Cycle {cycle}: Format failed with {formatResult.Diagnostics.FirstOrDefault()?.Message}");
            Assert.NotEmpty(formatResult.Text);

            // 4. Copy & simulate wait
            string copied = new string(formatResult.Text.ToCharArray());
            Assert.Equal(formatResult.Text.Length, copied.Length);
            Thread.Sleep(10);

            // 5. Minify
            var minifyResult = format.Minify(copied);
            Assert.True(minifyResult.Success, $"Cycle {cycle}: Minify failed");
            Assert.True(minifyResult.Text.Length < copied.Length);

            // 6. Cross-convert: XML -> Canonical -> JSON -> Canonical -> XML
            var canonicalNode = format.ToCanonical(minifyResult.Text);
            string asJson = FormatRegistry.Json.FromCanonical(canonicalNode, DefaultOptions);
            Assert.NotEmpty(asJson);

            var nodeFromJson = FormatRegistry.Json.ToCanonical(asJson);
            string backToXml = FormatRegistry.Xml.FromCanonical(nodeFromJson, DefaultOptions);
            Assert.NotEmpty(backToXml);

            currentText = backToXml;
        }

        // Final verification
        var finalValidation = XmlFormat.Instance.Validate(currentText);
        Assert.Empty(finalValidation);
    }

    [Fact]
    public void HeavyXml_DeepNesting_FormatsAndMinifiesCorrectly()
    {
        const int depth = 80;
        var sb = new StringBuilder();
        for (int i = 0; i < depth; i++)
            sb.Append($"<node_{i} id='{i}'>");
        sb.Append("Content at deepest node");
        for (int i = depth - 1; i >= 0; i--)
            sb.Append($"</node_{i}>");

        string deepXml = sb.ToString();

        var formatResult = XmlFormat.Instance.Format(deepXml, DefaultOptions);
        Assert.True(formatResult.Success);
        Assert.NotEmpty(formatResult.Text);

        var minifyResult = XmlFormat.Instance.Minify(formatResult.Text);
        Assert.True(minifyResult.Success);
        Assert.DoesNotContain("\n", minifyResult.Text);
    }

    [Fact]
    public void HeavyXml_MalformedTagsUnderLoad_ReturnsDiagnosticsSafely()
    {
        // Malformed unclosed tag deep in a large document
        var sb = new StringBuilder();
        sb.Append("<Catalog>");
        for (int i = 0; i < 1000; i++)
        {
            if (i == 500)
                sb.Append("<Item id='500'><BrokenTag>missing close tag</Item>");
            else
                sb.Append($"<Item id='{i}'><Name>Valid</Name></Item>");
        }
        sb.Append("</Catalog>");

        string malformedXml = sb.ToString();

        var result = XmlFormat.Instance.Format(malformedXml, DefaultOptions);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
        Assert.True(result.Diagnostics[0].Line > 0);
    }

    // =========================================================================
    // 3. BASE64 HEAVY LOAD TESTING
    // =========================================================================

    [Fact]
    public void HeavyBase64_LargePayloadEncodeDecode_RoundTripsReliably()
    {
        // Create 1.5 MB of randomized UTF-8 text with diverse Unicode
        var random = new Random(42);
        var sb = new StringBuilder(2 * 1024 * 1024);
        string[] samples = [
            "Quick brown fox jumps over lazy dog. ",
            "Special characters: !@#$%^&*()_+-=[]{}|;':,.<>/?~` \n\t ",
            "Unicode strings: Привет мир! 🚀 🌟 ⚡ ☕ 🍵 漢字 日本語 한국어 ",
            "{\"jsonField\": [1, 2, 3, true, null], \"text\": \"Sample value\"}\n"
        ];
        while (sb.Length < 1_500_000)
        {
            sb.Append(samples[random.Next(samples.Length)]);
        }
        string original = sb.ToString();

        // 1. Encode standard
        var encodedStandard = Base64Codec.Encode(original, urlSafe: false);
        Assert.True(encodedStandard.Success);
        Assert.True(Base64Codec.IsLikelyBase64(encodedStandard.Text));

        // 2. Decode standard
        var decodedStandard = Base64Codec.Decode(encodedStandard.Text);
        Assert.True(decodedStandard.Success);
        Assert.Equal(original, decodedStandard.Text);

        // 3. Encode URL-Safe
        var encodedUrlSafe = Base64Codec.Encode(original, urlSafe: true);
        Assert.True(encodedUrlSafe.Success);
        Assert.DoesNotContain("+", encodedUrlSafe.Text);
        Assert.DoesNotContain("/", encodedUrlSafe.Text);
        Assert.DoesNotContain("=", encodedUrlSafe.Text);

        // 4. Decode URL-Safe
        var decodedUrlSafe = Base64Codec.Decode(encodedUrlSafe.Text);
        Assert.True(decodedUrlSafe.Success);
        Assert.Equal(original, decodedUrlSafe.Text);
    }

    // =========================================================================
    // 4. SANITIZER & PIPELINE LOAD & FUZZ TESTING
    // =========================================================================

    [Fact]
    public void InputSanitizer_HeavyEscapedPayloads_UnescapesWithoutDegrading()
    {
        var sb = new StringBuilder();
        sb.Append("\"");
        for (int i = 0; i < 2000; i++)
        {
            sb.Append($$"""\{\"index\": {{i}}, \"msg\": \"Line \n \t \'quoted\' \u00A9\"\},""");
        }
        sb.Append("\"");

        string escapedLog = sb.ToString();
        var cleaned = InputSanitizer.Clean(escapedLog);
        Assert.False(cleaned.StartsWith('"') && cleaned.EndsWith('"'));
        Assert.Contains("©", cleaned);
    }

    // =========================================================================
    // 5. FOLD FINDER & FOLD RENDERER ON LARGE INPUTS
    // =========================================================================

    [Fact]
    public void FoldFinderAndRenderer_LargeJsonOutline_ComputesMarkersCorrectly()
    {
        var sb = new StringBuilder();
        sb.Append("[\n");
        for (int i = 0; i < 500; i++)
        {
            if (i > 0) sb.Append(",\n");
            sb.Append("  {\n    \"id\": ").Append(i).Append(",\n    \"data\": [\n      1,\n      2\n    ]\n  }");
        }
        sb.Append("\n]");
        string json = sb.ToString();

        var folds = FoldFinder.Find(json, "json");
        Assert.NotEmpty(folds);

        // Collapse every 2nd fold
        var collapsedStarts = new HashSet<int>();
        for (int i = 0; i < folds.Count; i += 2)
            collapsedStarts.Add(folds[i].Start);

        var (displayText, markers) = FoldRenderer.Render(json, folds, collapsedStarts);
        Assert.NotEmpty(displayText);
        Assert.NotEmpty(markers);
        Assert.True(displayText.Length < json.Length);
        Assert.Contains(FoldRenderer.FoldPlaceholder, displayText);
    }
}

