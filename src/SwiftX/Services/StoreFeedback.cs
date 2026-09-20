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

    public static async Task RequestRatingAsync(nint hwnd)
    {
        try
        {
            StoreContext context = StoreContext.GetDefault();
            InitializeWithWindow.Initialize(context, hwnd);
            StoreRateAndReviewResult result = await context.RequestRateAndReviewAppAsync();
            if (result.Status is StoreRateAndReviewStatus.Succeeded
                or StoreRateAndReviewStatus.CanceledByUser)
                return;
        }
        catch (Exception ex)
        {
            Diag.Log($"Store in-app rating failed: {ex.GetType().Name}: {ex.Message}");
        }

        await LaunchStoreReviewAsync();
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
