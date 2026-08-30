namespace SwiftX.Services.Syntax;

/// <summary>One <see cref="FoldRegion"/> as it currently shows up in a rendered outline: the
/// 1-based display line it starts on, and whether it's the collapsed or expanded one right now.
/// A UI layer turns this into a clickable gutter marker; this type carries no click behavior of
/// its own, so it stays plain data that a test can compare directly.</summary>
public readonly record struct FoldMarker(int Line, FoldRegion Region, bool Collapsed);

/// <summary>
/// Renders a document's collapsed/expanded state to display text plus the marker each visible
/// fold shows up as — the pure half of <c>FormatterPage</c>'s outline mode, split out so the
/// nesting/offset arithmetic can be tested without a live RichEditBox.
/// </summary>
public static class FoldRenderer
{
    /// <summary>Placeholder a collapsed region's hidden text is replaced with.</summary>
    public const string FoldPlaceholder = "…";

    /// <summary>
    /// Builds the display text and marker list for <paramref name="source"/> given
    /// <paramref name="allRegions"/> (every foldable node — see <see cref="FoldFinder"/>) and
    /// <paramref name="collapsedStarts"/> (which of them, by <see cref="FoldRegion.Start"/>, are
    /// currently collapsed).
    /// <para>
    /// A region nested inside a currently-collapsed one is simply never visited — its content
    /// went with its parent's placeholder — which is why filtering collapsed regions down to
    /// their outermost members is enough to keep nesting correct with no recursion: a collapsed
    /// region always removes everything inside it regardless of that content's own fold state,
    /// and every region this walk does visit is either collapsed itself (contributes a
    /// placeholder and skips its own contents) or expanded (contributes nothing but its own
    /// marker and lets the walk fall straight through the text it opens).
    /// </para>
    /// </summary>
    public static (string DisplayText, IReadOnlyList<FoldMarker> Markers) Render(
        string source, IReadOnlyList<FoldRegion> allRegions, IReadOnlySet<int> collapsedStarts)
    {
        var collapsed = allRegions
            .Where(r => collapsedStarts.Contains(r.Start))
            .OrderBy(r => r.Start)
            .ToList();
        var effectiveCollapsed = new List<FoldRegion>();
        int lastEnd = -1;
        foreach (var r in collapsed)
        {
            if (r.Start < lastEnd)
                continue; // nested inside the previous outermost collapsed region
            effectiveCollapsed.Add(r);
            lastEnd = r.End;
        }
        var effectiveCollapsedStarts = new HashSet<int>(effectiveCollapsed.Select(r => r.Start));

        bool HiddenByCollapse(FoldRegion r) =>
            effectiveCollapsed.Any(c => c.Start != r.Start && c.Start <= r.Start && r.End <= c.End);

        var visible = allRegions.Where(r => !HiddenByCollapse(r)).OrderBy(r => r.Start).ToList();

        var text = new System.Text.StringBuilder(source.Length);
        var markers = new List<FoldMarker>();
        int cursor = 0;
        int line = 1;

        foreach (var region in visible)
        {
            for (int k = cursor; k < region.Start; k++)
                if (source[k] == '\n')
                    line++;
            text.Append(source, cursor, region.Start - cursor);

            bool isCollapsed = effectiveCollapsedStarts.Contains(region.Start);
            markers.Add(new FoldMarker(line, region, isCollapsed));

            if (isCollapsed)
            {
                text.Append(FoldPlaceholder);
                cursor = region.End; // skip the hidden content — none of it is visible or counted
            }
            else
            {
                cursor = region.Start; // nothing to skip; later regions/the flush walk it normally
            }
        }
        text.Append(source, cursor, source.Length - cursor);

        return (text.ToString(), markers);
    }
}
