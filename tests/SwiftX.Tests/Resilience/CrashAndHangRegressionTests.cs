using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Xml;
using SwiftX.Services;
using SwiftX.Services.Formats;
using SwiftX.Services.Syntax;
using Xunit;

namespace SwiftX.Tests.Resilience;

/// <summary>
/// Regression cover for the ways SwiftX could be made to die or stop responding on input a user
/// can actually hand it — a pasted document, a dropped file.
/// <para>
/// The distinction these tests are built around is that <b>not every failure is catchable</b>. A
/// <see cref="FormatException"/> from a bad file is a result; a <see cref="StackOverflowException"/>
/// is the end of the process, with no handler, no log line, and no dialog. Two of the cases below
/// were exactly that before the guards they now pin, so "the tool reported an error" is the
/// assertion, and the test surviving to make it is half the point:
/// </para>
/// <list type="bullet">
/// <item><b>Unbounded recursion per nesting level.</b> <c>XmlFormat.ConvertElement</c> recursed
/// once per element, so Convert-to-JSON on a few thousand levels overflowed the stack and killed
/// the process outright. Measured: it survived depth 2000 and died at 4000, from 24 KB of input.</item>
/// <item><b>Work that is superlinear in the input.</b> Indentation costs one character per level
/// per line, so a 350 KB document 50000 deep asked <c>Format</c> for a multi-gigabyte string and
/// threw <see cref="OutOfMemoryException"/> — which the <see cref="XmlException"/> handlers around
/// it do not catch.</item>
/// <item><b>Allocating from a number an attacker chose.</b> A 35-byte keystore declaring
/// <see cref="int.MaxValue"/> certificates had <c>JksReader</c> size a list from that count before
/// reading a single one of them.</item>
/// </list>
/// <para>
/// The hang guards are stopwatch-bounded rather than open-ended. Every budget here is an order of
/// magnitude above what the operation actually costs, so they catch a complexity regression
/// (linear becoming quadratic) without turning into flaky failures on a loaded machine.
/// </para>
/// </summary>
public class CrashAndHangRegressionTests
{
    // ---- Builders ---------------------------------------------------------------------------

    private static string NestedXml(int depth) =>
        string.Concat(Enumerable.Repeat("<a>", depth)) + "leaf" +
        string.Concat(Enumerable.Repeat("</a>", depth));

    private static string NestedJson(int depth) =>
        string.Concat(Enumerable.Repeat("[", depth)) + "1" +
        string.Concat(Enumerable.Repeat("]", depth));

    // =========================================================================================
    // 1. XML nesting: the stack-overflow and out-of-memory cases
    // =========================================================================================

    /// <summary>
    /// The headline regression. Before the depth guard this did not fail — it terminated the test
    /// host, because a <see cref="StackOverflowException"/> is not routed through
    /// <c>App.UnhandledException</c> (nor any catch block) and cannot be handled. Reaching the
    /// assertion at all is the real result; the exception type is the detail.
    /// </summary>
    [Fact]
    public void DeeplyNestedXml_ToCanonical_ReportsInsteadOfOverflowingTheStack()
    {
        var xml = NestedXml(50_000);

        var thrown = Assert.Throws<XmlException>(() => XmlFormat.Instance.ToCanonical(xml));
        Assert.Contains(XmlFormat.MaxDepth.ToString(), thrown.Message);
    }

    /// <summary>
    /// The same document through the Formatter's "Convert to JSON" button, which is the path a
    /// user actually reaches it by: <c>ToCanonical</c> on the detected source format, then
    /// <c>FromCanonical</c> on the target. <c>FormatterPage.ConvertTo</c> wraps this in
    /// <c>catch (Exception)</c> and shows the message on the status bar, so an
    /// <see cref="XmlException"/> here is a reported error rather than a crash.
    /// </summary>
    [Fact]
    public void DeeplyNestedXml_ThroughTheConvertPipeline_SurfacesAsACatchableError()
    {
        var xml = NestedXml(50_000);
        var source = FormatRegistry.DetectBest(xml);
        Assert.Equal("xml", source.Id);

        var thrown = Record.Exception(() =>
        {
            var node = source.ToCanonical(xml);
            return FormatRegistry.Json.FromCanonical(node, FormatOptions.Default);
        });

        Assert.IsType<XmlException>(thrown);
    }

