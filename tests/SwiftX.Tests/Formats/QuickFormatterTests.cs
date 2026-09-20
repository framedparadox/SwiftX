using System.Diagnostics;
using System.Text;
using SwiftX.Services;
using SwiftX.Services.Formats;
using SwiftX.Services.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace SwiftX.Tests.Formats;

/// <summary>
/// Cover for <see cref="QuickFormatter"/> — the whole of Quick Format's "what do I do with this
/// text" decision, extracted from the page so it can be asserted without a UI thread.
/// <para>
/// Quick Format is the one tool that runs without being asked: it formats whatever is on the
/// clipboard the moment its window opens, and again on every paste. That gives its failure modes a
/// different shape from every other tool's. A tool that throws when you press its button shows you
/// an error; this one was running inside a discarded <c>Task</c>, where the runtime swallows the
/// exception — so the same fault surfaced as a window that opened and then showed nothing at all,
/// forever, with no message and nothing to retry. Hence <see cref="Analyze_NeverThrows_ForHostileInput"/>:
/// "it returns something" is the contract, and every case below is one of the somethings.
/// </para>
/// </summary>
public class QuickFormatterTests(ITestOutputHelper output)
{
    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    // =========================================================================================
    // 1. The three outcomes: formatted, invalid, plain
    // =========================================================================================

    [Fact]
    public void ValidJson_IsFormattedAtTwoSpaces_AndNamedAsJson()
    {
        var result = QuickFormatter.Analyze("""{"name":"Ada","n":1}""");

        Assert.False(result.IsInvalid);
        Assert.Equal("json", result.Format?.Id);
        Assert.Same(JsonTokenizer.Instance, result.Tokenizer);
        Assert.Equal(Normalize("""
            {
              "name": "Ada",
              "n": 1
            }
            """), Normalize(result.Text));
    }

    [Theory]
    [InlineData("<root><a>1</a></root>", "xml")]
    [InlineData("name: Ada\nrole: engineer\n", "yaml")]
    [InlineData("id,name\n1,Ada\n2,Grace\n", "csv")]
    public void EachSupportedFormat_IsDetectedAndFormatted(string text, string expectedId)
    {
        var result = QuickFormatter.Analyze(text);

        Assert.False(result.IsInvalid);
        Assert.Equal(expectedId, result.Format?.Id);
        Assert.NotEmpty(result.Text);
    }

