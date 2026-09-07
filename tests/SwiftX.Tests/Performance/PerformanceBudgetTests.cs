using System.Diagnostics;
using System.Text;
using SwiftX.Services;
using SwiftX.Services.Formats;
using SwiftX.Services.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace SwiftX.Tests.Performance;

/// <summary>
/// Heavy-load performance cover for the pure computation behind the tools — the work that happens
/// on the UI thread while the window is unable to repaint, so its cost is felt as a freeze.
/// <para>
/// <b>Two kinds of assertion, and the second is the one that matters.</b> An absolute budget
/// ("under N milliseconds") is easy to read but is really a statement about the machine running
/// it, so every budget here is set an order of magnitude above the measured cost and exists only
/// to catch something becoming catastrophically slow. The <em>scaling</em> assertions are the real
/// regression cover: they time the same operation at two input sizes and check that the cost grows
/// roughly with the input rather than with its square. That is what actually distinguishes "this
/// machine is busy" from "someone made this quadratic", and quadratic behaviour on user-supplied
/// input is both a performance bug and a denial-of-service one — see
/// <c>CrashAndHangRegressionTests</c>, where indentation quadratic in nesting depth turned 350 KB
/// of XML into an <see cref="OutOfMemoryException"/>.
/// </para>
/// <para>
/// Timings take the <b>minimum</b> of several runs rather than the mean. A scheduler preemption or
/// a GC pause can only ever make a sample slower, so the minimum is the closest estimate of the
/// true cost and is dramatically more stable than an average on a shared or virtualised machine.
/// </para>
/// </summary>
public class PerformanceBudgetTests(ITestOutputHelper output)
{
    /// <summary>How many times a timed operation is repeated before the best run is taken. Five
    /// rather than three because the XML paths allocate a whole document tree per run, and the
    /// extra samples measurably tighten the minimum on the large sizes.</summary>
    private const int Samples = 5;

    // ---- Payload builders -------------------------------------------------------------------

