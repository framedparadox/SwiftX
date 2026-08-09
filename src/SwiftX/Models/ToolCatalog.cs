using SwiftX.Services.Formats;
using SwiftX.ToolPages;

namespace SwiftX.Models;

public enum ToolKind
{
    Json,
    Xml,
}

public sealed record ToolDefinition(
    ToolKind Kind,
    string DisplayName,
    string Description,
    string Glyph,
    Func<ToolPage> Factory)
{
}

/// <summary>Phase 1 catalog: JSON and XML formatters only.</summary>
public static class ToolCatalog
{
    public static IReadOnlyList<ToolDefinition> All { get; } =
    [
        new ToolDefinition(ToolKind.Json, "JSON", "Format, minify, and validate JSON.", "\uE943",
            () => new FormatterPage(FormatRegistry.Json)),
        new ToolDefinition(ToolKind.Xml, "XML", "Format, minify, and validate XML.", "</>",
            () => new FormatterPage(FormatRegistry.Xml)),
    ];

    public static ToolDefinition? Get(ToolKind kind) =>
        All.FirstOrDefault(t => t.Kind == kind);

    public static ToolDefinition? GetByName(string name) =>
        Enum.TryParse<ToolKind>(name, ignoreCase: true, out var kind) ? Get(kind) : null;
}
