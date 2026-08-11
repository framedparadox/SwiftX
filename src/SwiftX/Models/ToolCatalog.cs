using SwiftX.Services.Formats;
using SwiftX.ToolPages;

namespace SwiftX.Models;

public enum ToolKind
{
    Json,
    Xml,
    Data,
    SecureValues,
}

public sealed record ToolDefinition(
    ToolKind Kind,
    string DisplayName,
    string Description,
    string Glyph,
    Func<ToolPage> Factory)
{
}

/// <summary>Built-in developer tools: formatters and local secure values.</summary>
public static class ToolCatalog
{
    public static IReadOnlyList<ToolDefinition> All { get; } =
    [
        new ToolDefinition(ToolKind.Data, "Data", "Auto-detect and format JSON, XML, or CSV.", "\uE8A5",
            () => new FormatterPage(FormatRegistry.Auto)),
        new ToolDefinition(ToolKind.Json, "JSON", "Format, minify, and validate JSON.", "\uE943",
            () => new FormatterPage(FormatRegistry.Json)),
        new ToolDefinition(ToolKind.Xml, "XML", "Format, minify, and validate XML.", "</>",
            () => new FormatterPage(FormatRegistry.Xml)),
        new ToolDefinition(ToolKind.SecureValues, "Secure values", "Store keys and secrets locally (masked).", "\uE72E",
            () => new SecureValuesPage()),
    ];

    public static ToolDefinition? Get(ToolKind kind) =>
        All.FirstOrDefault(t => t.Kind == kind);

    public static ToolDefinition? GetByName(string name) =>
        Enum.TryParse<ToolKind>(name, ignoreCase: true, out var kind) ? Get(kind) : null;
}