    /// <summary>
    /// Format/Minify/Validate never recursed, so they did not overflow — they ran out of memory
    /// instead, from indentation that grows with the square of the depth. All three share one
    /// load path, so all three inherit the one guard.
    /// </summary>
    [Theory]
    [InlineData(4_000)]
    [InlineData(50_000)]
    public void DeeplyNestedXml_FormatMinifyValidate_ReturnDiagnosticsInsteadOfExhaustingMemory(int depth)
    {
        var xml = NestedXml(depth);

        var formatted = XmlFormat.Instance.Format(xml, FormatOptions.Default);
        Assert.False(formatted.Success);
        Assert.NotEmpty(formatted.Diagnostics);
        // The contract is that invalid input never clears the user's text.
        Assert.Empty(formatted.Text);

        var minified = XmlFormat.Instance.Minify(xml);
        Assert.False(minified.Success);
        Assert.NotEmpty(minified.Diagnostics);

        Assert.NotEmpty(XmlFormat.Instance.Validate(xml));
    }

    /// <summary>
    /// The limit is a ceiling on absurd input, not a new way to reject real documents: everything
    /// up to and including <see cref="XmlFormat.MaxDepth"/> still round-trips, and only the level
    /// past it is refused. Pinning both sides is what stops a later "make it safer" change from
    /// quietly tightening the limit onto documents that used to work.
    /// </summary>
    [Fact]
    public void XmlAtTheDepthLimit_IsAccepted_AndOnlyThePastItIsRefused()
    {
        var atLimit = XmlFormat.Instance.Format(NestedXml(XmlFormat.MaxDepth), FormatOptions.Default);
        Assert.True(atLimit.Success);

        var pastLimit = XmlFormat.Instance.Format(NestedXml(XmlFormat.MaxDepth + 1), FormatOptions.Default);
        Assert.False(pastLimit.Success);
    }

    /// <summary>
    /// <b>Refusing has to be cheap, not merely correct.</b> The depth ceiling used to be applied by
    /// walking the loaded <see cref="System.Xml.Linq.XDocument"/> — which meant the document it
    /// existed to refuse was fully built first, and
    /// <see cref="System.Xml.Linq.XDocument.Load(XmlReader, System.Xml.Linq.LoadOptions)"/> is
    /// superlinear in nesting depth. Measured on this exact input: 231 ms at depth 10000, 2.2 s at
    /// 20000, 10.5 s at 40000 and 73 s at 100000, with the walk that then rejected it taking
    /// 0.1 ms. Every one of those inputs is well inside the ceilings the app already applies —
    /// depth 100000 is 700 KB, an eighth of <c>InputLimits.MaxQuickFormatChars</c> — and Quick
    /// Format reads the clipboard unprompted, so a paste was enough.
    /// <para>
    /// The assertions the other tests here make ("it reported an error") all passed throughout
    /// that, because the tool did eventually report one. This is the assertion that did not exist:
    /// the cost of saying no must not scale with the size of the thing being said no to. The
    /// budget is two orders of magnitude above the measured cost after the fix (0.2–0.3 ms at
    /// every depth below) and still an order of magnitude below the old cost at the *smallest*
    /// size tested.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(10_000)]
    [InlineData(40_000)]
    [InlineData(100_000)]
    public void DeeplyNestedXml_IsRefusedInTimeThatDoesNotGrowWithTheNesting(int depth)
    {
        var xml = NestedXml(depth);

        FormatResult result = default!;
        AssertFasterThan(2_000, $"refuse xml nested {depth} deep", () =>
            result = XmlFormat.Instance.Format(xml, FormatOptions.Default));

        Assert.False(result.Success);
        Assert.Contains(XmlFormat.MaxDepth.ToString(), result.Diagnostics[0].Message);
    }

    /// <summary>
    /// Depth is measured per branch, not summed across the document: a wide, shallow document has
    /// thousands of elements and nests three levels, and must not be mistaken for a deep one.
    /// </summary>
    [Fact]
    public void WideButShallowXml_IsNotMistakenForDeepNesting()
    {
        var sb = new StringBuilder("<root>");
        for (int i = 0; i < 20_000; i++)
            sb.Append("<item><name>n").Append(i).Append("</name></item>");
        sb.Append("</root>");

        var result = XmlFormat.Instance.Format(sb.ToString(), FormatOptions.Default);
        Assert.True(result.Success);
    }

