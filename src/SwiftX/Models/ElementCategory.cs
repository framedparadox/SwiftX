namespace SwiftX.Models;

/// <summary>
/// The buckets an orbit item is shown under wherever items are listed by type: the "Add to orbit"
/// sidebar and the Elements screen. One definition, so the two can never disagree about which
/// category a thing belongs to, what it is called, or what order the categories come in.
/// </summary>
public enum ElementCategory
{
    Apps,
    Shortcuts,
    Folders,
    Links,
    Groups,
    Tools,
}

public static class ElementCategories
{
    /// <summary>Every category in the order it is listed.</summary>
    public static IReadOnlyList<ElementCategory> All { get; } =
    [
        ElementCategory.Apps,
        ElementCategory.Shortcuts,
        ElementCategory.Folders,
        ElementCategory.Links,
        ElementCategory.Groups,
        ElementCategory.Tools,
    ];

    /// <summary>
    /// The category an item of <paramref name="kind"/> is listed under. A file and a shortcut both
    /// live under Shortcuts, which is what the sidebar has always done with them; folders now have
    /// a category of their own.
    /// </summary>
    public static ElementCategory Of(OrbitItemKind kind) => kind switch
    {
        OrbitItemKind.Application => ElementCategory.Apps,
        OrbitItemKind.File => ElementCategory.Shortcuts,
        OrbitItemKind.Shortcut => ElementCategory.Shortcuts,
        OrbitItemKind.Folder => ElementCategory.Folders,
        OrbitItemKind.WebLink => ElementCategory.Links,
        OrbitItemKind.Group => ElementCategory.Groups,
        OrbitItemKind.Tool => ElementCategory.Tools,
        _ => ElementCategory.Shortcuts,
    };

    public static ElementCategory Of(OrbitItem item) => Of(item.Kind);

    /// <summary>The upper-case heading a category is shown under.</summary>
    public static string Label(ElementCategory category) => category switch
    {
        ElementCategory.Apps => "APPS",
        ElementCategory.Shortcuts => "SHORTCUTS",
        ElementCategory.Folders => "FOLDERS",
        ElementCategory.Links => "LINKS",
        ElementCategory.Groups => "GROUPS",
        ElementCategory.Tools => "TOOLS",
        _ => category.ToString().ToUpperInvariant(),
    };

    /// <summary>
    /// The Segoe Fluent Icons glyph beside a category's heading. Built from codepoints, never
    /// from literal private-use characters, which do not survive a copy/paste through an editor.
    /// </summary>
    public static string Glyph(ElementCategory category) => category switch
    {
        ElementCategory.Apps => char.ConvertFromUtf32(0xE7C3),
        ElementCategory.Shortcuts => char.ConvertFromUtf32(0xE71B),
        ElementCategory.Folders => char.ConvertFromUtf32(0xE8B7),
        ElementCategory.Links => char.ConvertFromUtf32(0xE774),
        ElementCategory.Groups => char.ConvertFromUtf32(0xE838),
        ElementCategory.Tools => char.ConvertFromUtf32(0xE943),
        _ => char.ConvertFromUtf32(0xE7C3),
    };
}
