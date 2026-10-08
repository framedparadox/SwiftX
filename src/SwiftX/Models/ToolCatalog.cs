using SwiftX.Services.Formats;
using SwiftX.ToolPages;

namespace SwiftX.Models;

public enum ToolKind
{
    Data,
    SecureValues,
    Base64,
    Certificate,
    QuickFormat,
}

public sealed record ToolDefinition(
    ToolKind Kind,
    string DisplayName,
    string Description,
    string Glyph,
    // Takes the OrbitManager rather than building the page bare: a page's own constructor is
    // where its panes, hover-accent buttons and privacy bubble all get built (see
    // ToolPages.EditorToolPage), and every one of those now reads the live theme and accent
    // straight off ToolPage.HostManager.Config — which means HostManager has to be real from the
    // very first line of that constructor, not assigned afterwards by ToolWindowBase the way
    // HostHwnd still is (that one really can't be: it needs a window handle that doesn't exist
    // until ToolWindowBase's own constructor runs).
    Func<OrbitManager, ToolPage> Factory)
{
    /// <summary>
    /// The client-area size <see cref="ToolWindows.ToolWindowBase"/> opens this tool at, or null
    /// for its 900x640 default. Quick Format opts into a smaller size — it has no editing surface
    /// or Advanced sidebar to fill the default footprint with, and a smaller window is what makes
    /// it read as the lightweight "quick glance" tool it is next to Data Formatter's full window.
    /// </summary>
    public (double Width, double Height)? WindowSize { get; init; }
}

/// <summary>Built-in developer tools: formatters and local secure values.</summary>
public static class ToolCatalog
{
    public static IReadOnlyList<ToolDefinition> All { get; } =
    [
        // JSON and XML used to be their own catalog entries, each this same FormatterPage pinned
        // to a fixed format. Since the page already auto-detects and offers "Convert to
        // JSON/XML" in its Advanced sidebar, a fixed format was never more than "Auto, minus
        // the detection step", so they were merged into this one entry.
        new ToolDefinition(ToolKind.Data, "Data Formatter", "Auto-detect and format JSON or XML.", "\uE943",
            m => new FormatterPage(m)),
        // The lightweight sibling of Data Formatter above: no editing, no command bar, no
        // Advanced sidebar. It reads the clipboard the moment it opens, auto-detects JSON, XML,
        // YAML or CSV, and shows the result formatted with a Copy and a Save button \u2014 see
        // QuickFormatPage's own doc comment for why this exists as a separate tool rather than a
        // mode of Data Formatter.
        new ToolDefinition(ToolKind.QuickFormat, "Quick Format", "Auto-format your last copied JSON, XML, YAML or CSV.", "\uE917",
            m => new QuickFormatPage(m))
        {
            WindowSize = (560, 460),
        },
        new ToolDefinition(ToolKind.SecureValues, "Secure values", "Store keys and secrets locally (masked).", "\uE72E",
            m => new SecureValuesPage(m)),
        new ToolDefinition(ToolKind.Base64, "Base64", "Encode and decode Base64 text.", "B64",
            m => new Base64Page(m)),
        new ToolDefinition(ToolKind.Certificate, "Certificate", "Inspect certificate, PKCS#12, and Java KeyStore files.", "\uEB95",
            m => new CertificatePage(m)),
    ];

    /// <summary>
    /// Target of the one orbit group that owns every built-in tool. Stored (unlike the
    /// <see cref="SystemOrbitItems"/> targets, which are synthesized at render time) so the user
    /// can move it around the orbit like any other item, but stable enough for
    /// <see cref="EnsureToolsGroup"/> to find it again on the next launch whatever it has been
    /// renamed to.
    /// </summary>
    public const string ToolsGroupTarget = "swiftx:toolsgroup";

    /// <summary>Glyph the tools group wears on the orbit.</summary>
    public const string ToolsGroupGlyph = "\uE90F";

    public static ToolDefinition? Get(ToolKind kind) =>
        All.FirstOrDefault(t => t.Kind == kind);

    public static ToolDefinition? GetByName(string name) =>
        Enum.TryParse<ToolKind>(name, ignoreCase: true, out var kind) ? Get(kind) : null;

