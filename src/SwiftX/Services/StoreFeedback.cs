using Windows.Services.Store;
using Windows.System;
using WinRT.Interop;

namespace SwiftX.Services;

/// <summary>
/// Microsoft Store rating and Feedback Hub entry points. In-app review first, then the Store
/// deep link, matching the Store feedback guide: never gate "happy" vs "unhappy" onto different
/// destinations, and always run on the UI thread.
/// </summary>
public static class StoreFeedback
{
    /// <summary>
    /// Partner Center product id. Placeholder until the listing is live — in-app review still
    /// works from a packaged build without it; the URI fallback needs the real twelve-character id.
    /// </summary>
    public const string ProductId = "0000000000000";

    /// <summary>
    /// Whether the in-app review dialog may be used at all. It is a <c>StoreContext</c> call, and
    /// <c>StoreContext</c> needs package identity, which only the MSIX build has.
    /// <para>
    /// <b>This is a crash guard, not a feature flag.</b> Called from the unpackaged portable build,
    /// <c>StoreContext</c> does not throw something the <c>catch</c> below could handle: it faults
    /// natively inside <c>Windows.ApplicationModel.Store.dll</c> (<c>0xc0000005</c>, offset
    /// <c>0xb6902</c>, seen on 2026-09-15 and again on 2026-09-22 with the dump showing
    /// <c>RateOnStore_Click</c> straight into <c>RequestRatingAsync</c>), and an access violation in
    /// native code ends the process before any managed handler runs. The question therefore has to
    /// be asked before the call, not answered by the exception after it.
    /// </para>
    /// </summary>
    internal static bool InAppRatingSupported => PackagedRuntime.IsPackaged;

    public static async Task RequestRatingAsync(nint hwnd)
    {
        if (InAppRatingSupported && await TryInAppRatingAsync(hwnd))
            return;

        await LaunchStoreReviewAsync();
    }

    /// <returns>True when the dialog ran to a result the user chose (rated, or dismissed it), so
    /// there is nothing more to offer; false when it could not run and the Store page should
    /// open instead.</returns>
    private static async Task<bool> TryInAppRatingAsync(nint hwnd)
    {
        try
        {
            StoreContext context = StoreContext.GetDefault();
            InitializeWithWindow.Initialize(context, hwnd);
            StoreRateAndReviewResult result = await context.RequestRateAndReviewAppAsync();
            return result.Status is StoreRateAndReviewStatus.Succeeded
                or StoreRateAndReviewStatus.CanceledByUser;
        }
        catch (Exception ex)
        {
            Diag.Log($"Store in-app rating failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public static async Task OpenFeedbackHubAsync()
    {
        var hub = new Uri("feedback-hub:");
        try
        {
            var support = await global::Windows.System.Launcher.QueryUriSupportAsync(
                hub, LaunchQuerySupportType.Uri);
            if (support == LaunchQuerySupportStatus.Available &&
                await global::Windows.System.Launcher.LaunchUriAsync(hub))
                return;
        }
        catch (Exception ex)
        {
            Diag.Log($"Feedback Hub launch failed: {ex.GetType().Name}: {ex.Message}");
        }

        await LaunchStoreReviewAsync();
    }

    public static async Task LaunchStoreReviewAsync()
    {
        var store = new Uri($"ms-windows-store://review/?ProductId={ProductId}");
        try
        {
            if (await global::Windows.System.Launcher.LaunchUriAsync(store))
                return;
        }
        catch (Exception ex)
        {
            Diag.Log($"Store review URI failed: {ex.GetType().Name}: {ex.Message}");
        }

        Launcher.OpenUrl($"https://apps.microsoft.com/detail/{ProductId}#reviews");
    }
}
