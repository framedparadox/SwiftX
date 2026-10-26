using Windows.Services.Store;
using WinRT.Interop;

namespace SwiftX.Services;

/// <summary>
/// The real Microsoft Store update check for the packaged (MSIX) build — see
/// <see cref="OrbitManager.UpdateChecksSupported"/> for why this is only ever offered on the
/// packaged build, which has package identity for <see cref="StoreContext"/> to resolve.
/// <para>
/// This replaces the GitHub-releases download-and-relaunch mechanism SwiftX shipped with before:
/// now that packaged installs come through the Store, the Store already knows how to check for,
/// download and install an update for its own package, which is simpler and more correct than
/// reimplementing that badly from inside the app.
/// </para>
/// <para>
/// <b>Crash guard, same shape as <see cref="StoreFeedback"/>.</b> Calling into the Store API
/// with no package identity does not throw something a <c>catch</c> could handle — it
/// faults natively inside <c>Windows.ApplicationModel.Store.dll</c> before any managed handler
/// runs (see <see cref="StoreFeedback.InAppRatingSupported"/>'s own remarks, which pin the exact
/// crash this guards against). <see cref="OrbitManager.UpdateChecksSupported"/> already keeps the
/// UI that would call this off the unpackaged build, but the check is repeated here, immediately
/// before each call, the same way <see cref="StoreFeedback"/> guards its own: a native fault ends
/// the process before a caller's earlier check can be trusted to have been honoured, so the
/// question is asked again right where it matters.
/// </para>
/// </summary>
public static class StoreUpdateChecker
{
    /// <summary>What a check produced, for the UI to show directly.</summary>
    public readonly record struct CheckResult(bool Ok, bool UpdateAvailable, string? LatestVersion);

    /// <summary>The updates a successful <see cref="CheckForUpdateAsync"/> most recently found, so
    /// <see cref="RequestInstallAsync"/> can hand them to the Store without asking again. Cleared
    /// on a failed check and after an install attempt either way.</summary>
    private static IReadOnlyList<StorePackageUpdate> _lastUpdates = [];

    /// <summary>
    /// Asks the Store whether a newer package is available for this install. Never throws: no
    /// package identity, no network, or the Store service being unavailable all come back as
    /// <c>Ok: false</c> rather than propagating.
    /// </summary>
    public static async Task<CheckResult> CheckForUpdateAsync()
    {
        if (!PackagedRuntime.IsPackaged)
            return new CheckResult(false, false, null);

        try
        {
            StoreContext context = StoreContext.GetDefault();
            var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
            _lastUpdates = updates;
            return new CheckResult(true, updates.Count > 0, updates.Count > 0 ? LatestVersionOf(updates) : null);
        }
        catch (Exception ex)
        {
            Diag.Log($"StoreUpdateChecker: update check failed: {ex.GetType().Name}: {ex.Message}");
            _lastUpdates = [];
            return new CheckResult(false, false, null);
        }
    }

    /// <summary>
    /// Hands the updates a prior <see cref="CheckForUpdateAsync"/> found to the Store's own
    /// download/install UI — a system-owned flow, not one this app draws any progress UI for
    /// itself. Returns false (and clears the cached updates either way) when there was nothing
    /// pending, when run from the unpackaged build, or when the request itself failed — never
    /// throws.
    /// </summary>
    public static async Task<bool> RequestInstallAsync(nint hwnd)
    {
        if (!PackagedRuntime.IsPackaged || _lastUpdates.Count == 0)
            return false;

        var updates = _lastUpdates;
        _lastUpdates = [];
        try
        {
            StoreContext context = StoreContext.GetDefault();
            InitializeWithWindow.Initialize(context, hwnd);
            var result = await context.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
            return result.OverallState == StorePackageUpdateState.Completed;
        }
        catch (Exception ex)
        {
            Diag.Log($"StoreUpdateChecker: install request failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The highest version among the packages the Store is offering, for the "Version {0} is
    /// ready" toast — read from each update's own package identity rather than guessed, so it is
    /// exactly what the Store will install. Null (never thrown) if a version cannot be read, which
    /// just drops the number from the message.
    /// </summary>
    private static string? LatestVersionOf(IReadOnlyList<StorePackageUpdate> updates)
    {
        try
        {
            var version = updates
                .Select(u => u.Package.Id.Version)
                .OrderByDescending(v => v.Major)
                .ThenByDescending(v => v.Minor)
                .ThenByDescending(v => v.Build)
                .ThenByDescending(v => v.Revision)
                .FirstOrDefault();
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch (Exception ex)
        {
            Diag.Log($"StoreUpdateChecker: couldn't read the update's version: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
