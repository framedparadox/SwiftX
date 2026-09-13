using System.Runtime.InteropServices;
using System.Text;
using SwiftX.Interop;

namespace SwiftX.Services;

/// <summary>
/// Creates and removes the optional <c>SwiftX.lnk</c> on the user's desktop.
/// <para>
/// <b>Why this is a setting and not an installer step.</b> The Store build is an MSIX package, and
/// a packaged app does not get to drop files on the desktop at install time — MSIX deployment
/// writes the Start entry and nothing else, by design, and a package that tried would not pass
/// certification. The portable zip has no installer at all. So the only honest place for "put a
/// SwiftX icon on my desktop" is inside the app, as something the user turns on: see
/// <see cref="Models.AppConfig.DesktopShortcut"/> and Settings ▸ General.
/// </para>
/// <para>
/// <b>What the shortcut points at depends on how SwiftX was installed</b>, and the difference is
/// not cosmetic:
/// </para>
/// <list type="bullet">
///   <item><description><b>Unpackaged.</b> Straight at <see cref="Environment.ProcessPath"/>. The
///   exe is where the user put it and stays there.</description></item>
///   <item><description><b>Packaged.</b> A shortcut to the exe would point inside
///   <c>%ProgramFiles%\WindowsApps\&lt;package full name&gt;\</c> — a path that contains the
///   package <em>version</em> and is therefore replaced by the next Store update, leaving a dead
///   shortcut. It also bypasses the package's activation, so the process would start without
///   package identity and quietly lose the startup task and everything else that depends on it.
///   The supported form is to launch the app by its AUMID through the shell's virtual
///   applications folder, which is exactly what Explorer itself writes when an app is dragged out
///   of Start.</description></item>
/// </list>
/// </summary>
public static class DesktopShortcutService
{
    /// <summary>
    /// Written into the shortcut's Description (the tooltip Explorer shows) and read back before
    /// the shortcut is ever deleted.
    /// <para>
    /// The point is that turning the setting off must never delete a file SwiftX did not write. A
    /// user may well have made their own <c>SwiftX.lnk</c> — pinned with custom arguments, or
    /// pointing at a different copy — and a settings toggle silently eating it would be
    /// unforgivable. Matching on the target path is not enough on its own, because the packaged
    /// shortcut's target is <c>explorer.exe</c> and always will be; the marker is unambiguous.
    /// </para>
    /// </summary>
    private const string Marker = "SwiftX quick-access orbit";

    /// <summary>
    /// The <c>Id</c> of the single <c>&lt;Application&gt;</c> element in
    /// <c>Package.appxmanifest</c>. An AUMID is <c>&lt;PackageFamilyName&gt;!&lt;Application
    /// Id&gt;</c>, so this string and the manifest have to change together.
    /// </summary>
    private const string ApplicationId = "App";

