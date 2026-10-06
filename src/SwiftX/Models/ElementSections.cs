namespace SwiftX.Models;

/// <summary>
/// One thing the Elements screen lists: a top-level orbit item and, when that item is a group, the
/// items inside it. A group's children travel with it rather than being sorted into a category of
/// their own, so a group and its contents always read as one unit.
/// </summary>
public sealed record ElementEntry(OrbitItem Item, IReadOnlyList<OrbitItem> Children);

/// <summary>One category's heading and everything listed under it, in orbit order.</summary>
public sealed record ElementSection(ElementCategory Category, IReadOnlyList<ElementEntry> Entries);

/// <summary>What sets an item's icon apart from the one its kind gets by default.</summary>
public enum ElementIconKind
{
    Default,
    Image,
    Glyph,
}

/// <summary>
/// The pure decisions behind the Elements screen, kept out of the window so they can be tested
/// without one: which items are listed and under which heading, and the facts a card states about
/// an item that need a little more than reading a property.
/// </summary>
public static class ElementSections
{
    /// <summary>
    /// Sorts the orbit's top-level items into one section per category, in the order
    /// <see cref="ElementCategories.All"/> lists them, the same order the "Add to orbit" sidebar
    /// uses. Every category is present even when nothing is in it, so the structure can be seen
    /// (and an empty category said so) rather than a heading appearing only once it has something
    /// under it.
    /// <para>
    /// The Tools category is left out on purpose, as are tool items and the built-in Tools group:
    /// the Tools screen owns them, and listing them here too would duplicate that screen instead of
    /// showing something new. See <see cref="IsListed"/>.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ElementSection> Build(IEnumerable<OrbitItem> topLevel)
    {
        var buckets = ElementCategories.All
            .Where(c => c != ElementCategory.Tools)
            .Select(c => (Category: c, Entries: new List<ElementEntry>()))
            .ToList();

        foreach (var item in topLevel)
        {
            if (!IsListed(item))
                continue;

            var bucket = buckets.FirstOrDefault(b => b.Category == ElementCategories.Of(item));
            if (bucket.Entries is null)
                continue;

            var children = item.IsGroup
                ? item.Children.Where(c => !c.IsTool).ToList()
                : new List<OrbitItem>();
            bucket.Entries.Add(new ElementEntry(item, children));
        }

        return buckets
            .Select(b => new ElementSection(b.Category, b.Entries))
            .ToList();
    }

    /// <summary>
    /// Whether the Elements screen lists <paramref name="item"/>. Tool items are managed from the
    /// Tools screen's own toggles, and the built-in Tools group is wholly owned by that screen.
    /// </summary>
    public static bool IsListed(OrbitItem item) =>
        !item.IsTool &&
        !item.IsEmpty &&
        !(item.IsGroup && string.Equals(
            item.Target, ToolCatalog.ToolsGroupTarget, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The path worth checking for existence on a card, or null when the item has none or checking
    /// it cannot be done cheaply and safely. Only an absolute drive-letter path qualifies: a URL, a
    /// UNC share, a shell URI, a bare command found on PATH or a <c>%VARIABLE%</c> path either says
    /// nothing about the disk or could stall on a network before answering.
    /// </summary>
    public static string? LocalPathToCheck(OrbitItem item)
    {
        if (item.Kind is not (OrbitItemKind.Application or OrbitItemKind.File
            or OrbitItemKind.Shortcut or OrbitItemKind.Folder))
            return null;

        var target = item.Target?.Trim();
        if (string.IsNullOrEmpty(target) || target.Length < 3)
            return null;

        bool driveRooted = char.IsAsciiLetter(target[0]) && target[1] == ':'
            && (target[2] == '\\' || target[2] == '/');
        return driveRooted ? target : null;
    }

    /// <summary>
    /// True only when <paramref name="path"/> is on a fixed local drive and nothing exists there.
    /// Anything uncertain (a network or removable drive, a drive letter with nothing behind it, an
    /// exception from the file system) answers false, because a "Missing" flag on something that
    /// is merely slow or unplugged is worse than no flag. Touches the disk, so call it off the UI
    /// thread.
    /// </summary>
    public static bool IsMissing(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || new DriveInfo(path[..1]).DriveType != DriveType.Fixed)
                return false;

            return !File.Exists(path) && !Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="item"/> draws a picture or glyph of its own choosing rather than the
    /// one its kind resolves to. A group's own mark is stored as its custom glyph (the Add dialog
    /// writes it), so a glyph only counts as custom when it differs from the kind's default one.
    /// </summary>
    public static ElementIconKind IconKindOf(OrbitItem item)
    {
        if (!string.IsNullOrEmpty(item.CustomGlyph) &&
            item.CustomGlyph != new OrbitItem { Kind = item.Kind }.Glyph)
            return ElementIconKind.Glyph;

        return string.IsNullOrWhiteSpace(item.CustomIconPath)
            ? ElementIconKind.Default
            : ElementIconKind.Image;
    }
}
