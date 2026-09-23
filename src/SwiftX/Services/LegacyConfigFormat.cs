using System.Text.Json;
using System.Text.Json.Nodes;

namespace SwiftX.Services;

/// <summary>
/// Rewrites a configuration written by a build that still called the dock a "ring" into the names
/// <see cref="Models.AppConfig"/> carries now.
/// <para>
/// The ring-to-orbit rename crossed the persistence boundary: three top-level property names
/// (<c>Rings</c>, <c>RingRadius</c>, <c>HotCornerRing</c>) and one enum <em>value</em>
/// (<c>HotCornerLayout: "Ring"</c>) are written into the config file by name, so a rename without
/// this pass would not degrade gracefully — it would silently discard every orbit item the user
/// had arranged. Losing a user's whole dock on upgrade is the failure this file exists to
/// prevent.
/// </para>
/// <para>
/// The property renames are the load-bearing half and always were: nothing downstream can recover
/// <c>Rings</c> once it has been read as an unknown key and dropped. The stale
/// <c>HotCornerLayout: "Ring"</c> is now also caught a second time further down — since
/// <see cref="TolerantEnumConverterFactory"/> replaced the throwing built-in converter, an
/// unrecognized enum name reads as that setting's default instead of taking
/// <see cref="OrbitStore.Load"/> down to a blank configuration. Mapping it here is still what
/// makes it read as the <em>right</em> value (<c>Orbit</c>, which is what the user had) rather
/// than merely a harmless one, and the two guards are independent: this one is a fact about the
/// rename, that one is a floor under every enum in the file.
/// </para>
/// <para>
/// Deliberately a pre-deserialization pass over the JSON itself rather than legacy properties on
/// <see cref="Models.AppConfig"/>: a converter runs before any property setter would, so the enum
/// value cannot be fixed from one, and keeping the compatibility shim out of the model means the
/// model reads as the shape the app actually has today.
/// </para>
/// </summary>
internal static class LegacyConfigFormat
{
    /// <summary>The file a pre-rename build wrote, alongside today's <c>orbit.json</c>.</summary>
    internal const string LegacyFileName = "ring.json";

    /// <summary>
    /// Returns <paramref name="json"/> with every legacy name mapped forward, or the original
    /// string when there is nothing to map (the overwhelmingly common case — one cheap parse and
    /// four key probes on a file that is kilobytes).
    /// <para>
    /// Never throws: malformed input is handed straight back, so the caller's own deserialize is
    /// still the single place a bad configuration is diagnosed and reported.
    /// </para>
    /// </summary>
    internal static string Upgrade(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
                return json;

            bool changed = false;
            changed |= Rename(root, "Rings", "Orbits");
            changed |= Rename(root, "RingRadius", "OrbitRadius");
            changed |= Rename(root, "HotCornerRing", "HotCornerOrbit");

            // The full-orbit hot-corner layout, whose enum member was Ring and is now Orbit.
            // Quadrant, the only other member, never changed name.
            if (root["HotCornerLayout"] is JsonValue layout &&
                layout.TryGetValue<string>(out var name) &&
                string.Equals(name, "Ring", StringComparison.Ordinal))
            {
                root["HotCornerLayout"] = "Orbit";
                changed = true;
            }

            return changed ? root.ToJsonString() : json;
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>
    /// Moves <paramref name="from"/> to <paramref name="to"/>, and reports whether it did. A file
    /// already carrying the current name wins outright — the legacy key is dropped rather than
    /// allowed to overwrite it — so a hand-edited config holding both cannot end up with the older
    /// of the two.
    /// </summary>
    private static bool Rename(JsonObject root, string from, string to)
    {
        if (!root.Remove(from, out var value))
            return false;
        if (root.ContainsKey(to))
            return true;
        root[to] = value;
        return true;
    }
}