    /// <summary>
    /// Rewrites orbit items still pointing at the retired "Json"/"Xml" tool targets (their catalog
    /// entries no longer exist, having been merged into "Data") to "Data" in place, walking into
    /// every group the way <see cref="SystemOrbitItems.Strip"/> does. Called once at startup on an
    /// existing config so an orbit item or "Dev tools" entry a user already has keeps working in
    /// the same slot instead of going dead. Returns true if anything changed, matching
    /// <see cref="SystemOrbitItems.Strip"/>'s contract, so the caller knows whether to persist.
    /// <para>
    /// A group that held the old "Data" entry <em>alongside</em> separate "Json" and "Xml" ones
    /// (the pre-merge "Dev tools" shape) ends up with three copies of "Data" side by side once
    /// the rename above runs — so each list that actually had a legacy target gets a pass that
    /// collapses same-target Tool duplicates back down to one, keeping whichever came first. A
    /// list untouched by the rename is left alone: two tools that legitimately point at the same
    /// target in different groups (or top-level vs. inside a group) were never this migration's
    /// business.
    /// </para>
    /// </summary>
    public static bool MigrateLegacyTargets(List<OrbitItem> items)
    {
        bool changed = false;
        bool renamedHere = false;
        foreach (var item in items)
        {
            if (item.Kind == OrbitItemKind.Tool && (item.Target == "Json" || item.Target == "Xml"))
            {
                item.Target = "Data";
                changed = true;
                renamedHere = true;
            }
            if (item.IsGroup)
                changed |= MigrateLegacyTargets(item.Children);
        }

        if (renamedHere)
        {
            var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Kind != OrbitItemKind.Tool)
                    continue;
                if (!seenTargets.Add(item.Target))
                {
                    items.RemoveAt(i);
                    i--;
                }
            }
        }

        return changed;
    }

    /// <summary>Builds the tools group itself — one orbit item holding every built-in tool.</summary>
    public static OrbitItem CreateToolsGroup() => new()
    {
        Kind = OrbitItemKind.Group,
        DisplayName = "Tools",
        Target = ToolsGroupTarget,
        CustomGlyph = ToolsGroupGlyph,
        Children = All.Select(CreateToolItem).ToList(),
    };

    private static OrbitItem CreateToolItem(ToolDefinition tool) => new()
    {
        Kind = OrbitItemKind.Tool,
        DisplayName = tool.DisplayName,
        Target = tool.Kind.ToString(),
        CustomGlyph = tool.Glyph,
    };

    /// <summary>
    /// Gathers the tools loose on the orbit into a single "Tools" group and makes sure that group
    /// holds all of them, so a tool never takes an orbit slot of its own and none is missing.
    /// <para>
    /// The group is found by <see cref="ToolsGroupTarget"/> first and by name second, which is
    /// what adopts the "Dev tools" group earlier builds seeded rather than leaving it beside a
    /// new one. Tools found at the top level are moved in — keeping the order they already had,
    /// and dropping duplicates of a kind the group already holds — and any built-in tool that is
    /// on the orbit nowhere at all is added. A group created here takes the orbit slot of the first
    /// tool it absorbed, so the tools stay roughly where the user last saw them.
    /// </para>
    /// <para>
    /// A tool inside any other group is left where it is. It is a copy the user put there on
    /// purpose (see <see cref="GroupDropRules"/>), and the Tools group keeps its own.
    /// </para>
    /// <para>
    /// Returns true if anything changed, matching <see cref="MigrateLegacyTargets"/> and
    /// <see cref="SystemOrbitItems.Strip"/>, so the caller knows whether to persist. Note it never
    /// creates the group on an orbit that has no tools at all: a user who deleted the group and its
    /// tools outright should not find them back on the next launch.
    /// </para>
    /// </summary>
    public static bool EnsureToolsGroup(List<OrbitItem> items)
    {
        var group = FindToolsGroup(items);
        var loose = new List<OrbitItem>();
        int firstLooseIndex = -1;

        for (int i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            if (ReferenceEquals(item, group) || !item.IsTool)
                continue;
            loose.Insert(0, item);
            items.RemoveAt(i);
            firstLooseIndex = i;
        }

        if (group is null)
        {
            // Nothing to hold and nothing to hold it in: leave the orbit exactly as it is.
            if (loose.Count == 0)
                return false;
            group = new OrbitItem
            {
                Kind = OrbitItemKind.Group,
                DisplayName = "Tools",
                Target = ToolsGroupTarget,
                CustomGlyph = ToolsGroupGlyph,
            };
            items.Insert(firstLooseIndex >= 0 ? Math.Min(firstLooseIndex, items.Count) : items.Count, group);
        }

        bool changed = loose.Count > 0;
        changed |= AdoptGroup(group);

        var held = new HashSet<string>(
            group.Children.Where(c => c.IsTool).Select(c => c.Target), StringComparer.OrdinalIgnoreCase);
        foreach (var tool in loose)
        {
            if (held.Add(tool.Target))
                group.Children.Add(tool);
        }

        // Every built-in tool belongs to this group, including one the orbit never had an item for
        // (a tool added to the catalog after this config was written).
        foreach (var tool in All)
        {
            if (held.Add(tool.Kind.ToString()))
            {
                group.Children.Add(CreateToolItem(tool));
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Points every tool item on the orbit at the Tools page's switches, so a tool that is turned
    /// off is hidden wherever it sits. Run at startup as well as on a toggle: a tool item
    /// <see cref="EnsureToolsGroup"/> has just added has no idea yet whether it is meant to be
    /// showing, and a hand-edited config can disagree with itself.
    /// </summary>
    public static void ApplyVisibility(List<OrbitItem> items, AppConfig config)
    {
        foreach (var item in items)
        {
            if (item.IsTool && Enum.TryParse<ToolKind>(item.Target, ignoreCase: true, out var kind))
                item.Hidden = !config.IsToolShown(kind);
            if (item.Children.Count > 0)
                ApplyVisibility(item.Children, config);
        }
    }

    private static OrbitItem? FindToolsGroup(List<OrbitItem> items)
    {
        var byTarget = items.FirstOrDefault(
            i => i.IsGroup && string.Equals(i.Target, ToolsGroupTarget, StringComparison.OrdinalIgnoreCase));
        if (byTarget is not null)
            return byTarget;

        // The names earlier builds gave it, before it had a target of its own to be found by.
        return items.FirstOrDefault(i => i.IsGroup &&
            (string.Equals(i.DisplayName, "Tools", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(i.DisplayName, "Dev tools", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Stamps the group's stable target on a group adopted by name. Its display name is
    /// left alone — renaming it is the user's business, and the target is what finds it now.</summary>
    private static bool AdoptGroup(OrbitItem group)
    {
        if (string.Equals(group.Target, ToolsGroupTarget, StringComparison.Ordinal))
            return false;
        group.Target = ToolsGroupTarget;
        return true;
    }
}

