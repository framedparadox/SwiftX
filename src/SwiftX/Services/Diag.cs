using System.Diagnostics;

namespace SwiftX.Services;

/// <summary>
/// Lightweight file logger for diagnosing runtime layout/window issues.
/// <para>
/// The file is truncated at startup and again whenever it passes <see cref="MaxBytes"/>, so a
/// SwiftX left running for weeks in the tray cannot grow one. Unbounded was survivable while the
/// log only recorded startup and failures, but the app logs every launch and every failed icon
/// resolution, and the two feeders that never stop — the hot corner's 20 Hz poll and the orbit's
/// outside-click timer — each log on fault. A repeating fault therefore wrote until the disk
/// said no, on the user's system drive, for a diagnostic nobody had asked for.
/// </para>
/// <para>
/// Nothing secret is written here by design: what is logged is item names, targets and exception
/// messages. Secure values never reach it — <see cref="SecureValueStore"/> logs the failure, not
/// the payload — and neither does certificate or clipboard content. Keep it that way; this file
/// sits at a fixed, predictable path in <c>%TEMP%</c> and is not encrypted.
/// </para>
/// </summary>
public static class Diag
{
    /// <summary>
    /// The point at which the log is started over. Large enough to hold a long session's worth
    /// of the lines this app actually writes, small enough that a runaway logger costs a
    /// megabyte rather than a volume.
    /// </summary>
    private const long MaxBytes = 4L * 1024 * 1024;

    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "swiftx.log");

    private static readonly object Gate = new();

    /// <summary>Bytes written since the last truncation, tracked rather than measured so the
    /// common path is not a file-metadata call per line.</summary>
    private static long _written;

    static Diag()
    {
        try { Restart("started"); }
        catch { /* ignore */ }
    }

    public static void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        Debug.WriteLine("[SwiftX] " + line);
        try
        {
            lock (Gate)
            {
                if (_written >= MaxBytes)
                    Restart("log truncated at " + MaxBytes + " bytes");

                line += "\n";
                File.AppendAllText(LogPath, line);
                _written += line.Length;
            }
        }
        catch { /* ignore */ }
    }

    /// <summary>Replaces the file with a fresh header. Callers hold <see cref="Gate"/>, except
    /// the static constructor, which runs before any caller can.</summary>
    private static void Restart(string reason)
    {
        var header = $"=== SwiftX {reason} {DateTime.Now:O} ===\n";
        File.WriteAllText(LogPath, header);
        _written = header.Length;
    }
}
