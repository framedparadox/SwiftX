using SwiftX.Models;
using SwiftX.ToolWindows;

namespace SwiftX.Services;

public sealed class ToolWindowLauncher(RingManager manager)
{
    public ToolWindowBase? Open(ToolDefinition definition, string? seedFilePath = null, bool forceNewInstance = false)
    {
        if (!forceNewInstance && manager.Config.ReuseToolWindows &&
            manager.ToolWindows.TryFocusMostRecent(definition.Kind))
            return null;

        var page = definition.Factory();
        var window = new ToolWindowBase(manager, page, definition);
        window.Activate();
        window.BringToFront();

        if (!string.IsNullOrEmpty(seedFilePath))
            _ = SeedAsync(page, seedFilePath);

        return window;
    }

    private static async Task SeedAsync(ToolPages.ToolPage page, string path)
    {
        try
        {
            await page.LoadFileAsync(path);
        }
        catch (Exception ex)
        {
            Diag.Log($"ToolWindowLauncher: could not seed '{path}': {ex.GetType().Name}: {ex.Message}");
        }
    }
}
