using SwiftX.Services.Formats;
using SwiftX.ToolPages;

namespace SwiftX.Models;

public enum ToolKind
{
    Data,
    SecureValues,
    Base64,
    Certificate,
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
        // JSON and XML used to be their own catalog entries, each this same FormatterPage pinned
        // to a fixed format. Since the page already auto-detects and offers "Convert to
        // JSON/XML/CSV" in its Advanced sidebar, a fixed format was never more than "Auto, minus
        // the detection step", so they were merged into this one entry.
        new ToolDefinition(ToolKind.Data, "Data Formatter", "Auto-detect and format JSON, XML, or CSV.", "\uE943",
            () => new FormatterPage()),
        new ToolDefinition(ToolKind.SecureValues, "Secure values", "Store keys and secrets locally (masked).", "\uE72E",
            () => new SecureValuesPage()),
        new ToolDefinition(ToolKind.Base64, "Base64", "Encode and decode Base64 text.", "B64",
            () => new Base64Page()),
        new ToolDefinition(ToolKind.Certificate, "Certificate", "Inspect certificate, PKCS#12, and Java KeyStore files.", "\uEB95",
            () => new CertificatePage()),
    ];

    public static ToolDefinition? Get(ToolKind kind) =>
        All.FirstOrDefault(t => t.Kind == kind);

    public static ToolDefinition? GetByName(string name) =>
        Enum.TryParse<ToolKind>(name, ignoreCase: true, out var kind) ? Get(kind) : null;

    /// <summary>
    /// Rewrites ring items still pointing at the retired "Json"/"Xml" tool targets (their catalog
    /// entries no longer exist, having been merged into "Data") to "Data" in place, walking into
    /// every group the way <see cref="SystemRingItems.Strip"/> does. Called once at startup on an
    /// existing config so a ring item or "Dev tools" entry a user already has keeps working in
    /// the same slot instead of going dead. Returns true if anything changed, matching
    /// <see cref="SystemRingItems.Strip"/>'s contract, so the caller knows whether to persist.
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
    public static bool MigrateLegacyTargets(List<RingItem> items)
    {
        bool changed = false;
        bool renamedHere = false;
        foreach (var item in items)
        {
            if (item.Kind == RingItemKind.Tool && (item.Target == "Json" || item.Target == "Xml"))
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
                if (item.Kind != RingItemKind.Tool)
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
}
