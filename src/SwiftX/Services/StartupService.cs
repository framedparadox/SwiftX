using Microsoft.Win32;
using Windows.ApplicationModel;

namespace SwiftX.Services;

/// <summary>
/// Toggles "launch SwiftX at sign-in", by whichever mechanism this build's packaging supports.
/// <para>
/// <b>Unpackaged</b> (the portable zip) uses the per-user <c>Run</c> registry key — per-user
/// (HKCU) so it needs no elevation.
/// </para>
/// <para>
/// <b>Packaged</b> (the Microsoft Store MSIX) must not: a packaged process's writes under
/// <c>HKCU\Software</c> are captured in the package's own virtualized registry hive, which
/// Windows' autostart never reads — the toggle would appear to work and then do nothing after a
/// reboot, and the value would be invisible to the user in Task Manager's Startup tab. The
/// supported equivalent is the <c>windows.startupTask</c> extension declared in
/// <c>Package.appxmanifest</c> (<c>TaskId</c> must match <see cref="TaskId"/> exactly), enabled
/// at runtime through the <see cref="StartupTask"/> API. That also puts the entry where users
/// expect to manage it, which is the point: Windows lets them override the app from Task Manager
/// or Settings, and when they have, <see cref="RequestEnableAsync"/> reports
/// <see cref="StartupTaskState.DisabledByUser"/> and the app must respect it rather than keep
/// asking.
/// </para>
/// <para>
/// Every call is guarded — a locked-down registry or an unavailable startup task means the
/// setting reports "off" rather than crashing the dock.
/// </para>
/// </summary>
public static class StartupService
{
    /// <summary>
    /// Identifies the startup task in the MSIX manifest. Changing this string means changing
    /// <c>Package.appxmanifest</c>'s <c>desktop:StartupTask TaskId</c> in the same commit — a
    /// mismatch makes <see cref="StartupTask.GetAsync"/> throw and the toggle a no-op.
    /// </summary>
    private const string TaskId = "SwiftXStartupTask";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SwiftX";

    /// <summary>
    /// Marks a launch as Windows' own, not the user's.
    /// <para>
    /// Launching SwiftX by hand now opens Settings, so that the Start-menu, taskbar and desktop
    /// entries visibly do something for an app that has no window of its own (see
    /// <see cref="SingleInstanceSignal"/>). An autostart launch must not: nobody wants a settings
    /// window in their face every time they sign in. The two are indistinguishable from inside the
    /// process unless the autostart registration says so, which is what this argument is for.
    /// </para>
    /// </summary>
    public const string StartupArgument = "--startup";

    /// <summary>
    /// Whether Windows started this process at sign-in rather than the user starting it.
    /// <para>
    /// Two mechanisms, because there are two registrations. The unpackaged build's <c>Run</c> value
    /// carries <see cref="StartupArgument"/> on its command line. The packaged build's
    /// <c>windows.startupTask</c> has no command line to carry anything — the manifest gives no
    /// place to put arguments — so it is recognised by its activation kind instead, which is the
    /// only signal Windows offers there.
    /// </para>
    /// </summary>
    public static bool IsAutostartLaunch { get; } = ResolveAutostartLaunch();