    /// <summary>
    /// The single most common thing to paste into this tool is JSON that is broken — that is why
    /// you are looking at it. It has to come back flagged, with the text intact, rather than
    /// quietly downgraded to "plain text" because the parser could not make sense of it.
    /// </summary>
    [Theory]
    [InlineData("""{"name":"Ada","n":1""")]                   // truncated mid-document
    [InlineData("""{"name":"Ada",}""")]                        // trailing comma
    [InlineData("""[1,2,3""")]                                 // unclosed array
    public void BrokenJson_IsFlaggedAsInvalid_AndKeepsTheUsersTextVerbatim(string text)
    {
        var result = QuickFormatter.Analyze(text);

        Assert.True(result.IsInvalid);
        Assert.Equal("json", result.Format?.Id);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void BrokenXml_IsFlaggedAsInvalid_AndReportsWhere()
    {
        var result = QuickFormatter.Analyze("<root><a>1</a>");

        Assert.True(result.IsInvalid);
        Assert.Equal("xml", result.Format?.Id);
        Assert.NotNull(result.Problem);
    }

    /// <summary>
    /// Prose was never structured data, so it is shown as it was copied — no format name, no
    /// warning glyph. A sentence containing a colon ("Note: see the attached file.") is the shape
    /// that most easily gets mistaken for YAML, so it is the one pinned here.
    /// </summary>
    [Theory]
    [InlineData("Note: see the attached file.")]
    [InlineData("just some words, and a comma")]
    [InlineData("   ")]
    public void ProseIsLeftAlone_WithNoFormatAndNoWarning(string text)
    {
        var result = QuickFormatter.Analyze(text);

        Assert.False(result.IsInvalid);
        Assert.Null(result.Format);
        Assert.Same(PlainTokenizer.Instance, result.Tokenizer);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void EmptyInput_ProducesAnEmptyPlainResult()
    {
        var result = QuickFormatter.Analyze("");

        Assert.False(result.IsInvalid);
        Assert.Null(result.Format);
        Assert.Equal("", result.Text);
    }

    /// <summary>
    /// The paste-cleanup pass (<see cref="InputSanitizer.Clean"/>) is deliberately <em>not</em> run
    /// here: its unescape step turns any backslash-letter pair it does not recognize into just the
    /// letter, so an ordinary Windows path copied to the clipboard would lose its separators the
    /// instant this tool opened, with nothing on screen to say it had happened.
    /// </summary>
    [Fact]
    public void OrdinaryClipboardText_IsNeverUnescaped()
    {
        const string path = @"C:\Users\name\notes.csv";

        var result = QuickFormatter.Analyze(path);

        Assert.Equal(path, result.Text);
    }

    // =========================================================================================
    // 2. Never throwing — the contract that keeps the window from opening onto nothing
    // =========================================================================================

    /// <summary>
    /// Each format's <c>Format</c> catches the one exception type its own parser is documented to
    /// raise and nothing else, so an allocation failure, a depth guard tripping on a path that does
    /// not declare it, or a writer refusing a document its reader accepted, all escape as something
    /// no caller declared. On the Quick Format path there was nobody to catch it: the call sat
    /// inside a discarded <c>Task</c>, whose exception the runtime drops on the floor.
    /// </summary>
    [Fact]
    public void Analyze_NeverThrows_ForHostileInput()
    {
        var hostile = new List<string>
        {
            new('{', 20_000),
            new('[', 20_000),
            new('<', 20_000),
            new('"', 20_000),
            new('\\', 20_000),
            string.Concat(Enumerable.Repeat("<a>", 2_000)),   // unclosed all the way down
            string.Concat(Enumerable.Repeat("[", 20_000)) + "1",
            string.Concat(Enumerable.Repeat("a:\n b:\n", 5_000)),
            "\t- tabs: are not legal yaml indentation",
            "- &anchor\n- *alias\n",
            "a,\"unterminated\nb,c\n",
            "\0\0\0\u0001\u0002",
            "{\"a\":\"\ud800\"}",      // a lone high surrogate
            "--- one\n--- two\n",      // multiple YAML documents
        };

        foreach (var text in hostile)
        {
            var thrown = Record.Exception(() => QuickFormatter.Analyze(text));
            Assert.Null(thrown);
        }
    }

    /// <summary>
    /// The scanners and parsers run against whatever half of a document happened to be copied, so
    /// a truncation at every single boundary is the cheap way to cover the "walked off the end"
    /// family — and every one of them still has to come back as a result.
    /// </summary>
    [Fact]
    public void Analyze_OnEveryTruncationOfADocument_ReturnsAResult()
    {
        const string json = """{"a":[1,2,{"b":"text with \" escape"}],"c":null}""";
        const string xml = """<root a="1"><!-- note --><child><![CDATA[raw]]></child></root>""";
        const string yaml = "root:\n  items:\n    - one\n    - two\n  flag: true\n";

        foreach (var document in new[] { json, xml, yaml })
        {
            for (int cut = 0; cut <= document.Length; cut++)
            {
                var slice = document[..cut];
                var thrown = Record.Exception(() => QuickFormatter.Analyze(slice));
                Assert.Null(thrown);
            }
        }
    }

    /// <summary>
    /// Nesting past a format's depth ceiling is refused by the formats themselves — the point here
    /// is that Quick Format turns that refusal into the invalid-data state rather than letting the
    /// exception out, since <c>&lt;a&gt;</c> repeated a few thousand times is a few kilobytes of
    /// text that anyone could have on their clipboard.
    /// <para>
    /// A couple of thousand levels rather than the tens of thousands
    /// <c>CrashAndHangRegressionTests</c> uses: both are far past the 256-level ceiling, both take
    /// the same path, and the larger figure costs this suite sixteen seconds because building the
    /// document tree to the point of refusal is itself superlinear in depth. That cost belongs to
    /// the formats and is pinned over there; what is being asserted here is only what Quick Format
    /// makes of the refusal.
    /// </para>
    /// </summary>
    [Fact]
    public void NestingPastTheFormatCeilings_ComesBackAsInvalidRatherThanAsAThrow()
    {
        var xml = string.Concat(Enumerable.Repeat("<a>", 2_000)) + "leaf" +
                  string.Concat(Enumerable.Repeat("</a>", 2_000));
        var json = string.Concat(Enumerable.Repeat("[", 2_000)) + "1" +
                   string.Concat(Enumerable.Repeat("]", 2_000));

        var xmlResult = QuickFormatter.Analyze(xml);
        Assert.True(xmlResult.IsInvalid);
        Assert.Equal("xml", xmlResult.Format?.Id);

        var jsonResult = QuickFormatter.Analyze(json);
        Assert.True(jsonResult.IsInvalid);
        Assert.Equal("json", jsonResult.Format?.Id);
    }

    // =========================================================================================
    // 3. Detection is paid for once, not twice
    // =========================================================================================

    /// <summary>
    /// <see cref="FormatRegistry.Detect"/> exists so a caller that needs both the winner and its
    /// score does not have to score the document twice — two of the four scorers walk every line to
    /// answer, so the second pass is a full extra scan of the user's paste. It has to agree exactly
    /// with the two calls it replaces, or the "was this recognizable at all" fallback below it
    /// changes meaning.
    /// </summary>
    [Theory]
    [InlineData("""{"a":1}""")]
    [InlineData("<root/>")]
    [InlineData("a: 1\nb: 2\n")]
    [InlineData("x,y\n1,2\n")]
    [InlineData("just prose")]
    [InlineData("")]
    public void Detect_AgreesWithDetectBest_AndWithTheWinnersOwnConfidence(string text)
    {
        var (format, confidence) = FormatRegistry.Detect(text);

        Assert.Same(FormatRegistry.DetectBest(text), format);
        Assert.Equal(format.DetectConfidence(text), confidence);
    }

    // =========================================================================================
    // 4. Hang guards
    // =========================================================================================

    private static string RecordsJson(int rows)
    {
        var sb = new StringBuilder(rows * 80);
        sb.Append("{\"rows\":[");
        for (int i = 0; i < rows; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(i).Append(",\"name\":\"row ").Append(i).Append("\"}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    /// <summary>
    /// The budget is deliberately loose — an order of magnitude over the measured cost — so it only
    /// fires on a genuine complexity regression rather than on a loaded machine. The scaling check
    /// below it is the real cover.
    /// </summary>
    [Fact]
    public void Analyze_OnAHeavyPayload_StaysWellInsideAnInteractiveBudget()
    {
        var json = RecordsJson(40_000);
        Assert.True(json.Length > 1_000_000);

        var clock = Stopwatch.StartNew();
        var result = QuickFormatter.Analyze(json);
        clock.Stop();

        output.WriteLine($"{json.Length:N0} chars analyzed in {clock.ElapsedMilliseconds}ms");
        Assert.False(result.IsInvalid);
        Assert.True(clock.ElapsedMilliseconds < 5_000, $"took {clock.ElapsedMilliseconds}ms, past the 5000ms budget");
    }

    /// <summary>
    /// Detection used to be run twice per analysis — once to pick the format and once more, on the
    /// winner, to ask whether the input was recognizable at all — and two of the four scorers walk
    /// every line of the document. Neither pass was ever quadratic, but a growth check is what keeps
    /// a later "just check one more thing" from making it so on input a user chose the size of.
    /// </summary>
    [Fact]
    public void Analyze_CostGrowsWithTheInput_NotItsSquare()
    {
        double small = Measure(5_000);
        double large = Measure(40_000);

        double timeRatio = large / Math.Max(small, 1.0);
        output.WriteLine($"analyze: {small:0.0}ms at 5,000 rows -> {large:0.0}ms at 40,000 rows (x{timeRatio:0.0} for size x8)");

        // Linear is x8 over this span and quadratic is x64; x24 sits between the two with room for
        // the garbage collector, which is what actually moves these numbers around.
        Assert.True(timeRatio < 24, $"analyze grew by x{timeRatio:0.0} when the input grew by x8 — that is superlinear");

        static double Measure(int rows)
        {
            var json = RecordsJson(rows);
            QuickFormatter.Analyze(json); // one untimed pass so JIT is not billed to the measurement

            double best = double.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                var clock = Stopwatch.StartNew();
                QuickFormatter.Analyze(json);
                clock.Stop();
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }
            return best;
        }
    }

    /// <summary>
    /// The ceiling Quick Format refuses a payload at has to stay comfortably under the file-read
    /// ceiling that feeds it, or the refusal is unreachable and the tool is back to handing a
    /// 50 MB document to a rich-text control on the UI thread.
    /// </summary>
    [Fact]
    public void TheQuickFormatCeiling_SitsWellBelowTheFileReadCeiling()
    {
        Assert.True(InputLimits.MaxQuickFormatChars > 0);
        Assert.True(InputLimits.MaxQuickFormatChars < InputLimits.MaxTextBytes);
    }
}
