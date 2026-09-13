using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using SwiftX.Models;
using SwiftX.Services;
using SwiftX.Services.Formats;
using SwiftX.Services.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace SwiftX.Tests.Load;

/// <summary>
/// Load cover along the two axes the per-operation performance tests do not reach: <b>many at
/// once</b> and <b>many in a row</b>.
/// <para>
/// <b>Why concurrency is in scope for a desktop app.</b> Every format, tokenizer and codec in
/// SwiftX is exposed as a process-wide singleton — <c>JsonFormat.Instance</c>,
/// <c>XmlTokenizer.Instance</c>, and so on — and the tool windows are genuinely independent: the
/// user can have the Formatter, the Base64 tool and the Certificate inspector open at once, each
/// with its own debounced timer, and <c>IconService</c> resolves icons on background tasks while
/// they do. So these singletons really are reached from more than one thread. A singleton that
/// kept per-call state in a field would be correct in every single-threaded test in this repository
/// and would corrupt output the first time two windows worked at the same time. The assertions
/// below are on <em>results</em>, not just on the absence of exceptions, because that is the shape
/// the bug would take: no crash, just the wrong answer.
/// </para>
/// <para>
/// <b>Why endurance is in scope.</b> The orbit is a long-running tray application that is summoned
/// and dismissed hundreds of times a day and rarely restarted. A per-cycle leak or a cache that is
/// never trimmed is invisible in a single run and obvious after a week, so the endurance tests
/// repeat the real cycle enough times for retention to show up as heap growth.
/// </para>
/// </summary>
public class ConcurrencyAndEnduranceTests(ITestOutputHelper output)
{
    /// <summary>Enough threads to genuinely overlap on any developer machine, without making the
    /// suite's runtime depend on core count.</summary>
    private static readonly int Workers = Math.Max(8, Environment.ProcessorCount * 2);