    /// <summary>
    /// The JSON side had its own ceiling already (the parser's <c>MaxDepth</c>), so the mirror
    /// case is that a legal-but-deep JSON document still converts to XML — the write-side guard
    /// added alongside must not fire below the shared limit.
    /// </summary>
    [Fact]
    public void DeepButLegalJson_ConvertsToXml_WithoutTrippingTheWriteSideGuard()
    {
        // Object nesting rather than arrays: an array has no member name to become an element.
        const int depth = 200;
        var sb = new StringBuilder();
        for (int i = 0; i < depth; i++)
            sb.Append("{\"level\":");
        sb.Append("\"leaf\"");
        for (int i = 0; i < depth; i++)
            sb.Append('}');

        var node = JsonFormat.Instance.ToCanonical(sb.ToString());
        var xml = XmlFormat.Instance.FromCanonical(node, FormatOptions.Default);

        Assert.Contains("leaf", xml);
    }

    /// <summary>
    /// JSON past its own parser ceiling is refused by every entry point rather than reaching the
    /// recursive writer behind them.
    /// </summary>
    [Fact]
    public void JsonPastItsParserDepthCeiling_IsRefusedByEveryEntryPoint()
    {
        var json = NestedJson(5_000);

        Assert.False(JsonFormat.Instance.Format(json, FormatOptions.Default).Success);
        Assert.False(JsonFormat.Instance.Minify(json).Success);
        Assert.NotEmpty(JsonFormat.Instance.Validate(json));
        Assert.ThrowsAny<Exception>(() => JsonFormat.Instance.ToCanonical(json));
    }

    // =========================================================================================
    // 2. Keystore parsing: allocating from an attacker-chosen count
    // =========================================================================================

    /// <summary>Builds a JKS header by hand so a single field can be made hostile.</summary>
    private static byte[] Keystore(Action<KeystoreWriter> build)
    {
        var writer = new KeystoreWriter();
        writer.Int32(unchecked((int)0xFEEDFEED));
        writer.Int32(2);
        build(writer);
        return writer.ToArray();
    }

    private sealed class KeystoreWriter
    {
        private readonly List<byte> _bytes = [];

        public void Int32(int value)
        {
            var buffer = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            _bytes.AddRange(buffer);
        }

        public void Int64(long value)
        {
            var buffer = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(buffer, value);
            _bytes.AddRange(buffer);
        }

