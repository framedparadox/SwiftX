using SwiftX.Models;

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
            ".exe" or ".lnk" or ".bat" or ".cmd" or ".com" => OrbitItemKind.Application,
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
}