    private static string RecordsJson(int rows)
    {
        var sb = new StringBuilder(rows * 120);
        sb.Append("{\"records\":[");
        for (int i = 0; i < rows; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(i)
              .Append(",\"name\":\"Record ").Append(i)
              .Append("\",\"active\":").Append(i % 2 == 0 ? "true" : "false")
              .Append(",\"score\":").Append(i * 1.5)
              .Append(",\"tags\":[\"alpha\",\"beta\"],\"note\":\"unicode cafe and an emoji\"}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static string RecordsXml(int rows)
    {
        var sb = new StringBuilder(rows * 120);
        sb.Append("<records>");
        for (int i = 0; i < rows; i++)
        {
            sb.Append("<record id=\"").Append(i).Append("\">")
              .Append("<name>Record ").Append(i).Append("</name>")
              .Append("<score>").Append(i * 1.5).Append("</score>")
              .Append("</record>");
        }
        sb.Append("</records>");
        return sb.ToString();
    }

    // ---- Timing helpers ---------------------------------------------------------------------

    private static double BestMilliseconds(Action operation)
    {
        // One untimed pass so JIT compilation and first-touch allocation are not billed to the
        // measurement - they happen once per process, not once per user action.
        operation();

        double best = double.MaxValue;
        for (int i = 0; i < Samples; i++)
        {
            var clock = Stopwatch.StartNew();
            operation();
            clock.Stop();
            best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
        }
        return best;
    }

    /// <summary>
    /// Builds one size's payload, times it, and lets the payload die before returning — the
    /// caller never holds two of them at once. The collection happens up front so the measurement
    /// starts from a settled heap rather than paying for the previous size's garbage.
    /// </summary>
    private static double MeasureInIsolation(int size, Func<int, Action> build)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return BestMilliseconds(build(size));
    }

    private void Budget(string what, int budgetMs, Action operation)
    {
        double best = BestMilliseconds(operation);
        output.WriteLine($"{what}: {best:0.0}ms (budget {budgetMs}ms)");
        Assert.True(best < budgetMs, $"{what} took {best:0.0}ms, past the {budgetMs}ms budget");
    }

    /// <summary>
    /// Times <paramref name="operation"/> at a small and a large size and asserts the growth is
    /// not far worse than proportional. With a 4x larger input a linear operation costs about 4x;
    /// a quadratic one costs about 16x. The 2x-the-size-ratio ceiling sits between the two, so it
    /// separates the classes without failing on ordinary measurement noise.
    /// <para>
    /// <b>Each size is measured in isolation, and that is not incidental.</b> An earlier version
    /// built both payloads up front and held them alive in closures across both phases, which made
    /// the large measurement pay for collecting the small one — <c>XmlFormat.Validate</c> reported
    /// a x20 growth for a x8 input and looked quadratic, while the same operation timed on its own
    /// grows about x8.7. Measuring one size at a time, with the previous payload dropped and
    /// collected first, is what makes the number a property of the algorithm rather than of the
    /// heap it happened to run on. XML matters most here because every load builds a full
    /// <c>XDocument</c> tree — hundreds of thousands of objects — so it is by far the most
    /// GC-sensitive path being measured.
    /// </para>
    /// </summary>
    /// <param name="noiseAllowance">
    /// How many times the size ratio the time ratio may reach before the growth counts as
    /// superlinear. The default of 2 suits operations that mostly scan strings. Operations that
    /// build a large object graph per run need 3: an 80 000-row <c>XmlFormat.Format</c> was
    /// measured between 180 ms and 298 ms across four runs of the identical input — a 1.7x spread
    /// from garbage collection alone — so a 2x allowance failed intermittently on code that is
    /// linear. Over the x8 size span used there, linear is x8 and quadratic is x64, so even a 3x
    /// allowance leaves a comfortable margin below quadratic while absorbing that spread.
    /// </param>
    private void ScalesRoughlyLinearly(
        string what, int smallSize, int largeSize, Func<int, Action> build, double noiseAllowance = 2)
    {
        double small = MeasureInIsolation(smallSize, build);
        double large = MeasureInIsolation(largeSize, build);

        double sizeRatio = (double)largeSize / smallSize;
        // A floor on the baseline: dividing by a sub-millisecond reading turns timer granularity
        // into an enormous apparent ratio, which would be noise rather than a finding.
        double baseline = Math.Max(small, 1.0);
        double timeRatio = large / baseline;
        double ceiling = sizeRatio * noiseAllowance;

        output.WriteLine(
            $"{what}: {small:0.0}ms at {smallSize} -> {large:0.0}ms at {largeSize} " +
            $"(time x{timeRatio:0.0} for size x{sizeRatio:0.0}, ceiling x{ceiling:0.0})");

        Assert.True(
            timeRatio < ceiling,
            $"{what} grew by x{timeRatio:0.0} when the input grew by x{sizeRatio:0.0} - " +
            $"that is superlinear and suggests a quadratic path");
    }

    // =========================================================================================
    // 1. Absolute budgets on realistic heavy payloads
    // =========================================================================================

    [Fact]
    public void Json_HeavyPayload_EveryOperationStaysInteractive()
    {
        var json = RecordsJson(50_000);
        output.WriteLine($"payload: {json.Length:N0} chars");
        Assert.True(json.Length > 5_000_000, "the payload should be genuinely heavy");

        Budget("json validate", 10_000, () => JsonFormat.Instance.Validate(json));
        Budget("json format", 10_000, () => JsonFormat.Instance.Format(json, FormatOptions.Default));
        Budget("json minify", 10_000, () => JsonFormat.Instance.Minify(json));
        Budget("json sort keys", 10_000,
            () => JsonFormat.Instance.Format(json, new FormatOptions { SortKeys = true }));
        Budget("json canonical round trip", 15_000, () =>
        {
            var node = JsonFormat.Instance.ToCanonical(json);
            JsonFormat.Instance.FromCanonical(node, FormatOptions.Default);
        });
    }

    [Fact]
    public void Xml_HeavyPayload_EveryOperationStaysInteractive()
    {
        var xml = RecordsXml(50_000);
        output.WriteLine($"payload: {xml.Length:N0} chars");
        Assert.True(xml.Length > 3_000_000);

        Budget("xml validate", 10_000, () => XmlFormat.Instance.Validate(xml));
        Budget("xml format", 15_000, () => XmlFormat.Instance.Format(xml, FormatOptions.Default));
        Budget("xml minify", 15_000, () => XmlFormat.Instance.Minify(xml));
        Budget("xml to canonical", 15_000, () => XmlFormat.Instance.ToCanonical(xml));
    }

    /// <summary>
    /// The editor re-tokenizes on a debounced keystroke, so the tokenizers are the hottest path in
    /// the app. <c>CodeEditor.MaxHighlightLength</c> caps what is ever handed to them at 100 000
    /// characters; this measures an order of magnitude past that cap, so the budget holds with
    /// room to spare at the size the control actually uses.
    /// </summary>
    [Fact]
    public void Tokenizers_WellPastTheEditorHighlightCap_StayFast()
    {
        var json = RecordsJson(10_000);
        var xml = RecordsXml(10_000);
        output.WriteLine($"json {json.Length:N0} chars, xml {xml.Length:N0} chars");

        Budget("json tokenize", 5_000, () => JsonTokenizer.Instance.Tokenize(json));
        Budget("xml tokenize", 5_000, () => XmlTokenizer.Instance.Tokenize(xml));
        Budget("json folds", 5_000, () => FoldFinder.Find(json, "json"));
        Budget("xml folds", 5_000, () => FoldFinder.Find(xml, "xml"));
        Budget("line index", 2_000, () => new LineIndex(json));
    }

    /// <summary>
    /// Base64's file mode allows up to <c>InputLimits.MaxBinaryBytes</c>, far more than the text
    /// tools, because nothing tokenizes or lays the bytes out. This checks the codec keeps up at a
    /// size in that band.
    /// </summary>
    [Fact]
    public void Base64_MultiMegabytePayload_RoundTripsWithinBudget()
    {
        var random = new Random(20260907);
        var sb = new StringBuilder(4 * 1024 * 1024);
        while (sb.Length < 4 * 1024 * 1024)
            sb.Append((char)random.Next(32, 126));
        var text = sb.ToString();

        string encoded = "";
        Budget("base64 encode", 5_000, () => encoded = Base64Codec.Encode(text, urlSafe: false).Text);
        Budget("base64 decode", 5_000, () => Base64Codec.Decode(encoded));

        Assert.Equal(text, Base64Codec.Decode(encoded).Text);
    }

    /// <summary>
    /// The paste-cleanup pass runs before every validate, on whatever was on the clipboard.
    /// </summary>
    [Fact]
    public void InputSanitizer_OnAHeavyEscapedPayload_StaysFast()
    {
        var inner = RecordsJson(5_000);
        var escaped = "\"" + inner.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        output.WriteLine($"escaped payload: {escaped.Length:N0} chars");

        Budget("sanitize", 5_000, () => InputSanitizer.Clean(escaped));
    }

    // =========================================================================================
    // 2. Scaling: the assertions that actually catch a regression
    // =========================================================================================

    [Fact]
    public void JsonPipeline_CostGrowsWithTheInput_NotItsSquare()
    {
        ScalesRoughlyLinearly("json validate", 5_000, 40_000,
            rows => { var text = RecordsJson(rows); return () => JsonFormat.Instance.Validate(text); });

        ScalesRoughlyLinearly("json format", 5_000, 40_000,
            rows => { var text = RecordsJson(rows); return () => JsonFormat.Instance.Format(text, FormatOptions.Default); });

        ScalesRoughlyLinearly("json minify", 5_000, 40_000,
            rows => { var text = RecordsJson(rows); return () => JsonFormat.Instance.Minify(text); });
    }

    /// <summary>
    /// XML is measured over a wider size range than a first attempt used, for two separate
    /// reasons that both came out of the measurements rather than out of theory.
    /// <para>
    /// The baseline starts at 10 000 rows rather than 5 000 because the smaller sizes are not
    /// representative: a 5 000-row load costs about 3 ms with its whole working set in cache, so
    /// comparing it against 40 000 rows measures the memory hierarchy at least as much as the
    /// algorithm. It reported x18 growth for a x8 input while the same operation timed directly
    /// grows about x4 over that range.
    /// </para>
    /// <para>
    /// The <em>span</em> is x8 rather than x4 because a x4 step does not separate the two
    /// hypotheses well enough to assert on. Linear costs x4 and quadratic x16, so the ceiling has
    /// to sit between them — and observed values ranged from x4.7 to x8.1 run to run, straddling
    /// it. Over a x8 span the same two hypotheses are x8 and x64, so a x16 ceiling has four times
    /// the headroom above the noise while still being nowhere near quadratic. Widening the
    /// measurement is the fix here; loosening the ceiling would only have made the assertion
    /// weaker.
    /// </para>
    /// </summary>
    [Fact]
    public void XmlPipeline_CostGrowsWithTheInput_NotItsSquare()
    {
        ScalesRoughlyLinearly("xml validate", 10_000, 80_000,
            rows => { var text = RecordsXml(rows); return () => XmlFormat.Instance.Validate(text); },
            noiseAllowance: 3);

        ScalesRoughlyLinearly("xml format", 10_000, 80_000,
            rows => { var text = RecordsXml(rows); return () => XmlFormat.Instance.Format(text, FormatOptions.Default); },
            noiseAllowance: 3);

        ScalesRoughlyLinearly("xml to canonical", 10_000, 80_000,
            rows => { var text = RecordsXml(rows); return () => XmlFormat.Instance.ToCanonical(text); },
            noiseAllowance: 3);
    }

    [Fact]
    public void SyntaxPipeline_CostGrowsWithTheInput_NotItsSquare()
    {
        ScalesRoughlyLinearly("json tokenize", 5_000, 40_000,
            rows => { var text = RecordsJson(rows); return () => JsonTokenizer.Instance.Tokenize(text); });

        ScalesRoughlyLinearly("xml tokenize", 5_000, 40_000,
            rows => { var text = RecordsXml(rows); return () => XmlTokenizer.Instance.Tokenize(text); });

        ScalesRoughlyLinearly("json folds", 5_000, 40_000,
            rows => { var text = RecordsJson(rows); return () => FoldFinder.Find(text, "json"); });

        ScalesRoughlyLinearly("xml folds", 5_000, 40_000,
            rows => { var text = RecordsXml(rows); return () => FoldFinder.Find(text, "xml"); });

        ScalesRoughlyLinearly("line index", 5_000, 40_000,
            rows => { var text = RecordsJson(rows); return () => new LineIndex(text); });
    }

    /// <summary>
    /// Nesting depth is the axis the XML indent bug lived on: cost grew with depth squared while
    /// the input grew linearly. The guard now refuses anything past
    /// <see cref="XmlFormat.MaxDepth"/>, so this measures inside the permitted band and pins that
    /// the permitted band itself is affordable.
    /// </summary>
    [Fact]
    public void XmlFormatting_AcrossThePermittedDepthBand_StaysAffordable()
    {
        static string Nested(int depth) =>
            string.Concat(Enumerable.Repeat("<a>", depth)) + "leaf" +
            string.Concat(Enumerable.Repeat("</a>", depth));

        ScalesRoughlyLinearly("xml format by depth", 64, 256,
            depth => { var text = Nested(depth); return () => XmlFormat.Instance.Format(text, FormatOptions.Default); });
    }

    // =========================================================================================
    // 3. Allocation behaviour
    // =========================================================================================

    /// <summary>
    /// A formatter that holds onto every document it has seen shows up as a steadily climbing
    /// working set rather than as a failed assertion anywhere else. Running the full cycle many
    /// times over and checking the managed heap afterwards catches a cache or a static list that
    /// is never trimmed. The threshold is deliberately coarse: this is a leak detector, not an
    /// allocation budget.
    /// </summary>
    [Fact]
    public void RepeatedFormatCycles_DoNotRetainEachDocument()
    {
        var json = RecordsJson(2_000);

        // Warm up so first-use statics and the JIT are not counted as growth.
        for (int i = 0; i < 3; i++)
            JsonFormat.Instance.Format(json, FormatOptions.Default);

        long before = GC.GetTotalMemory(forceFullCollection: true);

        for (int i = 0; i < 200; i++)
        {
            var formatted = JsonFormat.Instance.Format(json, FormatOptions.Default);
            var minified = JsonFormat.Instance.Minify(formatted.Text);
            var node = JsonFormat.Instance.ToCanonical(minified.Text);
            JsonFormat.Instance.FromCanonical(node, FormatOptions.Default);
        }

        long after = GC.GetTotalMemory(forceFullCollection: true);
        long growthMb = (after - before) / (1024 * 1024);
        output.WriteLine($"managed heap: {before / (1024 * 1024)}MB -> {after / (1024 * 1024)}MB (growth {growthMb}MB)");

        Assert.True(growthMb < 64, $"managed heap grew {growthMb}MB across 200 cycles, which suggests retention");
    }
}
