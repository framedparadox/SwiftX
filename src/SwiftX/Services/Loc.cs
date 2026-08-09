namespace SwiftX.Services;

/// <summary>English-only string table for Phase 1.</summary>
public static class Loc
{
    private static readonly Dictionary<string, string> Strings = new(StringComparer.Ordinal)
    {
        ["Tray.Tooltip"] = "SwiftX",
        ["Tray.Show"] = "Show ring",
        ["Tray.HideDock"] = "Hide ring",
        ["Tray.AddNew"] = "Add item…",
        ["Tray.Settings"] = "Settings",
        ["Tray.Quit"] = "Quit",
        ["Settings.Hotkey"] = "Invoke",
        ["Hotkey.Press"] = "Press keys…",
        ["Hotkey.None"] = "None",
        ["Tool.CopyOutput"] = "Copy",
        ["Tool.Save"] = "Save",
        ["Tool.Clear"] = "Clear",
        ["Tool.Validate"] = "Validate",
        ["Tool.Run"] = "Run",
        ["Tool.SwapInOut"] = "Swap",
        ["Tool.Format"] = "Format",
        ["Tool.Minify"] = "Minify",
        ["Tool.DiscardTitle"] = "Discard changes?",
        ["Tool.DiscardBody"] = "This window has unsaved content. Discard it?",
        ["Tool.Discard"] = "Discard",
        ["Common.Cancel"] = "Cancel",
        ["Common.Input"] = "Input",
        ["Formatter.InputPlaceholder"] = "Paste JSON or XML here…",
        ["Formatter.Indent"] = "Indent width",
        ["Tool.SavedTo"] = "Saved to {0}",
        ["Tool.FileUnreadable"] = "Could not read that file.",
        ["Tool.FileTooLarge"] = "File is too large (limit {0}).",
        ["Status.Counts"] = "{0} chars · {1} bytes",
        ["Status.Valid"] = "Valid",
        ["Tool.InvalidAt"] = "Invalid at {0}:{1}",
        ["Format.Auto"] = "Auto",
    };

    public static void Initialize(string? _ = null) { }

    public static string Get(string key) =>
        Strings.TryGetValue(key, out var value) ? value : key;

    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        try { return string.Format(template, args); }
        catch { return template; }
    }
}