    /// <summary>Where the shortcut goes. <c>DesktopDirectory</c>, not <c>Desktop</c>: the latter is
    /// the virtual shell folder and comes back as an empty string outside an Explorer host.</summary>
    public static string ShortcutPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "SwiftX.lnk");

    /// <summary>Whether a desktop shortcut to SwiftX is there now — whoever created it.</summary>
    public static bool Exists()
    {
        try
        {
            return File.Exists(ShortcutPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Brings the desktop into line with <paramref name="wanted"/>, and reports what is actually
    /// on the desktop afterwards — which is not always what was asked for: a read-only or
    /// redirected desktop can refuse the write, and a shortcut SwiftX did not create is never
    /// removed. Callers are expected to reflect the returned value rather than assume.
    /// </summary>
    public static bool Apply(bool wanted) => wanted ? Create() : Remove();

    /// <summary>Writes the shortcut. Returns whether one is there when it finishes.</summary>
    public static bool Create()
    {
        try
        {
            if (File.Exists(ShortcutPath) && !IsOurs(ShortcutPath))
            {
                // Someone else's SwiftX.lnk. The user asked for a SwiftX shortcut on the desktop
                // and there is one, so this counts as done — overwriting their version to plant
                // our marker in it would be taking ownership of a file we were not given.
                Diag.Log("DesktopShortcutService: a SwiftX.lnk we didn't write is already there; leaving it.");
                return true;
            }

            Write();
            return true;
        }
        catch (Exception ex)
        {
            Diag.Log("DesktopShortcutService.Create failed: " + ex);
            return false;
        }
    }

    /// <summary>Deletes the shortcut, but only if SwiftX wrote it. Returns whether one is still
    /// there afterwards, so a refusal to delete a foreign file shows up in the UI as the toggle
    /// staying on rather than as a silent no-op.</summary>
    public static bool Remove()
    {
        try
        {
            if (!File.Exists(ShortcutPath))
                return false;

            if (!IsOurs(ShortcutPath))
            {
                Diag.Log("DesktopShortcutService: SwiftX.lnk isn't ours; refusing to delete it.");
                return true;
            }

            File.Delete(ShortcutPath);
            return false;
        }
        catch (Exception ex)
        {
            Diag.Log("DesktopShortcutService.Remove failed: " + ex);
            return File.Exists(ShortcutPath);
        }
    }

    private static void Write()
    {
        object? link = null;
        try
        {
            link = new NativeMethods.ShellLink();
            var shellLink = (NativeMethods.IShellLinkW)link;

            if (PackagedRuntime.IsPackaged && TryGetAumid(out string aumid))
            {
                // shell:AppsFolder is the shell's own virtual folder of installed applications,
                // keyed by AUMID; handing that to Explorer activates the package exactly as
                // clicking the Start tile does. Nothing here names a file path, which is the whole
                // reason it survives a package update.
                shellLink.SetPath(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
                shellLink.SetArguments(@"shell:AppsFolder\" + aumid);

                // An icon has to be named explicitly here: with explorer.exe as the target,
                // Explorer would otherwise draw explorer's own icon on the shortcut. The package's
                // logos are .png and a .lnk can only reference an icon resource, so this points at
                // SwiftX.ico instead — copied out to the data directory first, because the copy
                // inside the package install folder lives under the same version-stamped path the
                // target deliberately avoids.
                string icon = StableIconPath();
                if (icon.Length > 0)
                    shellLink.SetIconLocation(icon, 0);
            }
            else
            {
                string exe = Environment.ProcessPath ??
                    Path.Combine(AppContext.BaseDirectory, "SwiftX.exe");
                shellLink.SetPath(exe);
                shellLink.SetWorkingDirectory(Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory);
                // Index 0 is the exe's own <ApplicationIcon>, so the shortcut tracks the app's
                // icon automatically and there is nothing extra to keep in step.
                shellLink.SetIconLocation(exe, 0);
            }

            shellLink.SetDescription(Marker);
            ((NativeMethods.IPersistFile)link).Save(ShortcutPath, false);
            Diag.Log("DesktopShortcutService: wrote " + ShortcutPath);
        }
        finally
        {
            if (link is not null && Marshal.IsComObject(link))
                Marshal.ReleaseComObject(link);
        }
    }

    /// <summary>
    /// Whether the shortcut at <paramref name="path"/> carries SwiftX's own <see cref="Marker"/>.
    /// A shortcut that cannot be read at all is treated as not ours, which is the safe direction:
    /// the only thing this gates is deletion.
    /// </summary>
    private static bool IsOurs(string path)
    {
        object? link = null;
        try
        {
            link = new NativeMethods.ShellLink();
            ((NativeMethods.IPersistFile)link).Load(path, 0 /* STGM_READ */);

            var description = new StringBuilder(1024);
            ((NativeMethods.IShellLinkW)link).GetDescription(description, description.Capacity);
            return string.Equals(description.ToString().Trim(), Marker, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            Diag.Log($"DesktopShortcutService: couldn't read '{path}' ({ex.GetType().Name}); treating it as not ours.");
            return false;
        }
        finally
        {
            if (link is not null && Marshal.IsComObject(link))
                Marshal.ReleaseComObject(link);
        }
    }

    /// <summary>
    /// This package's Application User Model ID, <c>&lt;PackageFamilyName&gt;!App</c>.
    /// <para>
    /// The family name — not the full name — is the version-independent half of a package's
    /// identity (<c>Name_PublisherHash</c>), which is what makes the AUMID stable across updates.
    /// </para>
    /// </summary>
    private static bool TryGetAumid(out string aumid)
    {
        try
        {
            aumid = Windows.ApplicationModel.Package.Current.Id.FamilyName + "!" + ApplicationId;
            return true;
        }
        catch (Exception ex)
        {
            // Package.Current throws outright when there is no package identity. PackagedRuntime
            // has already said there is one, so reaching here means something stranger — fall back
            // to the unpackaged shape rather than write a shortcut that launches nothing.
            Diag.Log("DesktopShortcutService: package identity lookup failed: " + ex.Message);
            aumid = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// A copy of SwiftX.ico somewhere the shortcut can keep pointing at, or an empty string if it
    /// could not be produced. The copy is refreshed whenever the shipped icon is newer, so a
    /// rebranded build updates the shortcut's icon on the next toggle.
    /// </summary>
    private static string StableIconPath()
    {
        try
        {
            string source = Path.Combine(AppContext.BaseDirectory, "Assets", "SwiftX.ico");
            if (!File.Exists(source))
                return Environment.ProcessPath ?? string.Empty;

            string destination = Path.Combine(OrbitStore.DataDirectory, "SwiftX.ico");
            Directory.CreateDirectory(OrbitStore.DataDirectory);
            if (!File.Exists(destination) ||
                File.GetLastWriteTimeUtc(destination) < File.GetLastWriteTimeUtc(source))
                File.Copy(source, destination, overwrite: true);
            return destination;
        }
        catch (Exception ex)
        {
            Diag.Log("DesktopShortcutService: icon copy failed: " + ex.Message);
            return Environment.ProcessPath ?? string.Empty;
        }
    }
}
