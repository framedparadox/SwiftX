using System.Diagnostics;
using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Resilience;

/// <summary>
/// An orbit item on a network share must never be able to freeze the app.
/// <para>
/// <b>The failure.</b> <c>Directory.Exists(@"\\192.0.2.1\share\folder")</c> took 21.04 s to answer
/// "no" on the development machine: the SMB redirector connects first and waits out its own
/// timeout. <c>IconService.LoadIconAsync</c> made that call inline, callers start it on the UI
/// thread, and an async method runs synchronously up to its first <c>await</c> — so summoning the
/// orbit with one unreachable share on it (a laptop that has left the office, a dropped VPN) froze
/// the whole app for the length of the timeout, every time the redirector's negative cache expired.
/// </para>
/// <para>
/// Real unreachable hosts are not a thing a unit test can rely on, so these pin the two properties
/// that make the freeze impossible instead: the file-system questions are asked on a pool thread
/// and not the caller's, and a slow answer leaves the caller free.
/// </para>
/// </summary>
public class NetworkPathHangTests
{
    [Theory]
    [InlineData(@"\\server\share\folder", false)]
    [InlineData(@"\\192.0.2.1\share", false)]
    [InlineData("//server/share/file.txt", false)]
    [InlineData(@"\\?\UNC\server\share\file.txt", false)]
    [InlineData(@"C:\Windows\System32\notepad.exe", true)]
    [InlineData(@"c:\windows", true)]
    [InlineData(@"\\?\C:\Windows", true)]
    [InlineData(@"\\.\C:", true)]
    [InlineData(@"relative\path.txt", true)]
    [InlineData("https://example.com/favicon.ico", true)]
    [InlineData(@"shell:AppsFolder\Microsoft.WindowsCalculator", true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData(null, true)]
    public void PathLocality_ClassifiesFromTheShapeOfThePath(string? path, bool expectedLocal)
    {
        Assert.Equal(expectedLocal, PathLocality.IsLocal(path));
    }

    [Fact]
    public void LaunchingANetworkTarget_IsDoneOffTheCallingThread_AndALocalOneIsNot()
    {
        Assert.True(Launcher.ShouldLaunchOffTheCallerThread(
            new OrbitItem { Kind = OrbitItemKind.Application, Target = @"\\nas\tools\app.exe" }));
        Assert.False(Launcher.ShouldLaunchOffTheCallerThread(
            new OrbitItem { Kind = OrbitItemKind.Application, Target = @"C:\Windows\System32\notepad.exe" }));
        Assert.False(Launcher.ShouldLaunchOffTheCallerThread(
            new OrbitItem { Kind = OrbitItemKind.WebLink, Target = "https://example.com" }));
    }

    [Fact]
    public void IconProbe_AsksTheFileSystemOnAPoolThread_NotTheThreadThatCalledIt()
    {
        var item = new OrbitItem { Kind = OrbitItemKind.Folder, Target = @"\\192.0.2.1\share\folder" };
        int callerThread = -1, probeThread = -1;
        bool? onPool = null;
        Exception? failure = null;

        // A dedicated thread, because that is the position the UI thread is in: not a pool thread,
        // and the one the freeze landed on.
        var caller = new Thread(() =>
        {
            try
            {
                callerThread = Environment.CurrentManagedThreadId;
                var probe = IconService.ProbeAsync(item, _ => true, _ =>
                {
                    onPool = Thread.CurrentThread.IsThreadPoolThread;
                    probeThread = Environment.CurrentManagedThreadId;
                    return true;
                }).GetAwaiter().GetResult();
                Assert.True(probe.TargetDirectoryExists);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        caller.Start();
        caller.Join();

        Assert.Null(failure);
        Assert.True(onPool, "the directory check ran on a thread that is not a pool thread");
        Assert.NotEqual(callerThread, probeThread);
    }

    [Fact]
    public async Task IconProbe_ASlowAnswer_LeavesTheCallerFree()
    {
        var item = new OrbitItem { Kind = OrbitItemKind.File, Target = @"\\192.0.2.1\share\app.exe" };
        using var release = new ManualResetEventSlim();

        var clock = Stopwatch.StartNew();
        var probe = IconService.ProbeAsync(item, _ => { release.Wait(TimeSpan.FromSeconds(30)); return true; }, _ => true);
        long returnedAfter = clock.ElapsedMilliseconds;

        // The check is still "waiting on the network" and the caller already has its Task back.
        Assert.False(probe.IsCompleted);
        Assert.True(returnedAfter < 500, $"ProbeAsync took {returnedAfter} ms to hand back a task; it must not wait on the check");

        release.Set();
        var result = await probe.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.TargetFileExists);
    }

    [Fact]
    public async Task IconProbe_RepeatedSummons_ShareOneProbePerItemWhileItIsRunning()
    {
        // The same dead share, summoned three times during one SMB timeout: one parked pool thread,
        // not three.
        var item = new OrbitItem { Kind = OrbitItemKind.Folder, Target = @"\\192.0.2.1\dedupe-" + Guid.NewGuid().ToString("N") };
        int runs = 0;
        using var release = new ManualResetEventSlim();
        IconService.IconProbe Slow(OrbitItem _)
        {
            Interlocked.Increment(ref runs);
            release.Wait(TimeSpan.FromSeconds(30));
            return new IconService.IconProbe(false, false, false, true);
        }

        var first = IconService.ProbeOncePerItemAsync(item, Slow);
        var second = IconService.ProbeOncePerItemAsync(item, Slow);
        var third = IconService.ProbeOncePerItemAsync(item, Slow);

        Assert.Same(first, second);
        Assert.Same(first, third);

        release.Set();
        var results = await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.All(results, r => Assert.True(r.TargetDirectoryExists));
        Assert.Equal(1, runs);

        // Once it has finished the entry is gone, so the next summon asks again: a share that has
        // come back is noticed at once rather than answered from a stale result.
        await Task.Delay(100);
        int before = runs;
        await IconService.ProbeOncePerItemAsync(item, i => { Interlocked.Increment(ref runs); return new IconService.IconProbe(false, false, false, false); })
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(before + 1, runs);
    }

    [Fact]
    public void IconProbe_OnlyAsksTheQuestionsTheItemCanNeed()
    {
        var asked = new List<string>();
        bool FileCheck(string p) { asked.Add("file:" + p); return true; }
        bool DirCheck(string p) { asked.Add("dir:" + p); return true; }

        IconService.Probe(new OrbitItem { Kind = OrbitItemKind.WebLink, Target = "https://example.com" }, FileCheck, DirCheck);
        IconService.Probe(new OrbitItem { Kind = OrbitItemKind.Group, Target = "" }, FileCheck, DirCheck);
        Assert.Empty(asked);

        IconService.Probe(new OrbitItem { Kind = OrbitItemKind.Folder, Target = @"C:\data" }, FileCheck, DirCheck);
        Assert.Equal(new[] { @"dir:C:\data" }, asked);

        asked.Clear();
        IconService.Probe(new OrbitItem { Kind = OrbitItemKind.Application, Target = @"C:\a.exe" }, FileCheck, DirCheck);
        Assert.Equal(new[] { @"file:C:\a.exe" }, asked);

        asked.Clear();
        var module = IconService.Probe(
            new OrbitItem { Kind = OrbitItemKind.Application, Target = @"C:\a.exe", CustomIconPath = @"C:\icons.dll,3" }, FileCheck, DirCheck);
        Assert.True(module.ModuleExists);
        Assert.False(module.CustomFileExists);
        Assert.Equal(new[] { @"file:C:\icons.dll", @"file:C:\a.exe" }, asked);

        asked.Clear();
        var custom = IconService.Probe(
            new OrbitItem { Kind = OrbitItemKind.Application, Target = @"C:\a.exe", CustomIconPath = @"C:\pic.png" }, FileCheck, DirCheck);
        Assert.True(custom.CustomFileExists);
        Assert.False(custom.ModuleExists);
    }
}
