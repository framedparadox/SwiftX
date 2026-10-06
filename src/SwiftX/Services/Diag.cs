using System.Diagnostics;
using System.Text;

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

    /// <summary>
    /// Names a directory to write the log in instead of <c>%TEMP%</c>. Set by the test assembly and
    /// by any harness that runs a second copy of the app beside the user's own.
    /// <para>
    /// The log is one fixed file shared by every SwiftX process on the machine, and a process
    /// <em>truncates it when it starts</em>. So a test run, or a measurement launch, wiped the
    /// diagnostic record of the user's real session — including the only managed trace of a crash
    /// that had just happened. <c>SWIFTX_DATA_DIR</c> already redirects everything else those
    /// processes write; this does the same for the one file that lives outside it.
    /// </para>
    /// </summary>
    internal const string LogDirectoryVariable = "SWIFTX_LOG_DIR";

    /// <summary>Where the log is written; see <see cref="ResolveLogPath"/>.</summary>
    internal static string LogPath { get; } = ResolveLogPath(
        Environment.GetEnvironmentVariable(LogDirectoryVariable), Path.GetTempPath());

    /// <summary>
    /// <c>swiftx.log</c> inside <paramref name="overrideDirectory"/> when one is given and usable,
    /// otherwise inside <paramref name="defaultDirectory"/>. Never throws: a log that cannot be
    /// placed where it was asked falls back to the default rather than costing the app its
    /// diagnostics.
    /// </summary>
    internal static string ResolveLogPath(string? overrideDirectory, string defaultDirectory)
    {
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            try
            {
                var directory = Path.GetFullPath(overrideDirectory);
                Directory.CreateDirectory(directory);
                return Path.Combine(directory, "swiftx.log");
            }
            catch
            {
                // Fall through to the default.
            }
        }
        return Path.Combine(defaultDirectory, "swiftx.log");
    }

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
                // Bytes, not characters. AppendAllText writes UTF-8, and what gets logged here is
                // item names, targets and exception messages — all of which carry whatever
                // alphabet the user's files and Windows locale use. Counting UTF-16 code units
                // let the file run to three times MaxBytes on a CJK or Cyrillic system, so the
                // one number this ceiling is stated in was the one it was not measured in.
                _written += Encoding.UTF8.GetByteCount(line);
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
        _written = Encoding.UTF8.GetByteCount(header);
    }
}
