using SwiftX.Models;

namespace SwiftX.Services;

/// <summary>
/// Logos SwiftX ships for sites whose own favicon does not survive the orbit. GitHub's is a dark
/// mark on a transparent ground, which all but disappears on the dark glass, and no Segoe icon font
/// carries a GitHub mark to use as a glyph instead — so the default GitHub link used to wear the
/// generic "code" glyph, the very one the Data tool wears.
/// <para>
/// The picture is a white mark in a dark disc with a white rim (<c>Images/Brands/github.png</c>),
/// legible on both the light and the dark glass. It stands in for the favicon only: a picture or
/// glyph the user picked for the item still wins (see <see cref="IconService.LoadIconAsync"/>).
/// </para>
/// </summary>
public static class BrandIcons
{
    /// <summary>
    /// The glyph the default GitHub link used to carry — also the Data tool's. Recognised only to
    /// clear it again (see <see cref="MigrateLegacyGitHubGlyph"/>).
    /// </summary>
    // Built from the codepoint rather than written as a literal: a private-use character is invisible
    // in an editor, so a stray paste can swap it for another one without anyone seeing why.
    public static readonly string LegacyGitHubGlyph = char.ConvertFromUtf32(0xE943);

    /// <summary>Whether <paramref name="item"/> is a web link to GitHub, or one of its subdomains.</summary>
    public static bool IsGitHub(OrbitItem item)
    {
        if (item.Kind != OrbitItemKind.WebLink)
            return false;
        if (!Uri.TryCreate(item.Target, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return false;

        return string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The bundled logo for <paramref name="item"/>, or null when it has none or the file is not
    /// there (an unpackaged build run from a folder that lost its Images, say) — in which case the
    /// caller falls back to the favicon exactly as before.
    /// </summary>
    /// <param name="baseDirectory">Where <c>Images</c> lives; the app's own folder unless a test says otherwise.</param>
    public static string? PathFor(OrbitItem item, string? baseDirectory = null)
    {
        if (!IsGitHub(item))
            return null;

        return baseDirectory is null ? Bundled.Value : Find(baseDirectory);
    }

    private const string GitHubFile = "github.png";

    /// <summary>
    /// The app's own copy, looked for once: this is asked on every icon resolution, which can be on
    /// the UI thread, and the answer cannot change while the process runs.
    /// </summary>
    private static readonly Lazy<string?> Bundled = new(() => Find(AppContext.BaseDirectory));

    private static string? Find(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, "Images", "Brands", GitHubFile);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Takes the old "code" glyph off GitHub links that still carry it, so they show the logo
    /// instead. That glyph was the default's, never a choice anyone made at the picker — it was
    /// planted by <c>OrbitManager.SeedDefaults</c> and the first-run guide — so an orbit that came
    /// from either picks up the new logo without having to be reset. A GitHub link with any other
    /// glyph, or a picture, is the user's own and is left alone. Returns whether anything changed,
    /// like the other repairs run at start-up, so the caller knows whether to save.
    /// </summary>
    public static bool MigrateLegacyGitHubGlyph(IEnumerable<OrbitItem> items)
    {
        bool changed = false;
        foreach (var item in items)
        {
            if (IsGitHub(item) &&
                string.Equals(item.CustomGlyph, LegacyGitHubGlyph, StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(item.CustomIconPath))
            {
                item.CustomGlyph = null;
                item.IconSignature = null;
                changed = true;
            }

            if (item.Children.Count > 0)
                changed |= MigrateLegacyGitHubGlyph(item.Children);
        }
        return changed;
    }
}
