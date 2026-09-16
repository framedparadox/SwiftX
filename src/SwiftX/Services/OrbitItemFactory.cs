using SwiftX.Models;
using Windows.ApplicationModel.DataTransfer;

namespace SwiftX.Services;

/// <summary>Shared helpers for turning a raw target string into a typed dock item.</summary>
public static class OrbitItemFactory
{
    /// <summary>Infers the <see cref="OrbitItemKind"/> of a target (path or URL).</summary>
    public static OrbitItemKind Classify(string target)
    {
        if (Directory.Exists(target))
            return OrbitItemKind.Folder;

        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return OrbitItemKind.WebLink;

        return Path.GetExtension(target).ToLowerInvariant() switch
        {
            ".lnk" => OrbitItemKind.Shortcut,
            ".exe" or ".bat" or ".cmd" or ".com" => OrbitItemKind.Application,
            _ => OrbitItemKind.File,
        };
    }

    /// <summary>A friendly default display name derived from a target.</summary>
    public static string SuggestName(string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return uri.Host.Length > 0 ? uri.Host : target;

        var trimmed = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileNameWithoutExtension(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    /// <summary>An orbit item seeded from a dropped or typed target string.</summary>
    public static OrbitItem FromTarget(string target) => new()
    {
        Kind = Classify(target),
        DisplayName = SuggestName(target),
        Target = target,
    };

    /// <summary>
    /// The first usable target in a drag payload, or null. Storage items first, then a web link,
    /// then plain text. Shared by the Add dialog and the Elements sidebar.
    /// </summary>
    public static async Task<string?> ReadDroppedTargetAsync(DataPackageView data)
    {
        var all = await ReadDroppedTargetsAsync(data);
        return all.Count > 0 ? all[0] : null;
    }

    /// <summary>
    /// Every usable target in a drag payload. A multi-select from Explorer yields one path per
    /// item that actually has one; a browser drag yields a single URL.
    /// A dropped <c>.lnk</c> keeps its own path and is classified as
    /// <see cref="OrbitItemKind.Shortcut"/> rather than resolved through ShortcutResolver to the
    /// executable behind it — the shortcut carries arguments, working directory and icon the
    /// bare target does not.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ReadDroppedTargetsAsync(DataPackageView data)
    {
        var found = new List<string>();

        if (data.Contains(StandardDataFormats.StorageItems))
        {
            var items = await data.GetStorageItemsAsync();
            foreach (var path in items.Select(item => item.Path))
            {
                if (!string.IsNullOrWhiteSpace(path))
                    found.Add(path);
            }
            if (found.Count > 0)
                return found;
        }

        if (data.Contains(StandardDataFormats.WebLink))
        {
            var uri = await data.GetWebLinkAsync();
            if (uri is not null)
                return [uri.ToString()];
        }

        if (data.Contains(StandardDataFormats.Text) &&
            TryReadTextTarget(await data.GetTextAsync(), out var text))
            return [text];

        return found;
    }

    /// <summary>
    /// Whether a payload is worth advertising as a drop: files, folders, shortcuts, or a web link.
    /// Text is accepted only when it is a real URL or an existing path — see
    /// <see cref="TryReadTextTarget"/>.
    /// </summary>
    public static bool LooksDroppable(DataPackageView data) =>
        data.Contains(StandardDataFormats.StorageItems) ||
        data.Contains(StandardDataFormats.WebLink);

    /// <summary>
    /// Whether a plain-text payload is something an orbit item can actually use, and what of it.
    /// Accepts an http/https URL or a path that exists on disk; surrounding quotes come off first.
    /// </summary>
    public static bool TryReadTextTarget(string? text, out string target)
    {
        target = string.Empty;

        var trimmed = text?.Trim().Trim('"');
        if (string.IsNullOrEmpty(trimmed))
            return false;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            target = trimmed;
            return true;
        }

        if (File.Exists(trimmed) || Directory.Exists(trimmed))
        {
            target = trimmed;
            return true;
        }

        return false;
    }
}
