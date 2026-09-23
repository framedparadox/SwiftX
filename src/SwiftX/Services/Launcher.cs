using System.Diagnostics;
using SwiftX.Models;
using Microsoft.Win32;

namespace SwiftX.Services;

/// <summary>Launches a dock item via the shell (handles apps, files, folders, .lnk and URLs).</summary>
public static class Launcher
{
    public static void Launch(OrbitItem item)
    {
        Diag.Log($"Launch requested: kind={item.Kind} name='{item.DisplayName}' target='{item.Target}'");
        if (string.IsNullOrWhiteSpace(item.Target))
        {
            Diag.Log("Launch skipped: separator or empty target");
            return;
        }

        // A "File" item with no registered handler doesn't throw and doesn't visibly open
        // anything — ShellExecute silently spins up a helper process that exits on its own.
        // Detect that up front and hand it to the shell's own "Open with" picker instead of
        // clicking the item and nothing happening.
        if (item.Kind == OrbitItemKind.File && File.Exists(item.Target) &&
            !HasFileAssociation(Path.GetExtension(item.Target)))
        {
            Diag.Log($"Launch: no app associated with '{Path.GetExtension(item.Target)}' — opening the picker");
            OpenWithPicker(item.Target);
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = item.Target,
                UseShellExecute = true, // resolves associations, shortcuts, and default browser for URLs
            };

            if (item.Kind == OrbitItemKind.Application && !string.IsNullOrWhiteSpace(item.Arguments))
                psi.Arguments = item.Arguments;

            // Give apps a sensible working directory — but never let resolving it block the
            // launch itself (a bad path here must not stop the item from opening).
            var dir = TryGetWorkingDirectory(item);
            if (dir is not null)
                psi.WorkingDirectory = dir;

            Process.Start(psi);
            Diag.Log("Launch: Process.Start succeeded");
        }
        catch (Exception ex)
        {
            Diag.Log($"Launch FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Hands a URL to the user's default browser. For links the app itself owns (Settings' Feedback
    /// &amp; Support page), not orbit items — those go through <see cref="Launch"/>, which has to
    /// resolve working directories, arguments, and file associations that a plain URL never has.
    /// <para>
    /// <c>Process.Start</c> with ShellExecute rather than <c>Windows.System.Launcher</c>: SwiftX
    /// builds unpackaged (<c>WindowsPackageType=None</c>), and ShellExecute is the path the rest of
    /// this file already relies on to reach the default browser. Never throws — a browser that
    /// won't open is not worth taking a click handler down over.
    /// </para>
    /// </summary>
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            Diag.Log($"OpenUrl: launched '{url}'");
        }
        catch (Exception ex)
        {
            Diag.Log($"OpenUrl FAILED for '{url}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// True if Windows has an app registered to open <paramref name="extension"/> (e.g. ".md"),
    /// checked the same way the shell resolves it: the per-user "UserChoice" override first
    /// (also covers packaged/Store apps), then the classic HKEY_CLASSES_ROOT ProgId. Never
    /// throws; an inconclusive lookup returns true so it never blocks a launch that might
    /// otherwise work fine.
    /// </summary>
    private static bool HasFileAssociation(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;
        try
        {
            using (var userChoice = Registry.CurrentUser.OpenSubKey(
                       $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice"))
            {
                if (userChoice?.GetValue("ProgId") is string progId && !string.IsNullOrEmpty(progId))
                    return true;
            }
            using (var classesRoot = Registry.ClassesRoot.OpenSubKey(extension))
            {
                if (classesRoot?.GetValue(null) is string progId && !string.IsNullOrEmpty(progId))
                    return true;
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Summons the shell's own "How do you want to open this file?" picker.</summary>
    private static void OpenWithPicker(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "rundll32.exe",
                ArgumentList = { "shell32.dll,OpenAs_RunDLL", path },
                UseShellExecute = true,
            });
            Diag.Log("Launch: 'Open with' picker invoked");
        }
        catch (Exception ex)
        {
            Diag.Log($"Launch: 'Open with' picker FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Working directory for a real local file/app, or null. Never throws.</summary>
    private static string? TryGetWorkingDirectory(OrbitItem item)
    {
        if (item.Kind is not (OrbitItemKind.Application or OrbitItemKind.File))
            return null; // folders, web links, shell commands: let the shell decide
        try
        {
            var dir = Path.GetDirectoryName(item.Target);
            return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null;
        }
        catch
        {
            return null;
        }
    }
}
