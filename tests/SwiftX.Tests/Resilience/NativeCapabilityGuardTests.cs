using System.Text.RegularExpressions;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Resilience;

/// <summary>
/// Guards around native APIs whose failure is not an exception.
/// <para>
/// <b>Why these are tests of the source as well as of behaviour.</b> The crash they pin cannot be
/// reproduced by a test: <c>StoreContext</c> called from a process with no package identity faults
/// inside <c>Windows.ApplicationModel.Store.dll</c> with an access violation, which ends the
/// process — including the test host — before any assertion or <c>catch</c> can run. The only
/// thing a test can check is that the code never gets as far as the call, and "never" is a property
/// of every call site in the product, so the second test reads them all.
/// </para>
/// <para>
/// The evidence: <c>0xc0000005</c> at offset <c>0xb6902</c> in <c>Windows.ApplicationModel.Store.dll</c>,
/// on 2026-09-15 (v1.0.1.0) and 2026-09-22 (v2.0.0.0), with the later dump holding
/// <c>ShellWindow.RateOnStore_Click</c> and <c>StoreFeedback.RequestRatingAsync</c>.
/// </para>
/// </summary>
public class NativeCapabilityGuardTests
{
    [Fact]
    public void InAppStoreRating_IsOffForAnUnpackagedProcess()
    {
        // The test host has no package identity, which is exactly the build that crashed.
        Assert.False(PackagedRuntime.IsPackaged);
        Assert.False(StoreFeedback.InAppRatingSupported);
    }

    [Fact]
    public void StoreContext_IsOnlyReferencedInsideAGuardedMethod()
    {
        // Two files legitimately call into the Store API, each owning its own packaging guard:
        // StoreFeedback.cs (in-app rating) and StoreUpdateChecker.cs (the real Store update check
        // that replaced the old GitHub-releases mechanism — see OrbitManager.UpdateChecksSupported
        // and StoreUpdateChecker's own remarks). Anywhere else, a reference to the Store API has no
        // guard this test knows to check and is an offender by construction.
        var allowedFiles = new HashSet<string> { "StoreFeedback.cs", "StoreUpdateChecker.cs" };
        var offenders = new List<string>();

        foreach (var file in SourceTree.Files())
        {
            var text = File.ReadAllText(file);
            if (!Regex.IsMatch(text, @"\bStoreContext\b|Windows\.Services\.Store|Windows\.ApplicationModel\.Store"))
                continue;

            var name = Path.GetFileName(file);
            if (!allowedFiles.Contains(name))
            {
                offenders.Add($"{name}: touches the Store API outside its guarded owner (StoreFeedback.cs / StoreUpdateChecker.cs)");
                continue;
            }

            if (name == "StoreFeedback.cs")
            {
                // The call has to sit behind the guard: the method that makes it must only be
                // reached after InAppRatingSupported has been asked.
                int call = text.IndexOf("StoreContext.GetDefault()", StringComparison.Ordinal);
                int guard = text.IndexOf("InAppRatingSupported &&", StringComparison.Ordinal);
                int entry = text.IndexOf("TryInAppRatingAsync(hwnd)", StringComparison.Ordinal);

                if (call < 0 || guard < 0 || entry < 0)
                    offenders.Add("StoreFeedback.cs: the guard-then-call shape this test pins is gone; re-establish it or update the test with the new guard");
                else if (guard > entry)
                    offenders.Add("StoreFeedback.cs: TryInAppRatingAsync is called before InAppRatingSupported is checked");
            }
            else // StoreUpdateChecker.cs
            {
                // Every call is preceded by its own "if (!PackagedRuntime.IsPackaged)" guard,
                // immediately above it in the same method — checked pairwise in file order rather
                // than just counting occurrences, so a guard added to one method can never be
                // mistaken for cover on another.
                var calls = AllIndicesOf(text, "StoreContext.GetDefault()");
                var guards = AllIndicesOf(text, "!PackagedRuntime.IsPackaged");

                if (calls.Count == 0)
                {
                    offenders.Add("StoreUpdateChecker.cs: no StoreContext.GetDefault() call found — the guard-then-call shape this test pins may have moved");
                }
                else if (guards.Count < calls.Count)
                {
                    offenders.Add("StoreUpdateChecker.cs: fewer PackagedRuntime.IsPackaged guards than StoreContext.GetDefault() calls");
                }
                else
                {
                    for (int i = 0; i < calls.Count; i++)
                    {
                        if (guards[i] >= calls[i])
                        {
                            offenders.Add($"StoreUpdateChecker.cs: a StoreContext.GetDefault() call at offset {calls[i]} has no PackagedRuntime.IsPackaged guard before it");
                            break;
                        }
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Every index in <paramref name="text"/> where <paramref name="marker"/> occurs, in
    /// order — used to pair each call site in <c>StoreUpdateChecker.cs</c> with its own preceding
    /// guard rather than one guard covering every call by coincidence of count alone.</summary>
    private static List<int> AllIndicesOf(string text, string marker)
    {
        var indices = new List<int>();
        int i = 0;
        while ((i = text.IndexOf(marker, i, StringComparison.Ordinal)) >= 0)
        {
            indices.Add(i);
            i += marker.Length;
        }
        return indices;
    }
}
