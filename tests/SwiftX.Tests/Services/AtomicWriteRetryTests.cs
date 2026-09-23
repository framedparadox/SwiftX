using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// Publishing over a file somebody else currently has open.
/// <para>
/// <b>Readable is not the same as replaceable.</b> <c>File.Move(overwrite: true)</c> is
/// <c>MoveFileEx</c> with <c>MOVEFILE_REPLACE_EXISTING</c>, which needs delete access to the file
/// it replaces — so a reader holding the destination without <c>FILE_SHARE_DELETE</c> fails the
/// move, even though nothing is wrong with either file. For a file in the user's AppData that is
/// an ordinary, constant occurrence: the app's own <c>Load</c> on another thread, an antivirus
/// on-access scan, a backup or folder-sync agent, a second SwiftX pointed at the same
/// <c>SWIFTX_DATA_DIR</c>.
/// </para>
/// <para>
/// Before the retry, that window was a save that did not happen — silently for
/// <c>OrbitStore</c> (which swallows), and as "Access to the path … is denied" in the Secure
/// values status bar for <c>SecureValueStore</c> (which rethrows). Both messages are wrong about
/// the cause, and in both cases the write would have succeeded a few milliseconds later.
/// </para>
/// </summary>
/// <remarks>
/// In the shared-data collection because the last test goes through <c>SecureValueStore</c>, which
/// writes the one <c>secure-values.json</c> every class in this assembly shares — see
/// <see cref="SharedDataDirectoryCollection"/>. The rest work in their own temp directory.
/// </remarks>
[Collection(SharedDataDirectoryCollection.Name)]
public class AtomicWriteRetryTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "swiftx-atomicwrite-tests", Guid.NewGuid().ToString("N"));

    public AtomicWriteRetryTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch { /* ignore */ }
    }

    /// <summary>Opens <paramref name="path"/> the restrictive way a scanner or a plain
    /// <c>File.ReadAllText</c> does — read access, no delete sharing — which is precisely the
    /// handle that blocks a replace.</summary>
    private static FileStream OpenBlockingReader(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    [Fact]
    public async Task A_publish_waits_out_a_reader_that_is_holding_the_destination()
    {
        var path = Path.Combine(_directory, "orbit.json");
        File.WriteAllText(path, "{\"old\":true}");

        var reader = OpenBlockingReader(path);
        // Released while the publish is mid-retry. 60 ms lands after the first attempt has already
        // failed and well inside the ~300 ms the retry schedule covers, so this asserts the wait
        // actually happens rather than that the first attempt happened to get lucky. Publish is
        // synchronous and blocks this thread, so the release has to run on another one.
        var release = Task.Run(async () =>
        {
            await Task.Delay(60);
            reader.Dispose();
        });

        AtomicWrite.Publish(path, "{\"new\":true}");
        await release;

        Assert.Equal("{\"new\":true}", File.ReadAllText(path));
    }

    /// <summary>
    /// The failure that made this worth fixing, stated as the exception type it actually arrives
    /// as. A sharing violation on the <em>destination</em> of a replace is reported as an access
    /// denial, not an I/O error — which is why the endurance suite's <c>catch (IOException)</c>
    /// did not cover it and the test failed outright instead of recording a lost write.
    /// </summary>
    [Fact]
    public void A_reader_that_never_lets_go_still_reports_rather_than_hanging()
    {
        var path = Path.Combine(_directory, "secure-values.json");
        File.WriteAllText(path, "original");

        using var reader = OpenBlockingReader(path);

        var started = Environment.TickCount64;
        var thrown = Record.Exception(() => AtomicWrite.Publish(path, "replacement"));
        var elapsed = Environment.TickCount64 - started;

        Assert.NotNull(thrown);
        Assert.True(
            thrown is UnauthorizedAccessException or IOException,
            $"expected a sharing-violation exception, got {thrown!.GetType().Name}");

        // Bounded: the caller is the UI thread, so "gives up" has to mean seconds, not minutes.
        Assert.True(elapsed < 5_000, $"publish took {elapsed} ms to give up");

        // And the file it could not replace is untouched — a half-published store is the outcome
        // AtomicWrite exists to prevent, and a failed retry must not become one.
        Assert.Equal("original", File.ReadAllText(path));
    }

    /// <summary>
    /// A failed publish must not leave its scratch file behind. The names are unique per call, so
    /// litter here is never reused and never cleaned up either — it just accumulates in the user's
    /// AppData, one file per failed save.
    /// </summary>
    [Fact]
    public void A_failed_publish_leaves_no_scratch_file_behind()
    {
        var path = Path.Combine(_directory, "orbit.json");
        File.WriteAllText(path, "original");

        using (var reader = OpenBlockingReader(path))
            Assert.NotNull(Record.Exception(() => AtomicWrite.Publish(path, "replacement")));

        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.Equal(new[] { "orbit.json" }, Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    /// <summary>The uncontended path, which is the one that runs essentially always: no retry, no
    /// wait, and the directory created if it is not there.</summary>
    [Fact]
    public void An_uncontended_publish_writes_immediately_and_creates_its_directory()
    {
        var path = Path.Combine(_directory, "nested", "deeper", "orbit.json");

        var started = Environment.TickCount64;
        AtomicWrite.Publish(path, "content");
        var elapsed = Environment.TickCount64 - started;

        Assert.Equal("content", File.ReadAllText(path));
        Assert.True(elapsed < 1_000, $"an uncontended publish took {elapsed} ms");
    }

    /// <summary>
    /// The store-level statement of the same thing, through the API a tool page actually calls:
    /// Secure values reports a save failure to the user, so a transient reader must not produce
    /// one.
    /// </summary>
    [Fact]
    public async Task Saving_secure_values_survives_a_reader_holding_the_store_open()
    {
        var path = Path.Combine(OrbitStore.ResolveDataDirectory(
            Environment.SpecialFolder.LocalApplicationData), "secure-values.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Seed through the store so the file is a real one to contend with, whatever shape a
        // previous test in this collection left behind.
        SwiftX.Services.SecureValueStore.Save([
            new SwiftX.Models.SecureValueEntry { Id = "seed", Key = "SEED", Value = "v" },
        ]);

        var reader = OpenBlockingReader(path);
        var release = Task.Run(async () =>
        {
            await Task.Delay(60);
            reader.Dispose();
        });

        var thrown = Record.Exception(() => SwiftX.Services.SecureValueStore.Save([
            new SwiftX.Models.SecureValueEntry { Id = "after", Key = "AFTER", Value = "w" },
        ]));
        await release;

        Assert.Null(thrown);
        var loaded = SwiftX.Services.SecureValueStore.Load();
        Assert.Equal("AFTER", Assert.Single(loaded).Key);
    }
}
