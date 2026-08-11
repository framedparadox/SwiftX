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
        ["Formatter.InputPlaceholder"] = "Paste JSON, XML, or CSV here…",
        ["Formatter.Indent"] = "Indent width",
        ["Formatter.Detected"] = "Detected: {0}",
        ["Formatter.FormatName"] = "{0}",
        ["Formatter.SortKeys"] = "Sort keys",
        ["Formatter.Escape"] = "Escape",
        ["Formatter.Unescape"] = "Unescape",
        ["Formatter.Convert"] = "Convert",
        ["Formatter.ToJson"] = "To JSON",
        ["Formatter.ToXml"] = "To XML",
        ["Formatter.ToCsv"] = "To CSV",
        ["Formatter.Escaped"] = "Escaped as JSON string",
        ["Formatter.SortKeysJsonOnly"] = "Sort keys works best on JSON objects.",
        ["Tool.Advanced"] = "Advanced",
        ["Tool.SavedTo"] = "Saved to {0}",
        ["Tool.FileUnreadable"] = "Could not read that file.",
        ["Tool.FileTooLarge"] = "File is too large (limit {0}).",
        ["Status.Counts"] = "{0} chars · {1} bytes",
        ["Status.Valid"] = "Valid",
        ["Tool.InvalidAt"] = "Invalid at {0}:{1}",
        ["Format.Auto"] = "Auto",
        ["Format.Csv"] = "CSV",
        ["SecureValues.Blurb"] = "Keys and secrets stay on this PC (DPAPI). Values are masked; Copy uses the real secret.",
        ["SecureValues.Add"] = "Add",
        ["SecureValues.Key"] = "Key",
        ["SecureValues.Value"] = "Value",
        ["SecureValues.KeyPlaceholder"] = "Key name",
        ["SecureValues.ValuePlaceholder"] = "Secret value",
        ["SecureValues.Save"] = "Save",
        ["SecureValues.Copy"] = "Copy value",
        ["SecureValues.Delete"] = "Delete",
        ["SecureValues.KeyRequired"] = "Enter a key before saving.",
        ["SecureValues.Saved"] = "Saved locally",
        ["SecureValues.Copied"] = "Copied to clipboard",
        ["SecureValues.Count"] = "{0} saved",
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
