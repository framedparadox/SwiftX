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

    /// <summary>
    /// Home, Settings and Close (see <see cref="SystemOrbitItems"/>) — not a scannable kind of
    /// content the way every other category is, so unlike them it has no <see cref="Of"/> mapping
    /// and is never reached by sorting an orbit item's <c>Kind</c>. Built instead from a fixed list
    /// the sidebar special-cases, the same way <see cref="Groups"/> already is.
    /// </summary>
    Controls,

    /// <summary>
    /// Keyboard actions and run-command actions the user has created — stored in
    /// <see cref="OrbitProfile.DetachedActions"/> and shown in the sidebar ready to be dragged onto
    /// the orbit. Like <see cref="Controls"/>, has no <see cref="Of"/> mapping and is never reached
    /// by sorting a kind.
    /// </summary>
    CustomActions,
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
        ElementCategory.Controls,
        ElementCategory.CustomActions,
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
        // Controls, not a RowsFor-reachable category (see ElementCategory.Controls' own remarks):
        // mapped shortcuts and commands are never scanned suggestions, and more than one can share
        // nothing to de-duplicate on the way RowsFor's other categories do by Target.
        OrbitItemKind.KeyboardAction => ElementCategory.Controls,
        OrbitItemKind.CommandAction => ElementCategory.Controls,
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
        ElementCategory.Controls => "CONTROLS",
        ElementCategory.CustomActions => "CUSTOM ACTIONS",
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
        ElementCategory.Controls => char.ConvertFromUtf32(0xE713),
        ElementCategory.CustomActions => char.ConvertFromUtf32(0xE945),
        _ => char.ConvertFromUtf32(0xE7C3),
    };
}