    private static bool ResolveAutostartLaunch()
    {
        foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
        {
            if (string.Equals(arg, StartupArgument, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        try
        {
            var kind = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent()
                .GetActivatedEventArgs()?.Kind;
            if (kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask)
                return true;
        }
        catch (Exception ex)
        {
            // Only reachable on the packaged build in practice, and a failure here means the app
            // opens Settings on a sign-in launch — mildly annoying, never broken — so it is logged
            // and shrugged off rather than allowed to stop startup.
            Diag.Log("StartupService: activation-kind lookup failed: " + ex.Message);
        }

        return false;
    }

    /// <summary>
    /// Brings an existing autostart registration up to date with what this build writes.
    /// <para>
    /// Specifically: a <c>Run</c> value written by a build from before
    /// <see cref="StartupArgument"/> existed has no argument on it, so every sign-in would look
    /// like a user launch and pop Settings open. Rather than make the user toggle "Start with
    /// Windows" off and on again to fix it, the app repairs its own entry at startup. It is
    /// cheap (one registry read, and a write only when something is actually wrong) and it cannot
    /// run away with itself, because after the first repair the value already matches.
    /// </para>
    /// <para>
    /// Deliberately narrow: it rewrites the entry only when the entry points at <em>this</em>
    /// executable. A value naming some other copy of SwiftX belongs to that copy — a portable
    /// build run once out of a downloads folder has no business repointing the installed copy's
    /// autostart at itself. Nothing to do on the packaged build: the startup task carries no
    /// command line to be out of date.
    /// </para>
    /// </summary>
    public static void RepairRegistration()
    {
        if (PackagedRuntime.IsPackaged)
            return;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is not string existing || existing.Length == 0)
                return;

            string exe = CurrentExecutable();
            string expected = RunCommandFor(exe);
            if (string.Equals(existing, expected, StringComparison.OrdinalIgnoreCase))
                return;
            if (!string.Equals(ExecutableFrom(existing), exe, StringComparison.OrdinalIgnoreCase))
                return;

            key.SetValue(ValueName, expected);
            Diag.Log("StartupService: repaired the autostart entry to carry " + StartupArgument);
        }
        catch (Exception ex)
        {
            Diag.Log("StartupService.RepairRegistration failed: " + ex.Message);
        }
    }

    /// <summary>What the startup entry ended up as after a request to change it.</summary>
    public enum StartupState
    {
        /// <summary>SwiftX will not start at sign-in.</summary>
        Disabled,

        /// <summary>SwiftX will start at sign-in.</summary>
        Enabled,

        /// <summary>The user turned SwiftX off in Task Manager / Settings ▸ Startup apps. Only the
        /// user can turn it back on there — the app is not allowed to override that choice.</summary>
        BlockedByUser,

        /// <summary>Group policy decides this, not the app or the user.</summary>
        BlockedByPolicy,
    }

    /// <summary>True when SwiftX is currently set to start at sign-in.</summary>
    public static async Task<bool> IsEnabledAsync()
    {
        if (!PackagedRuntime.IsPackaged)
            return IsRunKeyPresent();

        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        catch (Exception ex)
        {
            Diag.Log("StartupService.IsEnabledAsync failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Turns the startup entry on or off.
    /// </summary>
    /// <returns>What it actually ended up as — which is not always what was asked for: on the
    /// packaged build, Windows can refuse to re-enable a task the user disabled themselves. The
    /// caller is expected to reflect the returned state in the UI rather than assume the request
    /// took effect.</returns>
    public static async Task<StartupState> SetEnabledAsync(bool enabled)
    {
        if (!PackagedRuntime.IsPackaged)
        {
            SetRunKey(enabled);
            return enabled ? StartupState.Enabled : StartupState.Disabled;
        }

        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (!enabled)
            {
                task.Disable();
                return StartupState.Disabled;
            }
            return Map(await task.RequestEnableAsync());
        }
        catch (Exception ex)
        {
            Diag.Log("StartupService.SetEnabledAsync failed: " + ex.Message);
            return StartupState.Disabled;
        }
    }

    /// <summary>Collapses the platform's five states onto the four the UI distinguishes: the two
    /// "on" states differ only in who turned it on, which is not something to tell the user.</summary>
    private static StartupState Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => StartupState.Enabled,
        StartupTaskState.DisabledByUser => StartupState.BlockedByUser,
        StartupTaskState.DisabledByPolicy => StartupState.BlockedByPolicy,
        _ => StartupState.Disabled,
    };

    // ---- Unpackaged: the per-user Run key -----------------------------------

    private static bool IsRunKeyPresent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    private static void SetRunKey(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
                return;

            if (enabled)
                key.SetValue(ValueName, RunCommandFor(CurrentExecutable()));
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Diag.Log("StartupService.SetRunKey failed: " + ex.Message);
        }
    }

    private static string CurrentExecutable() =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SwiftX.exe");

    /// <summary>The exact command line an autostart entry for <paramref name="exe"/> should hold.
    /// Quoted because Program Files and most user profiles contain spaces.</summary>
    private static string RunCommandFor(string exe) => $"\"{exe}\" {StartupArgument}";

    /// <summary>
    /// The executable named by a <c>Run</c> command line, or null if it cannot be picked out. Only
    /// the quoted and bare-single-token forms are understood, which is all SwiftX has ever
    /// written; anything stranger is left alone rather than guessed at.
    /// </summary>
    private static string? ExecutableFrom(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        int space = command.IndexOf(' ');
        return space < 0 ? command : command[..space];
    }
}
