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
    public void StoreContext_IsOnlyReferencedInsideTheGuardedStoreFeedbackMethod()
    {
        var offenders = new List<string>();

        foreach (var file in SourceTree.Files())
        {
            var text = File.ReadAllText(file);
            if (!Regex.IsMatch(text, @"\bStoreContext\b|Windows\.Services\.Store|Windows\.ApplicationModel\.Store"))
                continue;

            var name = Path.GetFileName(file);
            if (name != "StoreFeedback.cs")
            {
                offenders.Add($"{name}: touches the Store API outside StoreFeedback.cs, which owns the packaging guard");
                continue;
            }

            // Within StoreFeedback.cs the call has to sit behind the guard: the method that makes
            // it must only be reached after InAppRatingSupported has been asked.
            int call = text.IndexOf("StoreContext.GetDefault()", StringComparison.Ordinal);
            int guard = text.IndexOf("InAppRatingSupported &&", StringComparison.Ordinal);
            int entry = text.IndexOf("TryInAppRatingAsync(hwnd)", StringComparison.Ordinal);

            if (call < 0 || guard < 0 || entry < 0)
                offenders.Add("StoreFeedback.cs: the guard-then-call shape this test pins is gone; re-establish it or update the test with the new guard");
            else if (guard > entry)
                offenders.Add("StoreFeedback.cs: TryInAppRatingAsync is called before InAppRatingSupported is checked");
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }
}