    private static string RecordsJson(int rows, int seed)
    {
        var sb = new StringBuilder();
        sb.Append("{\"seed\":").Append(seed).Append(",\"records\":[");
        for (int i = 0; i < rows; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(i).Append(",\"name\":\"row ").Append(i).Append("\"}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static string RecordsXml(int rows, int seed)
    {
        var sb = new StringBuilder();
        sb.Append("<records seed=\"").Append(seed).Append("\">");
        for (int i = 0; i < rows; i++)
            sb.Append("<record id=\"").Append(i).Append("\"><name>row ").Append(i).Append("</name></record>");
        sb.Append("</records>");
        return sb.ToString();
    }

    /// <summary>
    /// Runs <paramref name="work"/> on <see cref="Workers"/> threads that all start together. The
    /// barrier matters: without it the first workers finish before the last ones start, and the
    /// test measures nothing about concurrency at all.
    /// </summary>
    private static void InParallel(int iterationsPerWorker, Action<int, int> work)
    {
        using var barrier = new Barrier(Workers);
        var failures = new ConcurrentBag<Exception>();

        var threads = Enumerable.Range(0, Workers).Select(worker => new Thread(() =>
        {
            try
            {
                barrier.SignalAndWait();
                for (int iteration = 0; iteration < iterationsPerWorker; iteration++)
                    work(worker, iteration);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        })
        { IsBackground = true }).ToList();

        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();

        Assert.Empty(failures);
    }

    // =========================================================================================
    // 1. The format singletons under simultaneous use
    // =========================================================================================

    /// <summary>
    /// Each worker formats a document only it knows the contents of, then checks the result is
    /// that document's own. Distinct payloads per worker are what turns this from a smoke test
    /// into a real one: with identical input, a formatter that leaked state between calls would
    /// still return the right answer by accident.
    /// </summary>
    [Fact]
    public void JsonFormat_UsedFromManyThreadsAtOnce_NeverCrossesResults()
    {
        InParallel(iterationsPerWorker: 20, (worker, iteration) =>
        {
            int seed = worker * 1000 + iteration;
            var json = RecordsJson(200, seed);

            var formatted = JsonFormat.Instance.Format(json, FormatOptions.Default);
            Assert.True(formatted.Success);
            Assert.Contains($"\"seed\": {seed}", formatted.Text);

            var minified = JsonFormat.Instance.Minify(formatted.Text);
            Assert.True(minified.Success);
            Assert.Contains($"\"seed\":{seed}", minified.Text);

            Assert.Empty(JsonFormat.Instance.Validate(minified.Text));
        });
    }

    [Fact]
    public void XmlFormat_UsedFromManyThreadsAtOnce_NeverCrossesResults()
    {
        InParallel(iterationsPerWorker: 20, (worker, iteration) =>
        {
            int seed = worker * 1000 + iteration;
            var xml = RecordsXml(200, seed);

            var formatted = XmlFormat.Instance.Format(xml, FormatOptions.Default);
            Assert.True(formatted.Success);
            Assert.Contains($"seed=\"{seed}\"", formatted.Text);

            var node = XmlFormat.Instance.ToCanonical(xml);
            var asJson = JsonFormat.Instance.FromCanonical(node, FormatOptions.Default);
            Assert.Contains(seed.ToString(), asJson);
        });
    }

    /// <summary>
    /// The tokenizers are the singletons reached most often — one debounced pass per editor per
    /// keystroke — and they are documented as pure functions of a string. Two formats' scanners
    /// running at once is the realistic case: two tool windows, two documents.
    /// </summary>
    [Fact]
    public void Tokenizers_UsedFromManyThreadsAtOnce_ProduceStableTokenCovers()
    {
        var json = RecordsJson(300, 7);
        var xml = RecordsXml(300, 7);
        int expectedJsonTokens = JsonTokenizer.Instance.Tokenize(json).Count;
        int expectedXmlTokens = XmlTokenizer.Instance.Tokenize(xml).Count;

        InParallel(iterationsPerWorker: 15, (worker, _) =>
        {
            if (worker % 2 == 0)
            {
                var tokens = JsonTokenizer.Instance.Tokenize(json);
                Assert.Equal(expectedJsonTokens, tokens.Count);
                AssertNonOverlappingAndInBounds(tokens, json.Length);
            }
            else
            {
                var tokens = XmlTokenizer.Instance.Tokenize(xml);
                Assert.Equal(expectedXmlTokens, tokens.Count);
                AssertNonOverlappingAndInBounds(tokens, xml.Length);
            }
        });
    }

    /// <summary>
    /// The property every tokenizer is documented to satisfy: tokens are in bounds and do not
    /// overlap, so rendering them as consecutive runs reconstructs the text rather than printing
    /// characters twice.
    /// </summary>
    private static void AssertNonOverlappingAndInBounds(IReadOnlyList<Token> tokens, int length)
    {
        int previousEnd = 0;
        foreach (var token in tokens)
        {
            Assert.InRange(token.Start, previousEnd, length);
            Assert.InRange(token.End, token.Start, length);
            previousEnd = token.End;
        }
    }

    [Fact]
    public void Base64Codec_UsedFromManyThreadsAtOnce_RoundTripsEachPayloadToItself()
    {
        InParallel(iterationsPerWorker: 40, (worker, iteration) =>
        {
            var payload = $"worker {worker} iteration {iteration} unicode cafe and an emoji";

            var encoded = Base64Codec.Encode(payload, urlSafe: iteration % 2 == 0);
            Assert.True(encoded.Success);

            var decoded = Base64Codec.Decode(encoded.Text);
            Assert.True(decoded.Success);
            Assert.Equal(payload, decoded.Text);
        });
    }

    /// <summary>
    /// <c>ShortcutResolver</c> is the one service with a genuine shared mutable cache behind a
    /// lock, so it is the one where concurrent access could actually corrupt a data structure
    /// rather than merely interleave. Hammering it with paths that do not resolve still exercises
    /// the cache's lookup and insert paths.
    /// </summary>
    [Fact]
    public void ShortcutResolver_UnderConcurrentLookups_StaysConsistent()
    {
        var paths = Enumerable.Range(0, 32)
            .Select(i => Path.Combine(OrbitStore.DataDirectory, $"shortcut-{i}.lnk"))
            .ToArray();

        InParallel(iterationsPerWorker: 50, (worker, iteration) =>
        {
            var path = paths[(worker + iteration) % paths.Length];
            // Every one of these is a missing .lnk, so null is the correct answer; the point is
            // that the cache is read and written from every thread while it happens.
            Assert.Null(ShortcutResolver.Resolve(path));
        });
    }

    // =========================================================================================
    // 2. Configuration store under load
    // =========================================================================================

    /// <summary>
    /// A configuration far larger than any real orbit, to check the save/load round trip is bounded
    /// by the data rather than by something quadratic in the item count, and that it survives the
    /// atomic temp-file-then-move write intact.
    /// </summary>
    [Fact]
    public void OrbitStore_WithAVeryLargeConfiguration_RoundTripsIntact()
    {
        var config = new AppConfig().EnsurePrimary();
        for (int i = 0; i < 5_000; i++)
        {
            config.Primary.Items.Add(new OrbitItem
            {
                Kind = OrbitItemKind.Application,
                DisplayName = $"Item {i} with a name long enough to matter",
                Target = $@"C:\Program Files\Vendor {i}\app-{i}.exe",
                Arguments = $"--flag-{i} --another",
            });
        }

        var clock = Stopwatch.StartNew();
        OrbitStore.Save(config);
        var reloaded = OrbitStore.Load();
        clock.Stop();
        output.WriteLine($"5000-item config save+load: {clock.ElapsedMilliseconds}ms");

        Assert.Equal(config.Primary.Items.Count, reloaded.Primary.Items.Count);
        Assert.Equal(config.Primary.Items[4_999].Target, reloaded.Primary.Items[4_999].Target);
        Assert.True(clock.ElapsedMilliseconds < 30_000);
    }

    /// <summary>
    /// Export and import are the user-visible round trip of the same data, over a path the user
    /// picks. Repeating it many times over checks the writer and reader stay in agreement rather
    /// than drifting after the first pass.
    /// </summary>
    [Fact]
    public void OrbitStore_ExportImportCycle_IsStableAcrossManyRepetitions()
    {
        var config = new AppConfig { Theme = AppTheme.Dark, ItemSize = OrbitItemSize.Large }.EnsurePrimary();
        for (int i = 0; i < 50; i++)
        {
            config.Primary.Items.Add(new OrbitItem
            {
                Kind = OrbitItemKind.WebLink,
                DisplayName = $"Link {i}",
                Target = $"https://example.invalid/{i}",
            });
        }

        var path = Path.Combine(OrbitStore.DataDirectory, "roundtrip.json");
        var current = config;

        for (int cycle = 0; cycle < 25; cycle++)
        {
            Assert.True(OrbitStore.ExportTo(current, path));
            var imported = OrbitStore.ImportFrom(path);

            Assert.NotNull(imported);
            Assert.Equal(config.Primary.Items.Count, imported.Primary.Items.Count);
            Assert.Equal(AppTheme.Dark, imported.Theme);
            Assert.Equal(OrbitItemSize.Large, imported.ItemSize);
            current = imported;
        }
    }

    /// <summary>
    /// The secure store is loaded on every tool-window open and saved on every edit, so a
    /// save/load cycle repeated in quick succession is the realistic pattern. Each cycle changes
    /// the data so a stale read would be caught rather than matching by luck.
    /// </summary>
    [Fact]
    public void SecureValueStore_RepeatedSaveLoadCycles_AlwaysReadBackTheLastWrite()
    {
        for (int cycle = 0; cycle < 40; cycle++)
        {
            var entries = Enumerable.Range(0, 25).Select(i => new SecureValueEntry
            {
                Id = $"{cycle}-{i}",
                Key = $"KEY_{cycle}_{i}",
                Value = $"value-{cycle}-{i}",
            }).ToList();

            SecureValueStore.Save(entries);
            var loaded = SecureValueStore.Load();

            Assert.Equal(entries.Count, loaded.Count);
            Assert.Equal($"value-{cycle}-24", loaded.Single(e => e.Key == $"KEY_{cycle}_24").Value);
        }
    }

    // =========================================================================================
    // 3. Endurance
    // =========================================================================================

    /// <summary>
    /// The Formatter's full cycle — detect, validate, format, minify, convert, fold — repeated far
    /// more times than a session would, checking the managed heap afterwards. This is a retention
    /// detector: the threshold is coarse on purpose, because the failure it looks for is unbounded
    /// growth, not a few megabytes of steady state.
    /// </summary>
    [Fact]
    public void TheFullFormatterCycle_RepeatedHundredsOfTimes_DoesNotAccumulate()
    {
        var json = RecordsJson(500, 1);
        var xml = RecordsXml(500, 1);

        // Warm up so first-use statics are not counted as growth.
        for (int i = 0; i < 5; i++)
        {
            JsonFormat.Instance.Format(json, FormatOptions.Default);
            XmlFormat.Instance.Format(xml, FormatOptions.Default);
        }

        long before = GC.GetTotalMemory(forceFullCollection: true);
        var clock = Stopwatch.StartNew();

        for (int cycle = 0; cycle < 300; cycle++)
        {
            var detected = FormatRegistry.DetectBest(json);
            Assert.Empty(detected.Validate(json));

            var formatted = detected.Format(json, FormatOptions.Default);
            var minified = detected.Minify(formatted.Text);
            var node = detected.ToCanonical(minified.Text);
            var asXml = XmlFormat.Instance.FromCanonical(node, FormatOptions.Default);
            Assert.NotEmpty(asXml);

            FoldFinder.Find(formatted.Text, "json");
            JsonTokenizer.Instance.Tokenize(formatted.Text);
            XmlTokenizer.Instance.Tokenize(xml);
        }

        clock.Stop();
        long after = GC.GetTotalMemory(forceFullCollection: true);
        long growthMb = (after - before) / (1024 * 1024);
        output.WriteLine(
            $"300 full cycles in {clock.ElapsedMilliseconds}ms; " +
            $"heap {before / (1024 * 1024)}MB -> {after / (1024 * 1024)}MB (growth {growthMb}MB)");

        Assert.True(growthMb < 64, $"managed heap grew {growthMb}MB across 300 cycles");
    }

    /// <summary>
    /// The same endurance question for the encode/decode tools, which hold no document state at
    /// all and so should return to exactly where they started.
    /// </summary>
    [Fact]
    public void CodecsAndSanitizer_RepeatedThousandsOfTimes_DoNotAccumulate()
    {
        var payload = string.Concat(Enumerable.Repeat("payload with unicode and punctuation; ", 200));

        for (int i = 0; i < 20; i++)
            Base64Codec.Decode(Base64Codec.Encode(payload, urlSafe: false).Text);

        long before = GC.GetTotalMemory(forceFullCollection: true);

        for (int i = 0; i < 3_000; i++)
        {
            var encoded = Base64Codec.Encode(payload, urlSafe: i % 2 == 0);
            var decoded = Base64Codec.Decode(encoded.Text);
            Assert.Equal(payload, decoded.Text);
            InputSanitizer.Clean(payload);
            ValueMasking.Mask(payload);
        }

        long after = GC.GetTotalMemory(forceFullCollection: true);
        long growthMb = (after - before) / (1024 * 1024);
        output.WriteLine($"3000 codec cycles; heap growth {growthMb}MB");

        Assert.True(growthMb < 32, $"managed heap grew {growthMb}MB across 3000 codec cycles");
    }

    /// <summary>
    /// Mixed simultaneous load across every tool at once, which is the closest this suite gets to
    /// "the user has everything open and is pasting into all of it". No result assertions beyond
    /// each tool's own, since the point is the combination rather than any one answer.
    /// </summary>
    [Fact]
    public void EveryToolUnderSimultaneousLoad_CompletesWithoutFaulting()
    {
        var clock = Stopwatch.StartNew();

        InParallel(iterationsPerWorker: 10, (worker, iteration) =>
        {
            switch ((worker + iteration) % 5)
            {
                case 0:
                    Assert.True(JsonFormat.Instance.Format(RecordsJson(300, worker), FormatOptions.Default).Success);
                    break;
                case 1:
                    Assert.True(XmlFormat.Instance.Format(RecordsXml(300, worker), FormatOptions.Default).Success);
                    break;
                case 2:
                    var text = $"payload {worker}-{iteration}";
                    Assert.Equal(text, Base64Codec.Decode(Base64Codec.Encode(text, false).Text).Text);
                    break;
                case 3:
                    Assert.NotNull(FoldFinder.Find(RecordsJson(300, worker), "json"));
                    break;
                default:
                    var garbage = new byte[512];
                    new Random(worker * 100 + iteration).NextBytes(garbage);
                    Assert.Null(Record.Exception(() => CertificateReader.Read(garbage, null)));
                    break;
            }
        });

        clock.Stop();
        output.WriteLine($"mixed load across {Workers} threads: {clock.ElapsedMilliseconds}ms");
    }
}
