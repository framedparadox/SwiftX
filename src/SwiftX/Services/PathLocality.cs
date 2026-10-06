namespace SwiftX.Services;

/// <summary>
/// Whether touching a path can reach the network.
/// <para>
/// <b>Why this is a question worth asking.</b> <c>File.Exists</c>, <c>Directory.Exists</c>, a
/// working-directory check and a launch all look like instant local calls, and on a UNC path or a
/// mapped network drive they are not: the redirector connects first, and when the host is
/// unreachable it blocks until its own timeout — measured here at <b>21 s</b> for
/// <c>\\192.0.2.1\share</c>. On the UI thread that is a frozen app (Windows ghosts the window as
/// "not responding" after five seconds). An orbit item on a share, opened from a laptop that has
/// left the office or lost its VPN, is exactly that case.
/// </para>
/// <para>
/// This answers from the path's <em>shape</em> and the drive's type, both of which Windows knows
/// without touching the wire (<c>GetDriveType</c> does no network I/O, even for a disconnected
/// mapped drive), so it is safe to call anywhere.
/// </para>
/// </summary>
public static class PathLocality
{
    /// <summary>
    /// False when <paramref name="path"/> is a UNC path or lives on a network drive; true for
    /// everything else, including paths that are not file-system paths at all (URLs, shell
    /// monikers) and ones too malformed to classify — those never block, so they are not what this
    /// guards against.
    /// </summary>
    public static bool IsLocal(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        var p = path.AsSpan().Trim();

        if (p.StartsWith(@"\\", StringComparison.Ordinal) || p.StartsWith("//", StringComparison.Ordinal))
        {
            // \\?\UNC\server\share is the long spelling of a UNC path; \\?\C:\ and \\.\C: are the
            // long and device spellings of a local one. Anything else starting \\ is a plain UNC.
            if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                return false;
            if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal))
                return p.Length < 6 || p[5] != ':' || IsLocalDrive(p[4]);
            return false;
        }

        // "Z:\folder" or "Z:file": a drive letter, which may be mapped to a share.
        if (p.Length >= 2 && p[1] == ':' && char.IsAsciiLetter(p[0]))
            return IsLocalDrive(p[0]);

        return true;
    }

    private static bool IsLocalDrive(char letter)
    {
        try
        {
            // A drive letter that is not mapped at all reads as NoRootDirectory, and probing it is
            // an immediate "no such device" rather than a network wait, so only a mapped network
            // drive counts as remote.
            return new DriveInfo(char.ToUpperInvariant(letter).ToString()).DriveType != DriveType.Network;
        }
        catch
        {
            return true;
        }
    }
}
