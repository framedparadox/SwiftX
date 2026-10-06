using System.Runtime.CompilerServices;

namespace SwiftX.Tests;

/// <summary>
/// Redirects the whole test assembly onto a throwaway data directory before a single test runs.
/// <para>
/// <b>This is not hygiene, it is data loss prevention.</b> <c>OrbitStore.DataDirectory</c> resolves
/// to <c>%AppData%\SwiftX</c> unless <c>SWIFTX_DATA_DIR</c> overrides it, and
/// <c>SecureValueStore</c> / <c>OrbitStore</c> write <em>whole-file</em> — <c>Save</c> serializes
/// the list it is handed and moves it over the existing file. So a test that called
/// <c>SecureValueStore.Save(...)</c> with generated entries did not add rows to a scratch store:
/// it replaced the running user's real <c>secure-values.json</c>, DPAPI blob and all, with the
/// test's own. There is no backup and no undo — the previous contents are simply gone.
/// </para>
/// <para>
/// A <see cref="ModuleInitializerAttribute"/> is what makes the guard reliable. The directory has
/// to be redirected before <c>OrbitStore.DataDirectory</c> is first read, because it is a
/// <c>static readonly</c> resolved once per process; a fixture, a base class or a per-test setup
/// all run after some other test may already have touched it. A module initializer runs when the
/// assembly is loaded, ahead of every test in it.
/// </para>
/// </summary>
internal static class TestEnvironment
{
    /// <summary>The redirected data directory, unique per test process so parallel runs cannot
    /// collide with each other.</summary>
    internal static string DataDirectory { get; } = Path.Combine(
        Path.GetTempPath(),
        "swiftx-tests",
        Environment.ProcessId.ToString() + "-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("SWIFTX_DATA_DIR", DataDirectory);
        // The diagnostic log is the one file the stores' redirect does not cover: it lives in %TEMP%,
        // is shared by every SwiftX process, and is truncated at process start — so without this a
        // test run erased the log of the user's real session. See Diag.LogDirectoryVariable.
        Environment.SetEnvironmentVariable("SWIFTX_LOG_DIR", DataDirectory);
        Directory.CreateDirectory(DataDirectory);
    }
}
