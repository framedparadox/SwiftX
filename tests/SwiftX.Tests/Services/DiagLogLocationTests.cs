using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// Where the diagnostic log goes — and, more to the point, where a test run must <em>not</em> put it.
/// <para>
/// <c>%TEMP%\swiftx.log</c> is one file shared by every SwiftX process, truncated by each at start-up.
/// Before <c>SWIFTX_LOG_DIR</c> existed, running this suite erased the log of whatever real session
/// was running on the machine: the only managed trace of a crash, gone the first time someone
/// verified a fix.
/// </para>
/// </summary>
public class DiagLogLocationTests
{
    [Fact]
    public void TheTestAssembly_NeverWritesTheRealUsersLog()
    {
        var realLog = Path.Combine(Path.GetTempPath(), "swiftx.log");

        Assert.False(
            string.Equals(realLog, Diag.LogPath, StringComparison.OrdinalIgnoreCase),
            $"the test run is writing the real log at {realLog}");
        Assert.StartsWith(TestEnvironment.DataDirectory, Diag.LogPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithoutAnOverride_TheLogStaysInTheTempDirectory()
    {
        Assert.Equal(
            Path.Combine(@"C:\somewhere", "swiftx.log"),
            Diag.ResolveLogPath(null, @"C:\somewhere"));
        Assert.Equal(
            Path.Combine(@"C:\somewhere", "swiftx.log"),
            Diag.ResolveLogPath("   ", @"C:\somewhere"));
    }

    [Fact]
    public void AnOverride_MovesTheLogAndCreatesTheDirectory()
    {
        var directory = Path.Combine(TestEnvironment.DataDirectory, "log-" + Guid.NewGuid().ToString("N"), "nested");

        var path = Diag.ResolveLogPath(directory, @"C:\somewhere");

        Assert.Equal(Path.Combine(directory, "swiftx.log"), path);
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    public void AnUnusableOverride_FallsBackRatherThanLosingTheLog()
    {
        // A path with an illegal character cannot be created; the log must still land somewhere.
        var path = Diag.ResolveLogPath("C:\\bad|dir<>", @"C:\somewhere");

        Assert.Equal(Path.Combine(@"C:\somewhere", "swiftx.log"), path);
    }
}