        public void JavaUtf(string value)
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            var length = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)encoded.Length);
            _bytes.AddRange(length);
            _bytes.AddRange(encoded);
        }

        public byte[] ToArray() => [.. _bytes];
    }

    /// <summary>
    /// A 35-byte file that claims two billion certificates in one chain. Every individual read was
    /// already bounds-checked, so the lie surfaced harmlessly on the first read past the end — but
    /// the list was sized from the count <em>before</em> that read, asking for a 17 GB array and
    /// throwing <see cref="OutOfMemoryException"/> straight through a parser documented as never
    /// throwing.
    /// </summary>
    [Fact]
    public void Keystore_DeclaringMoreChainCertificatesThanItsOwnLength_IsRefusedNotAllocated()
    {
        var hostile = Keystore(w =>
        {
            w.Int32(1);              // one entry
            w.Int32(1);              // tag: private key entry
            w.JavaUtf("alias");
            w.Int64(0);              // timestamp
            w.Int32(0);              // encrypted key length
            w.Int32(int.MaxValue);   // chain length - the hostile field
        });

        Assert.True(hostile.Length < 100, "the point is that a tiny file makes the claim");

        var result = JksReader.TryParse(hostile);

        Assert.True(result.IsKeyStore);
        Assert.True(result.Truncated);
        Assert.Empty(result.Entries);
    }

    /// <summary>The same lie told about the entry count rather than a chain length.</summary>
    [Fact]
    public void Keystore_DeclaringMoreEntriesThanItsOwnLength_IsRefusedNotAllocated()
    {
        var hostile = Keystore(w => w.Int32(int.MaxValue));

        var result = JksReader.TryParse(hostile);

        Assert.True(result.IsKeyStore);
        Assert.True(result.Truncated);
    }

    /// <summary>
    /// <c>CertificateReader</c> documents "never throws" for every failure mode, and it is the
    /// layer the drop handler and file picker actually call. A hostile keystore has to come back
    /// as a result, not an exception, or an <c>async void</c> drop handler takes the fault.
    /// </summary>
    [Fact]
    public void CertificateReader_OnAHostileKeystore_ReturnsAResultRatherThanThrowing()
    {
        var hostile = Keystore(w =>
        {
            w.Int32(1);
            w.Int32(1);
            w.JavaUtf("alias");
            w.Int64(0);
            w.Int32(0);
            w.Int32(int.MaxValue);
        });

        var outcome = Record.Exception(() => CertificateReader.Read(hostile, password: null));

        Assert.Null(outcome);
        Assert.False(CertificateReader.Read(hostile, password: null).Success);
    }

    // =========================================================================================
    // 3. Hang guards: work that must stay linear in the input
    // =========================================================================================

    /// <summary>
    /// Every one of these runs on the UI thread when the user presses the button, so the failure
    /// mode for a superlinear implementation is a frozen window rather than a wrong answer. The
    /// budgets are deliberately loose — an order of magnitude over the measured cost — so they
    /// only fire on a genuine complexity regression.
    /// </summary>
    [Fact]
    public void LargeDocumentOperations_StayWellInsideAnInteractiveBudget()
    {
        var sb = new StringBuilder("{\"rows\":[");
        for (int i = 0; i < 40_000; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(i).Append(",\"name\":\"row ").Append(i).Append("\"}");
        }
        sb.Append("]}");
        var json = sb.ToString();
        Assert.True(json.Length > 1_000_000);

        AssertFasterThan(5_000, "validate", () => JsonFormat.Instance.Validate(json));
        AssertFasterThan(5_000, "format", () => JsonFormat.Instance.Format(json, FormatOptions.Default));
        AssertFasterThan(5_000, "minify", () => JsonFormat.Instance.Minify(json));
        AssertFasterThan(5_000, "tokenize", () => JsonTokenizer.Instance.Tokenize(json));
        AssertFasterThan(5_000, "fold", () => FoldFinder.Find(json, "json"));
        AssertFasterThan(5_000, "line index", () => new LineIndex(json));
        AssertFasterThan(5_000, "detect", () => FormatRegistry.DetectBest(json));
    }

    /// <summary>
    /// The scanners run against half-typed documents on every debounced keystroke, so an
    /// unterminated construct at the end of the buffer must terminate rather than scan forever.
    /// A truncation at every boundary is the cheap way to cover the "walked off the end" family.
    /// </summary>
    [Fact]
    public void Tokenizers_OnEveryTruncationOfADocument_TerminateAndStayInBounds()
    {
        const string json = """{"a":[1,2,{"b":"text with \" escape"}],"c":null}""";
        const string xml = """<root a="1"><!-- note --><child><![CDATA[raw]]></child></root>""";

        for (int cut = 0; cut <= json.Length; cut++)
        {
            var slice = json[..cut];
            foreach (var token in JsonTokenizer.Instance.Tokenize(slice))
            {
                Assert.InRange(token.Start, 0, slice.Length);
                Assert.InRange(token.End, token.Start, slice.Length);
            }
        }

        for (int cut = 0; cut <= xml.Length; cut++)
        {
            var slice = xml[..cut];
            foreach (var token in XmlTokenizer.Instance.Tokenize(slice))
            {
                Assert.InRange(token.Start, 0, slice.Length);
                Assert.InRange(token.End, token.Start, slice.Length);
            }
        }
    }

    /// <summary>
    /// <c>FoldFinder</c> walks tokens with an explicit stack rather than recursing, which is what
    /// lets it survive nesting that would overflow a recursive matcher. It is also reached from
    /// the editor on documents the formatter has already refused, so it has to cope with input
    /// deeper than <see cref="XmlFormat.MaxDepth"/>.
    /// </summary>
    [Fact]
    public void FoldFinder_OnNestingFarPastTheFormatterCeiling_CompletesWithoutOverflowing()
    {
        AssertFasterThan(10_000, "deep json folds", () => FoldFinder.Find(NestedJson(50_000), "json"));
        AssertFasterThan(10_000, "deep xml folds", () => FoldFinder.Find(NestedXml(20_000), "xml"));
    }

    /// <summary>
    /// The paste-cleanup pass runs on whatever was on the clipboard. A long run of backslashes is
    /// the shape that breaks a naive unescaper, either by reading past the end on the last one or
    /// by rescanning what it already consumed.
    /// </summary>
    [Fact]
    public void InputSanitizer_OnPathologicalEscapeRuns_TerminatesQuickly()
    {
        AssertFasterThan(5_000, "backslash run", () => InputSanitizer.Clean(new string('\\', 200_000)));
        AssertFasterThan(5_000, "truncated unicode escapes",
            () => InputSanitizer.Clean(string.Concat(Enumerable.Repeat("\\u00", 50_000))));
        AssertFasterThan(5_000, "quote run", () => InputSanitizer.Clean(new string('"', 200_000)));
    }

    private static void AssertFasterThan(int budgetMs, string what, Action operation)
    {
        var clock = Stopwatch.StartNew();
        operation();
        clock.Stop();
        Assert.True(
            clock.ElapsedMilliseconds < budgetMs,
            $"{what} took {clock.ElapsedMilliseconds}ms, past the {budgetMs}ms budget");
    }
}
